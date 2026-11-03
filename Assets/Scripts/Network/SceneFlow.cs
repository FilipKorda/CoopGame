using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace CoopGame
{
    /// <summary>
    /// Takes both players from one level to the next. Lives on the game session object, which survives level loads.
    /// The server asks for a scene (and which of its entrances to arrive at, see <see cref="PlayerSpawnPoints"/>);
    /// every screen fades to black, the server loads the scene for everyone (players and their gear come along),
    /// the players step onto the new spawn points by themselves, and the screens fade back in.
    /// If a <see cref="StoryInterlude"/> is set up for that change of level, the narrator tells it on the black
    /// screen while the next level loads underneath; the screens fade in once both are done (or every player has
    /// skipped it with Enter / gamepad Start).
    /// </summary>
    public class SceneFlow : NetworkBehaviour
    {
        public static SceneFlow Instance { get; private set; }

        [SerializeField] float fadeTime = 0.35f;
        [Tooltip("Extra time on black before the load starts, and after it ends (the camera settles meanwhile).")]
        [SerializeField] float holdBlack = 0.15f;
        [Tooltip("Story breaks between levels (see StoryInterlude).")]
        [SerializeField] StoryInterlude[] interludes;
        [Tooltip("How long a line fades in and out.")]
        [SerializeField] float lineFade = 0.6f;

        bool transitioning; // server
        bool dark;          // every client: fading out / staying black
        float fade;         // every client: 0 clear .. 1 black

        // Server: the interlude that has to finish before the fade in.
        bool loadDone, interludeRunning;
        double interludeEnds;
        readonly HashSet<ulong> skipVoters = new();

        // Every client: the interlude on screen.
        StoryInterlude shown;
        double shownStart;
        int spokenLine = -1;
        bool votedSkip;
        AudioSource voice;
        InputAction skipAction;
        GUIStyle lineStyle, speakerStyle, titleStyle, hintStyle;

        /// <summary>Server: a level change is under way.</summary>
        public bool IsTransitioning => transitioning;
        /// <summary>Local: a story interlude is on screen.</summary>
        public bool IsShowingInterlude => shown != null;

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        void Awake() => Instance = this;
        [Header("Testing")]
        [SerializeField] StoryInterlude testInterlude;

        [ContextMenu("Testing/Play Story Interlude")]
        public void TestInterlude()
        {
            if (testInterlude == null)
            {
                Debug.LogWarning("[SceneFlow] Test Interlude is not assigned.", this);
                return;
            }

            dark = true;
            fade = 1f;

            shown = testInterlude;
            shownStart = Time.timeAsDouble;
            spokenLine = -1;
            votedSkip = false;

            if (voice == null)
            {
                voice = gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 0f;
            }
        }

        [ContextMenu("Testing/Stop Story Interlude")]
        public void StopInterludeTest()
        {
            dark = false;
            shown = null;

            if (voice != null)
                voice.Stop();
        }


        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            skipAction?.Dispose();
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) NetworkManager.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            skipAction = new InputAction("InterludeSkip", InputActionType.Button);
            skipAction.AddBinding("<Keyboard>/enter");
            skipAction.AddBinding("<Gamepad>/start");
            skipAction.Enable();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null && NetworkManager.SceneManager != null)
                NetworkManager.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
            skipAction?.Dispose();
            skipAction = null;
        }

        /// <summary>
        /// Server: take everyone to <paramref name="scene"/> (it must be in the Build Settings), arriving at the spawn
        /// points with <paramref name="entry"/> as their id (empty = the scene's default). Ignored mid-transition.
        /// </summary>
        public void ServerGoTo(string scene, string entry)
        {
            if (!IsServer || transitioning) return;
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogWarning($"[SceneFlow] Can't go to '{scene}': it isn't in the Build Settings (File > Build Profiles > Scene List).");
                return;
            }
            transitioning = true;
            loadDone = false;
            skipVoters.Clear();

            int interlude = FindInterlude(SceneManager.GetActiveScene().name, scene);
            interludeRunning = interlude >= 0;
            double start = Now + fadeTime + holdBlack;
            if (interludeRunning) interludeEnds = start + interludes[interlude].Duration;
            BeginRpc(entry ?? "", interlude, start);
            StartCoroutine(LoadWhenDark(scene));
        }

        int FindInterlude(string from, string to)
        {
            if (interludes == null) return -1;
            for (int i = 0; i < interludes.Length; i++)
                if (interludes[i] != null && interludes[i].lines.Count > 0 && interludes[i].Matches(from, to)) return i;
            return -1;
        }

        IEnumerator LoadWhenDark(string scene)
        {
            yield return new WaitForSecondsRealtime(fadeTime + holdBlack);
            var status = NetworkManager.SceneManager.LoadScene(scene, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogWarning($"[SceneFlow] Loading '{scene}' failed: {status}.");
                transitioning = false;
                interludeRunning = false;
                EndRpc();
            }
        }

        void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            if (!transitioning) return; // the first level, loaded from the menu
            loadDone = true;
        }

        void Update()
        {
            fade = Mathf.MoveTowards(fade, dark ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeTime));
            if (!IsSpawned) return;

            if (IsServer && transitioning && loadDone)
            {
                // The level is there; fade in once the story is told (or everyone skipped it).
                bool everyoneSkipped = skipVoters.Count > 0 && skipVoters.Count >= NetworkManager.ConnectedClientsIds.Count;
                if (!interludeRunning || Now >= interludeEnds || everyoneSkipped)
                {
                    transitioning = false;
                    interludeRunning = false;
                    StartCoroutine(FadeInSoon());
                }
            }

            UpdateInterlude();
        }

        IEnumerator FadeInSoon()
        {
            yield return new WaitForSecondsRealtime(holdBlack);
            EndRpc();
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void BeginRpc(string entry, int interlude, double start)
        {
            PlayerSpawnPoints.SetRequestedEntry(entry);
            dark = true;
            shown = interlude >= 0 && interludes != null && interlude < interludes.Length ? interludes[interlude] : null;
            shownStart = start;
            spokenLine = -1;
            votedSkip = false;
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void EndRpc()
        {
            dark = false;
            shown = null;
            if (voice != null) voice.Stop();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void SkipRpc(RpcParams rpcParams = default)
        {
            if (interludeRunning) skipVoters.Add(rpcParams.Receive.SenderClientId);
        }

        // ---------- Interlude (every client) ----------

        void UpdateInterlude()
        {
            if (shown == null) return;
            float t = (float)(Now - shownStart);
            int line = shown.LineAt(t, out _);
            if (line >= 0 && line != spokenLine)
            {
                spokenLine = line;
                var clip = shown.lines[line].voice;
                if (clip != null && voice != null) voice.PlayOneShot(clip);
            }
            if (!votedSkip && skipAction != null && skipAction.WasPressedThisFrame())
            {
                votedSkip = true;
                if (voice != null) voice.Stop();
                SkipRpc();
            }
        }

        void OnGUI()
        {
            if (fade <= 0f) return;
            GUI.depth = -1000; // over everything else
            GUI.color = new Color(0f, 0f, 0f, fade);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            if (fade < 0.95f) return;
            GUI.color = Color.white;
            MakeStyles();

            float t = shown != null ? (float)(Now - shownStart) : 0f;
            int line = shown != null && !votedSkip ? shown.LineAt(t, out float into) : -1;
            bool storyOver = shown == null || votedSkip || t >= shown.Duration;

            if (shown != null && !storyOver)
            {
                if (!string.IsNullOrEmpty(shown.title))
                    GUI.Label(new Rect(0f, Screen.height * 0.12f, Screen.width, 60f), shown.title, titleStyle);
                if (line >= 0)
                {
                    shown.LineAt(t, out float lineTime);
                    float length = shown.LineLength(line);
                    float alpha = Mathf.Clamp01(Mathf.Min(lineTime, length - lineTime) / Mathf.Max(0.01f, lineFade));
                    float width = Screen.width * 0.66f;
                    var area = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.38f, width, Screen.height * 0.3f);
                    if (!string.IsNullOrEmpty(shown.speaker))
                    {
                        GUI.color = new Color(1f, 0.82f, 0.4f, alpha * 0.9f);
                        GUI.Label(new Rect(area.x, area.y - 44f, area.width, 40f), shown.speaker, speakerStyle);
                    }
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    GUI.Label(area, shown.lines[line].text, lineStyle);
                }
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                string hint = votedSkip ? "Pomijanie — czekam na drugiego gracza" : "[Enter] pomiń";
                GUI.Label(new Rect(0f, Screen.height - 50f, Screen.width - 30f, 30f), hint, hintStyle);
                GUI.color = Color.white;
                return;
            }

            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 32 };
            GUI.Label(new Rect(0f, Screen.height * 0.5f - 30f, Screen.width, 60f), "Ładowanie...", style);
        }

        void MakeStyles()
        {
            if (lineStyle != null) return;
            lineStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = Mathf.Max(20, Screen.height / 26) };
            speakerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontStyle = FontStyle.Italic, fontSize = Mathf.Max(16, Screen.height / 40) };
            titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.Max(24, Screen.height / 20) };
            hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontSize = Mathf.Max(13, Screen.height / 52) };
        }
    }
}
