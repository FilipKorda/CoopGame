using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace CoopGame
{
    /// <summary>
    /// A sliding-block puzzle on a grid of cells. Every block slides along its long side only (long in Z: forward /
    /// back, long in X: left / right), one cell at a time, and never into another block or off the board. Blocks
    /// are moved with the wrench beam: catch one (a Fixed <see cref="Graspable"/> on it) and push the way it goes.
    /// When the key block reaches its target the door opens and the key block rides out to the exit point.
    /// The server keeps each block's offset (in cells); every machine slides the blocks to it.
    /// </summary>
    public class SlidePuzzle : NetworkBehaviour
    {
        [SerializeField] Transform[] blocks;
        [Tooltip("Index of the block that has to reach the target.")]
        [SerializeField] int keyBlock;
        [Tooltip("Where the key block has to stand (same size as it).")]
        [SerializeField] Transform target;
        [SerializeField] Transform door;
        [SerializeField] float doorLift = 4.2f;
        [Tooltip("Where the key block goes once it's out.")]
        [SerializeField] Transform exitPoint;
        [SerializeField] float cellSize = 2f;
        [SerializeField] float slideSpeed = 6f;
        [Tooltip("How hard the holder must push along the block's way to move it a cell.")]
        [SerializeField, Range(0.1f, 1f)] float pushThreshold = 0.5f;

        public UnityEvent onSolvedServer = new();

        NetworkList<int> offsets;
        readonly NetworkVariable<bool> solved = new();

        Vector3[] rest; Vector3[] axis; Vector2Int[] cellMin, cellMax; Graspable[] grips;
        Vector3 origin, doorRest; Vector2Int boardSize;
        float[] nextMove;

        public bool IsSolved => solved.Value;
        public int Offset(int i) => i < offsets.Count ? offsets[i] : 0;
        public int BlockCount => blocks.Length;

        void Awake()
        {
            offsets = new NetworkList<int>();
            int n = blocks.Length;
            rest = new Vector3[n]; axis = new Vector3[n]; cellMin = new Vector2Int[n]; cellMax = new Vector2Int[n]; grips = new Graspable[n]; nextMove = new float[n];
            var board = BoundsOf(target);
            for (int i = 0; i < n; i++) if (InPlay(i)) board.Encapsulate(BoundsOf(blocks[i]));
            origin = board.min;
            boardSize = new Vector2Int(Mathf.RoundToInt(board.size.x / cellSize), Mathf.RoundToInt(board.size.z / cellSize));
            for (int i = 0; i < n; i++)
            {
                rest[i] = blocks[i].position;
                var b = BoundsOf(blocks[i]);
                cellMin[i] = Cell(b.min + Vector3.one * 0.1f); cellMax[i] = Cell(b.max - Vector3.one * 0.1f);
                axis[i] = b.size.x > b.size.z ? Vector3.right : Vector3.forward;
                grips[i] = blocks[i].GetComponent<Graspable>();
            }
            if (door != null) doorRest = door.position;
        }

        // A block switched off in the scene is out of the puzzle.
        bool InPlay(int i) => blocks[i] != null && blocks[i].gameObject.activeInHierarchy;
        static Bounds BoundsOf(Transform t) { var c = t.GetComponent<Collider>(); var r = t.GetComponent<Renderer>(); return c != null && c.enabled ? c.bounds : r != null ? r.bounds : new Bounds(t.position, Vector3.one); }
        Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt((p.x - origin.x) / cellSize), Mathf.FloorToInt((p.z - origin.z) / cellSize));
        Vector2Int Step(int i) => axis[i] == Vector3.right ? new Vector2Int(1, 0) : new Vector2Int(0, 1);

        public override void OnNetworkSpawn()
        {
            if (IsServer && offsets.Count != blocks.Length) { offsets.Clear(); for (int i = 0; i < blocks.Length; i++) offsets.Add(0); }
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();
            float step = slideSpeed * Time.deltaTime;
            for (int i = 0; i < blocks.Length; i++)
            {
                Vector3 want = solved.Value && i == keyBlock && exitPoint != null ? new Vector3(exitPoint.position.x, rest[i].y, exitPoint.position.z) : rest[i] + axis[i] * (Offset(i) * cellSize);
                blocks[i].position = Vector3.MoveTowards(blocks[i].position, want, step);
            }
            if (door != null) door.position = Vector3.MoveTowards(door.position, doorRest + Vector3.up * (solved.Value ? doorLift : 0f), step);
        }

        // ---------- Server ----------

        void ServerTick()
        {
            if (offsets.Count != blocks.Length) return;
            for (int i = 0; i < blocks.Length; i++)
            {
                var grip = grips[i];
                if (grip == null || !grip.IsHeld) continue;
                if (solved.Value && i == keyBlock) { grip.ServerForceRelease(); continue; }
                if (Time.time < nextMove[i]) continue;
                float push = Vector3.Dot(grip.SteerWorld, axis[i]);
                if (Mathf.Abs(push) < pushThreshold) continue;
                ServerMove(i, push > 0f ? 1 : -1);
            }
        }

        /// <summary>Server: slide block <paramref name="i"/> one cell (+1 along its way, -1 back). False if it's blocked.</summary>
        public bool ServerMove(int i, int dir)
        {
            if (i < 0 || i >= blocks.Length || (solved.Value && i == keyBlock) || !Free(i, Offset(i) + dir)) return false;
            offsets[i] = Offset(i) + dir;
            nextMove[i] = Time.time + cellSize / slideSpeed + 0.05f;
            if (i == keyBlock && OnTarget()) { solved.Value = true; if (grips[i] != null) grips[i].ServerForceRelease(); onSolvedServer.Invoke(); }
            return true;
        }

        void CellsOf(int i, int offset, out Vector2Int min, out Vector2Int max)
        {
            var d = Step(i) * offset; min = cellMin[i] + d; max = cellMax[i] + d;
        }

        bool Free(int i, int offset)
        {
            CellsOf(i, offset, out var min, out var max);
            if (min.x < 0 || min.y < 0 || max.x >= boardSize.x || max.y >= boardSize.y) return false;
            for (int j = 0; j < blocks.Length; j++)
            {
                if (j == i || !InPlay(j) || (solved.Value && j == keyBlock)) continue;
                CellsOf(j, Offset(j), out var jMin, out var jMax);
                if (min.x <= jMax.x && max.x >= jMin.x && min.y <= jMax.y && max.y >= jMin.y) return false;
            }
            return true;
        }

        bool OnTarget()
        {
            var t = BoundsOf(target);
            CellsOf(keyBlock, Offset(keyBlock), out var min, out var max);
            return min == Cell(t.min + Vector3.one * 0.1f) && max == Cell(t.max - Vector3.one * 0.1f);
        }

        public void Setup(Transform[] puzzleBlocks, int key, Transform keyTarget, Transform puzzleDoor, Transform exit)
        {
            blocks = puzzleBlocks; keyBlock = key; target = keyTarget; door = puzzleDoor; exitPoint = exit;
        }
    }
}
