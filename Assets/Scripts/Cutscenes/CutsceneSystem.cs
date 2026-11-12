using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame
{
    public enum CutscenePhase : byte
    {
        None,
        /// <summary>Asked for; waiting until every player has the level loaded (and enough players are in).</summary>
        Waiting,
        Playing,
        /// <summary>Over, and the game is going to another level: the last frame stays on screen until then.</summary>
        Leaving,
    }

    /// <summary>What the server tells every machine about the cutscene that's on.</summary>
    public struct CutsceneSyncState : INetworkSerializable, System.IEquatable<CutsceneSyncState>
    {
        public FixedString128Bytes Key;
        /// <summary>Goes up with every cutscene started, so the same cutscene played again is a new run.</summary>
        public int Serial;
        public CutscenePhase Phase;
        /// <summary>Server time the Timeline's 0 is at.</summary>
        public double StartTime;
        /// <summary>Another level loads when it's over, so screens keep its last shot until then.</summary>
        public bool LeadsToScene;
        public byte Ready, Needed, Players, SkipVotes;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Key);
            serializer.SerializeValue(ref Serial);
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref StartTime);
            serializer.SerializeValue(ref LeadsToScene);
            serializer.SerializeValue(ref Ready);
            serializer.SerializeValue(ref Needed);
            serializer.SerializeValue(ref Players);
            serializer.SerializeValue(ref SkipVotes);
        }

        public bool Equals(CutsceneSyncState other) =>
            Key.Equals(other.Key) && Serial == other.Serial && Phase == other.Phase && StartTime.Equals(other.StartTime)
            && LeadsToScene == other.LeadsToScene && Ready == other.Ready && Needed == other.Needed && Players == other.Players && SkipVotes == other.SkipVotes;
    }

    /// <summary>
    /// Runs cutscenes (<see cref="Cutscene"/>) for both players. Lives on the game session object, so it carries on
    /// across levels and remembers which cutscenes have played.
    /// The server decides: a cutscene waits until every player has its level loaded, starts at a server time a
    /// moment ahead (so all screens start together), ends when its Timeline does or when every player has asked
    /// to skip, and can take everyone to another level afterwards. Every machine plays the Timeline itself, in step
    /// with the server's clock; a player joining mid-way starts mid-way, one joining later gets the end state.
    /// While a cutscene is on, the local player has no control (the Player input map is off), the players can't be
    /// hurt, and (per cutscene) the players and HUD are hidden and black bars and subtitles are drawn.
    /// </summary>
    public class CutsceneSystem : NetworkBehaviour
    {
        public static CutsceneSystem Instance { get; private set; }

        [Tooltip("A cutscene starts this long after everyone is ready, so every screen starts it at the same moment.")]
        [SerializeField] float startLead = 0.3f;
        [Tooltip("A connected player who still hasn't loaded the level is waited for at most this long.")]
        [SerializeField] float readyTimeout = 15f;
        [Tooltip("If the level change after a cutscene never comes, give control back after this long.")]
        [SerializeField] float leaveTimeout = 20f;
        [Tooltip("Height of each black bar, as a part of the screen height.")]
        [SerializeField, Range(0f, 0.25f)] float letterboxSize = 0.11f;
        [SerializeField] float letterboxSpeed = 2.5f;

        readonly NetworkVariable<CutsceneSyncState> state = new();
        // Keys of the cutscenes that have played this game (in order).
        NetworkList<FixedString128Bytes> finished;

        // Server
        Cutscene serverCutscene;
        string serverNextScene, serverNextEntry;
        readonly HashSet<ulong> readyClients = new();
        readonly HashSet<ulong> skipVoters = new();
        bool forceStart;
        float phaseSince;
        int nextServerEvent;
        int serialCounter;

        // Every machine
        Cutscene wanted;  // the server's cutscene, if its level is loaded here
        Cutscene local;   // the one this screen is showing
        int localSerial = -1, reportedSerial = -1, votedSerial = -1;
        bool locked;
        InputAction skipAction;
        InputActionMap playerMap;
        readonly HashSet<PlayerHealth> hiddenPlayers = new();
        readonly List<Renderer> hiddenRenderers = new();
        readonly List<Canvas> hiddenCanvases = new();
        float letterbox;
        GUIStyle centerStyle, subtitleStyle, promptStyle;

        /// <summary>Local: the player has no control (a cutscene is starting, on, or leading out of the level).</summary>
        public static bool ControlsLocked => Instance != null && Instance.locked;
        /// <summary>Local: a cutscene is on screen; gameplay prompts and debug text should stay hidden.</summary>
        public static bool IsPlaying => Instance != null && Instance.local != null;
        /// <summary>Local: hide gameplay UI drawn with OnGUI (a cutscene that hides the HUD is on).</summary>
        public static bool HidesGameplayUI => IsPlaying && Instance.local.HideHud;
        /// <summary>Server: the players can't be hurt (from the moment a cutscene is asked for until it's over).</summary>
        public static bool ProtectsPlayers => Instance != null && Instance.IsServer && Instance.state.Value.Phase != CutscenePhase.None;

        /// <summary>A cutscene is asked for, on, or leading out of the level.</summary>
        public bool IsBusy => state.Value.Phase != CutscenePhase.None;
        public CutscenePhase Phase => state.Value.Phase;

        double Now => NetworkManager.ServerTime.Time;

        void Awake()
        {
            Instance = this;
            finished = new NetworkList<FixedString128Bytes>();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SetLocked(false, null);
            skipAction?.Dispose();
            skipAction = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            skipAction = new InputAction("CutsceneSkip", InputActionType.Button);
            skipAction.AddBinding("<Keyboard>/enter");
            skipAction.AddBinding("<Keyboard>/numpadEnter");
            skipAction.AddBinding("<Gamepad>/start");
            skipAction.Enable();
        }

        public override void OnNetworkDespawn()
        {
            SetLocked(false, null);
            local = wanted = null;
            skipAction?.Dispose();
            skipAction = null;
        }

        /// <summary>Whether this cutscene has played this game.</summary>
        public bool HasFinished(Cutscene cutscene) => cutscene != null && finished != null && finished.Contains(cutscene.Key);

        /// <summary>
        /// Server: start <paramref name="cutscene"/> for everyone. Afterwards go to <paramref name="thenScene"/>
        /// (arriving at <paramref name="thenEntry"/>), or to the cutscene's own Next Scene if none is given.
        /// Returns false if it can't start now (another one is on, it has played and plays once, no Timeline).
        /// </summary>
        public bool ServerPlay(Cutscene cutscene, string thenScene = null, string thenEntry = null)
        {
            if (!IsServer || cutscene == null) return false;
            if (IsBusy)
            {
                Debug.LogWarning($"[CutsceneSystem] '{cutscene.Id}' not started: '{state.Value.Key}' is still on.", cutscene);
                return false;
            }
            if (cutscene.PlayOnce && HasFinished(cutscene)) return false;
            if (cutscene.Duration <= 0.0)
            {
                Debug.LogWarning($"[CutsceneSystem] '{cutscene.Id}' has no Timeline (Playable Director > Playable).", cutscene);
                return false;
            }

            serverCutscene = cutscene;
            bool ownTarget = string.IsNullOrEmpty(thenScene);
            serverNextScene = ownTarget ? cutscene.NextScene : thenScene;
            serverNextEntry = ownTarget ? cutscene.NextEntry : thenEntry;
            readyClients.Clear();
            skipVoters.Clear();
            forceStart = false;
            nextServerEvent = 0;
            phaseSince = Time.unscaledTime;
            state.Value = new CutsceneSyncState
            {
                Key = cutscene.Key,
                Serial = ++serialCounter,
                Phase = CutscenePhase.Waiting,
                LeadsToScene = !string.IsNullOrEmpty(serverNextScene),
                Needed = (byte)cutscene.WaitForPlayers,
                Players = (byte)NetworkManager.ConnectedClientsIds.Count,
            };
            return true;
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerUpdate();
            LocalUpdate();
        }

        // ---------- Server ----------

        void ServerUpdate()
        {
            var s = state.Value;
            if (s.Phase == CutscenePhase.None) return;
            if (serverCutscene == null)
            {
                // Its level is gone (the level change after it, or the game moved on): nothing left to wait for.
                s.Phase = CutscenePhase.None;
                state.Value = s;
                return;
            }

            var clients = NetworkManager.ConnectedClientsIds;
            s.Players = (byte)clients.Count;
            switch (s.Phase)
            {
                case CutscenePhase.Waiting:
                {
                    int ready = 0;
                    foreach (var id in clients)
                        if (readyClients.Contains(id)) ready++;
                    s.Ready = (byte)ready;
                    bool enough = ready >= s.Needed;
                    bool everyone = ready == clients.Count;
                    bool waitedLong = Time.unscaledTime - phaseSince > readyTimeout;
                    if ((forceStart && ready > 0) || (enough && (everyone || waitedLong)))
                    {
                        s.Phase = CutscenePhase.Playing;
                        s.StartTime = Now + startLead;
                        s.SkipVotes = 0;
                        skipVoters.Clear();
                        phaseSince = Time.unscaledTime;
                    }
                    break;
                }
                case CutscenePhase.Playing:
                {
                    double elapsed = Now - s.StartTime;
                    RunServerEvents(elapsed);
                    int votes = 0;
                    foreach (var id in clients)
                        if (skipVoters.Contains(id)) votes++;
                    s.SkipVotes = (byte)votes;
                    bool skipped = serverCutscene.Skippable && clients.Count > 0 && votes >= clients.Count;
                    if (skipped || elapsed >= serverCutscene.Duration) Finish(ref s);
                    break;
                }
                case CutscenePhase.Leaving:
                    if (Time.unscaledTime - phaseSince > leaveTimeout)
                    {
                        s.Phase = CutscenePhase.None;
                        serverCutscene = null;
                    }
                    break;
            }
            if (!s.Equals(state.Value)) state.Value = s;
        }

        void RunServerEvents(double upTo)
        {
            var events = serverCutscene.ServerEvents;
            while (nextServerEvent < events.Count && events[nextServerEvent].time <= upTo)
            {
                var serverEvent = events[nextServerEvent++];
                try { serverEvent.action?.Invoke(); }
                catch (System.Exception e) { Debug.LogException(e, serverCutscene); }
            }
        }

        void Finish(ref CutsceneSyncState s)
        {
            // A skipped cutscene still does everything it would have done.
            RunServerEvents(double.PositiveInfinity);
            if (!finished.Contains(serverCutscene.Key)) finished.Add(serverCutscene.Key);
            phaseSince = Time.unscaledTime;

            bool leave = false;
            if (!string.IsNullOrEmpty(serverNextScene))
            {
                if (SceneFlow.Instance == null)
                    Debug.LogWarning("[CutsceneSystem] No SceneFlow on the game session; staying in this level.");
                else if (!Application.CanStreamedLevelBeLoaded(serverNextScene))
                    Debug.LogWarning($"[CutsceneSystem] Can't go to '{serverNextScene}' after '{serverCutscene.Id}': it isn't in the Build Settings.");
                else
                    leave = true;
            }

            if (leave)
            {
                s.Phase = CutscenePhase.Leaving;
                SceneFlow.Instance.ServerGoTo(serverNextScene, serverNextEntry);
            }
            else
            {
                s.Phase = CutscenePhase.None;
                serverCutscene = null;
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ReadyRpc(int serial, RpcParams rpcParams = default)
        {
            if (serial == state.Value.Serial && state.Value.Phase == CutscenePhase.Waiting)
                readyClients.Add(rpcParams.Receive.SenderClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void SkipRpc(int serial, RpcParams rpcParams = default)
        {
            if (serial == state.Value.Serial && state.Value.Phase == CutscenePhase.Playing)
                skipVoters.Add(rpcParams.Receive.SenderClientId);
        }

        // ---------- Every machine ----------

        void LocalUpdate()
        {
            var s = state.Value;
            wanted = s.Phase != CutscenePhase.None ? Cutscene.Find(s.Key) : null;
            if (local is not null && local == null) local = null; // destroyed with its level

            if (local != null)
            {
                if (local != wanted || localSerial != s.Serial)
                {
                    // Over (or skipped): everything ends the way the cutscene leaves it.
                    EndShown(local);
                    local = null;
                }
                else if (s.Phase == CutscenePhase.Leaving) local.HoldEnd();
                else local.Sync(Now - s.StartTime, s.LeadsToScene);
            }

            if (local == null && wanted != null && s.Phase == CutscenePhase.Playing && localSerial != s.Serial && Now >= s.StartTime)
            {
                local = wanted;
                localSerial = s.Serial;
                local.BeginLocal(Now - s.StartTime);
            }

            // Tell the server this machine has the level (and the cutscene) loaded.
            if (wanted != null && s.Phase == CutscenePhase.Waiting && reportedSerial != s.Serial)
            {
                reportedSerial = s.Serial;
                ReadyRpc(s.Serial);
            }

            // Cutscenes that played while this machine wasn't here or in this level: catch up with their ends.
            foreach (var cutscene in Cutscene.All)
                if (!cutscene.EndStateApplied && cutscene != local && cutscene != wanted && finished.Contains(cutscene.Key))
                    cutscene.ApplyEndState();

            SetLocked(wanted != null, wanted);

            if (locked && skipAction != null && skipAction.WasPressedThisFrame())
            {
                if (s.Phase == CutscenePhase.Waiting && IsServer) forceStart = true; // the host doesn't wait any longer
                else if (s.Phase == CutscenePhase.Playing && local != null && local.Skippable && votedSerial != s.Serial)
                {
                    votedSerial = s.Serial;
                    SkipRpc(s.Serial);
                }
            }

            bool bars = local != null && local.Letterbox;
            letterbox = Mathf.MoveTowards(letterbox, bars ? 1f : 0f, Time.unscaledDeltaTime * letterboxSpeed);
        }

        void EndShown(Cutscene cutscene)
        {
            cutscene.EndLocal();
            var player = PlayerHealth.Local;
            if (player != null && cutscene.TryGetEndPose(NetworkManager.LocalClientId, out var pose)
                && player.TryGetComponent(out PlayerMovement movement))
                movement.TeleportTo(pose);
        }

        void SetLocked(bool on, Cutscene cutscene)
        {
            if (on)
            {
                if (!locked)
                {
                    locked = true;
                    if (playerMap == null && InputSystem.actions != null) playerMap = InputSystem.actions.FindActionMap("Player");
                    playerMap?.Disable();
                    if (cutscene.HideHud) HideCanvases(cutscene);
                }
                if (cutscene.HidePlayers) HidePlayers(); // again every frame: someone may join meanwhile
                return;
            }

            if (!locked) return;
            locked = false;
            playerMap?.Enable();
            foreach (var r in hiddenRenderers)
                if (r != null) r.forceRenderingOff = false;
            hiddenRenderers.Clear();
            hiddenPlayers.Clear();
            foreach (var canvas in hiddenCanvases)
                if (canvas != null) canvas.enabled = true;
            hiddenCanvases.Clear();
            CutsceneSubtitles.Clear();
        }

        void HideCanvases(Cutscene cutscene)
        {
            // Screen canvases (the HUD); a canvas inside the cutscene object is its own and stays.
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas || !canvas.enabled || canvas.renderMode == RenderMode.WorldSpace) continue;
                if (canvas.transform.IsChildOf(cutscene.transform)) continue;
                canvas.enabled = false;
                hiddenCanvases.Add(canvas);
            }
        }

        void HidePlayers()
        {
            foreach (var player in PlayerHealth.All)
            {
                if (player == null || !hiddenPlayers.Add(player)) continue;
                // forceRenderingOff, not enabled: other code (damage blinking) toggles enabled.
                foreach (var r in player.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.forceRenderingOff) continue;
                    r.forceRenderingOff = true;
                    hiddenRenderers.Add(r);
                }
            }
        }

        // ---------- Overlay ----------

        void OnGUI()
        {
            if (!IsSpawned) return;
            var s = state.Value;
            bool waitingBlack = s.Phase == CutscenePhase.Waiting && wanted != null && wanted.BlackWhileWaiting;
            if (!waitingBlack && letterbox <= 0f && local == null) return;
            MakeStyles();

            if (waitingBlack)
            {
                // Under the connection box (depth 0), so the host can still read out the join code.
                GUI.depth = 50;
                DrawWaiting(s);
                return;
            }

            GUI.depth = -100;
            DrawBars();
            if (local == null) return;
            DrawSubtitles();
            if (local.Skippable) DrawSkipPrompt(s);
        }

        void MakeStyles()
        {
            int h = Screen.height;
            centerStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            centerStyle.fontSize = Mathf.Max(18, h / 26);
            subtitleStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, wordWrap = true, richText = true };
            subtitleStyle.fontSize = Mathf.Max(16, h / 32);
            promptStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };
            promptStyle.fontSize = Mathf.Max(13, h / 52);
        }

        void DrawWaiting(in CutsceneSyncState s)
        {
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string text = s.Ready < s.Needed
                ? $"Czekam na drugiego gracza ({s.Ready}/{s.Needed})..."
                : "Czekam, aż wszyscy wczytają poziom...";
            if (IsServer && IsClient) text += "\n\n<size=" + Mathf.Max(13, Screen.height / 50) + ">[Enter] zacznij bez czekania</size>";
            centerStyle.richText = true;
            GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), text, centerStyle);
        }

        void DrawBars()
        {
            float t = Mathf.SmoothStep(0f, 1f, letterbox);
            float bar = Screen.height * letterboxSize * t;
            if (bar < 0.5f) return;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, bar), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, Screen.height - bar, Screen.width, bar), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void DrawSubtitles()
        {
            if (!CutsceneSubtitles.Visible) return;
            string text = CutsceneSubtitles.Text;
            if (!string.IsNullOrEmpty(CutsceneSubtitles.Speaker))
                text = $"<b><color=#{ColorUtility.ToHtmlStringRGB(CutsceneSubtitles.SpeakerColor)}>{CutsceneSubtitles.Speaker}</color></b>\n{text}";

            float width = Screen.width * 0.7f;
            float bottom = Screen.height - Screen.height * 0.018f;
            var rect = new Rect((Screen.width - width) * 0.5f, bottom - Screen.height * 0.3f, width, Screen.height * 0.3f);
            float alpha = CutsceneSubtitles.Alpha;
            GUI.color = new Color(0f, 0f, 0f, 0.85f * alpha);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), StripColors(text), subtitleStyle);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(rect, text, subtitleStyle);
            GUI.color = Color.white;
        }

        // The shadow copy of a line: same layout, no colour tags (they would show through the shadow's tint).
        static string StripColors(string text) =>
            System.Text.RegularExpressions.Regex.Replace(text, "</?color[^>]*>", "");

        void DrawSkipPrompt(in CutsceneSyncState s)
        {
            string text = votedSerial == s.Serial
                ? (s.Players > 1 ? $"Pomijanie: {s.SkipVotes}/{s.Players} — czekam na drugiego gracza" : "Pomijanie...")
                : s.SkipVotes > 0 ? $"Drugi gracz chce pominąć — [Enter] pomiń ({s.SkipVotes}/{s.Players})" : "[Enter] pomiń";
            float bar = Mathf.Max(Screen.height * letterboxSize * Mathf.SmoothStep(0f, 1f, letterbox), Screen.height * 0.06f);
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(0f, 0f, Screen.width - Screen.height * 0.03f, bar), text, promptStyle);
            GUI.color = Color.white;
        }
    }
}
