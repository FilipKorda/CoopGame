using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// One-way co-op lift, used once:
    /// 1. Locked: its doors stay shut until both levers outside are pulled at the same time (two players).
    /// 2. Open: players get in; someone presses the panel inside (Interact). Until everyone is in, every player
    ///    sees "waiting for the other player".
    /// 3. With the panel pressed and everyone inside, the doors slam shut and the cabin rides down
    ///    <see cref="travel"/> metres with the players on board.
    /// 4. At the bottom the cabin doors and the station doors there open by themselves. It never goes again.
    /// Lives on the cabin's NetworkObject: the server runs the states and moves the cabin (server NetworkTransform;
    /// players ride it like any moving platform). Doors and lamps animate on every client from the replicated state
    /// and its start time, so the door colliders match on every machine.
    /// </summary>
    public class CoopElevator : NetworkBehaviour
    {
        public enum State : byte { Locked, Open, Closing, Moving, Arrived }

        [SerializeField] BeamSwitch[] levers;
        [Tooltip("How far down the cabin goes.")]
        [SerializeField] float travel = 5.8f;
        [SerializeField] float travelTime = 4.5f;
        [Tooltip("Never wait for more players than this (the co-op pair).")]
        [SerializeField] int playersNeeded = 2;

        [Header("Cabin (local to the cabin, pivot on the floor)")]
        [Tooltip("Inside of the cabin, players' feet must be in here: half extents X/Z, centre offset.")]
        [SerializeField] Vector3 insideCenter = new Vector3(0f, 0f, -0.05f);
        [SerializeField] Vector2 insideHalfSize = new Vector2(1.55f, 1.5f);
        [SerializeField] Transform panel;
        [SerializeField] float panelReach = 1.8f;

        [Header("Doors (slide along local X: left to -X, right to +X)")]
        [SerializeField] Transform cabinDoorLeft;
        [SerializeField] Transform cabinDoorRight;
        [SerializeField] float cabinDoorSlide = 0.95f;
        [Tooltip("Doors of the bottom station, outside the shaft; they open when the cabin arrives.")]
        [SerializeField] Transform stationDoorLeft;
        [SerializeField] Transform stationDoorRight;
        [SerializeField] float stationDoorSlide = 1.1f;
        [SerializeField] float doorOpenTime = 1f;
        [SerializeField] float doorSlamTime = 0.25f;
        [Tooltip("Pause between the doors slamming and the cabin setting off.")]
        [SerializeField] float departDelay = 0.6f;

        [Header("Visuals")]
        [SerializeField] Renderer panelLamp;
        [SerializeField] Color lockedColor = new Color(0.25f, 0.25f, 0.3f);
        [SerializeField] Color readyColor = new Color(1f, 0.7f, 0.15f);
        [SerializeField] Color pressedColor = new Color(0.3f, 1f, 0.4f);
        [SerializeField] Color usedColor = new Color(0.3f, 0.8f, 1f);
        [Tooltip("Players further away than this don't see the lift's messages.")]
        [SerializeField] float messageRange = 30f;

        readonly NetworkVariable<State> state = new();
        readonly NetworkVariable<double> stateStart = new();
        readonly NetworkVariable<bool> panelPressed = new();
        readonly NetworkVariable<int> inside = new();
        readonly NetworkVariable<int> needed = new();

        Vector3 topPosition;
        Vector3 cabinLeftClosed, cabinRightClosed, stationLeftClosed, stationRightClosed;
        Material panelMaterial;
        InputAction interactAction;

        public State Current => state.Value;
        public bool PanelPressed => panelPressed.Value;

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        void Awake()
        {
            topPosition = transform.position;
            if (cabinDoorLeft != null) cabinLeftClosed = cabinDoorLeft.localPosition;
            if (cabinDoorRight != null) cabinRightClosed = cabinDoorRight.localPosition;
            if (stationDoorLeft != null) stationLeftClosed = stationDoorLeft.localPosition;
            if (stationDoorRight != null) stationRightClosed = stationDoorRight.localPosition;
            if (panelLamp != null) panelMaterial = panelLamp.material;
        }

        public override void OnNetworkSpawn()
        {
            interactAction = InputSystem.actions.FindAction("Player/Interact", true);
        }

        void Update()
        {
            if (!IsSpawned) return;
            float age = (float)(Now - stateStart.Value);
            if (IsServer) ServerTick(age);
            LocalPlayerTick();
            UpdateVisuals(age);
        }

        // ---------- Server ----------

        void ServerTick(float age)
        {
            switch (state.Value)
            {
                case State.Locked:
                    if (levers == null || levers.Length == 0) return;
                    foreach (var lever in levers)
                        if (lever == null || !lever.IsOn) return;
                    foreach (var lever in levers) lever.MarkDone();
                    SetState(State.Open);
                    break;

                case State.Open:
                {
                    int count = 0, total = 0;
                    foreach (var player in PlayerHealth.All)
                    {
                        total++;
                        if (IsInside(player.transform.position)) count++;
                    }
                    int need = Mathf.Clamp(total, 1, Mathf.Max(1, playersNeeded));
                    if (inside.Value != count) inside.Value = count;
                    if (needed.Value != need) needed.Value = need;
                    if (panelPressed.Value && count >= need && age >= doorOpenTime) SetState(State.Closing);
                    break;
                }

                case State.Closing:
                    if (age >= doorSlamTime + departDelay) SetState(State.Moving);
                    break;

                case State.Moving:
                {
                    float t = Mathf.Clamp01(age / travelTime);
                    transform.position = topPosition + Vector3.down * (travel * Mathf.SmoothStep(0f, 1f, t));
                    if (t >= 1f) SetState(State.Arrived);
                    break;
                }
            }
        }

        void SetState(State next)
        {
            state.Value = next;
            stateStart.Value = Now;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void PressPanelRpc(RpcParams rpcParams = default)
        {
            if (state.Value != State.Open || panelPressed.Value) return;
            // Only from inside the cabin.
            foreach (var player in PlayerHealth.All)
                if (player.OwnerClientId == rpcParams.Receive.SenderClientId && IsInside(player.transform.position))
                {
                    panelPressed.Value = true;
                    return;
                }
        }

        /// <summary>Server: press the panel as if a player inside had (tests, scripted sequences).</summary>
        public void ServerPressPanel()
        {
            if (IsServer && state.Value == State.Open) panelPressed.Value = true;
        }

        bool IsInside(Vector3 feet)
        {
            Vector3 local = transform.InverseTransformPoint(feet) - insideCenter;
            return Mathf.Abs(local.x) <= insideHalfSize.x && Mathf.Abs(local.z) <= insideHalfSize.y && local.y > -0.4f && local.y < 1.5f;
        }

        // ---------- Local player (every client) ----------

        PlayerHealth LocalPlayer => PlayerHealth.Local;

        bool LocalCanPressPanel()
        {
            var player = LocalPlayer;
            return player != null && player.IsAlive && panel != null && state.Value == State.Open && !panelPressed.Value
                && IsInside(player.transform.position)
                && Vector3.Distance(player.transform.position + Vector3.up, panel.position) <= panelReach;
        }

        void LocalPlayerTick()
        {
            if (interactAction != null && interactAction.WasPressedThisFrame() && LocalCanPressPanel()) PressPanelRpc();
        }

        // ---------- Visuals (every client) ----------

        void UpdateVisuals(float age)
        {
            float cabinOpen, stationOpen = 0f;
            switch (state.Value)
            {
                case State.Open: cabinOpen = Mathf.Clamp01(age / doorOpenTime); break;
                case State.Closing: cabinOpen = 1f - Mathf.Clamp01(age / doorSlamTime); break;
                case State.Arrived:
                    cabinOpen = Mathf.Clamp01(age / doorOpenTime);
                    stationOpen = cabinOpen;
                    break;
                default: cabinOpen = 0f; break;
            }
            cabinOpen = Mathf.SmoothStep(0f, 1f, cabinOpen);
            stationOpen = Mathf.SmoothStep(0f, 1f, stationOpen);
            if (cabinDoorLeft != null) cabinDoorLeft.localPosition = cabinLeftClosed + Vector3.left * (cabinDoorSlide * cabinOpen);
            if (cabinDoorRight != null) cabinDoorRight.localPosition = cabinRightClosed + Vector3.right * (cabinDoorSlide * cabinOpen);
            if (stationDoorLeft != null) stationDoorLeft.localPosition = stationLeftClosed + Vector3.left * (stationDoorSlide * stationOpen);
            if (stationDoorRight != null) stationDoorRight.localPosition = stationRightClosed + Vector3.right * (stationDoorSlide * stationOpen);

            if (panelMaterial != null)
            {
                panelMaterial.color = state.Value switch
                {
                    State.Locked => lockedColor,
                    State.Open when panelPressed.Value => pressedColor,
                    State.Open => Color.Lerp(readyColor * 0.6f, readyColor, 0.5f + 0.5f * Mathf.Sin(Time.time * 5f)),
                    State.Arrived => usedColor,
                    _ => pressedColor,
                };
            }
        }

        string Message()
        {
            var player = LocalPlayer;
            if (player == null || (player.transform.position - transform.position).sqrMagnitude > messageRange * messageRange) return null;
            bool localInside = IsInside(player.transform.position);
            switch (state.Value)
            {
                case State.Open:
                    if (inside.Value > 0 && inside.Value < needed.Value)
                        return $"Winda: czekam na drugiego gracza ({inside.Value}/{needed.Value})"
                            + (panelPressed.Value ? "" : localInside ? "  —  [F] panel: jazda w dół" : "");
                    if (localInside && !panelPressed.Value) return "Winda: [F] panel w środku — jazda w dół";
                    return null;
                case State.Closing:
                case State.Moving:
                    return localInside ? "Winda jedzie w dół..." : null;
                default:
                    return null;
            }
        }

        void OnGUI()
        {
            string text = Message();
            if (text == null || CutsceneSystem.HidesGameplayUI) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect(Screen.width * 0.5f - 330f, Screen.height - 190f, 660f, 44f), text, style);
        }

        public override void OnDestroy()
        {
            if (panelMaterial != null) Destroy(panelMaterial);
            base.OnDestroy();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(insideCenter + Vector3.up * 0.55f, new Vector3(insideHalfSize.x * 2f, 1.9f, insideHalfSize.y * 2f));
            Gizmos.matrix = Matrix4x4.identity;
            Vector3 top = Application.isPlaying ? topPosition : transform.position;
            Gizmos.DrawLine(top, top + Vector3.down * travel);
        }
    }
}
