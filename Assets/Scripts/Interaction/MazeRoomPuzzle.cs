using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// The maze room (A2, Hidden Room 1), for two players. Three <see cref="MazeBoard"/>s (two on the walls, one under the
    /// ceiling) and the <see cref="DockSphere"/>, which both players roll between four docks. The dock the sphere lies
    /// in decides which coloured bolts in the mazes are open: dock N opens the way through maze N (its bolts have dock
    /// N's colour) while the other mazes stay shut. Both wall mazes done opens the ceiling hatch over the last maze.
    /// Every pawn on its goal plus the sphere in the last dock solves the room: the sphere locks for good and the
    /// <see cref="AntennaPanel"/> waiting for this puzzle switches on.
    /// The server decides; the times things happened are shared and every machine animates from them.
    /// </summary>
    public class MazeRoomPuzzle : NetworkBehaviour
    {
        [Tooltip("Maze N is element N-1; its bolts open while the sphere lies in dock N.")]
        [SerializeField] MazeBoard[] mazes;
        [SerializeField] DockSphere sphere;
        [Tooltip("Dock (1-based) the sphere has to lie in, with every maze done, to solve the room.")]
        [SerializeField] int finalDock = 4;
        [Tooltip("Maze (1-based) hidden behind the ceiling hatch; the hatch opens once every other maze is done.")]
        [SerializeField] int hatchedMaze = 3;

        [Header("Ceiling hatch")]
        [Tooltip("Leaves hinged on their local Z axis (pivot on the hinge); they swing by Leaf Angles when the hatch opens.")]
        [SerializeField] Transform[] hatchLeaves;
        [SerializeField] float[] leafAngles = { -100f, 100f };
        [SerializeField] float hatchTime = 3f;

        [Header("Signals (red = not yet, green = done)")]
        [Tooltip("Signal of maze N is element N-1.")]
        [SerializeField] Renderer[] mazeSignals;
        [Tooltip("Green while the sphere lies in the final dock.")]
        [SerializeField] Renderer sphereSignal;

        [Header("Palette")]
        [Tooltip("Bright glow per dock colour (element 0 = dock 1).")]
        [SerializeField] Material[] dockGlow;
        [Tooltip("Dim glow per dock colour (element 0 = dock 1).")]
        [SerializeField] Material[] dockDim;
        [SerializeField] Material redGlow;
        [SerializeField] Material greenGlow;
        [SerializeField] Material offMaterial;

        [Header("Messages")]
        [SerializeField] float messageTime = 5f;
        [SerializeField] string hatchMessage = "Oba labirynty na ścianach zaliczone — sufit się otwiera!";
        [SerializeField] string solvedMessage = "Zagadka rozwiązana — panel anteny włączony!";

        readonly NetworkVariable<double> hatchOpenedAt = new(-1.0);
        readonly NetworkVariable<double> solvedAt = new(-1.0);

        Quaternion[] leafClosed;
        GUIStyle messageStyle;

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;

        public bool IsSolved => IsSpawned && solvedAt.Value >= 0.0;
        public bool IsHatchOpen => IsSpawned && hatchOpenedAt.Value >= 0.0 && Now - hatchOpenedAt.Value >= hatchTime * 0.7f;
        /// <summary>Dock (1-based) the sphere lies in, 0 while it's between docks.</summary>
        public int ActiveDock => sphere != null && sphere.IsSpawned ? sphere.DockNumber : 0;
        public int FinalDock => finalDock;

        public int MazesDone
        {
            get
            {
                int done = 0;
                if (mazes != null)
                    foreach (var maze in mazes)
                        if (maze != null && maze.IsSolved) done++;
                return done;
            }
        }

        public int MazeCount => mazes != null ? mazes.Length : 0;

        /// <summary>Short state for the locked panel's screen.</summary>
        public string ProgressText =>
            $"labirynty {MazesDone}/{MazeCount}, kula {(ActiveDock == finalDock ? "OK" : "→ dock " + finalDock)}";

        /// <summary>Can maze <paramref name="number"/> be played now (the hatched one only once its hatch is open)?</summary>
        public bool MazeAvailable(int number) => number != hatchedMaze || IsHatchOpen;

        public Material DockGlow(int dock) => Pick(dockGlow, dock);
        public Material DockDim(int dock) => Pick(dockDim, dock);
        public Material RedGlow => redGlow;
        public Material GreenGlow => greenGlow;
        public Material OffMaterial => offMaterial;

        static Material Pick(Material[] list, int dock) =>
            list != null && dock >= 1 && dock <= list.Length ? list[dock - 1] : null;

        void Awake()
        {
            int count = hatchLeaves != null ? hatchLeaves.Length : 0;
            leafClosed = new Quaternion[count];
            for (int i = 0; i < count; i++)
                if (hatchLeaves[i] != null) leafClosed[i] = hatchLeaves[i].localRotation;
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();
            UpdateHatch();
            UpdateSignals();
        }

        void ServerTick()
        {
            if (hatchOpenedAt.Value < 0.0 && OthersDone(hatchedMaze)) hatchOpenedAt.Value = Now;

            if (solvedAt.Value < 0.0 && MazeCount > 0 && MazesDone == MazeCount && sphere != null && sphere.DockNumber == finalDock)
            {
                solvedAt.Value = Now;
                sphere.ServerLock();
            }
        }

        bool OthersDone(int except)
        {
            if (mazes == null) return false;
            for (int i = 0; i < mazes.Length; i++)
            {
                if (i + 1 == except) continue;
                if (mazes[i] == null || !mazes[i].IsSolved) return false;
            }
            return true;
        }

        void UpdateHatch()
        {
            if (hatchLeaves == null || hatchOpenedAt.Value < 0.0) return;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)(Now - hatchOpenedAt.Value) / Mathf.Max(0.01f, hatchTime)));
            for (int i = 0; i < hatchLeaves.Length; i++)
            {
                if (hatchLeaves[i] == null) continue;
                float angle = leafAngles != null && i < leafAngles.Length ? leafAngles[i] : 90f;
                var target = leafClosed[i] * Quaternion.AngleAxis(angle * t, Vector3.forward);
                if (hatchLeaves[i].localRotation != target) hatchLeaves[i].localRotation = target;
            }
        }

        void UpdateSignals()
        {
            if (mazeSignals != null && mazes != null)
                for (int i = 0; i < mazeSignals.Length && i < mazes.Length; i++)
                    SetMaterial(mazeSignals[i], mazes[i] != null && mazes[i].IsSolved ? greenGlow : redGlow);
            SetMaterial(sphereSignal, ActiveDock == finalDock ? greenGlow : redGlow);
        }

        /// <summary>Swaps a renderer's material only when it changes (every client).</summary>
        public static void SetMaterial(Renderer renderer, Material material)
        {
            if (renderer != null && material != null && renderer.sharedMaterial != material) renderer.sharedMaterial = material;
        }

        // ---------- Messages ----------

        void OnGUI()
        {
            if (!IsSpawned || CutsceneSystem.HidesGameplayUI) return;
            string text = null;
            double latest = -1.0;
            Consider(solvedAt.Value, solvedMessage, ref text, ref latest);
            Consider(hatchOpenedAt.Value, hatchMessage, ref text, ref latest);
            if (mazes != null)
                for (int i = 0; i < mazes.Length; i++)
                    if (mazes[i] != null) Consider(mazes[i].SolvedAt, $"Labirynt {i + 1} zaliczony! Pionek zostaje na mecie.", ref text, ref latest);
            if (string.IsNullOrEmpty(text)) return;

            messageStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 24, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            var content = new GUIContent(text);
            float width = Mathf.Min(1000f, Screen.width - 40f);
            float height = Mathf.Max(48f, messageStyle.CalcHeight(content, width) + 10f);
            GUI.Box(new Rect((Screen.width - width) * 0.5f, Screen.height * 0.16f, width, height), content, messageStyle);
        }

        void Consider(double at, string message, ref string text, ref double latest)
        {
            if (at < 0.0 || at <= latest || Now - at < 0.0 || Now - at > messageTime) return;
            latest = at;
            text = message;
        }
    }
}
