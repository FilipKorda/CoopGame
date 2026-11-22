using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>A sliding bolt on one edge of a <see cref="MazeBoard"/>'s grid.</summary>
    [System.Serializable]
    public class MazeBolt
    {
        [Tooltip("Cell (row * columns + column) whose edge it closes.")]
        public int cell;
        [Tooltip("0 = the edge on the cell's +column side, 1 = the edge on its +row side.")]
        public int side;
        [Tooltip("Dock (1-based): the bolt is drawn in while the sphere lies in this dock.")]
        public int dock;
        [Tooltip("The bar; it sits out of the board when closed and slides into it (against Face Normal) when open.")]
        public Transform bar;
    }

    /// <summary>
    /// One maze of the <see cref="MazeRoomPuzzle"/>: a grid of cells on a board with fixed walls and coloured sliding
    /// bolts. A bolt is drawn in (open) only while the sphere lies in the dock of its colour; otherwise it sticks out
    /// and closes its edge. The way from start to goal runs through bolts of this maze's own dock colour, so the maze
    /// can only be solved while the sphere lies in its dock.
    /// The pawn has two handles for the wrench beam: UP-DOWN moves it along the rows, LEFT-RIGHT along the columns,
    /// one cell at a time (holding keeps it going). It only moves while both handles are held, so it takes two players.
    /// On the goal the maze is done for good: the pawn stays there, its handles can't be grabbed any more and the
    /// bolts draw in. The server moves the pawn and shares its cell; every machine animates the pawn and the bolts.
    /// </summary>
    public class MazeBoard : NetworkBehaviour
    {
        [Tooltip("Maze number = the dock (1-based) whose sphere opens its way.")]
        [SerializeField] int number = 1;
        [SerializeField] MazeRoomPuzzle room;

        [Header("Grid (in this object's local space, scale 1)")]
        [SerializeField] int columns = 15;
        [SerializeField] int rows = 6;
        [SerializeField] float cellSize = 0.6f;
        [Tooltip("Centre of the grid on the board's front face.")]
        [SerializeField] Vector3 gridCentre;
        [Tooltip("Columns count up along this; the LEFT-RIGHT handle moves the pawn along it.")]
        [SerializeField] Vector3 columnAxis = Vector3.right;
        [Tooltip("Rows count up along this; the UP-DOWN handle moves the pawn along it.")]
        [SerializeField] Vector3 rowAxis = Vector3.down;
        [Tooltip("Out of the board, into the room.")]
        [SerializeField] Vector3 faceNormal = Vector3.back;
        [SerializeField] int startCell;
        [SerializeField] int goalCell;
        [Tooltip("Per cell (row * columns + column): bit 1 = fixed wall on its +column side, bit 2 = on its +row side. " +
                 "The board's edge is always a wall. Bolted edges are not walls here.")]
        [SerializeField] byte[] walls;
        [SerializeField] MazeBolt[] bolts;
        [Tooltip("How far a bolt slides into the board when it opens (metres).")]
        [SerializeField] float boltTravel = 0.12f;
        [SerializeField] float boltTime = 0.45f;

        [Header("Pawn")]
        [SerializeField] Transform pawn;
        [Tooltip("Pawn centre's distance out of the front face.")]
        [SerializeField] float pawnLift = 0.05f;
        [SerializeField] Graspable upDownHandle;
        [SerializeField] Graspable leftRightHandle;
        [Tooltip("Seconds between steps while a handle is pushed.")]
        [SerializeField] float stepInterval = 0.22f;
        [Tooltip("How hard the holder must push along the handle's way to move a cell.")]
        [SerializeField, Range(0.1f, 1f)] float pushThreshold = 0.5f;

        [Header("Visuals")]
        [Tooltip("Frame lights: the maze's colour = open, red = shut, green = done, off = hidden.")]
        [SerializeField] Renderer[] statusLights;
        [SerializeField] Renderer pawnGlow;
        [SerializeField] Renderer goalGlow;

        readonly NetworkVariable<int> cell = new();
        readonly NetworkVariable<double> solvedAt = new(-1.0);
        // Pawn bumped into a closed edge: count * 4 + direction (0 +col, 1 -col, 2 +row, 3 -row).
        readonly NetworkVariable<int> bump = new();

        // Server
        float nextStep;
        bool lastMovedColumn;

        // Every machine
        Vector3[] boltClosed;
        Targetable upDownTarget, leftRightTarget;
        bool placed;
        Vector3 pawnBase;
        float bumpStart = -10f;
        Vector3 bumpDirection;
        GUIStyle hintStyle;

        public int Number => number;
        public bool IsSolved => IsSpawned && solvedAt.Value >= 0.0;
        public double SolvedAt => IsSpawned ? solvedAt.Value : -1.0;
        public int Cell => cell.Value;
        public int GoalCell => goalCell;
        public int Columns => columns;
        public int Rows => rows;
        public Graspable UpDownHandle => upDownHandle;
        public Graspable LeftRightHandle => leftRightHandle;

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;

        bool Usable => IsSpawned && !IsSolved && room != null && room.MazeAvailable(number);

        void Awake()
        {
            int count = bolts != null ? bolts.Length : 0;
            boltClosed = new Vector3[count];
            for (int i = 0; i < count; i++)
                if (bolts[i] != null && bolts[i].bar != null) boltClosed[i] = bolts[i].bar.localPosition;
            if (upDownHandle != null) upDownTarget = upDownHandle.GetComponent<Targetable>();
            if (leftRightHandle != null) leftRightTarget = leftRightHandle.GetComponent<Targetable>();
            if (pawn != null) pawnBase = pawn.localPosition;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && solvedAt.Value < 0.0) cell.Value = startCell;
            bump.OnValueChanged += OnBump;
            placed = false;
        }

        public override void OnNetworkDespawn() => bump.OnValueChanged -= OnBump;

        // ---------- Grid ----------

        public Vector3 CellLocal(int index)
        {
            int column = index % columns, row = index / columns;
            return gridCentre
                   + columnAxis * ((column - (columns - 1) * 0.5f) * cellSize)
                   + rowAxis * ((row - (rows - 1) * 0.5f) * cellSize);
        }

        /// <summary>Can the pawn go from cell <paramref name="from"/> one cell along columns (dColumn) or rows (dRow)?</summary>
        public bool CanStep(int from, int dColumn, int dRow)
        {
            int column = from % columns + dColumn, row = from / columns + dRow;
            if (column < 0 || column >= columns || row < 0 || row >= rows) return false;
            int to = row * columns + column;
            int edgeCell = dColumn > 0 || dRow > 0 ? from : to;
            int side = dColumn != 0 ? 0 : 1;
            if (walls != null && edgeCell < walls.Length && (walls[edgeCell] & (side == 0 ? 1 : 2)) != 0) return false;
            if (bolts != null)
                foreach (var bolt in bolts)
                    if (bolt != null && bolt.cell == edgeCell && bolt.side == side && !BoltOpen(bolt)) return false;
            return true;
        }

        bool BoltOpen(MazeBolt bolt) => IsSolved || (room != null && room.ActiveDock == bolt.dock);

        // ---------- Server ----------

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();
            UpdateHandles();
            UpdateVisuals();
        }

        void ServerTick()
        {
            if (IsSolved) return;
            if (!Usable)
            {
                if (upDownHandle != null) upDownHandle.ServerForceRelease();
                if (leftRightHandle != null) leftRightHandle.ServerForceRelease();
                return;
            }
            if (upDownHandle == null || leftRightHandle == null || !upDownHandle.IsHeld || !leftRightHandle.IsHeld) return;
            if (Time.time < nextStep) return;

            int dColumn = Direction(Push(leftRightHandle, columnAxis));
            int dRow = Direction(Push(upDownHandle, rowAxis));
            if (dColumn == 0 && dRow == 0) return;

            // One step per tick: both pushed at once take turns, so the pawn never cuts a corner.
            bool columnFirst = dRow == 0 || (dColumn != 0 && !lastMovedColumn);
            int from = cell.Value;
            int to = -1;
            if (columnFirst && dColumn != 0 && CanStep(from, dColumn, 0)) { to = from + dColumn; lastMovedColumn = true; }
            else if (dRow != 0 && CanStep(from, 0, dRow)) { to = from + dRow * columns; lastMovedColumn = false; }
            else if (!columnFirst && dColumn != 0 && CanStep(from, dColumn, 0)) { to = from + dColumn; lastMovedColumn = true; }

            nextStep = Time.time + stepInterval;
            if (to < 0)
            {
                int direction = dColumn != 0 ? (dColumn > 0 ? 0 : 1) : (dRow > 0 ? 2 : 3);
                bump.Value = (bump.Value / 4 + 1) * 4 + direction;
                return;
            }
            cell.Value = to;
            if (to == goalCell) ServerSolve();
        }

        int Direction(float push) => Mathf.Abs(push) >= pushThreshold ? (push > 0f ? 1 : -1) : 0;

        /// <summary>
        /// How hard the holder pushes along a board axis: a vertical axis takes W/S as up/down; a level one (the
        /// ceiling, or left-right on a wall) takes the move keys relative to the holder's camera, like walking.
        /// </summary>
        float Push(Graspable handle, Vector3 localAxis)
        {
            Vector3 axis = transform.TransformDirection(localAxis).normalized;
            if (Mathf.Abs(axis.y) > 0.5f) return handle.SteerInput.y * Mathf.Sign(axis.y);
            Vector3 flat = Vector3.ProjectOnPlane(axis, Vector3.up).normalized;
            return Vector3.Dot(handle.SteerWorld, flat);
        }

        void ServerSolve()
        {
            if (!IsServer || IsSolved) return;
            solvedAt.Value = Now;
            if (upDownHandle != null) upDownHandle.ServerForceRelease();
            if (leftRightHandle != null) leftRightHandle.ServerForceRelease();
        }

        /// <summary>Server, for tests: put the pawn on a cell (solves the maze if it's the goal).</summary>
        public void ServerSetCell(int index)
        {
            if (!IsServer || IsSolved || index < 0 || index >= columns * rows) return;
            cell.Value = index;
            if (index == goalCell) ServerSolve();
        }

        // ---------- Every machine ----------

        void UpdateHandles()
        {
            bool usable = Usable;
            Lock(upDownHandle, upDownTarget, usable);
            Lock(leftRightHandle, leftRightTarget, usable);
        }

        /// <summary>
        /// A done maze's handles are switched off for good (drops the holder). Otherwise a handle someone holds (or one
        /// that can't be used yet) can't be aimed at, so the second player's crosshair picks the free handle.
        /// </summary>
        void Lock(Graspable grip, Targetable target, bool usable)
        {
            if (grip == null) return;
            if (IsSolved && grip.enabled) grip.enabled = false;
            if (target != null)
            {
                bool aimable = usable && grip.enabled && !grip.IsHeld;
                if (target.enabled != aimable) target.enabled = aimable;
            }
        }

        void OnBump(int previous, int current)
        {
            int direction = current & 3;
            bumpDirection = direction switch
            {
                0 => columnAxis,
                1 => -columnAxis,
                2 => rowAxis,
                _ => -rowAxis,
            };
            bumpStart = Time.time;
        }

        void UpdateVisuals()
        {
            bool solved = IsSolved;
            float boltStep = boltTravel / Mathf.Max(0.05f, boltTime) * Time.deltaTime;
            if (bolts != null)
            {
                for (int i = 0; i < bolts.Length; i++)
                {
                    var bolt = bolts[i];
                    if (bolt == null || bolt.bar == null) continue;
                    Vector3 target = boltClosed[i] - (BoltOpen(bolt) ? faceNormal * boltTravel : Vector3.zero);
                    bolt.bar.localPosition = placed ? Vector3.MoveTowards(bolt.bar.localPosition, target, boltStep) : target;
                }
            }

            if (pawn != null)
            {
                Vector3 target = CellLocal(cell.Value) + faceNormal * pawnLift;
                pawnBase = placed ? Vector3.MoveTowards(pawnBase, target, cellSize / Mathf.Max(0.05f, stepInterval) * 1.25f * Time.deltaTime) : target;
                Vector3 offset = Vector3.zero;
                float bumpT = (Time.time - bumpStart) / 0.2f;
                if (bumpT >= 0f && bumpT < 1f) offset += bumpDirection * (0.06f * cellSize * Mathf.Sin(bumpT * Mathf.PI));
                bool straining = !solved && ((upDownHandle != null && upDownHandle.IsHeld) != (leftRightHandle != null && leftRightHandle.IsHeld));
                if (straining) offset += Random.insideUnitSphere * 0.008f;
                pawn.localPosition = pawnBase + offset;
            }
            placed = true;

            if (room == null) return;
            bool active = room.ActiveDock == number;
            Material status = solved ? room.GreenGlow
                : !room.MazeAvailable(number) ? room.OffMaterial
                : active ? room.DockGlow(number) : room.RedGlow;
            if (statusLights != null)
                foreach (var light in statusLights) MazeRoomPuzzle.SetMaterial(light, status);
            MazeRoomPuzzle.SetMaterial(pawnGlow, solved ? room.GreenGlow : active ? room.DockGlow(number) : room.DockDim(number));
            MazeRoomPuzzle.SetMaterial(goalGlow, solved ? room.GreenGlow : room.DockGlow(number));
        }

        // ---------- Hints for the player holding a handle ----------

        void OnGUI()
        {
            if (!IsSpawned || CutsceneSystem.HidesGameplayUI || NetworkManager == null) return;
            ulong me = NetworkManager.LocalClientId;
            bool holdsUpDown = upDownHandle != null && upDownHandle.IsHeld && upDownHandle.HolderClientId == me;
            bool holdsLeftRight = leftRightHandle != null && leftRightHandle.IsHeld && leftRightHandle.HolderClientId == me;
            if (!holdsUpDown && !holdsLeftRight) return;

            bool vertical = Mathf.Abs(transform.TransformDirection(rowAxis).y) > 0.5f;
            string mine = holdsUpDown
                ? (vertical ? "GÓRA-DÓŁ (W / S)" : "GÓRA-DÓŁ (wzdłuż strzałki, W / S)")
                : "LEWO-PRAWO (A / D)";
            Graspable other = holdsUpDown ? leftRightHandle : upDownHandle;
            string state;
            if (room != null && room.ActiveDock != number)
                state = $"Labirynt {number} zamknięty ryglami — połóżcie kulę w Docku {number}.";
            else if (other == null || !other.IsHeld)
                state = $"Drugi gracz musi złapać uchwyt {(holdsUpDown ? "LEWO-PRAWO" : "GÓRA-DÓŁ")} — pionek rusza się tylko, gdy trzymacie go obaj.";
            else
                state = "Doprowadźcie pionek do mety.";

            hintStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            var content = new GUIContent($"Labirynt {number}: trzymasz {mine}\n{state}");
            float width = Mathf.Min(900f, Screen.width - 40f);
            float height = Mathf.Max(56f, hintStyle.CalcHeight(content, width) + 10f);
            GUI.Box(new Rect((Screen.width - width) * 0.5f, Screen.height * 0.74f, width, height), content, hintStyle);
        }
    }
}
