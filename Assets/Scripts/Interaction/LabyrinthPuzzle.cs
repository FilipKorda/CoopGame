using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// The labyrinth in the cave, for two players:
    /// 1. One player holds the <see cref="HoldGate"/>'s lever, the other goes through the gate. The moment anyone
    ///    passes through, the gate slams shut for good (the players are split) and its lever stops working.
    /// 2. On the lever side a hidden passage opens (<see cref="enterDoor"/>): that player goes into the labyrinth.
    ///    The other one has the labyrinth's plan (its instruction board) and talks them through it.
    /// 3. Pulling both <see cref="mazeLevers"/> (they stay pulled) opens the labyrinth's exit (<see cref="exitDoor"/>).
    /// 4. Behind it, <see cref="doorButton"/> opens <see cref="instructionDoors"/> for both of them, for good.
    /// The server decides; the times things happened are shared, and every machine moves the doors (plain scene
    /// objects) itself, so a player who joins later sees them as they are.
    /// </summary>
    public class LabyrinthPuzzle : NetworkBehaviour
    {
        [Header("Splitting gate")]
        [SerializeField] HoldGate gate;
        [Tooltip("The gate's lever: switched off for good once the gate is sealed.")]
        [SerializeField] Graspable gateLever;
        [Tooltip("Speed (m/s) the gate starts slamming down at when it seals.")]
        [SerializeField] float slamSpeed = 14f;
        [Tooltip("How far (metres) in front of / behind the doorway a player still counts as in it.")]
        [SerializeField] float doorwayReach = 3f;

        [Header("Doors (plain scene objects, sunk into the floor when they open)")]
        [Tooltip("The hidden passage into the labyrinth, on the lever's side.")]
        [SerializeField] Transform enterDoor;
        [SerializeField] Transform exitDoor;
        [SerializeField] Transform[] instructionDoors;
        [SerializeField] float doorTime = 1.6f;
        [Tooltip("The hidden passage opens this long after the gate slams.")]
        [SerializeField] float enterDoorDelay = 0.8f;
        [Tooltip("Sunk this much deeper than their own height.")]
        [SerializeField] float doorSink = 0.1f;

        [Header("Switches")]
        [Tooltip("Inside the labyrinth; all of them pulled opens the exit (they stay pulled).")]
        [SerializeField] BeamSwitch[] mazeLevers;
        [Tooltip("Behind the exit: opens the instruction doors for both players.")]
        [SerializeField] BeamSwitch doorButton;

        [Header("Messages")]
        [SerializeField] float messageTime = 6f;
        [SerializeField] string sealedLeverSide = "Brama zatrzasnęła się na stałe! Odsłoniło się nowe przejście — wejdź do labiryntu.";
        [SerializeField] string sealedOtherSide = "Brama zatrzasnęła się na stałe! Znajdź instrukcję labiryntu i prowadź partnera.";
        [SerializeField] string exitMessage = "Obie dźwignie pociągnięte — wyjście z labiryntu otwarte!";
        [SerializeField] string doorsMessage = "Drzwi otwarte! Idźcie do skoczni i spotkajcie się przy windzie.";

        // Server time each step happened (negative = not yet).
        readonly NetworkVariable<double> sealedAt = new(-1.0);
        readonly NetworkVariable<double> exitAt = new(-1.0);
        readonly NetworkVariable<double> doorsAt = new(-1.0);

        readonly Dictionary<Transform, (Vector3 position, float sink)> doorStart = new();
        readonly Dictionary<ulong, float> lastSide = new();
        GUIStyle messageStyle;

        public bool IsSealed => sealedAt.Value >= 0.0;
        public bool IsExitOpen => exitAt.Value >= 0.0;
        public bool AreDoorsOpen => doorsAt.Value >= 0.0;

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;

        void Awake()
        {
            Remember(enterDoor);
            Remember(exitDoor);
            if (instructionDoors != null)
                foreach (var door in instructionDoors) Remember(door);
        }

        void Remember(Transform door)
        {
            if (door == null || doorStart.ContainsKey(door)) return;
            float height = door.TryGetComponent(out Renderer r) ? r.bounds.size.y : door.lossyScale.y;
            doorStart[door] = (door.position, height + doorSink);
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();

            // Every machine: the sealed gate's lever, and labyrinth levers already pulled, can't be grabbed any more.
            if (IsSealed) Lock(gateLever);
            if (mazeLevers != null)
                foreach (var lever in mazeLevers)
                    if (lever != null && lever.IsDone) Lock(lever.GetComponent<Graspable>());

            MoveDoor(enterDoor, sealedAt.Value, enterDoorDelay);
            MoveDoor(exitDoor, exitAt.Value, 0f);
            if (instructionDoors != null)
                foreach (var door in instructionDoors) MoveDoor(door, doorsAt.Value, 0f);
        }

        static void Lock(Graspable grip)
        {
            if (grip == null || !grip.enabled) return;
            grip.enabled = false; // drops whoever holds it
            if (grip.TryGetComponent(out Targetable target)) target.enabled = false;
        }

        void MoveDoor(Transform door, double openedAt, float delay)
        {
            if (door == null || openedAt < 0.0 || !doorStart.TryGetValue(door, out var start)) return;
            float t = Mathf.Clamp01((float)(Now - openedAt - delay) / Mathf.Max(0.01f, doorTime));
            Vector3 target = start.position + Vector3.down * (start.sink * Mathf.SmoothStep(0f, 1f, t));
            if ((door.position - target).sqrMagnitude > 1e-8f) door.position = target;
        }

        // ---------- Server ----------

        void ServerTick()
        {
            if (!IsSealed) WatchDoorway();

            // A labyrinth lever pulled all the way stays pulled (and lit); both pulled opens the exit.
            if (IsSealed && !IsExitOpen && mazeLevers != null && mazeLevers.Length > 0)
            {
                bool all = true;
                foreach (var lever in mazeLevers)
                {
                    if (lever == null) { all = false; continue; }
                    if (lever.IsOn) lever.MarkDone();
                    if (!lever.IsDone) all = false;
                }
                if (all) exitAt.Value = Now;
            }

            if (!AreDoorsOpen && doorButton != null && doorButton.IsOn)
            {
                doorsAt.Value = Now;
                doorButton.MarkDone();
            }
        }

        /// <summary>
        /// Seals the gate as soon as a player has gone through its doorway (clear of the gate on the other side, so it
        /// slams right behind them instead of on them).
        /// </summary>
        void WatchDoorway()
        {
            if (gate == null) return;
            float clear = gate.Size.z * 0.5f + 0.6f;
            foreach (var player in PlayerHealth.All)
            {
                if (player == null || !player.IsSpawned) continue;
                Vector3 local = DoorwayLocal(player.transform.position);
                bool nearDoorway = Mathf.Abs(local.x) < gate.Size.x * 0.5f + 0.6f && Mathf.Abs(local.z) < doorwayReach
                    && local.y > -gate.Size.y && local.y < gate.Size.y * 0.5f;
                ulong id = player.OwnerClientId;
                if (!nearDoorway) { lastSide.Remove(id); continue; }
                if (Mathf.Abs(local.z) < clear) continue; // under the gate: not on either side yet
                float side = Mathf.Sign(local.z);
                if (lastSide.TryGetValue(id, out float before) && before != side)
                {
                    ServerSeal();
                    return;
                }
                lastSide[id] = side;
            }
        }

        Vector3 DoorwayLocal(Vector3 point) => Quaternion.Inverse(gate.transform.rotation) * (point - gate.ClosedPosition);

        /// <summary>Server: shut the gate for good now (the doorway check calls it; also for testing).</summary>
        public void ServerSeal()
        {
            if (!IsServer || IsSealed) return;
            sealedAt.Value = Now;
            if (gate != null) gate.ServerSeal(slamSpeed);
            if (gateLever != null)
            {
                gateLever.ServerForceRelease();
                if (gateLever.TryGetComponent(out BeamSwitch lever)) lever.MarkDone();
            }
        }

        // ---------- Messages ----------

        void OnGUI()
        {
            if (!IsSpawned || CutsceneSystem.HidesGameplayUI) return;
            string text = null;
            if (Recent(doorsAt.Value)) text = doorsMessage;
            else if (Recent(exitAt.Value)) text = exitMessage;
            else if (Recent(sealedAt.Value)) text = OnLeverSide() ? sealedLeverSide : sealedOtherSide;
            if (string.IsNullOrEmpty(text)) return;

            messageStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 24, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            var content = new GUIContent(text);
            float width = Mathf.Min(1000f, Screen.width - 40f);
            float height = Mathf.Max(48f, messageStyle.CalcHeight(content, width) + 10f);
            GUI.Box(new Rect((Screen.width - width) * 0.5f, Screen.height * 0.16f, width, height), content, messageStyle);
        }

        bool Recent(double at) => at >= 0.0 && Now - at >= 0.0 && Now - at < messageTime;

        bool OnLeverSide()
        {
            var me = PlayerHealth.Local;
            if (me == null || gate == null || gateLever == null) return true;
            return Mathf.Sign(DoorwayLocal(me.transform.position).z) == Mathf.Sign(DoorwayLocal(gateLever.transform.position).z);
        }

        /// <summary>Editor setup.</summary>
        public void Setup(HoldGate splitGate, Graspable splitLever, Transform enter, Transform exit, Transform[] instructions,
            BeamSwitch[] levers, BeamSwitch button)
        {
            gate = splitGate;
            gateLever = splitLever;
            enterDoor = enter;
            exitDoor = exit;
            instructionDoors = instructions;
            mazeLevers = levers;
            doorButton = button;
        }
    }
}
