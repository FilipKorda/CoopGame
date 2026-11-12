using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CoopGame
{
    /// <summary>
    /// One cutscene in a level: a Timeline (on the <see cref="PlayableDirector"/> next to this) plus how it starts
    /// and ends. No NetworkObject needed: the server decides when it starts and ends (<see cref="CutsceneSystem"/>,
    /// on the game session) and every machine plays the same Timeline itself, kept in step by the server's clock.
    /// So everything in the Timeline (cameras, animation, activation, signals, dialogue) runs on every screen.
    /// Changes that must happen once, on the server (networked objects: a wall that blows up, enemies to spawn),
    /// go in <see cref="serverEvents"/>; they run once at their time, or all at the end if the cutscene is skipped.
    /// Start it with <see cref="startMode"/> (level start), a <see cref="CutsceneTrigger"/>, a
    /// <see cref="LevelExit"/> (outro before the next level) or <see cref="Play"/> from an event.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayableDirector))]
    public class Cutscene : MonoBehaviour
    {
        public enum StartMode
        {
            /// <summary>Started by a trigger, a level exit or <see cref="Play"/>.</summary>
            Manual,
            /// <summary>Starts as soon as the level is loaded (an act's opening cutscene).</summary>
            SceneStart,
        }

        [System.Serializable]
        public class ServerEvent
        {
            [Tooltip("Seconds from the start of the cutscene.")]
            public float time;
            [Tooltip("Runs on the server only, once.")]
            public UnityEvent action = new();
        }

        static readonly List<Cutscene> s_All = new();

        [Tooltip("Name of this cutscene, unique in its scene (the server and clients find it by scene + this name). Filled in with the object's name.")]
        [SerializeField] string id = "";

        [Header("Start")]
        [SerializeField] StartMode startMode = StartMode.Manual;
        [Tooltip("Plays only once per game; starting it again does nothing. Coming back to the level later (or joining late) shows the state it ends in.")]
        [SerializeField] bool playOnce = true;
        [Tooltip("How many players must be in the level before it starts. The host can start without waiting (Enter).")]
        [SerializeField, Range(1, 2)] int waitForPlayers = 1;
        [Tooltip("Black screen while waiting, so nothing gets seen before the cutscene (opening cutscenes).")]
        [SerializeField] bool blackWhileWaiting;

        [Header("While it plays")]
        [Tooltip("Enter / gamepad Start skips it once every player has asked to.")]
        [SerializeField] bool skippable = true;
        [Tooltip("Hide the real players: the Timeline animates its own stand-ins for them.")]
        [SerializeField] bool hidePlayers = true;
        [Tooltip("Hide the HUD (screen canvases outside this object) and gameplay prompts.")]
        [SerializeField] bool hideHud = true;
        [Tooltip("Black bars at the top and bottom of the screen.")]
        [SerializeField] bool letterbox = true;

        [Header("End")]
        [Tooltip("Where the players stand when it ends: player 1, player 2 (same order as the spawn points). Empty = where they were.")]
        [SerializeField] Transform[] endPoints;
        [Tooltip("Scene to go to when it ends (it must be in the Build Settings); empty = stay in this level. A Level Exit that plays this cutscene passes its own target instead.")]
        [SerializeField] string nextScene = "";
        [Tooltip("Entrance of that scene to arrive at (PlayerSpawnPoints Entry Id); empty = its default.")]
        [SerializeField] string nextEntry = "";

        [Header("Events")]
        [Tooltip("Server only, each once at its time. Skipping runs the ones not reached yet, in order. For networked changes (a wall that's destroyed, enemies to spawn); the visuals belong in the Timeline.")]
        [SerializeField] List<ServerEvent> serverEvents = new();
        [Tooltip("Every machine, when it starts on that screen.")]
        public UnityEvent onStarted = new();
        [Tooltip("Every machine, when it's over (also skipped, and for a player who joins after it or comes back to the level).")]
        public UnityEvent onFinished = new();

        PlayableDirector director;
        FixedString128Bytes key;
        bool startRequested;
        // The director is stopped once this frame is over, after it has evaluated the end (and passed its signals).
        int stopAfterFrame = -1;
        readonly List<(TrackAsset track, Object binding)> mutedBindings = new();
        readonly List<Behaviour> frozenBrains = new();

        /// <summary>Every cutscene in the loaded levels.</summary>
        public static IReadOnlyList<Cutscene> All => s_All;

        /// <summary>Server: an opening cutscene of <paramref name="scene"/> hasn't been started yet (gameplay there should wait).</summary>
        public static bool OpeningPending(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var cutscene in s_All)
                if (cutscene.startMode == StartMode.SceneStart && !cutscene.startRequested && cutscene.isActiveAndEnabled && cutscene.gameObject.scene == scene)
                    return true;
            return false;
        }

        public string Id => id;
        /// <summary>"Scene/id": what the network knows this cutscene by.</summary>
        public FixedString128Bytes Key => key;
        public PlayableDirector Director => director;
        /// <summary>Length of the Timeline in seconds (0 = no Timeline).</summary>
        public double Duration => director != null && director.playableAsset != null ? director.playableAsset.duration : 0.0;
        public bool PlayOnce => playOnce;
        public int WaitForPlayers => waitForPlayers;
        public bool BlackWhileWaiting => blackWhileWaiting;
        public bool Skippable => skippable;
        public bool HidePlayers => hidePlayers;
        public bool HideHud => hideHud;
        public bool Letterbox => letterbox;
        public string NextScene => nextScene;
        public string NextEntry => nextEntry;
        public IReadOnlyList<ServerEvent> ServerEvents => serverEvents;
        /// <summary>This machine has shown (or caught up with) the end of the latest run.</summary>
        public bool EndStateApplied { get; private set; }

        public static Cutscene Find(in FixedString128Bytes key)
        {
            foreach (var cutscene in s_All)
                if (cutscene.key.Equals(key)) return cutscene;
            return null;
        }

        void Reset()
        {
            id = name;
            var playable = GetComponent<PlayableDirector>();
            playable.playOnAwake = false;
            playable.extrapolationMode = DirectorWrapMode.Hold;
        }

        void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = name;
        }

        void Awake()
        {
            director = GetComponent<PlayableDirector>();
            // The system starts it; at the end it holds the last frame until the server says it's over.
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.Hold;
            if (director.state == PlayState.Playing) director.Stop();

            if (string.IsNullOrEmpty(id)) id = name;
            key.CopyFromTruncated(gameObject.scene.name + "/" + id);
            foreach (var other in s_All)
                if (other.key.Equals(key))
                    Debug.LogError($"[Cutscene] Two cutscenes named '{id}' in scene '{gameObject.scene.name}'; give them different ids.", this);
            s_All.Add(this);
            serverEvents.Sort((a, b) => a.time.CompareTo(b.time));
        }

        void OnDestroy()
        {
            s_All.Remove(this);
            UnfreezeCameras();
        }

        void Update()
        {
            // An opening cutscene: the server starts it once the level (and the cutscene system) is up.
            if (startMode != StartMode.SceneStart || startRequested) return;
            var system = CutsceneSystem.Instance;
            if (system == null || !system.IsSpawned || !system.IsServer || system.IsBusy) return;
            // Not under the black screen of a level change (a story interlude may still be playing).
            if (SceneFlow.Instance != null && SceneFlow.Instance.IsTransitioning) return;
            startRequested = true;
            system.ServerPlay(this);
        }

        void LateUpdate()
        {
            if (stopAfterFrame < 0 || Time.frameCount <= stopAfterFrame) return;
            StopNow();
            onFinished.Invoke();
        }

        /// <summary>
        /// Start this cutscene for everyone. Callable on any machine (e.g. from a UnityEvent); only the server acts.
        /// </summary>
        public void Play()
        {
            var system = CutsceneSystem.Instance;
            if (system != null && system.IsServer) system.ServerPlay(this);
        }

        /// <summary>Where a player ends up when the cutscene is over, if it says.</summary>
        public bool TryGetEndPose(ulong clientId, out Pose pose)
        {
            pose = default;
            if (endPoints == null || endPoints.Length == 0) return false;
            Transform point = endPoints[(int)(clientId % (ulong)endPoints.Length)];
            if (point == null) return false;
            pose = new Pose(point.position, point.rotation);
            return true;
        }

        double Clamp(double time) => System.Math.Clamp(time, 0.0, Duration);

        // ---------- Local playback (called by CutsceneSystem on every machine) ----------

        /// <summary>Start showing it, <paramref name="time"/> seconds in (late joiners start mid-way).</summary>
        internal void BeginLocal(double time)
        {
            if (stopAfterFrame >= 0) StopNow();
            UnfreezeCameras();
            EndStateApplied = false;
            time = Clamp(time);
            // Starting the graph at this time means signals before it only fire if they're Retroactive.
            director.initialTime = time;
            director.time = time;
            director.Play();
            if (System.Math.Abs(director.time - time) > 0.01) director.time = time;
            onStarted.Invoke();
        }

        /// <summary>
        /// Keep in step with the server's clock (a frame hitch shouldn't leave this screen behind). With
        /// <paramref name="holdLastShot"/> (another level comes next) the camera stops on the last shot instead of
        /// going back to the player when the Timeline runs out.
        /// </summary>
        internal void Sync(double time, bool holdLastShot)
        {
            time = Clamp(time);
            if (director.state != PlayState.Playing) director.Play();
            if (System.Math.Abs(director.time - time) > 0.25) director.time = time;
            // The director evaluates after Update: stop the camera before the frame that would pass the end.
            if (holdLastShot && director.time + Time.deltaTime * 1.5 >= Duration) FreezeCameras();
        }

        /// <summary>Over, and the level changes next: keep the last shot until the level unloads.</summary>
        internal void HoldEnd() => FreezeCameras();

        // The brains the Timeline drives stop where they are (a Cinemachine shot ends with the Timeline, after
        // which the brain would blend back to the player camera).
        void FreezeCameras()
        {
            if (frozenBrains.Count > 0 || !(director.playableAsset is TimelineAsset timeline)) return;
            foreach (var track in timeline.GetOutputTracks())
            {
                if (track.GetType().Name != "CinemachineTrack") continue;
                if (director.GetGenericBinding(track) is Behaviour brain && brain.enabled)
                {
                    brain.enabled = false;
                    frozenBrains.Add(brain);
                }
            }
        }

        void UnfreezeCameras()
        {
            foreach (var brain in frozenBrains)
                if (brain != null) brain.enabled = true;
            frozenBrains.Clear();
        }

        /// <summary>
        /// The server says it's over (ended or skipped). Jump to the end while still playing, so the next frame
        /// passes every signal that was skipped and leaves everything as the cutscene ends; then stop.
        /// </summary>
        internal void EndLocal()
        {
            EndStateApplied = true;
            UnfreezeCameras(); // the level change didn't happen after all
            if (director.state != PlayState.Playing) director.Play();
            director.time = Duration;
            stopAfterFrame = Time.frameCount;
        }

        /// <summary>
        /// It played while this machine wasn't here (joined late, came back to the level): put everything the way
        /// it ends without showing it (no camera cut); only Retroactive signals fire.
        /// </summary>
        internal void ApplyEndState()
        {
            EndStateApplied = true;
            if (director.playableAsset == null) return;
            if (director.playableAsset is TimelineAsset timeline)
                foreach (var track in timeline.GetOutputTracks())
                {
                    if (track.GetType().Name != "CinemachineTrack") continue;
                    mutedBindings.Add((track, director.GetGenericBinding(track)));
                    director.SetGenericBinding(track, null);
                }
            director.initialTime = Duration;
            director.time = Duration;
            director.Play();
            stopAfterFrame = Time.frameCount;
        }

        void StopNow()
        {
            stopAfterFrame = -1;
            director.Stop();
            foreach (var (track, binding) in mutedBindings) director.SetGenericBinding(track, binding);
            mutedBindings.Clear();
            director.initialTime = 0.0;
        }

        void OnDrawGizmos()
        {
            if (endPoints == null) return;
            Gizmos.color = new Color(1f, 0.4f, 1f);
            foreach (var point in endPoints)
            {
                if (point == null) continue;
                Gizmos.DrawWireSphere(point.position + Vector3.up, 0.5f);
                Gizmos.DrawRay(point.position + Vector3.up, point.forward);
            }
        }
    }
}
