using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// A timed button sequence: a player at the panel presses Interact (F) to start the clock, then the players have
    /// <see cref="timeLimit"/> seconds to press the <see cref="sequence"/> buttons (anywhere in the level) in order.
    /// The panel shows the symbols in that order (lit once pressed, the next one blinking) and the time left, and the
    /// same clock and order show on everyone's screen. A wrong button sends the sequence back to the start (the
    /// clock keeps running); time running out stops the attempt, and it can be started again at the panel.
    /// Once all are pressed in time <see cref="onSolved"/> runs on the server (e.g. a PowerLever's ServerUnlock).
    /// The server decides; the state is shared, so a player joining later sees it as it is.
    /// </summary>
    public class TimedSequencePanel : NetworkBehaviour
    {
        enum State : byte { Idle, Running, Solved }
        enum Outcome : byte { None, Wrong, TimeUp, Solved }

        [Tooltip("The buttons to press, in order.")]
        [SerializeField] SymbolButton[] sequence;
        [Tooltip("Where players start the clock (the panel's face).")]
        [SerializeField] Transform panelFace;
        [SerializeField] float timeLimit = 120f;
        [Tooltip("How close (metres) a player's chest has to be to the panel face / a button to use it.")]
        [SerializeField] float panelReach = 2.6f;
        [SerializeField] float buttonReach = 2.2f;

        [Header("Panel display")]
        [Tooltip("The symbols on the panel, in the same order as the sequence.")]
        [SerializeField] Renderer[] slotMarks;
        [SerializeField] TextMesh timerText;
        [Tooltip("Shrinks along its local X as the time runs out.")]
        [SerializeField] Transform timerBar;

        [Header("Messages")]
        [SerializeField] string title = "PANEL STERU";
        [SerializeField] string startHint = "[F] Uruchom panel sterowania";
        [SerializeField] string idleButtonHint = "Najpierw uruchom panel sterowania";
        [SerializeField] string wrongMessage = "Zła kolejność! Zacznijcie od pierwszego symbolu.";
        [SerializeField] string timeUpMessage = "Czas minął! Uruchomcie panel jeszcze raz.";
        [SerializeField] string solvedMessage = "Ster odblokowany!";
        [SerializeField] float messageTime = 4f;

        [Header("Server")]
        [SerializeField] UnityEvent onSolved;

        readonly NetworkVariable<byte> state = new();
        readonly NetworkVariable<int> progress = new();
        readonly NetworkVariable<double> deadline = new();
        readonly NetworkVariable<byte> lastEvent = new();
        readonly NetworkVariable<double> lastEventAt = new();

        InputAction interactAction;
        int nearestButton = -1;
        bool nearPanel;
        float nextPress;
        MaterialPropertyBlock block;
        Vector3 barScale, barPosition;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public bool IsRunning => state.Value == (byte)State.Running;
        public bool IsSolved => state.Value == (byte)State.Solved;
        public int Progress => progress.Value;
        public float TimeLeft => IsRunning ? Mathf.Max(0f, (float)(deadline.Value - Now)) : 0f;

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        void Awake()
        {
            if (timerBar != null) { barScale = timerBar.localScale; barPosition = timerBar.localPosition; }
        }

        public override void OnNetworkSpawn()
        {
            interactAction = InputSystem.actions.FindAction("Player/Interact", true);
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer && IsRunning && Now >= deadline.Value) ServerFail(Outcome.TimeUp);

            FindNearest();
            if (interactAction != null && interactAction.WasPressedThisFrame() && Time.time >= nextPress)
            {
                if (nearestButton >= 0 && IsRunning) { nextPress = Time.time + 0.25f; sequence[nearestButton].Press(); PressRpc(nearestButton); }
                else if (nearPanel && state.Value == (byte)State.Idle) { nextPress = Time.time + 0.25f; StartRpc(); }
            }
            UpdateVisuals();
        }

        void FindNearest()
        {
            nearestButton = -1;
            nearPanel = false;
            var player = PlayerHealth.Local;
            if (player == null || !player.IsAlive || CutsceneSystem.ControlsLocked || IsSolved) return;
            Vector3 chest = player.transform.position + Vector3.up * 1.2f;
            if (panelFace != null) nearPanel = Vector3.Distance(chest, panelFace.position) <= panelReach;
            float best = buttonReach;
            for (int i = 0; i < sequence.Length; i++)
            {
                if (sequence[i] == null) continue;
                float d = Vector3.Distance(chest, sequence[i].transform.position);
                if (d <= best) { best = d; nearestButton = i; }
            }
        }

        bool SenderNear(ulong clientId, Vector3 point, float reach)
        {
            foreach (var player in PlayerHealth.All)
                if (player.OwnerClientId == clientId && player.IsAlive
                    && Vector3.Distance(player.transform.position + Vector3.up * 1.2f, point) <= reach + 1.5f) return true;
            return false;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void StartRpc(RpcParams rpcParams = default)
        {
            if (panelFace != null && SenderNear(rpcParams.Receive.SenderClientId, panelFace.position, panelReach)) ServerStart();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void PressRpc(int index, RpcParams rpcParams = default)
        {
            if (index < 0 || index >= sequence.Length || sequence[index] == null) return;
            if (SenderNear(rpcParams.Receive.SenderClientId, sequence[index].transform.position, buttonReach)) ServerPress(index);
        }

        /// <summary>Server: start the clock (as if a player pressed F at the panel). False if it's already running or solved.</summary>
        public bool ServerStart()
        {
            if (!IsServer || state.Value != (byte)State.Idle) return false;
            progress.Value = 0;
            deadline.Value = Now + timeLimit;
            state.Value = (byte)State.Running;
            return true;
        }

        /// <summary>Server: press button <paramref name="index"/> of the sequence (as if a player had).</summary>
        public void ServerPress(int index)
        {
            if (!IsServer || !IsRunning) return;
            if (index < progress.Value) return;   // already pressed: no harm
            if (index != progress.Value) { ServerFail(Outcome.Wrong); return; }
            progress.Value++;
            if (progress.Value < sequence.Length) return;
            state.Value = (byte)State.Solved;
            Announce(Outcome.Solved);
            onSolved?.Invoke();
        }

        void ServerFail(Outcome why)
        {
            progress.Value = 0;
            // A wrong button restarts the order but keeps the clock; time running out stops the attempt.
            if (why == Outcome.TimeUp) state.Value = (byte)State.Idle;
            Announce(why);
        }

        void Announce(Outcome what)
        {
            lastEvent.Value = (byte)what;
            lastEventAt.Value = Now;
        }

        void UpdateVisuals()
        {
            block ??= new MaterialPropertyBlock();
            bool running = IsRunning, solved = IsSolved;
            bool blinkOn = Mathf.Repeat(Time.time * 2.5f, 1f) < 0.6f;
            for (int i = 0; i < sequence.Length; i++)
            {
                bool done = solved || (running && i < progress.Value);
                if (sequence[i] != null) sequence[i].Show(done, i == nearestButton && running);
                if (slotMarks == null || i >= slotMarks.Length || slotMarks[i] == null || sequence[i] == null) continue;
                Color tint = PlatformSymbols.Tint(sequence[i].Symbol);
                float glow = done ? 2.4f : running ? (i == progress.Value ? (blinkOn ? 2f : 0.3f) : 0.15f) : 0.6f;
                slotMarks[i].GetPropertyBlock(block);
                block.SetColor(BaseColorId, done || !running ? tint : tint * 0.45f);
                block.SetColor(EmissionId, tint * glow);
                slotMarks[i].SetPropertyBlock(block);
            }
            float left = TimeLeft;
            if (timerText != null)
            {
                timerText.text = solved ? "OK" : running ? Clock(left) : "START [F]";
                timerText.color = solved ? new Color(0.3f, 1f, 0.4f) : running && left < 15f ? new Color(1f, 0.3f, 0.25f) : Color.white;
            }
            if (timerBar != null)
            {
                float frac = solved ? 1f : running ? Mathf.Clamp01(left / Mathf.Max(0.01f, timeLimit)) : 0f;
                timerBar.localScale = new Vector3(barScale.x * frac, barScale.y, barScale.z);
                timerBar.localPosition = barPosition - Vector3.right * (barScale.x * (1f - frac) * 0.5f);
            }
        }

        static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        /// <summary>Editor setup.</summary>
        public void Setup(SymbolButton[] buttons, Transform face, Renderer[] marks, TextMesh text, Transform bar, float seconds)
        {
            sequence = buttons;
            panelFace = face;
            slotMarks = marks;
            timerText = text;
            timerBar = bar;
            timeLimit = seconds;
        }

        void OnGUI()
        {
            if (!IsSpawned || CutsceneSystem.HidesGameplayUI || sequence == null) return;
            var box = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
            if (IsRunning)
            {
                // The same clock and order as on the panel.
                float left = TimeLeft;
                string clock = left < 15f ? $"<color=#ff5544>{Clock(left)}</color>" : Clock(left);
                var order = new System.Text.StringBuilder();
                for (int i = 0; i < sequence.Length; i++)
                {
                    if (sequence[i] == null) continue;
                    string name = PlatformSymbols.DisplayName(sequence[i].Symbol);
                    string color = i < progress.Value ? "#55ee66" : i == progress.Value ? "#ffdd33" : "#9a9a9a";
                    if (i > 0) order.Append("  ›  ");
                    order.Append($"<color={color}>{(i == progress.Value ? "<b>" + name + "</b>" : name)}</color>");
                }
                GUI.Box(new Rect(Screen.width * 0.5f - 380f, 96f, 760f, 76f), $"{title}   {clock}   ({progress.Value}/{sequence.Length})\n{order}", box);
            }
            string text = null;
            if (lastEvent.Value != (byte)Outcome.None && Now - lastEventAt.Value < messageTime)
                text = lastEvent.Value switch { (byte)Outcome.Wrong => wrongMessage, (byte)Outcome.TimeUp => timeUpMessage, _ => solvedMessage };
            else if (nearestButton >= 0 && IsRunning) text = "[F] " + PlatformSymbols.DisplayName(sequence[nearestButton].Symbol);
            else if (nearestButton >= 0 && state.Value == (byte)State.Idle) text = idleButtonHint;
            else if (nearPanel && state.Value == (byte)State.Idle) text = startHint;
            if (string.IsNullOrEmpty(text)) return;
            GUI.Box(new Rect(Screen.width * 0.5f - 330f, Screen.height - 190f, 660f, 44f), text, box);
        }
    }
}
