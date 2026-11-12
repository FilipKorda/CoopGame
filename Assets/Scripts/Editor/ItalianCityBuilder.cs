using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Builds the Italian town test scene (StyleTest_City), the town around the A1_Start treasury.
    /// A large cobbled piazza closed on all sides by town houses whose walls are courses of StyleKit prefabs (the same pieces
    /// as the A1 vault walls, tinted terracotta / ochre / Venetian red / beige / pastel orange) with terracotta tile roofs;
    /// the middle house on the north side is the treasury (A1 vault) with a big portal. The houses are the map border
    /// (colliders + tall invisible walls). In the piazza: a StyleKit bell tower, a fountain, cypresses in pots and a civil
    /// field hospital (blue / white tents, beds, IV stands, screens, wheelchairs, waiting chairs). Warm Mediterranean sun, sky, fog and a Volume.
    /// Meshes go to Assets/Meshes/StyleCity. Re-running rebuilds the "Italian City" root; the scene itself is never saved.
    /// </summary>
    public static class ItalianCityBuilder
    {
        public const string ScenePath = "Assets/Scenes/Test Scene/StyleTest_City.unity";
        const string RootName = "Italian City";
        const string MeshDir = "Assets/Meshes/StyleCity";
        const string MatDir = "Assets/Materials/StyleCity";
        const string ProfilePath = "Assets/Settings/StyleTest_City_Volume.asset";

        // Piazza: open ground x ±PX, z ±PZ; houses D deep around it. Ground floor GH tall, upper floors FH.
        const float PX = 36f, PZ = 28f, D = 12f, GH = 4.4f, FH = 3.3f, Pitch = 22f;
        static readonly Vector3 FountainPos = new Vector3(14f, 0f, 3f);

        static Material solid, glow, cobbleMat, skyMat;
        static Transform root;
        static readonly HashSet<string> usedNames = new HashSet<string>();
        static readonly List<(Vector3 c, Vector3 s, Quaternion r, string n)> cols = new List<(Vector3, Vector3, Quaternion, string)>();
        static int boxCount, vertTotal, lightCount;
        static System.Random rnd;
        static MB glowMB;

        // ---------- Palette (alpha = shine) ----------
        static Color C(float r, float g, float b, float a = 0.03f) => new Color(r, g, b, a);
        static readonly Color[] FacadeCols = {
            C(0.80f, 0.46f, 0.31f),   // warm terracotta
            C(0.86f, 0.65f, 0.35f),   // Tuscan ochre
            C(0.64f, 0.24f, 0.18f),   // Venetian red
            C(0.90f, 0.80f, 0.64f),   // warm beige
            C(0.96f, 0.67f, 0.44f),   // pastel orange
            C(0.91f, 0.74f, 0.47f),   // light ochre
            C(0.85f, 0.55f, 0.42f) }; // faded terracotta
        static readonly Color[] BrickCols = { C(0.68f, 0.34f, 0.22f), C(0.60f, 0.29f, 0.19f), C(0.75f, 0.42f, 0.27f), C(0.53f, 0.25f, 0.17f), C(0.65f, 0.38f, 0.26f), C(0.71f, 0.30f, 0.20f) };
        static readonly Color[] TileCols = { C(0.76f, 0.37f, 0.22f), C(0.68f, 0.32f, 0.19f), C(0.82f, 0.45f, 0.27f), C(0.60f, 0.28f, 0.17f), C(0.72f, 0.41f, 0.26f), C(0.79f, 0.34f, 0.20f) };
        static readonly Color[] ShutterCols = { C(0.26f, 0.40f, 0.26f, 0.1f), C(0.20f, 0.36f, 0.36f, 0.1f), C(0.44f, 0.28f, 0.16f, 0.1f), C(0.40f, 0.45f, 0.27f, 0.1f), C(0.33f, 0.42f, 0.48f, 0.1f) };
        static readonly Color[] CypressCols = { C(0.09f, 0.20f, 0.10f, 0.02f), C(0.12f, 0.25f, 0.12f, 0.02f), C(0.07f, 0.17f, 0.09f, 0.02f), C(0.14f, 0.28f, 0.13f, 0.02f) };
        static readonly Color Stone = C(0.88f, 0.83f, 0.72f, 0.05f), StoneD = C(0.74f, 0.68f, 0.58f, 0.05f), StoneL = C(0.93f, 0.90f, 0.82f, 0.05f),
            Mortar = C(0.56f, 0.48f, 0.41f), MortarL = C(0.80f, 0.76f, 0.68f), TileUnder = C(0.47f, 0.22f, 0.14f), TileRidge = C(0.62f, 0.29f, 0.18f),
            Glass = C(0.13f, 0.16f, 0.20f, 0.6f), GlassShop = C(0.22f, 0.29f, 0.31f, 0.7f), Dark = C(0.09f, 0.07f, 0.06f, 0f), Frame = C(0.90f, 0.88f, 0.82f, 0.1f),
            Wood = C(0.46f, 0.28f, 0.15f, 0.1f), WoodD = C(0.31f, 0.18f, 0.10f, 0.1f), Iron = C(0.13f, 0.13f, 0.14f, 0.4f), Pot = C(0.74f, 0.40f, 0.25f),
            Leaf = C(0.24f, 0.44f, 0.20f), LeafD = C(0.16f, 0.32f, 0.15f), FlowerR = C(0.86f, 0.16f, 0.15f), FlowerP = C(0.93f, 0.45f, 0.62f), FlowerW = C(0.96f, 0.94f, 0.88f),
            Canvas = C(0.88f, 0.85f, 0.76f, 0.02f), CanvasD = C(0.76f, 0.72f, 0.62f, 0.02f), Olive = C(0.43f, 0.46f, 0.30f, 0.02f), OliveD = C(0.33f, 0.36f, 0.23f, 0.02f),
            Red = C(0.80f, 0.09f, 0.09f, 0.05f), White = C(0.95f, 0.95f, 0.93f, 0.1f), Metal = C(0.74f, 0.76f, 0.78f, 0.6f), MetalD = C(0.45f, 0.47f, 0.50f, 0.5f),
            Mattress = C(0.80f, 0.86f, 0.90f, 0.02f), BlanketB = C(0.36f, 0.46f, 0.60f, 0.02f), BlanketG = C(0.50f, 0.55f, 0.40f, 0.02f), BlanketGr = C(0.62f, 0.63f, 0.64f, 0.02f),
            IVBag = C(0.76f, 0.88f, 0.95f, 0.5f), OxyGreen = C(0.20f, 0.45f, 0.30f, 0.4f), Sand = C(0.76f, 0.68f, 0.50f, 0.02f), SandD = C(0.66f, 0.58f, 0.42f, 0.02f),
            Water = C(0.24f, 0.52f, 0.60f, 0.8f), WaterL = C(0.62f, 0.86f, 0.94f, 0.8f), Bronze = C(0.55f, 0.40f, 0.22f, 0.85f), Gold = C(0.95f, 0.75f, 0.32f, 1f),
            CobbleCol = C(0.66f, 0.60f, 0.53f, 0.04f), Trunk = C(0.36f, 0.25f, 0.16f), Soil = C(0.30f, 0.21f, 0.14f),
            TentW = C(0.94f, 0.95f, 0.95f, 0.04f), TentB = C(0.16f, 0.36f, 0.66f, 0.06f), FloorG = C(0.66f, 0.68f, 0.70f, 0.04f), BlanketL = C(0.72f, 0.84f, 0.92f, 0.02f),
            RescueO = C(0.95f, 0.48f, 0.14f, 0.1f), BoxBlue = C(0.22f, 0.45f, 0.75f, 0.2f), GenY = C(0.93f, 0.76f, 0.20f, 0.2f);
        static readonly Color GLamp = new Color(1f, 0.78f, 0.45f, 1f), GLampDim = new Color(0.75f, 0.55f, 0.32f, 1f);

        const int SkipBack = 1 << 4, SkipBottom = 1 << 2;

        // ---------- Entry points ----------

        [MenuItem("Tools/Coop Game/Style Test/Create City Scene (StyleTest_City)")]
        public static string CreateScene()
        {
            var cur = EditorSceneManager.GetActiveScene();
            if (cur.path == ScenePath) return Build();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) return "Scene " + EditorSceneManager.GetSceneAt(i).path + " has unsaved changes: save or discard them first.";
            if (System.IO.File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); return Build(); }
            var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(s, ScenePath);   // only creates the empty scene file; the built town stays unsaved
            return "Created empty " + ScenePath + ". " + Build();
        }

        [MenuItem("Tools/Coop Game/Style Test/Build Italian City")]
        public static string Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) return "Open " + ScenePath + " first (active scene is " + scene.path + ").";

            MakeKitMaterials();   // first: creating new material assets can reload the other unsaved ones
            MakeMaterials();
            AssetDatabase.SaveAssets();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build Italian City");
            int group = Undo.GetCurrentGroup();

            var old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            usedNames.Clear(); cols.Clear(); kitCache.Clear(); boxCount = 0; vertTotal = 0; lightCount = 0; kitCount = 0;
            rnd = new System.Random(20261106);
            glowMB = new MB();

            var go = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(go, "Build Italian City");
            root = go.transform;

            BuildGround();
            BuildBuildings();
            BuildCampanile();
            BuildFountain();
            BuildHospital();
            BuildCypresses();
            BuildStreetProps();
            Piece(Group("Props"), "Lantern Glow", glowMB, glow, false);
            BuildColliders();
            string env = BuildEnvironment();

            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<Light>() == null && t.GetComponent<Volume>() == null && t.GetComponent<Camera>() == null) t.gameObject.isStatic = true;

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = go;
            return $"Built '{RootName}': {kitCount} StyleKit pieces, {go.GetComponentsInChildren<MeshRenderer>(true).Length} renderers, {vertTotal} vertices, {cols.Count} colliders, {lightCount} lights. {env}";
        }

        // ---------- Ground: cobbles, travertine border strips and lines ----------

        static void BuildGround()
        {
            var g = Group("Ground");
            var cob = new MB();
            float ex = PX + D + 8f, ez = PZ + D + 8f;
            cob.Box(new Vector3(0, -0.25f, 0), new Vector3(2 * ex, 0.5f, 2 * ez), CobbleCol, Quaternion.identity, SkipBottom);
            Piece(g, "Cobbles", cob, cobbleMat);

            var st = new MB();
            // One plain travertine strip along the house fronts: long equal slabs, one colour, two alternating heights.
            void Strip(Vector3 o, Vector3 along, Vector3 inward, float len)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.4f)); float l = len / n;
                for (int i = 0; i < n; i++)
                {
                    var c = o + along * (l * (i + 0.5f)) + inward * 0.6f + Vector3.up * (i % 2 == 0 ? -0.015f : -0.025f);
                    st.Box(c, Abs(along * (l - 0.03f) + inward * 1.2f) + Vector3.up * 0.08f, Stone, Quaternion.identity, SkipBottom);
                }
            }
            Strip(new Vector3(-PX, 0, -PZ), Vector3.right, Vector3.forward, 2 * PX);
            Strip(new Vector3(-PX, 0, PZ), Vector3.right, Vector3.back, 2 * PX);
            Strip(new Vector3(-PX, 0, -PZ + 1.2f), Vector3.forward, Vector3.right, 2 * PZ - 2.4f);
            Strip(new Vector3(PX, 0, -PZ + 1.2f), Vector3.forward, Vector3.left, 2 * PZ - 2.4f);
            Piece(g, "Travertine Slabs", st, solid);
            cols.Add((new Vector3(0, -0.25f, 0), new Vector3(2 * ex, 0.5f, 2 * ez), Quaternion.identity, "Ground"));
        }

        // ---------- Town houses built from StyleKit prefabs ----------
        // Facades are laid like the A1_Start vault walls: courses of StyleKit stone rows in front of a dark mortar body
        // (Kit_Plinth 1.2 m, Kit_Wall 0.8 m, Kit_Thin 0.5 m, Kit_Edge 1.0 m). The pieces stay prefab instances; each house
        // gets a tinted copy of Style_Props for the Italian palette (the treasury keeps the original A1 material).

        const string KitDir = "Assets/Prefabs/StyleKit/";
        const float GroundTop = 5.4f, FloorH = 3.7f, CorniceH = 1.0f;
        static readonly Dictionary<string, GameObject> kitCache = new Dictionary<string, GameObject>();
        static readonly int[] KitLens = { 160, 80, 40, 20, 10, 9, 8, 7, 6, 5, 4, 3 };
        static readonly Color KitMortar = C(0.21f, 0.15f, 0.12f, 0f), KitGable = C(0.52f, 0.34f, 0.22f, 0.03f);
        static Material kitBase;
        static Material[] kitMats;
        static int kitCount;

        struct Rc { public float x0, x1, y0, y1; public Rc(float a, float b, float c, float d) { x0 = a; x1 = b; y0 = c; y1 = d; } }

        /// <summary>One course of the facade: kit family, bottom y, height; floor -1 = ground floor; slot = course index in its floor.</summary>
        struct Row { public string fam; public float y, h; public int floor, slot; }

        class HStyle { public int floors; public Material mat; public int[] ground; public Color shutter; public bool treasury; public float tunnel; }

        // Walk-through tunnel in East House 1, in line with the Coop_LeverGate placed in the scene (gate centre z = -14.95, 3 m wide, 3.5 m tall).
        // House frame x runs along +z from z = -PZ, so x = z + PZ.
        const float TunnelCX = 13.05f, TunnelHalf = 1.45f, TunnelH = 3.6f;
        static float TunnelZ0 => -PZ + TunnelCX - TunnelHalf;
        static float TunnelZ1 => -PZ + TunnelCX + TunnelHalf;

        static float HouseTop(int floors) => GroundTop + floors * FloorH + CorniceH;
        static float R1(float x) => Mathf.Round(x * 10f) / 10f;

        static List<Row> Courses(int floors)
        {
            var l = new List<Row> { new Row { fam = "Plinth", y = 0f, h = 1.2f, floor = -1, slot = 0 } };
            for (int k = 0; k < 4; k++) l.Add(new Row { fam = "Wall", y = 1.2f + 0.8f * k, h = 0.8f, floor = -1, slot = k + 1 });
            l.Add(new Row { fam = "Edge", y = 4.4f, h = 1.0f, floor = -1, slot = 5 });
            for (int f = 0; f < floors; f++)
            {
                float b = GroundTop + f * FloorH;
                for (int k = 0; k < 4; k++) l.Add(new Row { fam = "Wall", y = b + 0.8f * k, h = 0.8f, floor = f, slot = k });
                l.Add(new Row { fam = "Thin", y = b + 3.2f, h = 0.5f, floor = f, slot = 4 });
            }
            l.Add(new Row { fam = "Edge", y = GroundTop + floors * FloorH, h = CorniceH, floor = floors, slot = 9 });
            return l;
        }

        static void MakeKitMaterials()
        {
            kitBase = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Stylized/Style_Props.mat");
            var tints = new (string n, Color c)[] {
                ("Terracotta", new Color(0.80f, 0.62f, 0.55f)), ("Ochre", new Color(0.86f, 0.76f, 0.54f)), ("VenetianRed", new Color(0.74f, 0.50f, 0.48f)),
                ("Beige", new Color(0.95f, 0.88f, 0.78f)), ("PastelOrange", new Color(0.92f, 0.70f, 0.55f)), ("Natural", new Color(0.78f, 0.74f, 0.70f)) };
            kitMats = new Material[tints.Length];
            for (int i = 0; i < tints.Length; i++)
            {
                kitMats[i] = CopyMat("City_Kit_" + tints[i].n, kitBase);
                kitMats[i].SetColor("_BaseColor", tints[i].c);
                EditorUtility.SetDirty(kitMats[i]);
            }
        }

        const string KitFlatDir = "Assets/Prefabs/StyleKitFlat/";
        const double FlatShare = 0.6;   // share of pieces taken from StyleKitFlat (flush stones) to keep the facades calm

        static GameObject KitPrefab(string fam, int len10, bool flat)
        {
            string ls = len10 % 10 == 0 ? (len10 / 10) + "m" : len10 < 10 ? "0_" + len10 + "m" : (len10 / 10) + "_" + (len10 % 10) + "m";
            int v = rnd.Next(2);
            string key = fam + ls + "_" + v + (flat ? "F" : "");
            if (kitCache.TryGetValue(key, out var pf)) return pf;
            string b = "Kit_" + fam + "_" + ls + "_";
            if (flat) pf = AssetDatabase.LoadAssetAtPath<GameObject>(KitFlatDir + b + v + "_FlatAll.prefab") ?? AssetDatabase.LoadAssetAtPath<GameObject>(KitFlatDir + b + "0_FlatAll.prefab");
            pf = pf ?? AssetDatabase.LoadAssetAtPath<GameObject>(KitDir + b + v + ".prefab") ?? AssetDatabase.LoadAssetAtPath<GameObject>(KitDir + b + "0.prefab");
            kitCache[key] = pf;
            return pf;
        }

        /// <summary>Splits a length (in 10 cm units) into kit piece lengths; no remainder under 30 cm is ever left.</summary>
        static List<int> SplitKit(int len10, int max10)
        {
            var res = new List<int>(); int rem = len10;
            while (rem >= 3)
            {
                bool ok = false;
                foreach (var p in KitLens)
                {
                    if (p > max10 || p > rem) continue;
                    int r = rem - p; if (r != 0 && r < 3) continue;
                    res.Add(p); rem = r; ok = true; break;
                }
                if (!ok) break;
            }
            for (int i = res.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (res[i], res[j]) = (res[j], res[i]); }
            return res;
        }

        /// <summary>Lays one course of kit pieces along the wall frame from x0 to x1 at height y. Pieces touching an
        /// opening (flatStart / flatEnd) are always flush so no stone sticks out over a window or door.</summary>
        static bool forceFlat;   // true while building a prefab that must use flush StyleKit pieces only (no protruding stones)
        public static bool noAwnings;   // true: shops get no striped awning (the colour draw still runs so the rest of the facade stays the same)
        public static bool exactTop;   // true: flat BlockHouses get a flush stone plate inside the size instead of the raised terrace and coping (walkable tops of gameplay towers, name suffix _XT)

        static void KitRow(Transform parent, WF w, string fam, float x0, float x1, float y, Material mat, bool flatStart = false, bool flatEnd = false, float flatShare = -1f)
        {
            int n = Mathf.RoundToInt((x1 - x0) * 10f); if (n < 3) return;
            int max = fam == "Wall" || fam == "Plinth" ? 160 : 80;
            var parts = SplitKit(n, max);
            float x = x0;
            for (int i = 0; i < parts.Count; i++)
            {
                int l = parts[i]; float L = l / 10f;
                bool flat = forceFlat || (i == 0 && flatStart) || (i == parts.Count - 1 && flatEnd) || rnd.NextDouble() < (flatShare < 0f ? FlatShare : flatShare);
                var pf = KitPrefab(fam, l, flat);
                if (pf != null)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
                    go.transform.SetPositionAndRotation(w.P(x + L / 2f, y, 0f), w.R);
                    if (mat != null && mat != kitBase) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    kitCount++;
                }
                x += L;
            }
        }

        /// <summary>Wall intervals of [a,b] left between the openings (openings as x0,x1 pairs).</summary>
        static List<Vector2> Gaps(float a, float b, List<Vector2> open)
        {
            open.Sort((p, q) => p.x.CompareTo(q.x));
            var r = new List<Vector2>(); float x = a;
            foreach (var o in open) { if (o.x > x + 0.01f) r.Add(new Vector2(x, o.x)); x = Mathf.Max(x, o.y); }
            if (b > x + 0.01f) r.Add(new Vector2(x, b));
            return r;
        }

        static void BuildBuildings()
        {
            var g = Group("Buildings");
            var sides = new (string name, Vector3 o, Vector3 n, float[] widths)[] {
                ("South", new Vector3(-PX, 0, -PZ), Vector3.forward, new[] { 18f, 16f, 20f, 18f }),
                ("North", new Vector3(PX, 0, PZ), Vector3.back, new[] { 22f, 28f, 22f }),
                ("West", new Vector3(-PX, 0, PZ), Vector3.right, new[] { 18f, 20f, 18f }),
                ("East", new Vector3(PX, 0, -PZ), Vector3.left, new[] { 18f, 20f, 18f }) };
            var floorsOf = new Dictionary<string, int[]> {
                { "South", new[] { 3, 2, 4, 3 } }, { "North", new[] { 4, 3, 4 } }, { "West", new[] { 2, 4, 3 } }, { "East", new[] { 4, 2, 3 } } };
            int lastMat = -1;
            var endTops = new Dictionary<string, (float first, float last)>();
            foreach (var s in sides)
            {
                var u = Quaternion.LookRotation(s.n) * Vector3.right;
                var fl = floorsOf[s.name];
                float t = 0;
                for (int i = 0; i < s.widths.Length; i++)
                {
                    float W = s.widths[i];
                    var st = new HStyle { floors = fl[i], shutter = ShutterCols[rnd.Next(ShutterCols.Length)] };
                    st.treasury = s.name == "North" && i == 1;
                    if (s.name == "East" && i == 0) st.tunnel = TunnelCX;   // passage closed by the Coop_LeverGate
                    if (st.treasury) st.mat = kitMats[5];
                    else { int m; do m = rnd.Next(5); while (m == lastMat); lastMat = m; st.mat = kitMats[m]; }
                    float left = i > 0 ? HouseTop(fl[i - 1]) : 0f, right = i < fl.Length - 1 ? HouseTop(fl[i + 1]) : 0f;
                    // Ends of a side meet the corner blocks, which are as tall as the taller neighbour: nothing to clad there.
                    if (i == 0) left = 99f; if (i == fl.Length - 1) right = 99f;
                    string name = st.treasury ? "North Treasury (A1 Vault)" : $"{s.name} House {i + 1}";
                    KitHouse(g, name, new WF(s.o + u * t, s.n), W, D, st, left, right);
                    t += W;
                }
                endTops[s.name] = (HouseTop(fl[0]), HouseTop(fl[fl.Length - 1]));
            }
            // Corner blocks fill the squares behind the piazza corners; only their roofs and upper walls show.
            var corners = new[] {
                (new Vector3(-1, 0, -1), endTops["South"].first, endTops["West"].last),
                (new Vector3(1, 0, -1), endTops["South"].last, endTops["East"].first),
                (new Vector3(1, 0, 1), endTops["East"].last, endTops["North"].first),
                (new Vector3(-1, 0, 1), endTops["North"].last, endTops["West"].first) };
            for (int i = 0; i < 4; i++)
            {
                var (c, ta, tb) = corners[i];
                float top = Mathf.Max(ta, tb);
                int floors = Mathf.RoundToInt((top - GroundTop - CorniceH) / FloorH);
                var n = new Vector3(0, 0, -c.z);
                var R = Quaternion.LookRotation(n); var u = R * Vector3.right;
                float x0 = Vector3.Dot(u, new Vector3(c.x * PX, 0, 0)), x1 = Vector3.Dot(u, new Vector3(c.x * (PX + D), 0, 0));
                var o = new Vector3(0, 0, c.z * PZ) + u * Mathf.Min(x0, x1);
                var st = new HStyle { floors = floors, mat = kitMats[i % kitMats.Length] };
                KitBlock(g, $"Corner Block {i + 1}", new WF(o, n), D, D, st, Mathf.Min(ta, tb));
            }
        }

        static Transform HouseRoot(Transform g, string name, WF w, float W, float depth, HStyle st, out MB mb)
        {
            var hg = new GameObject(name).transform; hg.SetParent(g, false);
            mb = new MB();
            float top = HouseTop(st.floors), Ye = top + 0.35f;
            if (st.tunnel > 0f)
            {
                // Body split around the passage: left and right blocks, and a block above the tunnel ceiling.
                float a = st.tunnel - TunnelHalf, b = st.tunnel + TunnelHalf;
                mb.Box(w.P(a / 2, top / 2, -depth / 2), new Vector3(a - 0.01f, top, depth), KitMortar, w.R, SkipBottom);
                mb.Box(w.P((b + W) / 2, top / 2, -depth / 2), new Vector3(W - b - 0.01f, top, depth), KitMortar, w.R, SkipBottom);
                mb.Box(w.P((a + b) / 2, (TunnelH + top) / 2, -depth / 2), new Vector3(b - a, top - TunnelH, depth), KitMortar, w.R);
                cols.Add((w.P(a / 2, top / 2, -depth / 2), new Vector3(a, top, depth), w.R, name));
                cols.Add((w.P((b + W) / 2, top / 2, -depth / 2), new Vector3(W - b, top, depth), w.R, name));
                cols.Add((w.P((a + b) / 2, (TunnelH + top) / 2, -depth / 2), new Vector3(b - a, top - TunnelH, depth), w.R, name + " Tunnel Ceiling"));
            }
            else
            {
                mb.Box(w.P(W / 2, top / 2, -depth / 2), new Vector3(W - 0.02f, top, depth), KitMortar, w.R, SkipBottom);
                cols.Add((w.P(W / 2, top / 2, -depth / 2), new Vector3(W, top, depth), w.R, name));
            }
            float rise = depth / 2 * Mathf.Tan(Pitch * Mathf.Deg2Rad);
            mb.ExtrudePoly(new[] { w.P(0.01f, top, 0), w.P(0.01f, Ye - 0.1f, 0), w.P(0.01f, Ye + rise - 0.1f, -depth / 2), w.P(0.01f, Ye - 0.1f, -depth), w.P(0.01f, top, -depth) }, w.u, W - 0.02f, KitGable);
            GableRoof(mb, w, W, Ye, 0f, depth, 0.95f, 0.3f);
            int chim = rnd.Next(1, 3);
            for (int k = 0; k < chim; k++) Chimney(mb, w, Rf(1.4f, W - 1.4f), -depth * (rnd.NextDouble() < 0.5 ? 0.3f : 0.72f), Ye, depth);
            return hg;
        }

        /// <summary>Clads one side wall of a house (frame origin at its left end) with plain courses above the neighbour's top.</summary>
        static void CladSide(Transform hg, WF f, float len, List<Row> rows, float fromY, Material mat)
        {
            foreach (var r in rows) if (r.y + r.h > fromY + 0.05f) KitRow(hg, f, r.fam == "Plinth" ? "Wall" : r.fam, 0f, len, r.y, mat);
        }

        static WF LeftSide(WF w, float depth) => new WF(w.P(0, 0, -depth), -w.u);
        static WF RightSide(WF w, float W) => new WF(w.P(W, 0, 0), w.u);

        static void KitBlock(Transform g, string name, WF w, float W, float depth, HStyle st, float from)
        {
            var hg = HouseRoot(g, name, w, W, depth, st, out var mb);
            var rows = Courses(st.floors);
            CladSide(hg, w, W, rows, from, st.mat);
            CladSide(hg, LeftSide(w, depth), depth, rows, from, st.mat);
            CladSide(hg, RightSide(w, W), depth, rows, from, st.mat);
            CladSide(hg, new WF(w.P(W, 0, -depth), -w.n), W, rows, from, st.mat);
            Piece(hg, name + " Body", mb, solid);
        }

        // ---------- Stand-alone flat-roof house prefab (same StyleKit look as the town houses) ----------
        const string FlatHousePrefab = "Assets/Prefabs/StyleCity/House_FlatRoof.prefab";

        [MenuItem("Tools/Coop Game/Style Test/Build Flat Roof House Prefab")]
        static void BuildFlatRoofHouseMenu() => Debug.Log(BuildFlatRoofHouse());

        /// <summary>Builds a free-standing 12 x 10 m, two-storey house with a flat roof terrace (parapet, coping, stair hut,
        /// chimney) from StyleKit courses and saves it as a prefab. Front faces +z, the pivot is at the ground centre.</summary>
        public static string BuildFlatRoofHouse(float W = 12f, float depth = 10f, int floors = 2, int tint = 1, int seed = 777)
        {
            MakeKitMaterials();
            MakeMaterials();
            AssetDatabase.SaveAssets();
            rnd = new System.Random(seed); kitCount = 0; vertTotal = 0;
            var st = new HStyle { floors = floors, mat = kitMats[Mathf.Clamp(tint, 0, kitMats.Length - 1)], shutter = ShutterCols[rnd.Next(ShutterCols.Length)] };
            var go = new GameObject("House_FlatRoof");
            var hg = go.transform;
            var mb = new MB();
            float top = HouseTop(floors), par = 0.8f;
            var w = new WF(new Vector3(-W / 2, 0, depth / 2), Vector3.forward);
            // Body, roof terrace floor and the parapet ring behind the parapet course.
            mb.Box(w.P(W / 2, top / 2, -depth / 2), new Vector3(W - 0.02f, top, depth), KitMortar, w.R, SkipBottom);
            WBox(mb, w, 0.3f, W - 0.3f, top, top + 0.1f, -depth + 0.3f, -0.3f, StoneD, false);
            WBox(mb, w, 0f, W, top - 0.01f, top + par, -0.3f, -0.01f, KitGable, false);
            WBox(mb, w, 0f, W, top - 0.01f, top + par, -depth + 0.01f, -depth + 0.3f, KitGable, false);
            WBox(mb, w, 0.01f, 0.3f, top - 0.01f, top + par, -depth + 0.3f, -0.3f, KitGable, false);
            WBox(mb, w, W - 0.3f, W - 0.01f, top - 0.01f, top + par, -depth + 0.3f, -0.3f, KitGable, false);
            var faces = new[] { (w, W, true), (RightSide(w, W), depth, false), (new WF(w.P(W, 0, -depth), -w.n), W, false), (LeftSide(w, depth), depth, false) };
            foreach (var (f, len, front) in faces)
            {
                FlatFacade(hg, mb, f, len, st, front);
                KitRow(hg, f, "Wall", 0f, len, top, st.mat, true, true, 1f);   // parapet course
                WBox(mb, f, -0.06f, len + 0.06f, top + par, top + par + 0.1f, -0.36f, 0.1f, StoneL, false);   // coping
            }
            // Stair hut with a door onto the terrace, and a chimney.
            float hx0 = W - 3.6f, hx1 = W - 0.9f, hd0 = -depth + 0.9f, hd1 = -depth + 3.3f, hh = 2.4f, hy = top + 0.1f;
            mb.Box(w.P((hx0 + hx1) / 2, hy + hh / 2, (hd0 + hd1) / 2), new Vector3(hx1 - hx0, hh, hd1 - hd0), KitMortar, w.R, SkipBottom);
            var hut = new[] { (new WF(w.P(hx0, hy, hd1), w.n), hx1 - hx0), (new WF(w.P(hx1, hy, hd1), w.u), hd1 - hd0), (new WF(w.P(hx1, hy, hd0), -w.n), hx1 - hx0), (new WF(w.P(hx0, hy, hd0), -w.u), hd1 - hd0) };
            for (int k = 0; k < hut.Length; k++)
            {
                var (f, len) = hut[k];
                var open = k == 0 ? new List<Vector2> { new Vector2(0.8f, 1.9f) } : new List<Vector2>();
                for (int r = 0; r < 3; r++)
                    foreach (var seg in Gaps(0f, len, new List<Vector2>(open)))
                        KitRow(hg, f, "Wall", seg.x, seg.y, 0.8f * r, st.mat, true, true, 1f);
                WBox(mb, f, -0.08f, len + 0.08f, hh, hh + 0.12f, -0.1f, 0.12f, StoneL, false);
            }
            var df = hut[0].Item1;
            WBox(mb, df, 0.8f, 1.9f, 0f, 2.3f, -0.05f, 0.01f, Dark, false);
            WBox(mb, df, 0.86f, 1.84f, 0.02f, 2.2f, 0.01f, 0.06f, WoodD, false);
            WBox(mb, df, 1.7f, 1.76f, 1.05f, 1.15f, 0.06f, 0.1f, Gold, false);
            mb.Box(w.P(2.2f, top + 1.0f, -depth + 2.0f), new Vector3(0.9f, 1.8f, 0.9f), Mortar, w.R, SkipBottom);
            mb.Box(w.P(2.2f, top + 1.95f, -depth + 2.0f), new Vector3(1.1f, 0.12f, 1.1f), StoneL, w.R);
            mb.Box(w.P(2.2f, top + 2.2f, -depth + 2.0f), new Vector3(0.5f, 0.4f, 0.5f), Iron, w.R);
            usedNames.Remove("City_House_FlatRoof_Body");
            Piece(hg, "House FlatRoof Body", mb, solid);
            // Colliders: body, parapet walls, stair hut, chimney.
            void Col(Vector3 c, Vector3 s) { var b = go.AddComponent<BoxCollider>(); b.center = c; b.size = s; }
            Col(new Vector3(0, top / 2, 0), new Vector3(W, top, depth));
            Col(new Vector3(0, top + par / 2, depth / 2 - 0.15f), new Vector3(W, par, 0.4f));
            Col(new Vector3(0, top + par / 2, -depth / 2 + 0.15f), new Vector3(W, par, 0.4f));
            Col(new Vector3(-W / 2 + 0.15f, top + par / 2, 0), new Vector3(0.4f, par, depth));
            Col(new Vector3(W / 2 - 0.15f, top + par / 2, 0), new Vector3(0.4f, par, depth));
            Col(w.P((hx0 + hx1) / 2, hy + hh / 2, (hd0 + hd1) / 2), new Vector3(hx1 - hx0, hh, hd1 - hd0));
            Col(w.P(2.2f, top + 1.1f, -depth + 2.0f), new Vector3(1.1f, 2.2f, 1.1f));
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/StyleCity")) AssetDatabase.CreateFolder("Assets/Prefabs", "StyleCity");
            PrefabUtility.SaveAsPrefabAsset(go, FlatHousePrefab);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            return $"Saved {FlatHousePrefab}: {W}x{depth} m, {floors} floors, top {top + par + 0.1f:F1} m, {kitCount} StyleKit pieces, {vertTotal} body vertices.";
        }

        // ---------- Segment prefabs (used to rebuild greybox levels, e.g. A6_Start "Cave Segments") ----------
        const string SegHouseDir = "Assets/Prefabs/StyleCity/Houses", SegWallDir = "Assets/Prefabs/StyleCity/Walls";

        static void EnsureCityMats()
        {
            if (kitMats != null && kitMats.Length == 6 && kitMats[0] != null && solid != null) return;
            MakeKitMaterials(); MakeMaterials(); AssetDatabase.SaveAssets();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int k = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, k)); AssetDatabase.CreateFolder(path.Substring(0, k), path.Substring(k + 1));
        }

        static GameObject SavePrefab(GameObject go, string path)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = t.GetComponent<TMPro.TextMeshPro>() == null;   // shop sign text stays dynamic
            EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
            var pf = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return pf;
        }

        static int SeedOf(string s) { unchecked { int h = 17; foreach (char ch in s) h = h * 31 + ch; return h & 0x7fffffff; } }

        /// <summary>Courses that fill exactly height H (10 cm grid): Plinth 1.2 at the bottom, Edge 1.0 on top when they fit, Wall 0.8 / Thin 0.5 between.
        /// Any rest under 30 cm is left for the coping.</summary>
        static List<Row> FillCourses(float y0, float H, bool plinth = true, bool edge = true)
        {
            int R = Mathf.RoundToInt(H * 10f);
            foreach (int p in plinth ? new[] { 12, 0 } : new[] { 0 })
                foreach (int e in edge ? new[] { 10, 0 } : new[] { 0 })
                    for (int cut = 0; cut <= 4; cut++)
                    {
                        int rem = R - p - e - cut; if (rem < 0) continue;
                        for (int b = 0; b * 5 <= rem; b++)
                        {
                            if ((rem - b * 5) % 8 != 0) continue;
                            int a = (rem - b * 5) / 8;
                            var l = new List<Row>(); float y = y0;
                            if (p > 0) { l.Add(new Row { fam = "Plinth", y = y, h = 1.2f }); y += 1.2f; }
                            for (int i = 0; i < a; i++) { l.Add(new Row { fam = "Wall", y = y, h = 0.8f }); y += 0.8f; }
                            for (int i = 0; i < b; i++) { l.Add(new Row { fam = "Thin", y = y, h = 0.5f }); y += 0.5f; }
                            if (e > 0) l.Add(new Row { fam = "Edge", y = y, h = 1.0f });
                            return l;
                        }
                    }
            return new List<Row>();
        }

        /// <summary>Pitched-roof town house (same builder as the StyleTest_City houses). Front faces +z, pivot at the bottom centre of the front,
        /// body runs to -z. Both side walls are clad, the back is plain mortar. Cached by name.</summary>
        public static GameObject HousePrefab(float W, int floors, int tint, float depth = 10f, bool rebuild = false, bool twoSided = false, float tunnel = 0f, bool backDoors = true)
        {
            W = R1(W);
            depth = R1(depth);
            string name = $"House_Gable_W{Mathf.RoundToInt(W * 10)}_F{floors}_T{tint}" + (Mathf.Abs(depth - 10f) > 0.05f ? $"_D{Mathf.RoundToInt(depth * 10)}" : "") + (twoSided ? "_2S" : "") + (tunnel > 0f ? $"_TN{Mathf.RoundToInt(tunnel * 10)}" : "") + (twoSided && !backDoors ? "_BW" : "");
            string path = SegHouseDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            rnd = new System.Random(SeedOf(name)); cols.Clear(); glowMB = new MB();
            var go = new GameObject(name);
            var st = new HStyle { floors = floors, mat = kitMats[Mathf.Clamp(tint, 0, 5)], shutter = ShutterCols[rnd.Next(ShutterCols.Length)] };
            usedNames.Remove("City_" + name + "_Body");
            st.tunnel = tunnel;   // > 0: walk-through passage at this distance from the left end (like East House 1)
            KitHouse(go.transform, name, new WF(new Vector3(-W / 2, 0, 0), Vector3.forward), W, depth, st, 0f, 0f);
            if (twoSided)
            {
                // Second street side: the back gets a full facade too (windows, door, shop) instead of plain mortar.
                var hb = go.transform.GetChild(0); var bm = new MB();
                FlatFacade(hb, bm, new WF(new Vector3(W / 2, 0, -depth), Vector3.back), W, st, backDoors, tunnel > 0f ? W - tunnel : -1f);   // backDoors false: windows only (back above a drop)
                usedNames.Remove("City_" + name + "_Back");
                Piece(hb, name + " Back", bm, solid);
            }
            if (tunnel > 0f) { usedNames.Remove("City_" + name + "_Glow"); Piece(go.transform.GetChild(0), name + " Glow", glowMB, glow, false); }
            foreach (var c in cols) { var b = go.AddComponent<BoxCollider>(); b.center = c.c; b.size = c.s; }
            cols.Clear();
            return SavePrefab(go, path);
        }

        /// <summary>Thin wall dressed as a row-house front (for walls with another street right behind them): windows, door and shop
        /// on the front (+z), plain courses on the back and ends, attic courses up to H and a coping. Pivot at the volume centre, size W x H x D.</summary>
        public static GameObject FrontWallPrefab(float W, float H, float D, int tint, bool rebuild = false)
        {
            W = R1(W);
            string name = $"HouseFront_W{Mathf.RoundToInt(W * 10)}_H{Mathf.RoundToInt(H * 10)}_D{Mathf.RoundToInt(D * 100)}_T{tint}";
            string path = SegHouseDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            rnd = new System.Random(SeedOf(name));
            var go = new GameObject(name); var hg = go.transform; var mb = new MB();
            var mat = kitMats[Mathf.Clamp(tint, 0, 5)];
            int floors = 0; while (floors < 6 && HouseTop(floors + 1) <= H + 0.01f) floors++;
            var st = new HStyle { floors = floors, mat = mat, shutter = ShutterCols[rnd.Next(ShutterCols.Length)] };
            var w = new WF(new Vector3(-W / 2, -H / 2, D / 2), Vector3.forward);
            mb.Box(Vector3.zero, new Vector3(W - 0.02f, H, D - 0.02f), KitMortar, Quaternion.identity);
            float facadeTop = floors > 0 ? HouseTop(floors) : 0f;
            if (floors > 0) FlatFacade(hg, mb, w, W, st, true);
            var attic = FillCourses(facadeTop, H - facadeTop, floors == 0, false);
            foreach (var r in attic) KitRow(hg, w, r.fam, 0f, W, r.y, mat, true, true, 1f);
            // Back and both ends: plain courses over the full height.
            var plain = FillCourses(0f, H);
            var back = new WF(w.P(W, 0, -D), -w.n);
            var endL = new WF(w.P(0, 0, -D), -w.u); var endR = new WF(w.P(W, 0, 0), w.u);
            foreach (var r in plain)
            {
                KitRow(hg, back, r.fam, 0f, W, r.y, mat, true, true, 1f);
                KitRow(hg, endL, r.fam, 0f, D, r.y, mat, true, true, 1f);
                KitRow(hg, endR, r.fam, 0f, D, r.y, mat, true, true, 1f);
            }
            WBox(mb, w, -0.06f, W + 0.06f, H - 0.02f, H + 0.1f, -D - 0.06f, 0.1f, StoneL, false);
            usedNames.Remove("City_" + name + "_Body");
            Piece(hg, name + " Body", mb, solid);
            go.AddComponent<BoxCollider>().size = new Vector3(W, H, D);
            return SavePrefab(go, path);
        }

        /// <summary>Building that fills exactly one greybox block (size x H x z, pivot at the volume centre, BoxCollider = exact size), for levels
        /// where blocks are stacked and their tops are walkable: row-house facades (door, shops, windows per floor) on every face at least
        /// 7 m long, plain courses on shorter faces and above the last floor, flat stone roof with a coping just 10 cm above the block top.</summary>
        /// <param name="faces">Optional per-face layout, faces in the order +z|+x|-z|-x separated by "|": "g" or "g:a-b,c-d" = street level g m above
        /// the block bottom (plain courses below it), doors and shops only in the x ranges a-b (x along the face from its left end seen from outside;
        /// no ranges = everywhere), "g:" + nothing after the colon = no doors on that face. Empty = street at the bottom everywhere.</param>
        public static GameObject BlockHousePrefab(Vector3 size, int tint, bool rebuild = false, bool doors = true, string faces = "", bool flatKit = false, bool roof = false)
        {
            string name = $"BlockHouse_{Mathf.RoundToInt(size.x * 100)}x{Mathf.RoundToInt(size.y * 100)}x{Mathf.RoundToInt(size.z * 100)}_T{tint}" + (doors ? "" : "_ND") + (string.IsNullOrEmpty(faces) ? "" : "_F" + (SeedOf(faces) % 100000)) + (flatKit ? "_FL" : "") + (roof ? "_R" : "") + (exactTop && !roof ? "_XT" : "");
            string path = SegHouseDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            rnd = new System.Random(SeedOf(name));
            var go = new GameObject(name); var hg = go.transform; var mb = new MB();
            var mat = kitMats[Mathf.Clamp(tint, 0, 5)];
            float H = size.y;
            int floors = 0; while (floors < 12 && HouseTop(floors + 1) <= H + 0.01f) floors++;
            forceFlat = flatKit;   // flatKit: flush stones only, nothing sticks out under the windows
            var st = new HStyle { floors = floors, mat = mat, shutter = ShutterCols[rnd.Next(ShutterCols.Length)] };
            mb.Box(Vector3.zero, size - Vector3.one * 0.02f, KitMortar, Quaternion.identity);
            var plain = FillCourses(0f, H);
            var spec = string.IsNullOrEmpty(faces) ? new string[0] : faces.Split('|');
            var dirs = new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            for (int fi = 0; fi < 4; fi++)
            {
                var n = dirs[fi];
                bool alongX = Mathf.Abs(n.z) > 0.5f;
                float len = alongX ? size.x : size.z, half = alongX ? size.z / 2 : size.x / 2;
                var uf = Quaternion.LookRotation(n) * Vector3.right;
                var f = new WF(n * half - uf * (len / 2) - Vector3.up * (H / 2), n);
                // Face layout: street level g above the bottom and where doors may go.
                float g = 0f; List<Vector2> allow = null;
                if (fi < spec.Length && spec[fi].Length > 0)
                {
                    var sect = spec[fi].Split(';');
                var parts = sect[0].Split(':');
                    g = R1(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture));
                    if (parts.Length > 1)
                    {
                        allow = new List<Vector2>();
                        foreach (var rg in parts[1].Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                        {
                            var ab = rg.Split('-');
                            allow.Add(new Vector2(float.Parse(ab[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(ab[1], System.Globalization.CultureInfo.InvariantCulture)));
                        }
                    }
                }
                // After ";": "a-b@h,..." = no windows or doors in x range a-b whose bottom is below h (h measured from the block bottom), e.g. a raised floor in front.
                // Third part after a second ";": "a-b,..." = shops in these x ranges get no awning (a wall stands too close in front).
                List<Vector2> noAwn = null;
                if (fi < spec.Length && spec[fi].Split(';').Length > 2)
                {
                    noAwn = new List<Vector2>();
                    foreach (var rg in spec[fi].Split(';')[2].Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                    { var ab = rg.Split('-'); noAwn.Add(new Vector2(float.Parse(ab[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(ab[1], System.Globalization.CultureInfo.InvariantCulture))); }
                }
                List<Vector4> hide = null;
                if (fi < spec.Length && spec[fi].Contains(";"))
                {
                    hide = new List<Vector4>();
                    foreach (var hr in spec[fi].Split(';')[1].Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        var xh = hr.Split('@'); var ab = xh[0].Split('-'); var ci = System.Globalization.CultureInfo.InvariantCulture;
                        var hl = xh[1].Split('/');   // "h/lo" = only openings that also reach above lo (a slab or a ledge in front, not standing on the ground)
                        hide.Add(new Vector4(float.Parse(ab[0], ci), float.Parse(ab[1], ci), float.Parse(hl[0], ci) - g, hl.Length > 1 ? float.Parse(hl[1], ci) - g : -1000f));
                    }
                }
                float Hf = H - g;
                int fl = 0; while (fl < 12 && HouseTop(fl + 1) <= Hf + 0.01f) fl++;
                if (len >= 7f && HouseTop(0) <= Hf + 0.01f)
                {
                    // Below the street: plain courses (buried or hidden behind a lower block).
                    if (g > 0.05f) foreach (var r in FillCourses(0f, g, true, false)) KitRow(hg, f, r.fam, 0f, len, r.y, mat, true, true, 1f);
                    var fs = new WF(f.P(0, g, 0), n);
                    var sf = new HStyle { floors = fl, mat = mat, shutter = st.shutter };
                    FlatFacade(hg, mb, fs, len, sf, doors, -1f, allow, hide, noAwn);   // doors false: barred windows instead of doors and shops (raised blocks)
                    float ft = g + HouseTop(fl);
                    foreach (var r in FillCourses(ft, H - ft, false, false)) KitRow(hg, f, r.fam, 0f, len, r.y, mat, true, true, 1f);
                }
                else foreach (var r in plain) KitRow(hg, f, r.fam, 0f, len, r.y, mat, true, true, r.fam == "Edge" ? 0.7f : 1f);
            }
            if (roof)
            {
                BlockRoof(mb, size);
            }
            else if (exactTop)
            {
                mb.Box(Vector3.up * (H / 2 - 0.05f), new Vector3(size.x + 0.04f, 0.1f, size.z + 0.04f), StoneL, Quaternion.identity);   // flush top, walkable at exactly H
            }
            else
            {
            // Flat roof: dark stone terrace and a light coping ring, both 10 cm above the block top.
            mb.Box(Vector3.up * (H / 2 + 0.04f), new Vector3(size.x - 0.5f, 0.12f, size.z - 0.5f), StoneD, Quaternion.identity);
            foreach (var n in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
            {
                bool alongX = Mathf.Abs(n.z) > 0.5f;
                // Front/back rings run the full width, the side rings fit between them (no overlapping coplanar corners).
                float len = alongX ? size.x + 0.16f : size.z - 0.84f, half = alongX ? size.z / 2 : size.x / 2;
                var c = n * (half - 0.17f) + Vector3.up * (H / 2 + 0.045f);
                mb.Box(c, alongX ? new Vector3(len, 0.13f, 0.5f) : new Vector3(0.5f, 0.13f, len), StoneL, Quaternion.identity);
            }
            }
            usedNames.Remove("City_" + name + "_Body");
            forceFlat = false;
            Piece(hg, name + " Body", mb, solid);
            go.AddComponent<BoxCollider>().size = size;
            return SavePrefab(go, path);
        }

        /// <summary>Free-standing stone wall of size x H x z: dark core, flat StyleKit courses on all four faces, stone coping.
        /// Pivot at the volume centre (drop-in for a scaled greybox cube), BoxCollider = exact size.</summary>
        /// <summary>Tiled gable roof on a centre-pivot block: ridge along the longer side, gable walls on the short ends and chimneys.</summary>
        static void BlockRoof(MB mb, Vector3 size, bool chimneys = true)
        {
            float H = size.y;
                bool longX = size.x >= size.z;
                var rw = longX ? new WF(new Vector3(-size.x / 2, -H / 2, size.z / 2), Vector3.forward) : new WF(new Vector3(size.x / 2, -H / 2, size.z / 2), Vector3.right);
                float RW = longX ? size.x : size.z, RD = longX ? size.z : size.x, Ye = H + 0.35f;
                float rise = RD / 2 * Mathf.Tan(Pitch * Mathf.Deg2Rad);
                mb.ExtrudePoly(new[] { rw.P(0.01f, H, 0), rw.P(0.01f, Ye - 0.1f, 0), rw.P(0.01f, Ye + rise - 0.1f, -RD / 2), rw.P(0.01f, Ye - 0.1f, -RD), rw.P(0.01f, H, -RD) }, rw.u, RW - 0.02f, KitGable);
                GableRoof(mb, rw, RW, Ye, 0f, RD, 0.95f, 0.3f);
                if (chimneys && RW >= 3.5f && RD >= 3f) { int chim = Mathf.Clamp(Mathf.RoundToInt(RW / 14f), 1, 4); for (int k = 0; k < chim; k++) Chimney(mb, rw, Rf(1.4f, RW - 1.4f), -RD * (rnd.NextDouble() < 0.5 ? 0.3f : 0.72f), Ye, RD); }
        }

        public static GameObject StoneWallPrefab(Vector3 size, int tint = 5, bool rebuild = false, bool roof = false)
        {
            string name = $"StoneWall_{Mathf.RoundToInt(size.x * 100)}x{Mathf.RoundToInt(size.y * 100)}x{Mathf.RoundToInt(size.z * 100)}_T{tint}" + (roof ? "_R" : "");
            string path = SegWallDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            rnd = new System.Random(SeedOf(name));
            var go = new GameObject(name); var hg = go.transform; var mb = new MB();
            var mat = kitMats[Mathf.Clamp(tint, 0, 5)];
            float H = size.y;
            mb.Box(Vector3.zero, size - Vector3.one * 0.02f, KitMortar, Quaternion.identity);
            var rows = FillCourses(0f, H);
            foreach (var n in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
            {
                bool alongX = Mathf.Abs(n.z) > 0.5f;
                float len = alongX ? size.x : size.z, half = alongX ? size.z / 2 : size.x / 2;
                var uf = Quaternion.LookRotation(n) * Vector3.right;
                var f = new WF(n * half - uf * (len / 2) - Vector3.up * (H / 2), n);
                foreach (var r in rows) KitRow(hg, f, r.fam, 0f, len, r.y, mat, true, true, r.fam == "Edge" ? 0.7f : 1f);
            }
            if (roof) BlockRoof(mb, size);
            else
                mb.Box(Vector3.up * (H / 2 + 0.04f), new Vector3(size.x + 0.16f, 0.12f, size.z + 0.16f), StoneL, Quaternion.identity);
            usedNames.Remove("City_" + name + "_Body");
            Piece(hg, name + " Body", mb, solid);
            go.AddComponent<BoxCollider>().size = size;
            return SavePrefab(go, path);
        }

        /// <summary>Block with a walk-through tunnel along local x, dressed like the East House 1 passage of StyleTest_City (closed by a gate at one mouth).
        /// Pivot at the volume centre. Tunnel tw wide (centred on z), th tall, its floor fl above the block bottom. clad: faces with plain flush courses
        /// outside (bits +z|+x|-z|-x); mouth faces get an opening. Inside: kit side walls (when the block is wider than the tunnel), plank ceiling on
        /// beams, stone floor slabs, a lantern. frames: stone frame at the -x (bit 0) / +x (bit 1) mouth. extFloor / extCeil lengthen floor and ceiling
        /// past the +x end (a threshold between other walls). roofZ: gable roof with the ridge along z (fills a gap in a row of roofed blocks).</summary>
        public static GameObject VentTunnelPrefab(Vector3 size, float tw, float th, float fl, int tint, int clad, int frames, float extFloor = 0f, float extCeil = 0f, bool roofZ = false, bool rebuild = false)
        {
            int Cm(float v) => Mathf.RoundToInt(v * 100);
            string name = $"VentTunnel_{Cm(size.x)}x{Cm(size.y)}x{Cm(size.z)}_W{Cm(tw)}_H{Cm(th)}_T{tint}_C{clad}F{frames}" + (extFloor > 0f || extCeil > 0f ? $"_E{Cm(extFloor)}-{Cm(extCeil)}" : "") + (roofZ ? "_R" : "");
            string path = SegWallDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            rnd = new System.Random(SeedOf(name)); glowMB = new MB();
            var go = new GameObject(name); var hg = go.transform; var mb = new MB();
            var mat = kitMats[Mathf.Clamp(tint, 0, 5)];
            forceFlat = true;
            float L = size.x, H = size.y, W = size.z, y0 = -H / 2, yF = y0 + fl, yT = yF + th, sw = (W - tw) / 2;
            // Body around the passage (side walls, the floor under it, the mass above) and the same boxes as colliders.
            var boxes = new List<(Vector3 c, Vector3 s)>();
            if (sw > 0.02f) foreach (int s in new[] { -1, 1 }) boxes.Add((new Vector3(0, 0, s * (tw / 2 + sw / 2)), new Vector3(L, H, sw)));
            if (fl > 0.02f) boxes.Add((new Vector3(extFloor / 2, (y0 + yF) / 2, 0), new Vector3(L + extFloor, fl, tw)));
            if (H / 2 - yT > 0.02f) boxes.Add((new Vector3(extCeil / 2, (yT + H / 2) / 2, 0), new Vector3(L + extCeil, H / 2 - yT, tw)));
            foreach (var b in boxes) { mb.Box(b.c, b.s - Vector3.one * 0.02f, KitMortar, Quaternion.identity); var bc = go.AddComponent<BoxCollider>(); bc.center = b.c; bc.size = b.s; }
            // Outside: plain courses, with the tunnel mouth left open on the x faces.
            var rows = FillCourses(0f, H);
            var dirs = new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            for (int fi = 0; fi < 4; fi++)
            {
                if ((clad & (1 << fi)) == 0) continue;
                var n = dirs[fi];
                bool alongX = Mathf.Abs(n.z) > 0.5f;
                float len = alongX ? L : W, half = alongX ? W / 2 : L / 2;
                var uf = Quaternion.LookRotation(n) * Vector3.right;
                var f = new WF(n * half - uf * (len / 2) - Vector3.up * (H / 2), n);
                foreach (var r in rows)
                {
                    float ov = Mathf.Min(r.y + r.h, fl + th) - Mathf.Max(r.y, fl);   // part of this course inside the tunnel height
                    var open = new List<Vector2>();
                    if (!alongX && ov > r.h / 2) open.Add(new Vector2(len / 2 - tw / 2, len / 2 + tw / 2));
                    foreach (var seg in Gaps(0f, len, open)) KitRow(hg, f, r.fam, seg.x, seg.y, r.y, mat, true, true, 1f);
                }
            }
            // Inside: kit side walls, plank ceiling on cross beams, floor slabs, lantern.
            var tg = new GameObject("Tunnel").transform; tg.SetParent(hg, false);
            if (sw > 0.02f)
                foreach (int s in new[] { -1, 1 })
                {
                    var n = s < 0 ? Vector3.forward : Vector3.back;   // wall at z = s * tw/2, facing the tunnel axis
                    Vector3 a = new Vector3(-L / 2, yF, s * tw / 2), b = new Vector3(L / 2, yF, s * tw / 2);
                    var uf = Quaternion.LookRotation(n) * Vector3.right;
                    var f = new WF(Vector3.Dot(b - a, uf) > 0 ? a : b, n);
                    foreach (var r in FillCourses(0f, th, true, false)) KitRow(tg, f, r.fam, 0f, L, r.y, mat, true, true, 1f);
                }
            float cx0 = -L / 2, cx1 = L / 2 + extCeil;
            mb.Box(new Vector3((cx0 + cx1) / 2, yT - 0.045f, 0), new Vector3(cx1 - cx0, 0.05f, tw - 0.02f), Wood, Quaternion.identity);
            for (float x = cx0 + 0.9f; x < cx1 - 0.5f; x += 1.8f)
                mb.Box(new Vector3(x, yT - 0.175f, 0), new Vector3(0.26f, 0.25f, tw - 0.1f), WoodD, Quaternion.identity);
            float fx0 = -L / 2, fx1 = L / 2 + extFloor;
            int ns = Mathf.Max(2, Mathf.RoundToInt((fx1 - fx0) / 1.6f)); float sl = (fx1 - fx0) / ns;
            for (int k = 0; k < ns; k++)
            {
                float top = k % 2 == 0 ? 0.03f : 0.02f;
                mb.Box(new Vector3(fx0 + sl * (k + 0.5f), yF + (top - 0.06f) / 2, 0), new Vector3(sl - 0.03f, top + 0.06f, tw - 0.1f), Stone, Quaternion.identity);
            }
            // Stone frame around the chosen mouths (posts stand on the walls beside the opening).
            for (int m = 0; m < 2; m++)
            {
                if ((frames & (1 << m)) == 0) continue;
                float sx = m == 0 ? -1f : 1f, xm = m == 0 ? -L / 2 : L / 2 + extFloor;
                float xa = xm - sx * 0.05f, xb = xm + sx * 0.1f, xc = (xa + xb) / 2, dx = Mathf.Abs(xb - xa);
                foreach (int s in new[] { -1, 1 })
                    mb.Box(new Vector3(xc, (yF + yT + 0.35f) / 2, s * (tw / 2 + 0.175f)), new Vector3(dx, yT + 0.35f - yF, 0.35f), StoneL, Quaternion.identity);
                mb.Box(new Vector3(xm + sx * 0.035f, yT + 0.175f, 0), new Vector3(0.17f, 0.35f, tw + 0.7f), StoneL, Quaternion.identity);
            }
            var lp = new Vector3(0f, yT - 0.07f, 0f);
            mb.Prism(lp - Vector3.up * 0.35f, 4, 0.03f, 0.03f, 0.35f, Iron, 45f);
            mb.Prism(lp - Vector3.up * 0.43f, 4, 0.16f, 0.16f, 0.08f, Iron, 45f);
            glowMB.Prism(lp - Vector3.up * 0.81f, 4, 0.12f, 0.15f, 0.38f, GLampDim, 45f);
            AddLight(tg, "Tunnel Light", lp - Vector3.up * 0.65f, LightType.Point, new Color(1f, 0.78f, 0.5f), 2.2f, 8f);
            // Top: gable roof across a gap in a roofed row (ridge along z, no side overhang), else a flat stone coping.
            if (roofZ)
            {
                var rw = new WF(new Vector3(L / 2, -H / 2, W / 2), Vector3.right);
                float Ye = H + 0.35f, rise = L / 2 * Mathf.Tan(Pitch * Mathf.Deg2Rad);
                mb.ExtrudePoly(new[] { rw.P(0.01f, H, 0), rw.P(0.01f, Ye - 0.1f, 0), rw.P(0.01f, Ye + rise - 0.1f, -L / 2), rw.P(0.01f, Ye - 0.1f, -L), rw.P(0.01f, H, -L) }, rw.u, W - 0.02f, KitGable);
                GableRoof(mb, rw, W, Ye, 0f, L, 0.95f, -0.3f);   // the neighbours' gable overhangs (0.3 m) cover the rest of the gap
            }
            else mb.Box(new Vector3(0, H / 2 + 0.04f, 0), new Vector3(L + 0.16f, 0.12f, W + 0.16f), StoneL, Quaternion.identity);
            forceFlat = false;
            usedNames.Remove("City_" + name + "_Body"); usedNames.Remove("City_" + name + "_Glow");
            Piece(hg, name + " Body", mb, solid);
            Piece(tg, name + " Glow", glowMB, glow, false);
            return SavePrefab(go, path);
        }

        /// <summary>Block that fills a gap in a roofed row and holds a niche open on its +x face (e.g. the alarm button spot of SEG_06):
        /// pivot at the volume centre, niche over the full width (z), nBot..nTop above the block bottom, nDepth deep. Flush StyleKit courses on
        /// the x faces, stone sill and lintel bands round the niche, courses on its back wall, gable roof with the ridge along z (like VentTunnel roofZ).
        /// nDepth >= size.x: the opening goes through the block (e.g. the gun stand slot in the SEG_05 auction backdrop wall), framed on both x faces.</summary>
        public static GameObject NicheBlockPrefab(Vector3 size, float nDepth, float nBot, float nTop, int tint, bool rebuild = false)
        {
            int Cm(float v) => Mathf.RoundToInt(v * 100);
            bool through = nDepth >= size.x - 0.005f; if (through) nDepth = size.x;
            string name = $"NicheBlock_{Cm(size.x)}x{Cm(size.y)}x{Cm(size.z)}_N{Cm(nDepth)}-{Cm(nBot)}-{Cm(nTop)}_T{tint}";
            string path = SegWallDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            rnd = new System.Random(SeedOf(name));
            var go = new GameObject(name); var hg = go.transform; var mb = new MB();
            var mat = kitMats[Mathf.Clamp(tint, 0, 5)];
            forceFlat = true;
            float L = size.x, H = size.y, W = size.z, y0 = -H / 2, xb = L / 2 - nDepth;
            // Body: back part (full height), sill and lintel in front of it; the same boxes as colliders.
            var boxes = new List<(Vector3 c, Vector3 s)> {
                (new Vector3((-L / 2 + xb) / 2, 0, 0), new Vector3(xb + L / 2, H, W)),
                (new Vector3((xb + L / 2) / 2, y0 + nBot / 2, 0), new Vector3(nDepth, nBot, W)),
                (new Vector3((xb + L / 2) / 2, (y0 + nTop + H / 2) / 2, 0), new Vector3(nDepth, H - nTop, W)) };
            if (through) boxes.RemoveAt(0);
            foreach (var b in boxes) { mb.Box(b.c, b.s - Vector3.one * 0.02f, KitMortar, Quaternion.identity); var bc = go.AddComponent<BoxCollider>(); bc.center = b.c; bc.size = b.s; }
            // Courses on -x (full) and +x (rows touching the niche left out; the stone bands below fill up to the niche).
            var rows = FillCourses(0f, H);
            float below = 0f, above = H;
            foreach (var n in new[] { Vector3.right, Vector3.left })
            {
                var uf = Quaternion.LookRotation(n) * Vector3.right;
                var f = new WF(n * (L / 2) - uf * (W / 2) - Vector3.up * (H / 2), n);
                foreach (var r in rows)
                {
                    bool hit = (n.x > 0 || through) && r.y < nTop - 0.01f && r.y + r.h > nBot + 0.01f;
                    if (hit) continue;
                    if (n.x > 0) { if (r.y + r.h <= nBot + 0.01f) below = Mathf.Max(below, r.y + r.h); else above = Mathf.Min(above, r.y); }
                    KitRow(hg, f, r.fam, 0f, W, r.y, mat, true, true, 1f);
                }
            }
            // Stone sill (projecting a little) and lintel band on the +x face, back wall courses inside the niche.
            mb.Box(new Vector3(L / 2 - (nDepth - 0.06f) / 2 + 0.03f, y0 + (below + nBot) / 2, 0), new Vector3(nDepth + 0.06f, nBot - below, W - 0.02f), StoneL, Quaternion.identity);
            mb.Box(new Vector3(L / 2 + 0.01f - 0.1f, y0 + (nTop + above) / 2, 0), new Vector3(0.22f, above - nTop, W - 0.02f), StoneL, Quaternion.identity);
            if (through) mb.Box(new Vector3(-L / 2 - 0.01f + 0.1f, y0 + (nTop + above) / 2, 0), new Vector3(0.22f, above - nTop, W - 0.02f), StoneL, Quaternion.identity);
            if (through) mb.Box(new Vector3(-L / 2 - 0.015f, y0 + (below + nBot) / 2, 0), new Vector3(0.09f, nBot - below, W - 0.02f), StoneL, Quaternion.identity);   // sill nose on the -x face
            if (!through)
            {
                var n = Vector3.right; var uf = Quaternion.LookRotation(n) * Vector3.right;
                var f = new WF(new Vector3(xb, 0, 0) - uf * (W / 2) + Vector3.up * (y0 + nBot), n);
                foreach (var r in FillCourses(0f, nTop - nBot, false, false)) KitRow(hg, f, r.fam, 0f, W, r.y, mat, true, true, 1f);
            }
            // Gable roof with the ridge along z (fills the gap between the neighbours' roofs).
            var rw = new WF(new Vector3(L / 2, -H / 2, W / 2), Vector3.right);
            float Ye = H + 0.35f, rise = L / 2 * Mathf.Tan(Pitch * Mathf.Deg2Rad);
            mb.ExtrudePoly(new[] { rw.P(0.01f, H, 0), rw.P(0.01f, Ye - 0.1f, 0), rw.P(0.01f, Ye + rise - 0.1f, -L / 2), rw.P(0.01f, Ye - 0.1f, -L), rw.P(0.01f, H, -L) }, rw.u, W - 0.02f, KitGable);
            GableRoof(mb, rw, W, Ye, 0f, L, 0.95f, -0.3f);
            forceFlat = false;
            usedNames.Remove("City_" + name + "_Body");
            Piece(hg, name + " Body", mb, solid);
            return SavePrefab(go, path);
        }

        /// <summary>Creates the StyleKitFlat (flush, "_FlatAll") pieces that are missing for some lengths of Edge / Thin / Wall / Plinth:
        /// a copy of the StyleKit piece with every stone pushed back to 5 cm. Without them flatKit houses fell back to protruding stones.</summary>
        public static string MakeMissingFlatKit()
        {
            var rx = new System.Text.RegularExpressions.Regex(@"^(Kit_(Edge|Thin|Wall|Plinth)_.+_\d)\.prefab$");
            EnsureFolder("Assets/Meshes/StyleKitFlat");
            int made = 0;
            foreach (var file in System.IO.Directory.GetFiles(KitDir.TrimEnd('/'), "*.prefab"))
            {
                var m = rx.Match(System.IO.Path.GetFileName(file)); if (!m.Success) continue;
                string target = KitFlatDir + m.Groups[1].Value + "_FlatAll.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(target) != null) continue;
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(KitDir + m.Groups[1].Value + ".prefab");
                var mesh = Object.Instantiate(src.GetComponent<MeshFilter>().sharedMesh); mesh.name = m.Groups[1].Value + "_FlatAll";
                var v = mesh.vertices; for (int i = 0; i < v.Length; i++) if (v[i].z > 0.05f) v[i].z = 0.05f;
                mesh.vertices = v; mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, "Assets/Meshes/StyleKitFlat/" + mesh.name + ".asset");
                var go = (GameObject)Object.Instantiate(src); go.name = m.Groups[1].Value + "_FlatAll";
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                PrefabUtility.SaveAsPrefabAsset(go, target); Object.DestroyImmediate(go); made++;
            }
            kitCache.Clear();
            AssetDatabase.SaveAssets();
            return "flat pieces made: " + made;
        }

        /// <summary>Clock tower in the look of the StyleTest_City campanile, sized to a block (pivot at the centre, BoxCollider = size):
        /// flush StyleKit courses, a clock on every face, belfry with a bell under the cornice, pyramid tile roof with a cross above the block.
        /// doorFace 0..3 (+z|+x|-z|-x, -1 = none) and doorX (along that face from its left end seen from outside) place the ground door.</summary>
        public static GameObject ClockTowerPrefab(Vector3 size, int doorFace = -1, float doorX = 0f, bool rebuild = false)
        {
            string name = $"ClockTower_{Mathf.RoundToInt(size.x * 100)}x{Mathf.RoundToInt(size.y * 100)}x{Mathf.RoundToInt(size.z * 100)}" + (doorFace >= 0 ? $"_D{doorFace}_{Mathf.RoundToInt(doorX * 10)}" : "");
            string path = SegHouseDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            rnd = new System.Random(SeedOf(name));
            var go = new GameObject(name); var hg = go.transform; var mb = new MB();
            float H = size.y, Sx = size.x, Sz = size.z, sc = Mathf.Min(Sx, Sz) / 6.4f;
            var c = Vector3.down * (H / 2);
            float Top = H, Shaft = H - 6.0f, Bel = Shaft + 3.2f;
            forceFlat = true;
            mb.Box(c + Vector3.up * Shaft / 2, new Vector3(Sx - 0.02f, Shaft, Sz - 0.02f), KitMortar, Quaternion.identity);
            mb.Box(c + Vector3.up * (Bel + Top) / 2, new Vector3(Sx - 0.02f, Top - Bel, Sz - 0.02f), KitMortar, Quaternion.identity);
            var rows = new List<Row> { new Row { fam = "Plinth", y = 0f, h = 1.2f } };
            rows.AddRange(FillCourses(1.2f, Shaft - 1.2f, false, true));
            float y = Shaft;
            for (int k = 0; k < 4; k++) { rows.Add(new Row { fam = "Wall", y = y, h = 0.8f, slot = 1 }); y += 0.8f; }
            rows.Add(new Row { fam = "Edge", y = y, h = 1f }); y += 1f;
            rows.Add(new Row { fam = "Wall", y = y, h = 0.8f }); y += 0.8f;
            rows.Add(new Row { fam = "Edge", y = y, h = 1f });
            var mat = kitMats[5];
            var dirs = new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            float clockR = Mathf.Clamp(Mathf.Min(Sx, Sz) / 5.3f, 1.2f, 1.9f), clockY = Shaft - 1.0f - 0.5f - clockR;
            for (int fi = 0; fi < 4; fi++)
            {
                var n = dirs[fi]; bool alongX = Mathf.Abs(n.z) > 0.5f;
                float len = alongX ? Sx : Sz, half = alongX ? Sz / 2 : Sx / 2;
                var uf = Quaternion.LookRotation(n) * Vector3.right;
                var w = new WF(c + n * half - uf * (len / 2), n);
                int nb = Mathf.Max(2, Mathf.RoundToInt(len / 3.2f));
                var belfry = new List<Vector2>(); for (int i = 0; i < nb; i++) { float bc = len * (i + 0.5f) / nb; belfry.Add(new Vector2(bc - 0.7f, bc + 0.7f)); }
                float dx = Mathf.Clamp(doorX, 1.2f, len - 1.2f);
                foreach (var r in rows)
                {
                    var open = new List<Vector2>();
                    if (r.slot == 1) open.AddRange(belfry);
                    if (fi == doorFace && r.y < 3.5f) open.Add(new Vector2(dx - 0.9f, dx + 0.9f));
                    foreach (var seg in Gaps(0f, len, open)) KitRow(hg, w, r.fam, seg.x, seg.y, r.y, mat, true, true, 1f);
                }
                // Belfry piers and opening sills.
                foreach (var seg in Gaps(0f, len, new List<Vector2>(belfry))) WBox(mb, w, seg.x, seg.y, Shaft, Bel, -0.6f, 0f, KitMortar, false);
                foreach (var b in belfry) WBox(mb, w, b.x - 0.1f, b.y + 0.1f, Shaft - 0.02f, Shaft + 0.12f, -0.6f, 0.12f, StoneL, false);
                if (fi == doorFace)
                {
                    WBox(mb, w, dx - 0.9f, dx + 0.9f, 0.12f, 3.6f, 0.0f, 0.02f, Dark, false);
                    WBox(mb, w, dx - 0.85f, dx - 0.01f, 0.12f, 3.5f, 0.02f, 0.1f, Wood, false);
                    WBox(mb, w, dx + 0.01f, dx + 0.85f, 0.12f, 3.5f, 0.02f, 0.1f, Wood, false);
                    WBox(mb, w, dx - 1.05f, dx + 1.05f, 0f, 0.12f, 0.0f, 0.4f, StoneD, false);
                }
                // Clock on every face, set onto the flush courses.
                float cx = len / 2, k2 = clockR / 1.2f;
                var up = Quaternion.FromToRotation(Vector3.up, n);
                mb.Prism(w.P(cx, clockY, 0f), 16, clockR + 0.1f, clockR + 0.1f, 0.3f, StoneL, up);
                mb.Prism(w.P(cx, clockY, 0.3f), 16, clockR - 0.15f, clockR - 0.15f, 0.06f, White, up);
                for (int i = 0; i < 12; i++)
                {
                    float a = i * 30f * Mathf.Deg2Rad;
                    var p = w.P(cx + Mathf.Sin(a) * 0.85f * k2, clockY + Mathf.Cos(a) * 0.85f * k2, 0.38f);
                    mb.Box(p, new Vector3(0.08f, (i % 3 == 0 ? 0.24f : 0.13f) * k2, 0.04f), Iron, Quaternion.LookRotation(n, w.u * Mathf.Sin(a) + Vector3.up * Mathf.Cos(a)));
                }
                mb.Box(w.P(cx + 0.18f * k2, clockY + 0.3f * k2, 0.4f), new Vector3(0.06f, 0.7f * k2, 0.03f), Iron, Quaternion.LookRotation(n, w.u * 0.5f + Vector3.up * 0.87f));
                mb.Box(w.P(cx - 0.3f * k2, clockY - 0.1f * k2, 0.42f), new Vector3(0.07f, 0.55f * k2, 0.03f), Iron, Quaternion.LookRotation(n, -w.u * 0.95f - Vector3.up * 0.3f));
            }
            // Bell hung from a wooden beam.
            mb.Box(c + Vector3.up * (Bel - 0.3f), new Vector3(Sx - 1.2f, 0.3f, 0.35f), WoodD, Quaternion.identity);
            mb.Prism(c + Vector3.up * (Shaft + 1.3f * sc), 12, 0.85f * sc, 0.5f * sc, 0.7f * sc, Bronze);
            mb.Prism(c + Vector3.up * (Shaft + 2.0f * sc), 12, 0.5f * sc, 0.38f * sc, 0.75f * sc, Bronze);
            mb.Prism(c + Vector3.up * (Shaft + 2.75f * sc), 12, 0.38f * sc, 0.1f * sc, 0.25f * sc, Bronze);
            // Pyramid roof of terracotta with hip ridges and an iron cross.
            float rr = Mathf.Max(Sx, Sz) / 2 + 0.9f, hRoof = 4.4f * sc;
            mb.Prism(c + Vector3.up * Top, 4, rr * 1.414f, 0f, hRoof, TileCols[0], 45f);
            for (int i = 0; i < 4; i++)
            {
                var a = c + Vector3.up * Top + Quaternion.Euler(0, 45 + i * 90, 0) * Vector3.forward * rr * 1.414f;
                Rod(mb, a + Vector3.up * 0.05f, c + Vector3.up * (Top + hRoof + 0.05f), 0.2f, TileRidge);
            }
            for (int i = 0; i < 4; i++)
            {
                var fn = Quaternion.Euler(0, i * 90, 0) * Vector3.forward;
                for (int k = 1; k < 5; k++)
                {
                    float t = k / 5f, hw = rr * (1 - t);
                    var p = c + Vector3.up * (Top + hRoof * t) + fn * (rr * (1 - t));
                    mb.Box(p + Vector3.up * 0.12f, new Vector3(hw * 2 - 0.3f, 0.24f, 0.4f), Vary(TileCols[k % TileCols.Length], 0.04f), Quaternion.LookRotation(fn));
                }
            }
            mb.Sphere(c + Vector3.up * (Top + hRoof + 0.25f), 0.25f, 8, 4, (i, j) => Bronze);
            mb.Box(c + Vector3.up * (Top + hRoof + 1.2f), new Vector3(0.1f, 1.5f, 0.1f), Iron, Quaternion.identity);
            mb.Box(c + Vector3.up * (Top + hRoof + 1.5f), new Vector3(0.8f, 0.1f, 0.1f), Iron, Quaternion.identity);
            forceFlat = false;
            usedNames.Remove("City_" + name + "_Body");
            Piece(hg, name + " Body", mb, solid);
            go.AddComponent<BoxCollider>().size = size;
            return SavePrefab(go, path);
        }

        /// <summary>Plain stone platform slab of an exact size (pivot at the centre, BoxCollider = size): dark stone body, light top plate.</summary>
        public static GameObject SlabPrefab(Vector3 size, bool rebuild = false)
        {
            string name = $"Slab_{Mathf.RoundToInt(size.x * 100)}x{Mathf.RoundToInt(size.y * 100)}x{Mathf.RoundToInt(size.z * 100)}";
            string path = SegWallDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            var go = new GameObject(name); var mb = new MB();
            float tp = Mathf.Min(0.1f, size.y * 0.4f);
            mb.Box(Vector3.down * (tp / 2), new Vector3(size.x - 0.04f, size.y - tp, size.z - 0.04f), StoneD, Quaternion.identity);
            mb.Box(Vector3.up * (size.y / 2 - tp / 2), new Vector3(size.x, tp, size.z), StoneL, Quaternion.identity);
            usedNames.Remove("City_" + name + "_Body");
            Piece(go.transform, name + " Body", mb, solid);
            go.AddComponent<BoxCollider>().size = size;
            return SavePrefab(go, path);
        }

        /// <summary>Stair step of an exact size (pivot at the centre, BoxCollider = size): flush StyleKit courses of the house blocks on all four sides
        /// (aligned to the top, tint as the houses), only the top is a light stone tread (inside the size, so step heights stay the same).</summary>
        public static GameObject StairStepPrefab(Vector3 size, int tint = 4, bool rebuild = false)
        {
            string name = $"StairStep_{Mathf.RoundToInt(size.x * 100)}x{Mathf.RoundToInt(size.y * 100)}x{Mathf.RoundToInt(size.z * 100)}_T{tint}";
            string path = SegWallDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            rnd = new System.Random(SeedOf(name));
            var go = new GameObject(name); var hg = go.transform; var mb = new MB();
            var mat = kitMats[Mathf.Clamp(tint, 0, 5)];
            float H = size.y, tp = Mathf.Min(0.1f, H * 0.3f), Hc = H - tp;
            forceFlat = true;
            mb.Box(Vector3.down * (tp / 2), new Vector3(size.x - 0.02f, Hc, size.z - 0.02f), KitMortar, Quaternion.identity);
            var rows = FillCourses(0f, Hc, Hc >= 2.2f, false);
            float used = 0f; foreach (var r in rows) used += r.h;
            float shift = Hc - used;   // courses end right under the tread; the leftover (< 0.5 m) stays at the bottom, hidden by the step below
            foreach (var n in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
            {
                bool alongX = Mathf.Abs(n.z) > 0.5f;
                float len = alongX ? size.x : size.z, half = alongX ? size.z / 2 : size.x / 2;
                var uf = Quaternion.LookRotation(n) * Vector3.right;
                var f = new WF(n * half - uf * (len / 2) - Vector3.up * (H / 2), n);
                foreach (var r in rows) KitRow(hg, f, r.fam, 0f, len, r.y + shift, mat, true, true, 1f);
            }
            mb.Box(Vector3.up * (H / 2 - tp / 2), new Vector3(size.x + 0.04f, tp, size.z + 0.04f), StoneL, Quaternion.identity);
            forceFlat = false;
            usedNames.Remove("City_" + name + "_Body");
            Piece(hg, name + " Body", mb, solid);
            go.AddComponent<BoxCollider>().size = size;
            return SavePrefab(go, path);
        }

        /// <summary>Small market shop of an exact size (pivot at the centre, BoxCollider = size), e.g. the SEG_07 market stalls: flush StyleKit courses,
        /// shop fronts (KitShop: window, door, sign) on the faces in fronts (bits +z|+x|-z|-x), a stone lintel band over them and a tiled gable roof.</summary>
        public static GameObject ShopStallPrefab(Vector3 size, int tint, int fronts, bool rebuild = false)
        {
            string name = $"ShopStall_{Mathf.RoundToInt(size.x * 100)}x{Mathf.RoundToInt(size.y * 100)}x{Mathf.RoundToInt(size.z * 100)}_T{tint}_S{fronts}";
            string path = SegHouseDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            rnd = new System.Random(SeedOf(name));
            var go = new GameObject(name); var hg = go.transform; var mb = new MB();
            var mat = kitMats[Mathf.Clamp(tint, 0, 5)];
            float H = size.y, shopTop = 3.6f;
            forceFlat = true;
            mb.Box(Vector3.zero, size - Vector3.one * 0.02f, KitMortar, Quaternion.identity);
            var rows = FillCourses(0f, H);
            var dirs = new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            for (int fi = 0; fi < 4; fi++)
            {
                var n = dirs[fi]; bool alongX = Mathf.Abs(n.z) > 0.5f;
                float len = alongX ? size.x : size.z, half = alongX ? size.z / 2 : size.x / 2;
                var uf = Quaternion.LookRotation(n) * Vector3.right;
                var f = new WF(n * half - uf * (len / 2) - Vector3.up * (H / 2), n);
                var open = new List<Vector2>(); var cs = new List<float>();
                if ((fronts & (1 << fi)) != 0 && len >= 3.0f && H >= shopTop + 0.3f)
                {
                    int k = Mathf.Max(1, Mathf.FloorToInt((len - 0.6f) / 3.2f));
                    for (int i = 0; i < k; i++) { float cx = len * (i + 0.5f) / k; cs.Add(cx); open.Add(new Vector2(cx - 1.2f, cx + 1.2f)); }
                }
                float cut = shopTop;
                foreach (var r in rows)
                {
                    bool crosses = cs.Count > 0 && r.y < shopTop - 0.01f;
                    if (crosses) cut = Mathf.Max(cut, r.y + r.h);
                    foreach (var seg in Gaps(0f, len, crosses ? open : new List<Vector2>())) KitRow(hg, f, r.fam, seg.x, seg.y, r.y, mat, true, true, 1f);
                }
                foreach (var cx in cs)
                {
                    KitShop(mb, f, cx, false);
                    WBox(mb, f, cx - 1.3f, cx + 1.3f, shopTop, cut, 0f, 0.07f, StoneL, false);   // lintel band up to the next course
                }
            }
            BlockRoof(mb, size);
            forceFlat = false;
            usedNames.Remove("City_" + name + "_Body");
            Piece(hg, name + " Body", mb, solid);
            go.AddComponent<BoxCollider>().size = size;
            return SavePrefab(go, path);
        }

        /// <summary>Market shop themes: the name on the signs, the sign board, the letters. The goods on the counters follow the index (ShopGoods).</summary>
        public static readonly string[] ShopNames = { "Techniczny personel", "Gość aukcji", "Piekarnia", "Owoce i warzywa", "Ryby", "Wędliny", "Sery",
            "Wina", "Kwiaciarnia", "Przyprawy", "Ceramika", "Kawiarnia", "Lodziarnia", "Antyki" };
        static readonly Color[] ShopBoard = { C(0.16f, 0.24f, 0.38f), C(0.40f, 0.09f, 0.13f), C(0.45f, 0.27f, 0.14f), C(0.20f, 0.40f, 0.20f), C(0.14f, 0.32f, 0.52f),
            C(0.55f, 0.12f, 0.10f), C(0.86f, 0.68f, 0.22f), C(0.32f, 0.12f, 0.26f), C(0.90f, 0.62f, 0.70f), C(0.82f, 0.42f, 0.12f), C(0.12f, 0.42f, 0.44f),
            C(0.12f, 0.26f, 0.18f), C(0.62f, 0.86f, 0.80f), C(0.24f, 0.16f, 0.10f) };
        static readonly Color InkW = new Color(0.97f, 0.96f, 0.92f, 1f), InkG = new Color(0.98f, 0.80f, 0.36f, 1f), InkD = new Color(0.22f, 0.12f, 0.08f, 1f);
        static readonly Color[] ShopInk = { InkW, InkG, InkW, InkW, InkW, InkW, InkD, InkG, InkD, InkW, InkW, InkG, InkD, InkG };

        /// <summary>Market shop of an exact size (drop-in for ShopStallPrefab, BoxCollider = size): shop fronts on all four faces, a big painted sign board
        /// with the shop name over the fronts of every face, a double-sided name banner on the roof ridge, a counter with the shop's goods in front of
        /// every shop window and a gable roof without chimneys. theme = index into ShopNames.</summary>
        public static GameObject MarketShopPrefab(Vector3 size, int tint, int theme, bool rebuild = false)
        {
            theme = Mathf.Clamp(theme, 0, ShopNames.Length - 1);
            string name = $"MarketShop_{Mathf.RoundToInt(size.x * 100)}x{Mathf.RoundToInt(size.y * 100)}x{Mathf.RoundToInt(size.z * 100)}_T{tint}_M{theme:00}";
            string path = SegHouseDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            rnd = new System.Random(SeedOf(name));
            var go = new GameObject(name); var hg = go.transform; var mb = new MB();
            var mat = kitMats[Mathf.Clamp(tint, 0, 5)];
            float H = size.y, shopTop = 3.6f;
            string title = ShopNames[theme]; Color board = ShopBoard[theme], ink = ShopInk[theme];
            Color trim = theme == 1 || theme == 7 || theme == 11 || theme == 13 ? Gold : StoneL;
            forceFlat = true;
            mb.Box(Vector3.zero, size - Vector3.one * 0.02f, KitMortar, Quaternion.identity);
            var rows = FillCourses(0f, H);
            var dirs = new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            for (int fi = 0; fi < 4; fi++)
            {
                var n = dirs[fi]; bool alongX = Mathf.Abs(n.z) > 0.5f;
                float len = alongX ? size.x : size.z, half = alongX ? size.z / 2 : size.x / 2;
                var uf = Quaternion.LookRotation(n) * Vector3.right;
                var f = new WF(n * half - uf * (len / 2) - Vector3.up * (H / 2), n);
                var open = new List<Vector2>(); var cs = new List<float>();
                if (len >= 3.0f && H >= shopTop + 0.3f)
                {
                    int k = Mathf.Max(1, Mathf.FloorToInt((len - 0.6f) / 3.2f));
                    for (int i = 0; i < k; i++) { float cx = len * (i + 0.5f) / k; cs.Add(cx); open.Add(new Vector2(cx - 1.2f, cx + 1.2f)); }
                }
                float cut = shopTop;
                foreach (var r in rows)
                {
                    bool crosses = cs.Count > 0 && r.y < shopTop - 0.01f;
                    if (crosses) cut = Mathf.Max(cut, r.y + r.h);
                    foreach (var seg in Gaps(0f, len, crosses ? open : new List<Vector2>())) KitRow(hg, f, r.fam, seg.x, seg.y, r.y, mat, true, true, 1f);
                }
                foreach (var cx in cs)
                {
                    KitShop(mb, f, cx, false);
                    WBox(mb, f, cx - 1.3f, cx + 1.3f, shopTop, cut, 0f, 0.07f, StoneL, false);
                    ShopGoods(mb, f, cx - 1.12f, cx + 0.24f, theme);
                }
                if (cs.Count == 0) continue;
                // Sign board over the shop fronts, across the whole face, with the shop name.
                float y0 = 3.04f, y1 = Mathf.Min(H - 0.08f, y0 + 1.25f), a = 0.22f, b = len - 0.22f;
                WBox(mb, f, a - 0.07f, b + 0.07f, y0 - 0.07f, y1 + 0.07f, 0.12f, 0.2f, trim, false);
                WBox(mb, f, a, b, y0, y1, 0.12f, 0.23f, board, false);
                SignText(hg, "Sign " + fi, title, f.P(len / 2, (y0 + y1) / 2, 0.236f), n, b - a - 0.35f, (y1 - y0) * 0.78f, ink);
            }
            BlockRoof(mb, size, false);
            // Name banner on two iron posts over the ridge, readable from both long sides.
            bool longX = size.x >= size.z; float RW = longX ? size.x : size.z, RD = longX ? size.z : size.x;
            float ridge = H / 2 + 0.35f + RD / 2 * Mathf.Tan(Pitch * Mathf.Deg2Rad);
            float bw = Mathf.Min(RW - 1.4f, 4.6f), bh = 1.0f, yb = ridge + 0.75f;
            Vector3 along = longX ? Vector3.right : Vector3.forward, nrm = longX ? Vector3.forward : Vector3.right;
            var br = Quaternion.LookRotation(nrm);
            mb.Box(Vector3.up * (yb + bh / 2), new Vector3(bw + 0.14f, bh + 0.14f, 0.07f), trim, br);
            mb.Box(Vector3.up * (yb + bh / 2), new Vector3(bw, bh, 0.11f), board, br);
            foreach (int s in new[] { -1, 1 })
            {
                mb.Box(along * s * (bw / 2 - 0.4f) + Vector3.up * ((ridge + 0.2f + yb) / 2), new Vector3(0.08f, yb - ridge - 0.2f + 0.05f, 0.08f), Iron, br);
                SignText(hg, "Banner " + (s < 0 ? "B" : "A"), title, Vector3.up * (yb + bh / 2) + nrm * (s * 0.058f), nrm * s, bw - 0.3f, bh * 0.72f, ink);
            }
            forceFlat = false;
            usedNames.Remove("City_" + name + "_Body");
            Piece(hg, name + " Body", mb, solid);
            go.AddComponent<BoxCollider>().size = size;
            return SavePrefab(go, path);
        }

        /// <summary>World-space TextMeshPro label (auto-sized into w x h) at local pos, readable from the side 'facing' points to.</summary>
        static void SignText(Transform parent, string name, string text, Vector3 pos, Vector3 facing, float w, float h, Color col)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            var t = g.AddComponent<TMPro.TextMeshPro>();
            var font = TMPro.TMP_Settings.defaultFontAsset;
            if (font == null) font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            t.font = font; t.text = text; t.color = col; t.fontStyle = TMPro.FontStyles.Bold | TMPro.FontStyles.UpperCase;
            t.alignment = TMPro.TextAlignmentOptions.Center; t.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            t.enableAutoSizing = true; t.fontSizeMin = 0.3f; t.fontSizeMax = 14f; t.characterSpacing = 4f;
            t.rectTransform.sizeDelta = new Vector2(w, h);
            g.transform.localPosition = pos; g.transform.localRotation = Quaternion.LookRotation(-facing, Vector3.up);
            t.ForceMeshUpdate();
        }

        /// <summary>Wooden counter in front of a shop window (a..b along the facade, 0.55 m out) with a lower shelf and the goods of the shop theme.</summary>
        static void ShopGoods(MB mb, WF w, float a, float b, int theme)
        {
            const float top = 0.8f, sh = 0.34f, dm = 0.29f;
            WBox(mb, w, a - 0.03f, b + 0.03f, top - 0.05f, top, 0.02f, 0.56f, Wood, false);
            foreach (float x in new[] { a, b - 0.05f }) foreach (float d in new[] { 0.05f, 0.48f }) WBox(mb, w, x, x + 0.05f, 0.12f, top - 0.05f, d, d + 0.05f, WoodD, false);
            WBox(mb, w, a + 0.04f, b - 0.04f, sh - 0.04f, sh, 0.07f, 0.52f, WoodD, false);
            Vector3 Pt(float x, float y, float d) => w.P(x, y, d);
            var lay = w.R * Quaternion.Euler(0, 0, -90);   // prism lying along the facade (+u)
            void Crate(float x0, float x1, float y, float h, Color fill)
            {
                WBox(mb, w, x0, x1, y, y + 0.03f, 0.1f, 0.48f, Wood, false);
                WBox(mb, w, x0, x1, y, y + h, 0.1f, 0.12f, Wood, false); WBox(mb, w, x0, x1, y, y + h, 0.46f, 0.48f, Wood, false);
                WBox(mb, w, x0, x0 + 0.02f, y, y + h, 0.1f, 0.48f, Wood, false); WBox(mb, w, x1 - 0.02f, x1, y, y + h, 0.1f, 0.48f, Wood, false);
                float gx = (x1 - x0 - 0.04f) / 3f, gd = 0.34f / 3f;
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
                    mb.Box(Pt(x0 + 0.02f + gx * (i + 0.5f), y + h - 0.03f + Rf(-0.01f, 0.02f), 0.12f + gd * (j + 0.5f)), Vector3.one * Mathf.Min(gx, gd) * 0.85f, Vary(fill, 0.08f), w.R * Quaternion.Euler(0, Rf(-25f, 25f), 0));
            }
            Color bread = C(0.80f, 0.55f, 0.27f), crust = C(0.62f, 0.38f, 0.17f), cheese = C(0.95f, 0.78f, 0.30f), bottle = C(0.12f, 0.24f, 0.14f, 0.5f), wineR = C(0.30f, 0.06f, 0.09f, 0.5f);
            int n = 3; float sw = (b - a) / n;
            for (int i = 0; i < n; i++)
            {
                float x0 = a + sw * i + 0.03f, x1 = a + sw * (i + 1) - 0.03f, xm = (x0 + x1) / 2;
                switch (theme)
                {
                    case 0: case 1:   // folded clothes on the counter; hard hats (staff) or hat boxes (guests) on the shelf
                    {
                        var cols = theme == 0 ? new[] { C(0.14f, 0.20f, 0.34f, 0.02f), RescueO, BlanketGr } : new[] { C(0.06f, 0.06f, 0.07f, 0.05f), White, C(0.42f, 0.08f, 0.12f, 0.02f) };
                        int layers = 3 + rnd.Next(3);
                        for (int k = 0; k < layers; k++) WBox(mb, w, x0 + 0.02f, x1 - 0.02f, top + k * 0.055f, top + k * 0.055f + 0.05f, 0.12f, 0.46f, Vary(cols[i], 0.05f), false);
                        if (theme == 0) { mb.Prism(Pt(xm, sh, dm), 10, 0.15f, 0.07f, 0.13f, GenY); mb.Prism(Pt(xm, sh, dm), 10, 0.19f, 0.19f, 0.02f, GenY); }
                        else { mb.Prism(Pt(xm, sh, dm), 12, 0.14f, 0.14f, 0.2f, Canvas); mb.Prism(Pt(xm, sh + 0.2f, dm), 12, 0.15f, 0.15f, 0.04f, cols[i]); }
                        break;
                    }
                    case 2:   // bakery: long loaves in trays, round loaves on the shelf
                        WBox(mb, w, x0, x1, top, top + 0.03f, 0.1f, 0.48f, Wood, false);
                        for (int k = 0; k < 3; k++) mb.Prism(Pt(x0 + 0.04f, top + 0.09f, 0.17f + k * 0.12f), 6, 0.055f, 0.055f, x1 - x0 - 0.08f, Vary(k == 1 ? crust : bread, 0.06f), lay);
                        mb.Prism(Pt(xm, sh, dm), 8, 0.13f, 0.08f, 0.11f, Vary(bread, 0.06f));
                        break;
                    case 3:   // fruit and vegetables in crates
                        Crate(x0, x1, top, 0.14f, new[] { C(0.78f, 0.12f, 0.10f), C(0.95f, 0.55f, 0.10f), C(0.45f, 0.65f, 0.20f) }[i]);
                        Crate(x0, x1, sh, 0.14f, new[] { C(0.95f, 0.85f, 0.20f), C(0.40f, 0.15f, 0.40f), C(0.85f, 0.20f, 0.12f) }[i]);
                        break;
                    case 4:   // fish on ice
                        WBox(mb, w, x0 - 0.03f, x1 + 0.03f, top, top + 0.06f, 0.08f, 0.5f, White, false);
                        for (int k = 0; k < 3; k++) WBox(mb, w, x0 + 0.04f, x1 - 0.04f, top + 0.06f, top + 0.1f, 0.13f + k * 0.12f, 0.21f + k * 0.12f, Vary(k == 1 ? MetalD : Metal, 0.05f), false);
                        Crate(x0, x1, sh, 0.12f, MetalD);
                        break;
                    case 5:   // salami and hams
                        if (i % 2 == 0) for (int k = 0; k < 3; k++) mb.Prism(Pt(x0 + 0.03f, top + 0.05f, 0.17f + k * 0.12f), 8, 0.045f, 0.045f, x1 - x0 - 0.06f, Vary(C(0.55f, 0.15f, 0.12f), 0.06f), lay);
                        else mb.Prism(Pt(x0 + 0.04f, top + 0.11f, dm), 8, 0.11f, 0.05f, x1 - x0 - 0.08f, C(0.80f, 0.45f, 0.42f), lay);
                        Crate(x0, x1, sh, 0.12f, C(0.62f, 0.22f, 0.16f));
                        break;
                    case 6:   // cheese wheels
                        for (int k = 0, m = 2 + rnd.Next(2); k < m; k++) mb.Prism(Pt(xm, top + k * 0.105f, dm), 12, 0.17f, 0.17f, 0.1f, Vary(cheese, 0.06f));
                        mb.Prism(Pt(xm - 0.1f, sh, dm), 12, 0.09f, 0.09f, 0.07f, C(0.90f, 0.55f, 0.18f)); mb.Prism(Pt(xm + 0.1f, sh, dm), 12, 0.09f, 0.09f, 0.07f, cheese);
                        break;
                    case 7:   // wine bottles, a barrel on the shelf
                        for (int k = 0; k < 4; k++)
                        {
                            float x = x0 + 0.06f + k * (x1 - x0 - 0.12f) / 3f, d = k % 2 == 0 ? 0.2f : 0.36f; var c = k % 2 == 0 ? bottle : wineR;
                            mb.Prism(Pt(x, top, d), 8, 0.036f, 0.036f, 0.21f, c); mb.Prism(Pt(x, top + 0.21f, d), 8, 0.036f, 0.013f, 0.08f, c); mb.Prism(Pt(x, top + 0.29f, d), 6, 0.014f, 0.014f, 0.03f, Wood);
                        }
                        mb.Prism(Pt(xm, sh, dm), 10, 0.15f, 0.15f, 0.35f, Wood);
                        mb.Prism(Pt(xm, sh + 0.06f, dm), 10, 0.155f, 0.155f, 0.03f, Iron); mb.Prism(Pt(xm, sh + 0.26f, dm), 10, 0.155f, 0.155f, 0.03f, Iron);
                        break;
                    case 8:   // flower pots
                        mb.Prism(Pt(xm, top, dm), 8, 0.11f, 0.15f, 0.2f, Pot);
                        mb.Prism(Pt(xm, top + 0.2f, dm), 7, 0.17f, 0.12f, 0.12f, Leaf);
                        for (int k = 0; k < 6; k++) mb.Box(Pt(xm + Rf(-0.12f, 0.12f), top + 0.33f + Rf(0f, 0.06f), dm + Rf(-0.12f, 0.12f)), Vector3.one * 0.065f, new[] { FlowerR, FlowerP, FlowerW, GenY }[rnd.Next(4)], w.R);
                        mb.Prism(Pt(xm, sh, dm), 10, 0.12f, 0.13f, 0.18f, Metal); mb.Prism(Pt(xm, sh + 0.18f, dm), 7, 0.12f, 0.06f, 0.14f, LeafD);
                        break;
                    case 9:   // spice sacks, jars on the shelf
                    {
                        var sp = new[] { C(0.72f, 0.15f, 0.10f), C(0.93f, 0.72f, 0.15f), C(0.48f, 0.28f, 0.14f) }[i];
                        mb.Prism(Pt(xm, top, dm), 8, 0.16f, 0.14f, 0.22f, Canvas); mb.Prism(Pt(xm, top + 0.2f, dm), 8, 0.13f, 0.06f, 0.06f, sp);
                        for (int k = -1; k <= 1; k++) { mb.Prism(Pt(xm + k * 0.12f, sh, dm), 8, 0.05f, 0.05f, 0.14f, Vary(sp, 0.1f)); mb.Prism(Pt(xm + k * 0.12f, sh + 0.14f, dm), 8, 0.052f, 0.052f, 0.02f, Wood); }
                        break;
                    }
                    case 10:   // ceramic jugs, stacked plates on the shelf
                    {
                        var c = new[] { BoxBlue, White, Pot }[i];
                        mb.Prism(Pt(xm, top, dm), 10, 0.09f, 0.14f, 0.16f, c); mb.Prism(Pt(xm, top + 0.16f, dm), 10, 0.14f, 0.06f, 0.1f, c); mb.Prism(Pt(xm, top + 0.26f, dm), 10, 0.06f, 0.07f, 0.06f, c);
                        for (int k = 0; k < 4; k++) mb.Prism(Pt(xm, sh + k * 0.025f, dm), 12, 0.15f, 0.15f, 0.02f, k % 2 == 0 ? White : BoxBlue);
                        break;
                    }
                    case 11:   // café: espresso machine in the middle, cups and saucers, coffee sacks on the shelf
                        if (i == 1) { WBox(mb, w, x0, x1, top, top + 0.3f, 0.14f, 0.44f, Metal, false); WBox(mb, w, x0 + 0.05f, x1 - 0.05f, top + 0.2f, top + 0.26f, 0.44f, 0.5f, Iron, false); }
                        else for (int k = 0; k < 2; k++) { var p = Pt(xm + (k - 0.5f) * 0.18f, top, 0.22f + k * 0.12f); mb.Prism(p, 12, 0.065f, 0.065f, 0.012f, White); mb.Prism(p + Vector3.up * 0.012f, 10, 0.035f, 0.045f, 0.06f, White); }
                        mb.Prism(Pt(xm, sh, dm), 8, 0.15f, 0.13f, 0.2f, Canvas); mb.Prism(Pt(xm, sh + 0.19f, dm), 8, 0.12f, 0.05f, 0.05f, C(0.30f, 0.18f, 0.10f));
                        break;
                    case 12:   // gelato counter: metal tray with tubs under a glass front
                    {
                        var pastel = new[] { C(0.98f, 0.80f, 0.85f), C(0.75f, 0.92f, 0.70f), C(0.98f, 0.95f, 0.75f), C(0.55f, 0.35f, 0.22f), C(0.80f, 0.75f, 0.95f), C(0.98f, 0.70f, 0.45f) };
                        WBox(mb, w, x0 - 0.03f, x1 + 0.03f, top, top + 0.04f, 0.08f, 0.5f, Metal, false);
                        for (int k = 0; k < 2; k++) WBox(mb, w, x0 + 0.02f, x1 - 0.02f, top + 0.04f, top + 0.12f, 0.12f + k * 0.18f, 0.28f + k * 0.18f, pastel[(i * 2 + k) % pastel.Length], false);
                        WBox(mb, w, x0 - 0.03f, x1 + 0.03f, top, top + 0.3f, 0.5f, 0.52f, Glass, false);
                        Crate(x0, x1, sh, 0.12f, C(0.85f, 0.70f, 0.45f));
                        break;
                    }
                    default:   // antiques: candlesticks, books, a small chest
                        if (i == 0) for (int k = 0; k < 2; k++) { var p = Pt(xm + (k - 0.5f) * 0.16f, top, dm); mb.Prism(p, 8, 0.06f, 0.05f, 0.02f, Gold); mb.Prism(p, 8, 0.015f, 0.015f, 0.24f, Gold); mb.Prism(p + Vector3.up * 0.24f, 8, 0.016f, 0.016f, 0.08f, White); }
                        else if (i == 1) for (int k = 0; k < 4; k++) WBox(mb, w, x0 + 0.04f + Rf(0f, 0.04f), x1 - 0.04f - Rf(0f, 0.04f), top + k * 0.05f, top + k * 0.05f + 0.045f, 0.15f, 0.42f, new[] { C(0.45f, 0.10f, 0.10f), C(0.15f, 0.25f, 0.18f), C(0.20f, 0.22f, 0.40f), C(0.50f, 0.36f, 0.20f) }[k], false);
                        else { WBox(mb, w, x0 + 0.02f, x1 - 0.02f, top, top + 0.2f, 0.14f, 0.44f, WoodD, false); WBox(mb, w, x0 + 0.02f, x1 - 0.02f, top + 0.08f, top + 0.11f, 0.13f, 0.45f, Gold, false); }
                        mb.Prism(Pt(xm, sh, dm), 10, 0.1f, 0.12f, 0.05f, Bronze); mb.Prism(Pt(xm, sh + 0.05f, dm), 10, 0.025f, 0.025f, 0.12f, Bronze); mb.Prism(Pt(xm, sh + 0.17f, dm), 12, 0.11f, 0.11f, 0.05f, Bronze);
                        break;
                }
            }
        }

        const string AuctionDir = "Assets/Prefabs/StyleCity/Auction";
        static readonly Color WoodL = C(0.60f, 0.40f, 0.22f, 0.1f), Shingle = C(0.38f, 0.24f, 0.14f, 0.05f),
            Walnut = C(0.27f, 0.15f, 0.09f, 0.35f), WalnutD = C(0.17f, 0.09f, 0.05f, 0.35f), WalnutL = C(0.36f, 0.21f, 0.12f, 0.35f), Velvet = C(0.46f, 0.07f, 0.10f, 0.02f), Leather = C(0.12f, 0.27f, 0.18f, 0.15f);

        /// <summary>Auction hall props of an exact size (pivot at the volume centre, like a scaled greybox cube; front = +z, towards the stage):
        /// "StoneFence" stone balustrade (posts, balusters, top rail), "WoodTable" small bidding table (top at 0.78 m), "WoodChair" (seat on the top of
        /// the size, backrest at -z), "WoodStage" plank stage, "WoodPillar", "WoodRoof" (shingles, joists, fascia), "GunStand" wooden display stand with two
        /// forks whose tops are at the local points a and b. BoxCollider = size when collider is true.</summary>
        public static GameObject AuctionPropPrefab(string kind, Vector3 size, bool collider, bool rebuild = false, Vector3 a = default, Vector3 b = default)
        {
            string name = $"Auction{kind}_{Mathf.RoundToInt(size.x * 100)}x{Mathf.RoundToInt(size.y * 100)}x{Mathf.RoundToInt(size.z * 100)}" + (collider ? "" : "_NC");
            if (kind == "GunStand") name += "_" + (SeedOf(a.ToString("F2") + b.ToString("F2")) % 100000);
            string path = AuctionDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            rnd = new System.Random(SeedOf(name));
            var go = new GameObject(name); var hg = go.transform; var mb = new MB();
            float W = size.x, H = size.y, D = size.z;
            var I = Quaternion.identity;
            Vector3 Bp(float x, float y, float z) => new Vector3(x, y - H / 2, z);   // y from the bottom
            void Bx(Vector3 c, float sx, float sy, float sz, Color col) => mb.Box(c, new Vector3(sx, sy, sz), col, I);
            switch (kind)
            {
                case "StoneFence":
                {
                    bool lx = W >= D; float L = lx ? W : D, T = lx ? D : W;
                    Vector3 A = lx ? Vector3.right : Vector3.forward, N = lx ? Vector3.forward : Vector3.right;
                    Vector3 Q(float u, float y, float t) => A * (u - L / 2) + Vector3.up * (y - H / 2) + N * t;
                    Vector3 S(float su, float sy, float st) => A * su + Vector3.up * sy + N * st;
                    mb.Box(Q(L / 2, 0.11f, 0), S(L, 0.22f, T), StoneD, I);
                    int np = Mathf.Max(2, Mathf.RoundToInt(L / 1.6f) + 1); float pw = Mathf.Min(0.4f, T);
                    var us = new List<float>();
                    for (int k = 0; k < np; k++) us.Add(pw / 2 + (L - pw) * k / (np - 1));
                    foreach (var u in us)
                    {
                        mb.Box(Q(u, (0.22f + H - 0.12f) / 2, 0), S(pw, H - 0.34f, pw), StoneL, I);
                        mb.Box(Q(u, H - 0.06f, 0), S(Mathf.Min(T, pw + 0.08f), 0.12f, Mathf.Min(T, pw + 0.08f)), Stone, I);
                    }
                    mb.Box(Q(L / 2, H - 0.2f, 0), S(L - 0.02f, 0.12f, T * 0.7f), StoneL, I);
                    float hh = (H - 0.26f - 0.22f) / 2;
                    for (int k = 0; k + 1 < us.Count; k++)
                    {
                        float u0 = us[k] + pw / 2, gap = us[k + 1] - pw / 2 - u0; int nb = Mathf.FloorToInt(gap / 0.26f);
                        for (int j = 0; j < nb; j++)
                        {
                            float x = u0 + gap * (j + 0.5f) / nb;
                            mb.Prism(Q(x, 0.22f, 0), 8, 0.05f, 0.085f, hh, Stone); mb.Prism(Q(x, 0.22f + hh, 0), 8, 0.085f, 0.05f, hh, Stone);
                        }
                    }
                    break;
                }
                case "WoodTable":   // solid walnut bidding desk: plinth, closed panelled body, corner pilasters, thick moulded top with a leather inlay
                {
                    float th = Mathf.Min(0.78f, H), y0 = 0.07f, y1 = th - 0.07f;
                    Bx(Bp(0, 0.035f, 0), W - 0.02f, 0.07f, D - 0.02f, WalnutD);
                    Bx(Bp(0, 0.08f, 0), W - 0.06f, 0.03f, D - 0.06f, Walnut);
                    Bx(Bp(0, (y0 + y1) / 2, 0), W - 0.1f, y1 - y0, D - 0.1f, Walnut);
                    foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) Bx(Bp(sx * (W / 2 - 0.065f), (y0 + y1) / 2, sz * (D / 2 - 0.065f)), 0.09f, y1 - y0, 0.09f, WalnutD);
                    foreach (int sz in new[] { -1, 1 })
                    {
                        float zf = sz * (D / 2 - 0.05f + 0.006f);
                        // raised panels (two on the stage side, a drawer over one panel on the bidder side)
                        foreach (int sx in new[] { -1, 1 }) { Bx(Bp(sx * (W - 0.2f) / 4, (y0 + y1) / 2 - 0.05f, zf), (W - 0.2f) / 2 - 0.08f, y1 - y0 - 0.24f, 0.012f, WalnutL); Bx(Bp(sx * (W - 0.2f) / 4, (y0 + y1) / 2 - 0.05f, zf + sz * 0.007f), (W - 0.2f) / 2 - 0.16f, y1 - y0 - 0.32f, 0.008f, Walnut); }
                        Bx(Bp(0, y1 - 0.07f, zf), W - 0.22f, 0.09f, 0.012f, sz < 0 ? WalnutL : Walnut);
                        if (sz < 0) mb.Prism(Bp(0, y1 - 0.07f, zf - 0.006f), 10, 0.018f, 0.018f, 0.025f, Gold, Quaternion.Euler(-90f, 0, 0));
                        Bx(Bp(0, y1 - 0.005f, sz * (D / 2 - 0.045f)), W - 0.1f, 0.012f, 0.01f, Gold);   // brass line under the top
                    }
                    foreach (int sx in new[] { -1, 1 }) Bx(Bp(sx * (W / 2 - 0.05f + 0.006f), (y0 + y1) / 2 - 0.05f, 0), 0.012f, y1 - y0 - 0.24f, D - 0.24f, WalnutL);
                    Bx(Bp(0, th - 0.055f, 0), W - 0.02f, 0.03f, D - 0.02f, WalnutD);
                    Bx(Bp(0, th - 0.02f, 0), W + 0.02f, 0.04f, D + 0.02f, WalnutL);
                    Bx(Bp(0, th + 0.001f, -0.01f), W - 0.16f, 0.004f, D - 0.14f, Leather);
                    Bx(Bp(0, (y0 + y1) / 2 + 0.12f, D / 2 - 0.05f + 0.02f), 0.2f, 0.06f, 0.006f, Gold);   // lot number plate towards the stage
                    // Bid paddle, a leather catalogue and a brass bell.
                    Bx(Bp(W * 0.22f, th + 0.01f, -D * 0.18f), 0.03f, 0.016f, D * 0.36f, WalnutD);
                    mb.Prism(Bp(W * 0.22f, th + 0.003f, D * 0.1f), 14, 0.075f, 0.075f, 0.012f, White);
                    Bx(Bp(W * 0.22f, th + 0.016f, D * 0.1f), 0.05f, 0.004f, 0.06f, Red);
                    Bx(Bp(-W * 0.18f, th + 0.018f, -0.01f), 0.2f, 0.032f, Mathf.Min(0.26f, D - 0.1f), C(0.30f, 0.08f, 0.10f, 0.1f));
                    Bx(Bp(-W * 0.18f + 0.09f, th + 0.018f, -0.01f), 0.02f, 0.034f, Mathf.Min(0.26f, D - 0.1f) + 0.002f, Gold);
                    mb.Prism(Bp(-W * 0.4f, th + 0.003f, 0), 12, 0.045f, 0.045f, 0.012f, WalnutD);
                    mb.Prism(Bp(-W * 0.4f, th + 0.015f, 0), 12, 0.04f, 0.012f, 0.05f, Gold);
                    mb.Prism(Bp(-W * 0.4f, th + 0.065f, 0), 8, 0.008f, 0.008f, 0.03f, Gold);
                    break;
                }
                case "WoodChair":   // walnut armchair: turned legs, upholstered burgundy seat and back, arm rests
                {
                    float st = H - 0.02f, ap = st - 0.07f, back = -(D / 2 - 0.04f);
                    foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                    {
                        var p = Bp(sx * (W / 2 - 0.045f), 0, sz * (D / 2 - 0.045f));
                        mb.Prism(p, 8, 0.024f, 0.03f, 0.05f, WalnutD);
                        mb.Prism(p + Vector3.up * 0.05f, 8, 0.024f, 0.034f, ap - 0.13f, Walnut);
                        mb.Prism(p + Vector3.up * (ap - 0.08f), 8, 0.042f, 0.042f, 0.03f, WalnutL);
                        Bx(p + Vector3.up * (ap - 0.03f), 0.06f, 0.06f, 0.06f, Walnut);
                    }
                    Bx(Bp(0, ap - 0.03f, 0), W - 0.03f, 0.07f, D - 0.03f, Walnut);   // seat rails
                    Bx(Bp(0, ap + 0.005f, 0), W - 0.02f, 0.01f, D - 0.02f, WalnutL);
                    Bx(Bp(0, (ap + st) / 2 + 0.005f, 0.005f), W - 0.05f, st - ap - 0.01f, D - 0.06f, Velvet);   // cushion, top at the seat height
                    Bx(Bp(0, ap + 0.012f, D / 2 - 0.03f), W - 0.05f, 0.006f, 0.006f, Gold);
                    foreach (int sz in new[] { -1, 1 }) Bx(Bp(0, 0.16f, sz * (D / 2 - 0.045f)), W - 0.09f, 0.03f, 0.03f, WalnutD);
                    Bx(Bp(0, 0.16f, 0), 0.03f, 0.03f, D - 0.09f, WalnutD);
                    // Back: two posts, a crested top rail and an upholstered panel.
                    foreach (int sx in new[] { -1, 1 }) Bx(Bp(sx * (W / 2 - 0.045f), ap + 0.31f, back), 0.055f, 0.62f, 0.05f, Walnut);
                    Bx(Bp(0, ap + 0.59f, back), W - 0.04f, 0.08f, 0.06f, WalnutL);
                    Bx(Bp(0, ap + 0.645f, back), W * 0.45f, 0.04f, 0.05f, WalnutL);
                    Bx(Bp(0, ap + 0.33f, back + 0.01f), W - 0.13f, 0.4f, 0.06f, Velvet);
                    Bx(Bp(0, ap + 0.33f, back - 0.022f), W - 0.11f, 0.44f, 0.02f, Walnut);
                    // Arm rests on turned posts.
                    foreach (int sx in new[] { -1, 1 })
                    {
                        float ax = sx * (W / 2 - 0.035f);
                        mb.Prism(Bp(ax, st, D / 2 - 0.08f), 8, 0.02f, 0.016f, 0.2f, Walnut);
                        Bx(Bp(ax, st + 0.215f, -0.02f), 0.065f, 0.035f, D - 0.06f, WalnutL);
                        Bx(Bp(ax, st + 0.24f, -0.03f), 0.05f, 0.015f, D - 0.16f, Velvet);
                    }
                    break;
                }
                case "WoodStage":
                {
                    Bx(Vector3.zero, W - 0.02f, H - 0.02f, D - 0.02f, WoodD);
                    int n = Mathf.Max(1, Mathf.FloorToInt(D / 0.24f)); float bw = D / n;
                    for (int k = 0; k < n; k++) Bx(Bp(0, H - 0.03f, -D / 2 + bw * (k + 0.5f)), W, 0.06f, bw - 0.012f, Vary(k % 2 == 0 ? WoodL : Wood, 0.05f));
                    Bx(Bp(0, H - 0.09f, 0), W + 0.06f, 0.06f, D + 0.06f, WoodD);
                    foreach (int s in new[] { -1, 1 })
                    {
                        int m = Mathf.CeilToInt(W / 0.3f); for (int k = 0; k < m; k++) Bx(Bp(-W / 2 + W / m * (k + 0.5f), (H - 0.12f) / 2, s * (D / 2 + 0.005f)), W / m - 0.015f, H - 0.12f, 0.03f, Vary(Wood, 0.06f));
                        m = Mathf.CeilToInt(D / 0.3f); for (int k = 0; k < m; k++) Bx(Bp(s * (W / 2 + 0.005f), (H - 0.12f) / 2, -D / 2 + D / m * (k + 0.5f)), 0.03f, H - 0.12f, D / m - 0.015f, Vary(Wood, 0.06f));
                    }
                    break;
                }
                case "WoodPillar":
                {
                    float pw = Mathf.Min(W, D) * 0.6f;
                    Bx(Bp(0, H / 2, 0), pw, H, pw, Wood);
                    Bx(Bp(0, 0.15f, 0), W * 0.95f, 0.3f, D * 0.95f, WoodD); Bx(Bp(0, 0.36f, 0), W * 0.78f, 0.12f, D * 0.78f, WoodL);
                    Bx(Bp(0, H - 0.12f, 0), W, 0.24f, D, WoodD); Bx(Bp(0, H - 0.31f, 0), W * 0.8f, 0.14f, D * 0.8f, WoodL);
                    foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) Bx(Bp(sx * pw / 2, H / 2, sz * pw / 2), 0.05f, H - 0.9f, 0.05f, WoodD);
                    foreach (var d in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
                        mb.Box(Bp(0, H - 0.62f, 0) + d * (pw / 2 + 0.09f), new Vector3(0.08f, 0.45f, 0.08f), WoodD, Quaternion.LookRotation(d) * Quaternion.Euler(35f, 0, 0));
                    break;
                }
                case "WoodRoof":
                {
                    float dk = 0.14f;
                    Bx(Bp(0, H - 0.1f, 0), W - 0.1f, 0.08f, D - 0.1f, Wood);   // deck between the fascia boards (no face in the fascia plane)
                    int rows = Mathf.Max(1, Mathf.FloorToInt(D / 0.3f)); float rd = D / rows;
                    for (int j = 0; j < rows; j++)
                    {
                        int cnt = Mathf.CeilToInt(W / 0.35f); float sw = W / cnt, off = j % 2 == 0 ? 0f : sw / 2;
                        for (int k = 0; k < cnt; k++)
                        {
                            float x0 = -W / 2 + off + sw * k, x1 = Mathf.Min(W / 2, x0 + sw);
                            if (x1 - x0 < 0.05f) continue;
                            Bx(Bp((x0 + x1) / 2 - 0.006f, H - 0.03f + (j % 2) * 0.005f, -D / 2 + rd * (j + 0.5f)), x1 - x0 - 0.012f, 0.06f, rd - 0.01f, Vary(k % 3 == 0 ? WoodD : Shingle, 0.08f));
                        }
                        if (j % 2 == 1) Bx(Bp(-W / 2 + off / 2, H - 0.03f + 0.005f, -D / 2 + rd * (j + 0.5f)), off - 0.012f, 0.06f, rd - 0.01f, Vary(Shingle, 0.08f));
                    }
                    for (float x = -W / 2 + 0.35f; x < W / 2 - 0.2f; x += 0.9f) Bx(Bp(x, (H - dk) / 2, 0), 0.12f, H - dk, D - 0.1f, WoodD);
                    foreach (int s in new[] { -1, 1 }) Bx(Bp(0, (0.05f + H - dk) / 2, s * (D / 2 - 0.3f)), W - 0.1f, H - dk - 0.05f, 0.14f, Wood);   // beams end 5 cm above the joist bottoms
                    foreach (int s in new[] { -1, 1 }) { Bx(Bp(0, (H - 0.06f) / 2, s * (D / 2 - 0.025f)), W, H - 0.06f, 0.05f, WoodD); Bx(Bp(s * (W / 2 - 0.025f), (H - 0.06f) / 2, 0), 0.05f, H - 0.06f, D - 0.1f, WoodD); }   // end boards between the long ones
                    break;
                }
                case "GunStand":
                {
                    Bx(Bp(0, 0.06f, 0), W, 0.12f, D, WoodD);
                    Bx(Bp(0, (0.12f + H - 0.08f) / 2, 0), W - 0.2f, H - 0.2f, D - 0.2f, Wood);
                    foreach (int sz in new[] { -1, 1 }) foreach (int sx in new[] { -1, 1 })
                        Bx(Bp(sx * (W - 0.2f) / 4, (0.12f + H - 0.08f) / 2, sz * (D / 2 - 0.1f + 0.012f)), (W - 0.2f) / 2 - 0.14f, H - 0.4f, 0.025f, WoodL);
                    Bx(Bp(0, H - 0.04f, 0), W, 0.08f, D, WoodD);
                    foreach (var p in new[] { a, b })
                    {
                        float y0 = H / 2, y1 = p.y - 0.05f;
                        Bx(new Vector3(p.x, y0 + 0.02f, p.z), 0.22f, 0.04f, 0.22f, Wood);
                        Bx(new Vector3(p.x, (y0 + y1) / 2, p.z), 0.08f, y1 - y0, 0.08f, WoodD);
                        Bx(new Vector3(p.x, p.y - 0.05f, p.z), 0.1f, 0.05f, 0.34f, Wood);
                        foreach (int s in new[] { -1, 1 }) Bx(new Vector3(p.x, p.y + 0.02f, p.z + s * 0.15f), 0.1f, 0.12f, 0.04f, Wood);
                    }
                    break;
                }
                default: Object.DestroyImmediate(go); throw new System.ArgumentException("unknown auction prop " + kind);
            }
            usedNames.Remove("City_" + name + "_Body");
            Piece(hg, name + " Body", mb, solid);
            if (collider) go.AddComponent<BoxCollider>().size = size;
            return SavePrefab(go, path);
        }

        const string LabDir = "Assets/Prefabs/StyleCity/Lab";

        /// <summary>Glass bell dome (pivot at the centre of the base, up = +y): smooth glass wall of radius r up to wallH, a half sphere on top,
        /// a brass foot ring and a brass knob. No collider (the dome greybox keeps its own collider and is moved by LabHeist).</summary>
        public static GameObject GlassDomePrefab(float r, float wallH, bool rebuild = false)
        {
            string name = $"GlassDome_R{Mathf.RoundToInt(r * 100)}_H{Mathf.RoundToInt(wallH * 100)}";
            string path = LabDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            rnd = new System.Random(SeedOf(name));
            string gpath = MatDir + "/City_DomeGlass.mat";
            var glass = AssetDatabase.LoadAssetAtPath<Material>(gpath);
            if (glass == null) { AssetDatabase.CopyAsset("Assets/Materials/StyleTest/Turbine_Glass.mat", gpath); glass = AssetDatabase.LoadAssetAtPath<Material>(gpath); }
            var gc = new Color(0.62f, 0.85f, 0.95f, 0.38f);   // a bit more visible than the turbine glass under the bright city light
            glass.SetColor("_BaseColor", gc); glass.SetColor("_Color", gc); glass.SetFloat("_Smoothness", 0.95f); EditorUtility.SetDirty(glass);
            var go = new GameObject(name); var hg = go.transform;
            // Smooth glass surface (double-sided material): cylinder rings, then latitude rings of the half sphere.
            int seg = 40, lat = 12; var vs = new List<Vector3>(); var ns = new List<Vector3>(); var tris = new List<int>();
            var rings = new List<(float y, float rad, Vector3 nScale, float ny)>();
            float y0 = 0.005f;   // no foot: the glass starts right on the stand
            rings.Add((y0, r, Vector3.one, 0f)); rings.Add((wallH, r, Vector3.one, 0f));
            for (int j = 1; j <= lat; j++) { float a = j * Mathf.PI / 2 / lat; rings.Add((wallH + Mathf.Sin(a) * r, Mathf.Cos(a) * r, Vector3.one, Mathf.Sin(a))); }
            foreach (var rg in rings)
                for (int i = 0; i <= seg; i++)
                {
                    float t = i * Mathf.PI * 2 / seg; var dir = new Vector3(Mathf.Sin(t), 0, Mathf.Cos(t));
                    vs.Add(dir * Mathf.Max(rg.rad, 0.0001f) + Vector3.up * rg.y);
                    ns.Add((dir * Mathf.Sqrt(Mathf.Max(0f, 1f - rg.ny * rg.ny)) + Vector3.up * rg.ny).normalized);
                }
            for (int k = 0; k + 1 < rings.Count; k++)
                for (int i = 0; i < seg; i++)
                {
                    int a = k * (seg + 1) + i, b = a + 1, c = a + seg + 1, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d);
                }
            var mesh = new Mesh { name = "City_" + name + "_Glass" };
            mesh.SetVertices(vs); mesh.SetNormals(ns); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
            var gg = new GameObject(name + " Glass"); gg.transform.SetParent(hg, false);
            gg.AddComponent<MeshFilter>().sharedMesh = Save(mesh);
            var gr = gg.AddComponent<MeshRenderer>(); gr.sharedMaterial = glass; gr.shadowCastingMode = ShadowCastingMode.Off;
            // Brass foot ring, rim at the top of the wall and the knob.
            var mb = new MB();
            for (int i = 0; i < seg; i++)   // thin rim hoop where the wall meets the half sphere
            {
                float t0 = i * Mathf.PI * 2 / seg, t1 = (i + 1) * Mathf.PI * 2 / seg, rh = r + 0.014f;
                Rod(mb, new Vector3(Mathf.Sin(t0) * rh, wallH, Mathf.Cos(t0) * rh), new Vector3(Mathf.Sin(t1) * rh, wallH, Mathf.Cos(t1) * rh), 0.03f, Gold);
            }
            float top = wallH + r;
            // 8 brass ribs along the glass (wall and half sphere), just outside the glass.
            for (int m = 0; m < 8; m++)
            {
                float t = m * Mathf.PI / 4; var dir = new Vector3(Mathf.Sin(t), 0, Mathf.Cos(t)); float rr = r + 0.012f;
                Rod(mb, dir * rr + Vector3.up * y0, dir * rr + Vector3.up * wallH, 0.024f, Gold);
                for (int j = 0; j < lat; j++)
                {
                    float a0 = j * Mathf.PI / 2 / lat, a1 = (j + 1) * Mathf.PI / 2 / lat;
                    Rod(mb, dir * (Mathf.Cos(a0) * rr) + Vector3.up * (wallH + Mathf.Sin(a0) * rr), dir * (Mathf.Cos(a1) * rr) + Vector3.up * (wallH + Mathf.Sin(a1) * rr), 0.024f, Gold);
                }
            }
            mb.Prism(Vector3.up * (top - 0.01f), 12, 0.07f, 0.05f, 0.04f, Gold);
            mb.Prism(Vector3.up * (top + 0.03f), 12, 0.03f, 0.03f, 0.04f, Gold);
            mb.Prism(Vector3.up * (top + 0.07f), 12, 0.06f, 0.0f, 0.06f, Gold);
            usedNames.Remove("City_" + name + "_Brass");
            Piece(hg, name + " Brass", mb, solid);
            return SavePrefab(go, path);
        }

        /// <summary>Round stone stand under the glass dome (exact size box, pivot at the centre, BoxCollider = size): a stone disc with a brass inlay ring.</summary>
        public static GameObject DomeStandPrefab(Vector3 size, bool rebuild = false)
        {
            string name = $"DomeStand_{Mathf.RoundToInt(size.x * 100)}x{Mathf.RoundToInt(size.y * 100)}x{Mathf.RoundToInt(size.z * 100)}";
            string path = LabDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            rnd = new System.Random(SeedOf(name));
            var go = new GameObject(name); var hg = go.transform; var mb = new MB();
            float rr = Mathf.Min(size.x, size.z) / 2, H = size.y;
            mb.Prism(Vector3.down * (H / 2), 40, rr, rr - 0.02f, H, StoneL);
            mb.Prism(Vector3.up * (H / 2), 40, rr - 0.12f, rr - 0.12f, 0.006f, Bronze);
            mb.Prism(Vector3.up * (H / 2), 40, rr - 0.17f, rr - 0.17f, 0.008f, StoneL);
            usedNames.Remove("City_" + name + "_Body");
            Piece(hg, name + " Body", mb, solid);
            go.AddComponent<BoxCollider>().size = size;
            return SavePrefab(go, path);
        }

        const string SegHospitalDir = "Assets/Prefabs/StyleCity/Hospital", SegGroundDir = "Assets/Prefabs/StyleCity/Ground";

        /// <summary>Cobbled ground slab of exact size (pivot at the volume centre, like a scaled greybox cube). The cobble pattern is world-space.</summary>
        public static GameObject CobbleFloorPrefab(Vector3 size, bool rebuild = false)
        {
            string name = $"CobbleFloor_{Mathf.RoundToInt(size.x * 100)}x{Mathf.RoundToInt(size.y * 100)}x{Mathf.RoundToInt(size.z * 100)}";
            string path = SegGroundDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            var go = new GameObject(name); var mb = new MB();
            mb.Box(Vector3.zero, size, CobbleCol, Quaternion.identity, SkipBottom);
            usedNames.Remove("City_" + name);
            Piece(go.transform, name, mb, cobbleMat);
            go.AddComponent<BoxCollider>().size = size;
            return SavePrefab(go, path);
        }

        /// <summary>Field-hospital pieces from the StyleTest_City hospital as prefabs. kind: Bed (v = blanket 0..2), IVStand, Crate, CrateStack,
        /// Cabinet, Screen, Table, Stretcher (v 0 = on trestles), Oxygen, Sign, Generator, WaterTank, Wheelchair, WaitingChairs (v = seats),
        /// Tent (len x wid, v 0 = red cross on both roof sides; furnished with beds, IV stands, crates, cabinet and screen).
        /// Pivot on the ground at the centre; a tent's open (rolled-up) side faces +z.</summary>
        public static GameObject HospitalPrefab(string kind, int v = 0, float len = 10f, float wid = 6f, bool rebuild = false)
        {
            string name = "Hospital_" + kind + (kind == "Tent" ? $"_L{Mathf.RoundToInt(len * 10)}_W{Mathf.RoundToInt(wid * 10)}_{v}" : kind == "Bed" || kind == "Stretcher" || kind == "WaitingChairs" ? "_" + v : "");
            string path = SegHospitalDir + "/" + name + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (have != null && !rebuild) return have;
            EnsureCityMats();
            rnd = new System.Random(SeedOf(name)); cols.Clear(); glowMB = new MB();
            var go = new GameObject(name); var mb = new MB(); var f = new LF(Vector3.zero, 0f);
            var blankets = new[] { BlanketB, BlanketL, BlanketGr };
            switch (kind)
            {
                case "Bed": Bed(mb, f, 0, 0, 0f, blankets[v % 3]); break;
                case "IVStand": IVStand(mb, f, 0, 0); break;
                case "Crate": Crate(mb, f, 0, 0, 0.45f, 0.45f, 0.4f, 0f, White, true); break;
                case "CrateStack": CrateStack(mb, f); break;
                case "Cabinet": Cabinet(mb, f, 0, 0); break;
                case "Screen": Screen(mb, f, 0, 0, 0f); break;
                case "Table": Table(mb, f, 0, 0, 0f); break;
                case "Stretcher": Stretcher(mb, f, 0, 0, 0f, v == 0); break;
                case "Oxygen": Oxygen(mb, f); break;
                case "Sign": Sign(mb, f); break;
                case "Generator": Generator(mb, f); break;
                case "WaterTank": WaterTank(mb, f); break;
                case "Wheelchair": Wheelchair(mb, f); break;
                case "WaitingChairs": WaitingChairs(mb, f, Mathf.Max(2, v)); break;
                case "Tent":
                {
                    Tent(mb, f, len, wid, TentW, TentB, v == 0);
                    int n = Mathf.Max(2, Mathf.FloorToInt((len - 1.6f) / 2f));
                    float x0 = -(n - 1);
                    for (int i = 0; i < n; i++)
                    {
                        float x = x0 + i * 2f;
                        Bed(mb, f, x, -wid / 2 + 1.45f, 0f, blankets[rnd.Next(3)]);
                        if (i % 2 == 0) IVStand(mb, f, x + 0.75f, -wid / 2 + 0.8f);
                        else Crate(mb, f, x + 0.85f, -wid / 2 + 0.45f, 0.45f, 0.45f, 0.4f, 0f, White, true);
                    }
                    Cabinet(mb, f, -len / 2 + 0.4f, -wid / 2 + 0.5f);
                    Screen(mb, f, len / 2 - 0.7f, -0.6f, 90f);
                    break;
                }
                default: Object.DestroyImmediate(go); return null;
            }
            usedNames.Remove("City_" + name);
            var piece = Piece(go.transform, name, mb, solid);
            if (cols.Count == 0)
            {
                var bb = piece.GetComponent<MeshFilter>().sharedMesh.bounds;
                var bc = go.AddComponent<BoxCollider>(); bc.center = bb.center; bc.size = bb.size;
            }
            foreach (var c in cols)
            {
                var cg = new GameObject(c.n + " Collider"); cg.transform.SetParent(go.transform, false);
                cg.transform.SetPositionAndRotation(c.c, c.r); cg.AddComponent<BoxCollider>().size = c.s;
            }
            cols.Clear();
            return SavePrefab(go, path);
        }

        /// <summary>One facade of the flat-roof house: courses with window / door openings and their fillings.</summary>
        static void FlatFacade(Transform hg, MB mb, WF w, float W, HStyle st, bool front, float gap = -1f, List<Vector2> allow = null, List<Vector4> hide = null, List<Vector2> noAwn = null)
        {
            var rows = Courses(st.floors);
            int c = Mathf.Max(2, Mathf.FloorToInt((W - 3.2f) / 5.0f));
            float sp = (W - 3.2f) / c;
            var xc = new float[c]; for (int i = 0; i < c; i++) xc[i] = R1(1.6f + sp * (i + 0.5f));
            var ground = new int[c];
            int door = rnd.Next(c);
            for (int i = 0; i < c; i++) ground[i] = front ? (i == door ? 0 : 1) : 2;
            // gap > 0: tunnel mouth here (ground-floor opening wide enough for its stone frame, no filling).
            int gi = -1;
            if (gap > 0f) { gi = 0; for (int i = 1; i < c; i++) if (Mathf.Abs(xc[i] - gap) < Mathf.Abs(xc[gi] - gap)) gi = i; xc[gi] = gap; ground[gi] = 4; if (front && door == gi) ground[(gi + 1) % c] = 0; }
            float Half(int t) => t == 4 ? TunnelHalf + 0.35f : t == 5 ? 0f : t == 1 ? 1.2f : t == 0 ? 0.9f : 0.7f;
            // allow: ground-floor openings only inside these x ranges (5 = plain wall, e.g. where a raised floor or a block stands in front).
            if (allow != null) for (int i = 0; i < c; i++) { if (ground[i] == 4) continue; float h0 = xc[i] - Half(ground[i]), h1 = xc[i] + Half(ground[i]); bool ok = false; foreach (var a in allow) if (h0 >= a.x && h1 <= a.y) ok = true; if (!ok) ground[i] = 5; }
            // hide: (x0, x1, h) = no opening whose x range touches x0..x1 and whose bottom is below h (something stands in front of it).
            bool Hidden(float a, float b, float y0, float y1) { if (hide != null) foreach (var hd in hide) if (b > hd.x && a < hd.y && y0 < hd.z && y1 > hd.w) return true; return false; }
            for (int i = 0; i < c; i++) if (ground[i] != 4 && ground[i] != 5 && Hidden(xc[i] - Half(ground[i]), xc[i] + Half(ground[i]), ground[i] == 2 ? 1.2f : 0f, 3.6f)) ground[i] = 5;
            bool WinHidden(int i, int f) => Hidden(xc[i] - 0.7f, xc[i] + 0.7f, GroundTop + f * FloorH + 0.8f, GroundTop + f * FloorH + 3.2f);
            foreach (var r in rows)
            {
                var open = new List<Vector2>();
                for (int i = 0; i < c; i++)
                {
                    if (r.floor == -1)
                    {
                        int t = ground[i];
                        if (t != 5 && (t == 2 ? r.slot >= 1 && r.slot <= 3 : r.slot <= 3)) open.Add(new Vector2(xc[i] - Half(t), xc[i] + Half(t)));
                    }
                    else if (r.floor < st.floors && r.slot >= 1 && r.slot <= 3 && !WinHidden(i, r.floor)) open.Add(new Vector2(xc[i] - 0.7f, xc[i] + 0.7f));
                }
                foreach (var seg in Gaps(0f, W, open)) KitRow(hg, w, r.fam, seg.x, seg.y, r.y, st.mat, true, true);
            }
            for (int i = 0; i < c; i++)
            {
                switch (ground[i])
                {
                    case 0: KitDoor(mb, w, xc[i]); break;
                    case 1: { bool aw = true; if (noAwn != null) foreach (var na in noAwn) if (xc[i] + 1.2f > na.x && xc[i] - 1.2f < na.y) aw = false; KitShop(mb, w, xc[i], aw); break; }
                    case 4: break;
                    case 5: break;
                    default: KitWindow(mb, w, xc[i], 1.2f, 3.6f, st, true); break;
                }
                for (int f = 0; f < st.floors; f++) { if (WinHidden(i, f)) continue; float b = GroundTop + f * FloorH; KitWindow(mb, w, xc[i], b + 0.8f, b + 3.2f, st, false); }
            }
        }

        static void KitHouse(Transform g, string name, WF w, float W, float depth, HStyle st, float leftTop, float rightTop)
        {
            var hg = HouseRoot(g, name, w, W, depth, st, out var mb);
            var rows = Courses(st.floors);
            // Window / door columns on a 10 cm grid.
            int c = Mathf.Max(2, Mathf.FloorToInt((W - 3.2f) / 5.0f));   // few, widely spaced windows
            float sp = (W - 3.2f) / c;
            var xc = new float[c]; for (int i = 0; i < c; i++) xc[i] = R1(1.6f + sp * (i + 0.5f));
            // Ground floor: 0 door, 1 shop, 2 barred window, 3 treasury portal.
            st.ground = new int[c];
            int door = rnd.Next(c);
            for (int i = 0; i < c; i++) st.ground[i] = i == door ? 0 : rnd.NextDouble() < 0.5 ? 1 : 2;
            if (st.treasury)
            {
                int mid = c / 2; for (int i = 0; i < c; i++) st.ground[i] = 2;
                st.ground[mid] = 3; if (c % 2 == 0) xc[mid] = R1(W / 2);
            }
            // 4 = walk-through tunnel; the column nearest to it moves onto the tunnel axis.
            int ti = -1;
            if (st.tunnel > 0f)
            {
                ti = 0; for (int i = 1; i < c; i++) if (Mathf.Abs(xc[i] - st.tunnel) < Mathf.Abs(xc[ti] - st.tunnel)) ti = i;
                xc[ti] = st.tunnel; st.ground[ti] = 4;
            }
            // No first-floor window over the tunnel: the raised gate slides up in front of that spot.
            bool NoWindow(int i, int floor) => i == ti && floor == 0;
            float Half(int type) => type == 4 ? TunnelHalf : type == 3 ? 1.8f : type == 1 ? 1.2f : type == 0 ? 0.9f : 0.7f;

            foreach (var r in rows)
            {
                var open = new List<Vector2>();
                for (int i = 0; i < c; i++)
                {
                    bool o = false;
                    if (r.floor == -1)
                    {
                        int t = st.ground[i];
                        o = t == 3 ? r.slot <= 4 : t == 2 ? r.slot >= 1 && r.slot <= 3 : r.slot <= 3;
                        if (o) open.Add(new Vector2(xc[i] - Half(t), xc[i] + Half(t)));
                    }
                    else if (r.floor < st.floors && r.slot >= 1 && r.slot <= 3 && !NoWindow(i, r.floor)) open.Add(new Vector2(xc[i] - 0.7f, xc[i] + 0.7f));
                }
                foreach (var seg in Gaps(0f, W, open)) KitRow(hg, w, r.fam, seg.x, seg.y, r.y, st.mat, seg.x > 0.01f, seg.y < W - 0.01f);
            }
            // Fillings of the openings (own boxes: glass, frames, doors, shutters).
            for (int i = 0; i < c; i++)
            {
                switch (st.ground[i])
                {
                    case 0: KitDoor(mb, w, xc[i]); break;
                    case 1: KitShop(mb, w, xc[i]); break;
                    case 2: KitWindow(mb, w, xc[i], 1.2f, 3.6f, st, true); break;
                    case 3: KitPortal(mb, w, xc[i]); break;
                    case 4: KitTunnel(hg, mb, w, xc[i], depth, st.mat); break;
                }
                for (int f = 0; f < st.floors; f++)
                {
                    if (NoWindow(i, f)) continue;
                    float b = GroundTop + f * FloorH;
                    KitWindow(mb, w, xc[i], b + 0.8f, b + 3.2f, st, false);
                }
            }
            if (leftTop < HouseTop(st.floors)) CladSide(hg, LeftSide(w, depth), depth, rows, leftTop, st.mat);
            if (rightTop < HouseTop(st.floors)) CladSide(hg, RightSide(w, W), depth, rows, rightTop, st.mat);
            Piece(hg, name + " Body", mb, solid);
        }

        static void KitWindow(MB mb, WF w, float cx, float y0, float y1, HStyle st, bool barred)
        {
            float x0 = cx - 0.7f, x1 = cx + 0.7f;
            WBox(mb, w, x0, x1, y0, y1, 0.0f, 0.03f, Glass, false);
            WBox(mb, w, x0, x1, y0, y0 + 0.08f, 0.03f, 0.08f, Frame, false);
            WBox(mb, w, x0, x1, y1 - 0.08f, y1, 0.03f, 0.08f, Frame, false);
            WBox(mb, w, x0, x0 + 0.07f, y0 + 0.08f, y1 - 0.08f, 0.03f, 0.08f, Frame, false);
            WBox(mb, w, x1 - 0.07f, x1, y0 + 0.08f, y1 - 0.08f, 0.03f, 0.08f, Frame, false);
            WBox(mb, w, cx - 0.035f, cx + 0.035f, y0 + 0.08f, y1 - 0.08f, 0.03f, 0.07f, Frame, false);
            if (barred)
            {
                for (float x = x0 + 0.2f; x < x1 - 0.1f; x += 0.25f) WBox(mb, w, x - 0.018f, x + 0.018f, y0 + 0.1f, y1, 0.12f, 0.16f, Iron, false);
                WBox(mb, w, x0, x1, (y0 + y1) / 2 - 0.025f, (y0 + y1) / 2 + 0.025f, 0.11f, 0.17f, Iron, false);
                return;
            }
            // Wooden shutters inside the reveal: both closed, one closed, or open (none).
            double r = rnd.NextDouble();
            if (r < 0.6)
            {
                var sc = st.shutter; var scd = sc * 0.8f; scd.a = sc.a;
                foreach (int side in r < 0.3 ? new[] { -1, 1 } : new[] { rnd.NextDouble() < 0.5 ? -1 : 1 })
                {
                    float a = side < 0 ? x0 + 0.07f : cx + 0.005f, b = side < 0 ? cx - 0.005f : x1 - 0.07f;
                    WBox(mb, w, a, b, y0 + 0.1f, y1 - 0.08f, 0.09f, 0.13f, sc, false);
                    for (float y = y0 + 0.3f; y < y1 - 0.3f; y += 0.5f) WBox(mb, w, a + 0.05f, b - 0.05f, y, y + 0.06f, 0.13f, 0.15f, scd, false);
                }
            }
        }

        static void KitDoor(MB mb, WF w, float cx)
        {
            float x0 = cx - 0.9f, x1 = cx + 0.9f, top = 3.6f;
            WBox(mb, w, x0, x1, 0.12f, top, 0.0f, 0.02f, Dark, false);
            WBox(mb, w, x0 + 0.08f, x1 - 0.08f, 2.75f, top - 0.1f, 0.02f, 0.04f, Glass, false);
            WBox(mb, w, x0, x1, 2.65f, 2.75f, 0.02f, 0.1f, Wood, false);
            foreach (int s in new[] { -1, 1 })
            {
                float a = s < 0 ? x0 + 0.05f : cx + 0.01f, b = s < 0 ? cx - 0.01f : x1 - 0.05f;
                WBox(mb, w, a, b, 0.12f, 2.65f, 0.02f, 0.1f, Wood, false);
                WBox(mb, w, a + 0.12f, b - 0.12f, 0.4f, 1.3f, 0.1f, 0.13f, WoodD, false);
                WBox(mb, w, a + 0.12f, b - 0.12f, 1.5f, 2.45f, 0.1f, 0.13f, WoodD, false);
                WBox(mb, w, s < 0 ? b - 0.13f : a + 0.06f, s < 0 ? b - 0.06f : a + 0.13f, 1.33f, 1.45f, 0.1f, 0.15f, Gold, false);
            }
            WBox(mb, w, x0 - 0.15f, x1 + 0.15f, 0f, 0.12f, 0.0f, 0.7f, StoneD, false);
        }

        static void KitShop(MB mb, WF w, float cx, bool awning = true)
        {
            float x0 = cx - 1.2f, x1 = cx + 1.2f, top = 3.6f;
            WBox(mb, w, x0, x1, 0f, top, 0.0f, 0.02f, Dark, false);
            WBox(mb, w, x0 + 0.08f, cx + 0.3f, 0.7f, 2.9f, 0.02f, 0.05f, GlassShop, false);
            WBox(mb, w, x0 + 0.08f, cx + 0.3f, 0f, 0.7f, 0.02f, 0.12f, Wood, false);
            WBox(mb, w, cx + 0.35f, x1 - 0.08f, 0f, 2.9f, 0.02f, 0.1f, WoodD, false);
            WBox(mb, w, cx + 0.47f, x1 - 0.2f, 1.4f, 2.6f, 0.1f, 0.11f, GlassShop, false);
            WBox(mb, w, x0, x1, 2.9f, 3.0f, 0.02f, 0.14f, Wood, false);
            WBox(mb, w, x0 + 0.1f, x1 - 0.1f, 3.05f, 3.5f, 0.02f, 0.08f, WoodD, false);
            WBox(mb, w, cx - 0.8f, cx + 0.8f, 3.12f, 3.43f, 0.08f, 0.1f, Gold, false);
            if (rnd.NextDouble() < 0.5 || !awning) return;
            // Striped awning over the shop window.
            var col = new[] { C(0.75f, 0.16f, 0.14f), C(0.22f, 0.42f, 0.30f), C(0.20f, 0.33f, 0.55f), C(0.85f, 0.55f, 0.20f) }[rnd.Next(4)];
            if (noAwnings) return;
            var rr = w.R * Quaternion.Euler(24f, 0, 0);
            Vector3 fwd = rr * Vector3.forward;
            int n = 9; float sw = 2.4f / n;
            for (int i = 0; i < n; i++)
            {
                var a = w.P(x0 + sw * (i + 0.5f), 3.55f, 0.9f);
                var cc = i % 2 == 0 ? col : Canvas;
                mb.Box(a + fwd * 0.7f, new Vector3(sw, 0.03f, 1.4f), cc, rr);
                mb.Box(a + fwd * 1.4f - Vector3.up * 0.12f, new Vector3(sw, 0.24f, 0.025f), cc, w.R);
            }
            WBox(mb, w, x0, x1, 3.5f, 3.6f, 0.0f, 0.92f, Iron, false);
        }

        /// <summary>Walk-through passage under the house (closed at the piazza end by the Coop_LeverGate):
        /// StyleKit courses on both side walls, a plank ceiling on beams, stone floor slabs, a stone frame at the back mouth and a lantern.</summary>
        static void KitTunnel(Transform hg, MB mb, WF w, float cx, float depth, Material mat)
        {
            float x0 = cx - TunnelHalf, x1 = cx + TunnelHalf;
            var tg = new GameObject("Tunnel").transform; tg.SetParent(hg, false);
            // Side walls: the frame runs from the front to the back of the house, facing into the tunnel.
            foreach (float xs in new[] { x0, x1 })
            {
                var n = xs == x0 ? w.u : -w.u;
                Vector3 a = w.P(xs, 0, 0), b = w.P(xs, 0, -depth);
                var uf = Quaternion.LookRotation(n) * Vector3.right;
                var f = new WF(Vector3.Dot(b - a, uf) > 0 ? a : b, n);
                KitRow(tg, f, "Plinth", 0f, depth, 0f, mat, true, true, 1f);
                for (int k = 0; k < 3; k++) KitRow(tg, f, "Wall", 0f, depth, 1.2f + 0.8f * k, mat, true, true, 1f);
            }
            // Plank ceiling and cross beams.
            WBox(mb, w, x0, x1, TunnelH - 0.07f, TunnelH - 0.02f, -depth, 0f, Wood, false);
            for (float d = 0.9f; d < depth - 0.5f; d += 1.8f)
                WBox(mb, w, x0 + 0.05f, x1 - 0.05f, TunnelH - 0.3f, TunnelH - 0.05f, -d - 0.13f, -d + 0.13f, WoodD, false);
            // Floor slabs (same travertine as the strip along the house fronts).
            int ns = 6; float sl = depth / ns;
            for (int k = 0; k < ns; k++)
                WBox(mb, w, x0 + 0.05f, x1 - 0.05f, -0.06f, k % 2 == 0 ? 0.03f : 0.02f, -sl * (k + 1) + 0.015f, -sl * k - 0.015f, Stone, false);
            // Stone frame around the back mouth.
            WBox(mb, w, x0 - 0.35f, x0, 0f, TunnelH + 0.35f, -depth - 0.1f, -depth + 0.05f, StoneL, false);
            WBox(mb, w, x1, x1 + 0.35f, 0f, TunnelH + 0.35f, -depth - 0.1f, -depth + 0.05f, StoneL, false);
            WBox(mb, w, x0 - 0.35f, x1 + 0.35f, TunnelH, TunnelH + 0.35f, -depth - 0.12f, -depth + 0.05f, StoneL, false);
            // Lantern under the middle beam.
            var lp = w.P(cx, TunnelH - 0.07f, -depth / 2);   // hangs from the plank ceiling
            mb.Prism(lp - Vector3.up * 0.35f, 4, 0.03f, 0.03f, 0.35f, Iron, 45f);
            mb.Prism(lp - Vector3.up * 0.43f, 4, 0.16f, 0.16f, 0.08f, Iron, 45f);
            glowMB.Prism(lp - Vector3.up * 0.81f, 4, 0.12f, 0.15f, 0.38f, GLampDim, 45f);
            AddLight(tg, "Tunnel Light", lp - Vector3.up * 0.65f, LightType.Point, new Color(1f, 0.78f, 0.5f), 2.2f, 8f);
        }

        /// <summary>Big double door of the treasury (the A1_Start vault).</summary>
        static void KitPortal(MB mb, WF w, float cx)
        {
            float x0 = cx - 1.8f, x1 = cx + 1.8f, top = 4.4f;
            WBox(mb, w, x0, x1, 0f, top, 0.0f, 0.02f, Dark, false);
            foreach (int s in new[] { -1, 1 })
            {
                float a = s < 0 ? x0 + 0.1f : cx + 0.01f, b = s < 0 ? cx - 0.01f : x1 - 0.1f;
                WBox(mb, w, a, b, 0.2f, top - 0.15f, 0.02f, 0.14f, WoodD, false);
                for (float y = 0.6f; y < top - 0.4f; y += 0.7f)
                    for (float x = a + 0.25f; x < b - 0.1f; x += 0.42f) WBox(mb, w, x - 0.05f, x + 0.05f, y - 0.05f, y + 0.05f, 0.14f, 0.19f, Gold, false);
                WBox(mb, w, s < 0 ? b - 0.18f : a + 0.08f, s < 0 ? b - 0.08f : a + 0.18f, 1.9f, 2.4f, 0.14f, 0.21f, Gold, false);
            }
            WBox(mb, w, x0 - 0.3f, x1 + 0.3f, 0f, 0.15f, 0.0f, 1.2f, StoneL, false);
            WBox(mb, w, x0 - 0.15f, x1 + 0.15f, 0.15f, 0.3f, 0.0f, 0.75f, Stone, false);
        }

        static void GableRoof(MB mb, WF w, float W, float Ye, float dFront, float depth, float ov, float sov)
        {
            float p = Pitch * Mathf.Deg2Rad, rise = depth / 2 * Mathf.Tan(p), dm = dFront - depth / 2;
            Vector3 Ridge(float x) => w.P(x, Ye + rise, dm);
            float wt = W + 2 * sov;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 down = (w.n * s * Mathf.Cos(p) - Vector3.up * Mathf.Sin(p)).normalized;
                Vector3 nn = (Vector3.up * Mathf.Cos(p) + w.n * s * Mathf.Sin(p)).normalized;
                float L = (depth / 2 + ov) / Mathf.Cos(p);
                var rr = Quaternion.LookRotation(-down, nn);
                mb.Box(Ridge(W / 2) + down * (L / 2) - nn * 0.06f, new Vector3(wt, 0.12f, L + 0.02f), TileUnder, rr);   // closed underside: the roof is seen from the street below the eaves
                int nr = Mathf.FloorToInt(wt / 1.0f); float sp = wt / nr;   // tiles sized to the StyleKit stones
                int ns = Mathf.CeilToInt(L / 1.7f); float sl = L / ns;
                for (int k = 0; k < nr; k++)
                {
                    float x = -sov + sp * (k + 0.5f);
                    for (int j = 0; j < ns; j++)
                    {
                        float t = (j + 0.5f) * sl;
                        mb.Box(Ridge(x) + down * t + nn * (0.13f + (j % 2) * 0.04f), new Vector3((j + (s > 0 ? 0 : 1)) % 2 == 0 ? 0.72f : 0.76f, 0.24f, sl + 0.12f), Vary(TileCols[rnd.Next(TileCols.Length)], 0.04f), rr, SkipBottom);
                    }
                    // Tile end at the eave.
                    mb.Box(Ridge(x) + down * (L + 0.03f) + nn * 0.12f, new Vector3(0.74f, 0.3f, 0.1f), TileRidge, rr);
                }
            }
            for (float x = -sov; x < W + sov - 0.1f; x += 1.2f)
                mb.Box(Ridge(x + 0.6f) + Vector3.up * 0.28f, new Vector3(1.16f, 0.42f, 0.95f), Vary(TileRidge, 0.05f), w.R, SkipBottom);
        }

        static void Chimney(MB mb, WF w, float x, float d, float Ye, float depth)
        {
            float p = Pitch * Mathf.Deg2Rad, rise = depth / 2 * Mathf.Tan(p);
            float ys = Ye + rise - Mathf.Abs(d + depth / 2) * Mathf.Tan(p);
            float y0 = ys - 0.9f, y1 = ys + Rf(1.8f, 2.4f), h = 0.8f;
            mb.Box(w.P(x, (y0 + y1) / 2, d), new Vector3(1.6f, y1 - y0, 1.6f), Mortar, w.R, SkipBottom);
            // Bricks on all four faces.
            foreach (var n in new[] { w.n, -w.n, w.u, -w.u })
            {
                var uf = Quaternion.LookRotation(n) * Vector3.right;
                var o = w.P(x, 0, d) + n * h - uf * h;
                var f = new WF(o, n);
                Bricks(mb, f, new Rc(0.04f, 1.56f, ys - 0.4f, y1 - 0.25f), null, null, 0.04f, 0.09f, 0.0f, 0.78f, 0.38f, 0.05f);
            }
            mb.Box(w.P(x, y1 + 0.12f, d), new Vector3(2.0f, 0.24f, 2.0f), StoneD, w.R);
            mb.Box(w.P(x, y1 + 0.65f, d), new Vector3(0.7f, 0.85f, 0.7f), Mortar, w.R);
            mb.Prism(w.P(x, y1 + 1.05f, d), 4, 1.1f, 0f, 0.6f, TileRidge, w.R, 45f);
        }

        // ---------- Facade helpers ----------

        /// <summary>Running-bond bricks over the area (rows clipped around the excluded rects); front depth random in [f0,f1].</summary>
        static void Bricks(MB mb, WF w, Rc area, List<Rc> excl, System.Func<float, float, bool> keep, float f0, float f1, float back,
            float bl = 0.5f, float bh = 0.22f, float joint = 0.035f)
        {
            int row = 0;
            for (float y = area.y0 + joint * 0.5f; y < area.y1 - 0.06f; y += bh + joint, row++)
            {
                float yt = Mathf.Min(y + bh, area.y1 - joint * 0.5f);
                float off = (row % 2) * (bl + joint) * 0.5f;
                foreach (var iv in RowIntervals(area.x0, area.x1, y, yt, excl))
                {
                    for (float x = area.x0 - off; x < iv.y; x += bl + joint)
                    {
                        float a = Mathf.Max(x, iv.x + joint * 0.5f), b = Mathf.Min(x + bl, iv.y - joint * 0.5f);
                        if (b - a < 0.07f) continue;
                        if (keep != null && !keep((a + b) / 2, (y + yt) / 2)) continue;
                        float f = rnd.NextDouble() < 0.05 ? Mathf.Max(f0 - 0.012f, back + 0.008f) : Rf(f0, f1);
                        WBox(mb, w, a, b, y, yt, back - 0.03f, f, Vary(BrickCols[rnd.Next(BrickCols.Length)], 0.06f));
                    }
                }
            }
        }

        /// <summary>Travertine blocks of random length, rows clipped around the excluded rects.</summary>
        static void Blocks(MB mb, WF w, Rc area, List<Rc> excl, float f0, float f1)
        {
            for (float y = area.y0 + 0.015f; y < area.y1 - 0.08f; y += 0.53f)
            {
                float yt = Mathf.Min(y + 0.5f, area.y1 - 0.015f);
                foreach (var iv in RowIntervals(area.x0, area.x1, y, yt, excl))
                {
                    float x = iv.x + 0.015f;
                    while (x < iv.y - 0.1f)
                    {
                        float x1 = Mathf.Min(x + Rf(0.75f, 1.45f), iv.y - 0.015f); if (iv.y - 0.015f - x1 < 0.35f) x1 = iv.y - 0.015f;
                        WBox(mb, w, x, x1, y, yt, 0.01f, Rf(f0, f1), Vary(Stone, 0.05f));
                        x = x1 + 0.03f;
                    }
                }
            }
        }

        /// <summary>Ring of voussoirs (bricks or stones) around (cx, cy) in the wall plane between radii r0..r1 and angles a0..a1.</summary>
        static void ArchRing(MB mb, WF w, float cx, float cy, float r0, float r1, float a0, float a1, float front, bool brick)
        {
            float rm = (r0 + r1) / 2, arc = (a1 - a0) * Mathf.Deg2Rad * rm;
            int n = Mathf.Max(3, Mathf.RoundToInt(arc / (brick ? 0.17f : 0.32f)));
            for (int i = 0; i < n; i++)
            {
                float a = (a0 + (i + 0.5f) * (a1 - a0) / n) * Mathf.Deg2Rad;
                var radial = w.u * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a);
                float f = front + (i % 2 == 0 ? -0.012f : 0.012f) + (brick ? Rf(-0.004f, 0.004f) : 0f);   // neighbours never share a front plane
                var cen = w.P(cx, cy, 0) + radial * rm + w.n * ((f - 0.05f) / 2);
                var col = brick ? Vary(BrickCols[rnd.Next(BrickCols.Length)], 0.06f) : Vary(Stone, 0.04f);
                mb.Box(cen, new Vector3(arc / n - 0.03f, r1 - r0, f + 0.05f), col, Quaternion.LookRotation(w.n, radial), SkipBack);
            }
        }

        static void ArchRects(List<Rc> l, float cx, float ys, float r)
        {
            for (float y = ys; y < ys + r; y += 0.12f)
            {
                float dy = y - ys, hw = Mathf.Sqrt(Mathf.Max(0, r * r - dy * dy));
                l.Add(new Rc(cx - hw, cx + hw, y, Mathf.Min(y + 0.12f, ys + r)));
            }
        }

        static List<Vector2> RowIntervals(float x0, float x1, float ya, float yb, List<Rc> excl)
        {
            var cuts = new List<Vector2>();
            if (excl != null) foreach (var e in excl) if (e.y0 < yb - 0.001f && e.y1 > ya + 0.001f && e.x1 > x0 && e.x0 < x1) cuts.Add(new Vector2(e.x0, e.x1));
            cuts.Sort((a, b) => a.x.CompareTo(b.x));
            var res = new List<Vector2>(); float x = x0;
            foreach (var cu in cuts) { if (cu.x > x + 0.005f) res.Add(new Vector2(x, cu.x)); x = Mathf.Max(x, cu.y); }
            if (x1 > x + 0.005f) res.Add(new Vector2(x, x1));
            return res;
        }

        static List<Rc> FreeRects(Rc area, List<Rc> excl)
        {
            var ys = new List<float> { area.y0, area.y1 };
            foreach (var e in excl) { if (e.y0 > area.y0 && e.y0 < area.y1) ys.Add(e.y0); if (e.y1 > area.y0 && e.y1 < area.y1) ys.Add(e.y1); }
            ys.Sort();
            var res = new List<Rc>();
            for (int i = 0; i < ys.Count - 1; i++)
            {
                if (ys[i + 1] - ys[i] < 0.005f) continue;
                float ym = (ys[i] + ys[i + 1]) / 2;
                var cuts = new List<Vector2>();
                foreach (var e in excl) if (e.y0 <= ym && e.y1 >= ym && e.x1 > area.x0 && e.x0 < area.x1) cuts.Add(new Vector2(e.x0, e.x1));
                cuts.Sort((a, b) => a.x.CompareTo(b.x));
                float x = area.x0;
                foreach (var cu in cuts) { if (cu.x > x + 0.005f) res.Add(new Rc(x, cu.x, ys[i], ys[i + 1])); x = Mathf.Max(x, cu.y); }
                if (area.x1 > x + 0.005f) res.Add(new Rc(x, area.x1, ys[i], ys[i + 1]));
            }
            return res;
        }

        static bool Overlap(Rc a, Rc b) => a.x0 < b.x1 && b.x0 < a.x1 && a.y0 < b.y1 && b.y0 < a.y1;
        static Rc Expand(Rc a, float ex, float ey) => new Rc(a.x0 - ex, a.x1 + ex, a.y0 - ey, a.y1 + ey);

        // ---------- Bell tower (StyleKit courses) ----------

        static void BuildCampanile()
        {
            var g = Group("Campanile");
            var mb = new MB();
            const float S = 6.4f, Shaft = 24.2f, Bel = 27.4f, Top = 30.2f;
            var c = new Vector3(PX - S / 2 - 0.9f, 0, PZ - S / 2 - 0.9f);
            mb.Box(c + Vector3.up * Shaft / 2, new Vector3(S, Shaft, S), KitMortar, Quaternion.identity, SkipBottom);
            mb.Box(c + Vector3.up * (Bel + Top) / 2, new Vector3(S, Top - Bel, S), KitMortar, Quaternion.identity);
            cols.Add((c + Vector3.up * Top / 2, new Vector3(S, Top, S), Quaternion.identity, "Campanile"));
            // Courses: plinth, walls with Edge bands at 8.4 / 16.6 / 23.2, belfry walls, band, wall, cornice.
            var rows = new List<Row> { new Row { fam = "Plinth", y = 0f, h = 1.2f } };
            float y = 1.2f;
            foreach (int n in new[] { 9, 9, 7 })
            {
                for (int k = 0; k < n; k++) { rows.Add(new Row { fam = "Wall", y = y, h = 0.8f }); y += 0.8f; }
                rows.Add(new Row { fam = "Edge", y = y, h = 1f }); y += 1f;
            }
            for (int k = 0; k < 4; k++) { rows.Add(new Row { fam = "Wall", y = y, h = 0.8f, slot = 1 }); y += 0.8f; }
            rows.Add(new Row { fam = "Edge", y = y, h = 1f }); y += 1f;
            rows.Add(new Row { fam = "Wall", y = y, h = 0.8f }); y += 0.8f;
            rows.Add(new Row { fam = "Edge", y = y, h = 1f });
            var mat = kitMats[5];
            var faces = new[] { Vector3.back, Vector3.left, Vector3.forward, Vector3.right };
            var belfry = new List<Vector2> { new Vector2(0.6f, 2.0f), new Vector2(4.4f, 5.8f) };
            const float clockY = 20.8f, clockR = 1.2f;
            for (int fi = 0; fi < 4; fi++)
            {
                var n = faces[fi];
                var R = Quaternion.LookRotation(n); var u = R * Vector3.right;
                var w = new WF(c + n * (S / 2) - u * (S / 2), n);
                bool front = n == Vector3.back || n == Vector3.left;
                for (int k = 0; k < rows.Count; k++)
                {
                    var r = rows[k];
                    var open = new List<Vector2>();
                    if (r.slot == 1) open.AddRange(belfry);
                    if (n == Vector3.back && r.y < 3.5f) open.Add(new Vector2(2.3f, 4.1f));
                    // No opening for the clock: the flat 5 cm courses run behind the 30 cm clock disc right up to its rim.
                    // Flush corners: every course stops at the corner (no fins sticking out).
                    float ext = 0f;
                    var gaps = Gaps(-ext, S + ext, open);
                    foreach (var seg in gaps) KitRow(g, w, r.fam, seg.x, seg.y, r.y, mat, true, true, r.fam == "Edge" ? 0.7f : 1f); // smooth tower: flat pieces, a few raised stones only in the Edge bands
                }
                // Belfry piers (the walls between the openings need a body of their own).
                foreach (var seg in Gaps(0f, S, new List<Vector2>(belfry))) WBox(mb, w, seg.x, seg.y, Shaft, Bel, -0.6f, 0f, KitMortar, false);
                foreach (var b in belfry) WBox(mb, w, b.x - 0.1f, b.y + 0.1f, Shaft - 0.02f, Shaft + 0.12f, -0.6f, 0.12f, StoneL, false);
                if (n == Vector3.back)
                {
                    float cx = 3.2f;
                    WBox(mb, w, cx - 0.9f, cx + 0.9f, 0.12f, 3.6f, 0.0f, 0.02f, Dark, false);
                    WBox(mb, w, cx - 0.85f, cx - 0.01f, 0.12f, 3.5f, 0.02f, 0.1f, Wood, false);
                    WBox(mb, w, cx + 0.01f, cx + 0.85f, 0.12f, 3.5f, 0.02f, 0.1f, Wood, false);
                    WBox(mb, w, cx - 1.05f, cx + 1.05f, 0f, 0.12f, 0.0f, 0.4f, StoneD, false);
                }
                if (front)
                {
                    // Clock set into the stone rows.
                    var up = Quaternion.FromToRotation(Vector3.up, n);
                    mb.Prism(w.P(3.2f, clockY, 0f), 16, clockR + 0.1f, clockR + 0.1f, 0.3f, StoneL, up);
                    mb.Prism(w.P(3.2f, clockY, 0.3f), 16, clockR - 0.15f, clockR - 0.15f, 0.06f, White, up);
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i * 30f * Mathf.Deg2Rad;
                        var p = w.P(3.2f + Mathf.Sin(a) * 0.85f, clockY + Mathf.Cos(a) * 0.85f, 0.38f);
                        mb.Box(p, new Vector3(0.08f, i % 3 == 0 ? 0.24f : 0.13f, 0.04f), Iron, Quaternion.LookRotation(n, w.u * Mathf.Sin(a) + Vector3.up * Mathf.Cos(a)));
                    }
                    mb.Box(w.P(3.38f, clockY + 0.3f, 0.4f), new Vector3(0.06f, 0.7f, 0.03f), Iron, Quaternion.LookRotation(n, w.u * 0.5f + Vector3.up * 0.87f));
                    mb.Box(w.P(2.9f, clockY - 0.1f, 0.42f), new Vector3(0.07f, 0.55f, 0.03f), Iron, Quaternion.LookRotation(n, -w.u * 0.95f - Vector3.up * 0.3f));
                }
            }
            // Bell inside the belfry, hung from a wooden beam.
            mb.Box(c + Vector3.up * (Bel - 0.3f), new Vector3(S - 1.2f, 0.3f, 0.35f), WoodD, Quaternion.identity);
            mb.Prism(c + Vector3.up * (Shaft + 1.3f), 12, 0.85f, 0.5f, 0.7f, Bronze);
            mb.Prism(c + Vector3.up * (Shaft + 2.0f), 12, 0.5f, 0.38f, 0.75f, Bronze);
            mb.Prism(c + Vector3.up * (Shaft + 2.75f), 12, 0.38f, 0.1f, 0.25f, Bronze);
            mb.Prism(c + Vector3.up * (Shaft + 0.9f), 8, 0.12f, 0.12f, 0.5f, Bronze);
            // Pyramid roof of terracotta with hip ridges and an iron cross.
            float rr = S / 2 + 0.9f, hRoof = 4.4f;
            mb.Prism(c + Vector3.up * Top, 4, rr * 1.414f, 0f, hRoof, TileCols[0], 45f);
            for (int i = 0; i < 4; i++)
            {
                var a = c + Vector3.up * Top + Quaternion.Euler(0, 45 + i * 90, 0) * Vector3.forward * rr * 1.414f;
                Rod(mb, a + Vector3.up * 0.05f, c + Vector3.up * (Top + hRoof + 0.05f), 0.2f, TileRidge);
            }
            for (int i = 0; i < 4; i++)
            {
                var fn = Quaternion.Euler(0, i * 90, 0) * Vector3.forward;
                for (int k = 1; k < 5; k++)
                {
                    float t = k / 5f, half = rr * (1 - t);
                    var p = c + Vector3.up * (Top + hRoof * t) + fn * (rr * (1 - t));
                    mb.Box(p + Vector3.up * 0.12f, new Vector3(half * 2 - 0.3f, 0.24f, 0.4f), Vary(TileCols[k % TileCols.Length], 0.04f), Quaternion.LookRotation(fn));
                }
            }
            mb.Sphere(c + Vector3.up * (Top + hRoof + 0.25f), 0.25f, 8, 4, (i, j) => Bronze);
            mb.Box(c + Vector3.up * (Top + hRoof + 1.2f), new Vector3(0.1f, 1.5f, 0.1f), Iron, Quaternion.identity);
            mb.Box(c + Vector3.up * (Top + hRoof + 1.5f), new Vector3(0.8f, 0.1f, 0.1f), Iron, Quaternion.identity);
            Piece(g, "Campanile Body", mb, solid);
        }

        // ---------- Fountain ----------

        static void BuildFountain()
        {
            var g = Group("Fountain");
            var mb = new MB();
            var c = FountainPos;
            const float A = 4.0f, wallH = 0.75f, step = 0.14f;
            mb.Prism(c, 8, 5.2f / Mathf.Cos(22.5f * Mathf.Deg2Rad), 5.2f / Mathf.Cos(22.5f * Mathf.Deg2Rad), step, StoneD, 0f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f + 22.5f;
                var R = Quaternion.Euler(0, a, 0); var d = R * Vector3.forward;
                float len = 2 * A * Mathf.Tan(22.5f * Mathf.Deg2Rad);
                mb.Box(c + d * A + Vector3.up * (step + wallH / 2), new Vector3(len + 0.42f, wallH, 0.42f), Mortar, R, SkipBottom);
                mb.Box(c + d * (A + 0.02f) + Vector3.up * (step + wallH + 0.07f + (i % 2) * 0.012f), new Vector3(len + 0.66f, 0.14f, 0.62f), StoneL, R);
                // Bricks on the outer face.
                var u = R * Vector3.right;
                var w = new WF(c + d * (A + 0.21f) - u * (len / 2 + 0.21f) + Vector3.up * step, d);
                Bricks(mb, w, new Rc(0.02f, len + 0.4f, 0.02f, wallH), null, null, 0.025f, 0.05f, 0f);
                cols.Add((c + d * A + Vector3.up * 0.5f, new Vector3(len + 0.42f, 1.0f, 0.5f), R, "Fountain Wall"));
            }
            float wr = (A - 0.19f) / Mathf.Cos(22.5f * Mathf.Deg2Rad);
            mb.Prism(c + Vector3.up * step, 8, wr, wr, 0.5f, Water, 0f);
            // Central column with two bowls.
            mb.Prism(c + Vector3.up * step, 8, 0.95f, 0.75f, 1.0f, Stone, 22.5f);
            mb.Prism(c + Vector3.up * (step + 1.0f), 8, 0.4f, 0.32f, 1.0f, StoneL, 22.5f);
            mb.Prism(c + Vector3.up * 2.14f, 12, 0.35f, 1.65f, 0.42f, Stone);
            mb.Prism(c + Vector3.up * 2.5f, 12, 1.5f, 1.5f, 0.08f, Water);
            mb.Prism(c + Vector3.up * 2.56f, 8, 0.28f, 0.22f, 0.9f, StoneL, 22.5f);
            mb.Prism(c + Vector3.up * 3.4f, 12, 0.22f, 0.8f, 0.26f, Stone);
            mb.Prism(c + Vector3.up * 3.62f, 12, 0.7f, 0.7f, 0.06f, Water);
            mb.Sphere(c + Vector3.up * 3.95f, 0.24f, 8, 4, (i, j) => StoneL);
            cols.Add((c + Vector3.up * 1.5f, new Vector3(1.8f, 3f, 1.8f), Quaternion.identity, "Fountain Column"));
            // Falling water: thin streams from the bowls.
            for (int i = 0; i < 12; i++)
            {
                var d = Quaternion.Euler(0, i * 30f, 0) * Vector3.forward;
                Rod(mb, c + d * 1.62f + Vector3.up * 2.5f, c + d * 2.1f + Vector3.up * 0.66f, 0.035f, WaterL);
                if (i % 2 == 0) Rod(mb, c + d * 0.78f + Vector3.up * 3.62f, c + d * 1.15f + Vector3.up * 2.6f, 0.03f, WaterL);
            }
            Piece(g, "Fountain", mb, solid);
        }

        // ---------- Field hospital ----------

        struct LF { public Vector3 o; public Quaternion r; public float yaw; public LF(Vector3 o, float yaw) { this.o = o; this.yaw = yaw; r = Quaternion.Euler(0, yaw, 0); } public Vector3 P(float x, float y, float z) => o + r * new Vector3(x, y, z); }
        static void LB(MB mb, LF f, float x, float y, float z, float sx, float sy, float sz, Color col, int skip = 0) => mb.Box(f.P(x, y, z), new Vector3(sx, sy, sz), col, f.r, skip);
        static void LCol(LF f, float x, float y, float z, float sx, float sy, float sz, string n) => cols.Add((f.P(x, y, z), new Vector3(sx, sy, sz), f.r, n));

        static void BuildHospital()
        {
            var g = Group("Field Hospital");
            var tents = new MB(); var beds = new MB(); var props = new MB();

            // Two ward tents facing each other across a triage aisle, side walls rolled up towards the aisle.
            var tA = new LF(new Vector3(-24f, 0, -15f), 0f);
            var tB = new LF(new Vector3(-24f, 0, 7f), 180f);
            Tent(tents, tA, 10f, 6f, TentW, TentB, true);
            Tent(tents, tB, 10f, 6f, TentW, TentB, false);
            foreach (var t in new[] { tA, tB })
            {
                for (int i = 0; i < 5; i++)
                {
                    float x = -4f + i * 2f;
                    Bed(beds, t, x, -1.55f, 0f, new[] { BlanketB, BlanketL, BlanketGr }[rnd.Next(3)]);
                    if (i % 2 == 0) IVStand(props, t, x + 0.75f, -2.2f);
                    else Crate(props, t, x + 0.85f, -2.55f, 0.45f, 0.45f, 0.4f, 0f, White, true);
                }
                Cabinet(props, t, -4.6f, -2.5f);
                Screen(props, t, 4.3f, -0.6f, 90f);
            }
            // Open canopy for triage between the tents.
            var cp = new LF(new Vector3(-15f, 0, -4f), 0f);
            Canopy(tents, cp, 6.4f, 6.4f);
            Bed(beds, cp, -1.6f, -1.0f, 0f, BlanketGr);
            Bed(beds, cp, 0.0f, -1.0f, 0f, BlanketB);
            IVStand(props, cp, -0.8f, -1.6f);
            Screen(props, cp, 1.5f, -1.0f, 90f);
            Table(props, cp, 1.8f, 1.6f, 0f);
            Stretcher(props, cp, -1.6f, 1.8f, 90f, true);
            // Outside: stretchers, crates, oxygen, flag, sign, generator, water tank, sandbags, lamps.
            var o = new LF(Vector3.zero, 0f);
            Stretcher(props, new LF(new Vector3(-10.2f, 0, -1.5f), 15f), 0, 0, 0f, true);
            Stretcher(props, new LF(new Vector3(-10.5f, 0, -6.5f), -10f), 0, 0, 0f, false);
            CrateStack(props, new LF(new Vector3(-32.4f, 0, -4f), 90f));
            CrateStack(props, new LF(new Vector3(-9.5f, 0, -12.5f), 20f));
            CrateStack(props, new LF(new Vector3(-9.8f, 0, 4.6f), -15f));
            Oxygen(props, new LF(new Vector3(-19.4f, 0, -8.6f), 0f));
            Oxygen(props, new LF(new Vector3(-28.8f, 0, -10.7f), 90f));
            Sign(props, new LF(new Vector3(-8.6f, 0, -3.8f), -70f));
            Generator(props, new LF(new Vector3(-32.0f, 0, -20.5f), 90f));
            WaterTank(props, new LF(new Vector3(-32.2f, 0, 12.0f), 0f));
            Rod(props, new Vector3(-31.2f, 0.03f, -19.4f), new Vector3(-29.0f, 0.03f, -18.2f), 0.03f, Iron);
            Wheelchair(props, new LF(new Vector3(-11.6f, 0, -3.0f), -80f));
            Wheelchair(props, new LF(new Vector3(-20.4f, 0, -9.6f), 160f));
            WaitingChairs(props, new LF(new Vector3(-13.0f, 0, 0.9f), 180f), 4);
            WaitingChairs(props, new LF(new Vector3(-17.0f, 0, 0.9f), 180f), 3);
            FieldLamp(props, g, new Vector3(-18.5f, 0, -0.2f));
            FieldLamp(props, g, new Vector3(-29.6f, 0, -3.8f));
            Piece(g, "Tents", tents, solid);
            Piece(g, "Beds", beds, solid);
            Piece(g, "Hospital Props", props, solid);
        }

        static void Tent(MB mb, LF f, float len, float wid, Color cv, Color cvd, bool crossOnRoof)
        {
            float wallH = 1.75f, ridge = 3.7f, hl = len / 2, hw = wid / 2;
            // Ground sheet.
            LB(mb, f, 0, 0.035f, 0, len - 0.1f, 0.07f, wid - 0.1f, FloorG, SkipBottom);
            // Back wall, end walls (pentagons), roof, rolled-up front wall.
            LB(mb, f, 0, wallH / 2, -hw, len, wallH, 0.05f, cv);
            foreach (float sx in new[] { -1f, 1f })
                mb.ExtrudePoly(new[] { f.P(sx * hl, 0, -hw), f.P(sx * hl, 0, hw), f.P(sx * hl, wallH, hw), f.P(sx * hl, ridge, 0), f.P(sx * hl, wallH, -hw) }, f.r * Vector3.right * sx, 0.05f, cvd);
            float a = Mathf.Atan2(ridge - wallH, hw) * Mathf.Rad2Deg, sl = Mathf.Sqrt(hw * hw + (ridge - wallH) * (ridge - wallH));
            foreach (float sz in new[] { -1f, 1f })
            {
                var rr = f.r * Quaternion.Euler(sz > 0 ? a : -a, 0, 0);
                var cen = f.P(0, (wallH + ridge) / 2 + 0.03f, sz * hw / 2);
                mb.Box(cen, new Vector3(len + 0.5f, 0.05f, sl + 0.25f), cv, rr);
                // Scalloped valance along the eave.
                for (float x = -hl - 0.2f; x < hl + 0.2f; x += 0.5f)
                    LB(mb, f, x + 0.25f, wallH - 0.14f, sz * (hw + 0.1f), 0.46f, 0.26f, 0.04f, cvd);
                if (crossOnRoof || sz > 0)
                {
                    var nrm = rr * Vector3.up; var upv = rr * Vector3.forward * -sz;
                    var cc = cen + nrm * 0.03f;
                    var R2 = Quaternion.LookRotation(nrm, upv);
                    mb.Box(cc + nrm * 0.016f, new Vector3(1.9f, 1.9f, 0.012f), White, R2);
                    mb.Box(cc + nrm * 0.034f, new Vector3(1.5f, 0.48f, 0.014f), Red, R2);
                    mb.Box(cc + nrm * 0.034f, new Vector3(0.48f, 1.5f, 0.014f), Red, R2);
                }
            }
            // Rolled canvas over the open side, poles, ridge pole.
            mb.Prism(f.P(-hl, wallH - 0.08f, hw), 8, 0.13f, 0.13f, len, cvd, f.r * Quaternion.Euler(0, 0, -90));
            for (float x = -hl; x <= hl + 0.01f; x += len / 4)
            {
                mb.Prism(f.P(x, 0, hw - 0.05f), 6, 0.045f, 0.045f, wallH - 0.15f, MetalD);
                mb.Prism(f.P(x, 0, -hw + 0.08f), 6, 0.045f, 0.045f, wallH, MetalD);
                Rod(mb, f.P(x, wallH - 0.05f, hw + 0.05f), f.P(x, 0.02f, hw + 1.6f), 0.02f, Sand);
                Rod(mb, f.P(x, wallH - 0.05f, -hw - 0.05f), f.P(x, 0.02f, -hw - 1.6f), 0.02f, Sand);
                LB(mb, f, x, 0.08f, hw + 1.6f, 0.06f, 0.16f, 0.06f, MetalD);
                LB(mb, f, x, 0.08f, -hw - 1.6f, 0.06f, 0.16f, 0.06f, MetalD);
            }
            mb.Prism(f.P(0, 0, 0), 6, 0.06f, 0.06f, ridge - 0.05f, MetalD);
            mb.Prism(f.P(-hl + 0.1f, 0, 0), 6, 0.06f, 0.06f, ridge - 0.05f, MetalD);
            mb.Prism(f.P(hl - 0.1f, 0, 0), 6, 0.06f, 0.06f, ridge - 0.05f, MetalD);
            // Door flap on one end, red cross on the other end wall.
            LB(mb, f, -hl - 0.065f, 1.0f, 0, 0.02f, 2.0f, 1.2f, Dark);
            LB(mb, f, -hl - 0.09f, 1.05f, -0.75f, 0.06f, 2.1f, 0.35f, cv);
            LB(mb, f, -hl - 0.09f, 1.05f, 0.75f, 0.06f, 2.1f, 0.35f, cv);
            var en = f.r * Vector3.right; var ec = f.P(hl + 0.055f, 2.2f, 0);
            var R3 = Quaternion.LookRotation(en, Vector3.up);
            mb.Box(ec + en * 0.01f, new Vector3(1.2f, 1.2f, 0.01f), White, R3);
            mb.Box(ec + en * 0.026f, new Vector3(0.9f, 0.3f, 0.012f), Red, R3);
            mb.Box(ec + en * 0.026f, new Vector3(0.3f, 0.9f, 0.012f), Red, R3);
            LCol(f, 0, wallH / 2, -hw, len, wallH, 0.2f, "Tent Back");
            LCol(f, -hl, 1.6f, 0, 0.2f, 3.2f, wid, "Tent End");
            LCol(f, hl, 1.6f, 0, 0.2f, 3.2f, wid, "Tent End");
        }

        static void Canopy(MB mb, LF f, float sx, float sz)
        {
            float h = 2.6f;
            foreach (float px in new[] { -1f, 1f }) foreach (float pz in new[] { -1f, 1f })
                {
                    mb.Prism(f.P(px * sx / 2, 0, pz * sz / 2), 6, 0.06f, 0.06f, h + 0.05f, MetalD);
                    LCol(f, px * sx / 2, h / 2, pz * sz / 2, 0.15f, h, 0.15f, "Canopy Pole");
                }
            mb.Prism(f.P(0, h, 0), 4, sx * 0.72f, 0f, 1.1f, TentW, f.r, 45f);
            foreach (float s in new[] { -1f, 1f })
            {
                for (float x = -sx / 2; x < sx / 2 - 0.01f; x += 0.5f) LB(mb, f, x + 0.25f, h - 0.12f, s * (sz / 2 + 0.06f), 0.46f, 0.24f, 0.03f, TentB);
                for (float z = -sz / 2; z < sz / 2 - 0.01f; z += 0.5f) LB(mb, f, s * (sx / 2 + 0.06f), h - 0.12f, z + 0.25f, 0.03f, 0.24f, 0.46f, TentB);
            }
            LB(mb, f, 0, 0.03f, 0, sx - 0.4f, 0.06f, sz - 0.4f, CanvasD, SkipBottom);
        }

        static void Bed(MB mb, LF parent, float x, float z, float yaw, Color blanket)
        {
            var b = new LF(parent.P(x, 0, z), parent.yaw + yaw);
            float L = 1.95f, W = 0.86f, hy = 0.52f;
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f }) LB(mb, b, sx * (W / 2 - 0.02f), hy / 2, sz * (L / 2 - 0.02f), 0.04f, hy, 0.04f, Metal);
                LB(mb, b, sx * (W / 2 - 0.02f), hy - 0.03f, 0, 0.04f, 0.05f, L, Metal);
                LB(mb, b, sx * (W / 2 - 0.02f), hy + 0.25f, -(L / 2 - 0.02f), 0.045f, 0.5f, 0.045f, Metal);
                LB(mb, b, sx * (W / 2 - 0.02f), hy + 0.12f, L / 2 - 0.02f, 0.045f, 0.24f, 0.045f, Metal);
            }
            LB(mb, b, 0, hy - 0.03f, -(L / 2 - 0.02f), W, 0.05f, 0.04f, Metal);
            LB(mb, b, 0, hy - 0.03f, L / 2 - 0.02f, W, 0.05f, 0.04f, Metal);
            LB(mb, b, 0, hy + 0.5f, -(L / 2 - 0.02f), W, 0.05f, 0.05f, Metal);
            LB(mb, b, 0, hy + 0.24f, L / 2 - 0.02f, W, 0.05f, 0.05f, Metal);
            for (int i = -1; i <= 1; i++) LB(mb, b, i * 0.2f, hy + 0.3f, -(L / 2 - 0.02f), 0.025f, 0.4f, 0.025f, Metal);
            LB(mb, b, 0, hy + 0.07f, 0, W - 0.07f, 0.14f, L - 0.1f, Mattress);
            LB(mb, b, 0, hy + 0.19f, -L / 2 + 0.3f, W - 0.26f, 0.1f, 0.36f, White);
            LB(mb, b, 0, hy + 0.165f, 0.22f, W - 0.03f, 0.05f, L - 0.62f, blanket);
            LB(mb, b, 0, hy + 0.195f, -L / 2 + 0.66f, W - 0.03f, 0.02f, 0.16f, White);
            LCol(b, 0, 0.5f, 0, W, 1.0f, L, "Bed");
        }

        static void IVStand(MB mb, LF parent, float x, float z)
        {
            var b = parent.P(x, 0, z);
            for (int i = 0; i < 5; i++) { var d = Quaternion.Euler(0, i * 72f + parent.yaw, 0) * Vector3.forward; Rod(mb, b + Vector3.up * 0.06f, b + d * 0.28f + Vector3.up * 0.03f, 0.025f, Metal); }
            mb.Prism(b, 6, 0.02f, 0.02f, 1.85f, Metal);
            Rod(mb, b + Vector3.up * 1.82f + Vector3.left * 0.18f, b + Vector3.up * 1.82f + Vector3.right * 0.18f, 0.02f, Metal);
            mb.Box(b + Vector3.up * 1.6f + Vector3.left * 0.15f, new Vector3(0.14f, 0.24f, 0.05f), IVBag, Quaternion.identity);
            mb.Box(b + Vector3.up * 1.6f + Vector3.right * 0.15f, new Vector3(0.12f, 0.2f, 0.05f), IVBag, Quaternion.identity);
            Rod(mb, b + Vector3.up * 1.47f + Vector3.left * 0.15f, parent.P(x - 0.6f, 0.78f, z + 0.6f), 0.01f, IVBag);
        }

        static void Screen(MB mb, LF parent, float x, float z, float yaw)
        {
            var f = new LF(parent.P(x, 0, z), parent.yaw + yaw);
            for (int i = -1; i <= 1; i++)
            {
                var p = new LF(f.P(i * 0.66f, 0, (i == 0 ? 0f : 0.18f)), f.yaw + i * 22f);
                foreach (float sx in new[] { -0.32f, 0.32f }) LB(mb, p, sx, 0.9f, 0, 0.03f, 1.8f, 0.03f, Metal);
                LB(mb, p, 0, 1.78f, 0, 0.64f, 0.03f, 0.03f, Metal);
                LB(mb, p, 0, 0.25f, 0, 0.64f, 0.03f, 0.03f, Metal);
                LB(mb, p, 0, 1.0f, 0, 0.6f, 1.45f, 0.012f, i == 0 ? C(0.78f, 0.88f, 0.80f) : C(0.92f, 0.94f, 0.92f));
                foreach (float sx in new[] { -0.3f, 0.3f }) LB(mb, p, sx, 0.03f, 0, 0.05f, 0.06f, 0.3f, Metal);
            }
        }

        static void Crate(MB mb, LF parent, float x, float z, float sx, float sy, float sz, float yaw, Color col, bool cross, float y = 0f)
        {
            var f = new LF(parent.P(x, y, z), parent.yaw + yaw);
            LB(mb, f, 0, sy / 2, 0, sx, sy, sz, col);
            var ed = col * 0.8f; ed.a = col.a;
            foreach (float a in new[] { -1f, 1f }) foreach (float b in new[] { -1f, 1f }) LB(mb, f, a * (sx / 2 - 0.02f), sy / 2, b * (sz / 2 - 0.02f), 0.06f, sy + 0.01f, 0.06f, ed);
            LB(mb, f, 0, sy - 0.02f, 0, sx + 0.012f, 0.05f, sz + 0.012f, ed);
            if (!cross) return;
            float s = Mathf.Min(sx, sz) * 0.55f;
            mb.Box(f.P(0, sy + 0.008f, 0), new Vector3(s, 0.016f, s * 0.32f), Red, f.r);
            mb.Box(f.P(0, sy + 0.009f, 0), new Vector3(s * 0.32f, 0.016f, s), Red, f.r);
            float fs = Mathf.Min(sx, sy) * 0.5f;
            mb.Box(f.P(0, sy / 2, sz / 2 + 0.008f), new Vector3(fs, fs * 0.32f, 0.016f), Red, f.r);
            mb.Box(f.P(0, sy / 2, sz / 2 + 0.009f), new Vector3(fs * 0.32f, fs, 0.016f), Red, f.r);
        }

        static void CrateStack(MB mb, LF f)
        {
            Crate(mb, f, 0, 0, 1.0f, 0.65f, 0.8f, 0f, BoxBlue, false);
            Crate(mb, f, 1.1f, 0.1f, 0.9f, 0.6f, 0.7f, 8f, White, true);
            Crate(mb, f, 0.05f, 0.05f, 0.8f, 0.5f, 0.6f, -6f, White, true, 0.65f);
            Crate(mb, f, -0.95f, -0.1f, 0.7f, 0.9f, 0.7f, 12f, BoxBlue, false);
            Crate(mb, f, 1.05f, 0.1f, 0.5f, 0.35f, 0.45f, 20f, White, true, 0.6f);
            LCol(f, 0.1f, 0.6f, 0, 3.0f, 1.2f, 1.0f, "Crates");
        }

        static void Cabinet(MB mb, LF parent, float x, float z)
        {
            var f = new LF(parent.P(x, 0, z), parent.yaw);
            LB(mb, f, 0, 0.9f, 0, 0.7f, 1.8f, 0.42f, White);
            LB(mb, f, -0.17f, 1.15f, 0.215f, 0.3f, 1.1f, 0.012f, GlassShop);
            LB(mb, f, 0.17f, 1.15f, 0.215f, 0.3f, 1.1f, 0.012f, GlassShop);
            LB(mb, f, 0, 0.3f, 0.215f, 0.64f, 0.4f, 0.012f, C(0.88f, 0.88f, 0.86f, 0.1f));
            for (int i = 0; i < 4; i++) LB(mb, f, -0.2f + i * 0.13f, 1.35f, 0.1f, 0.07f, 0.18f, 0.07f, i % 2 == 0 ? Red : C(0.4f, 0.6f, 0.75f, 0.3f));
            LB(mb, f, 0, 1.6f, 0.216f, 0.24f, 0.24f, 0.01f, White);
            LB(mb, f, 0, 1.6f, 0.218f, 0.18f, 0.06f, 0.012f, Red);
            LB(mb, f, 0, 1.6f, 0.219f, 0.06f, 0.18f, 0.012f, Red);
            LCol(f, 0, 0.9f, 0, 0.7f, 1.8f, 0.42f, "Cabinet");
        }

        static void Table(MB mb, LF parent, float x, float z, float yaw)
        {
            var f = new LF(parent.P(x, 0, z), parent.yaw + yaw);
            LB(mb, f, 0, 0.76f, 0, 1.8f, 0.05f, 0.8f, MetalD);
            foreach (float a in new[] { -1f, 1f }) foreach (float b in new[] { -1f, 1f }) LB(mb, f, a * 0.85f, 0.37f, b * 0.35f, 0.04f, 0.74f, 0.04f, Metal);
            Crate(mb, f, -0.55f, 0, 0.4f, 0.22f, 0.3f, 10f, White, true, 0.785f);
            Crate(mb, f, -0.1f, 0.15f, 0.3f, 0.18f, 0.25f, -8f, White, true, 0.785f);
            for (int i = 0; i < 5; i++) mb.Prism(f.P(0.25f + i * 0.11f, 0.785f, -0.15f + (i % 2) * 0.1f), 6, 0.04f, 0.04f, 0.2f + (i % 3) * 0.04f, i % 2 == 0 ? C(0.45f, 0.62f, 0.78f, 0.5f) : C(0.62f, 0.38f, 0.2f, 0.5f));
            LB(mb, f, 0.6f, 0.8f, 0.2f, 0.4f, 0.03f, 0.3f, White);
            mb.Prism(f.P(0.15f, 0.785f, 0.22f), 10, 0.14f, 0.18f, 0.08f, Metal);
            LCol(f, 0, 0.4f, 0, 1.8f, 0.8f, 0.8f, "Table");
        }

        static void Stretcher(MB mb, LF parent, float x, float z, float yaw, bool trestles)
        {
            var f = new LF(parent.P(x, 0, z), parent.yaw + yaw);
            float y = trestles ? 0.62f : 0.1f;
            foreach (float s in new[] { -0.28f, 0.28f }) LB(mb, f, s, y, 0, 0.05f, 0.05f, 2.3f, Metal);
            LB(mb, f, 0, y + 0.01f, 0, 0.56f, 0.02f, 1.85f, RescueO);
            LB(mb, f, 0, y + 0.06f, -0.7f, 0.4f, 0.08f, 0.3f, White);
            if (trestles)
                foreach (float zz in new[] { -0.75f, 0.75f })
                {
                    Rod(mb, f.P(-0.35f, 0, zz - 0.2f), f.P(-0.35f, y - 0.03f, zz + 0.1f), 0.04f, MetalD);
                    Rod(mb, f.P(-0.35f, 0, zz + 0.2f), f.P(-0.35f, y - 0.03f, zz - 0.1f), 0.04f, MetalD);
                    Rod(mb, f.P(0.35f, 0, zz - 0.2f), f.P(0.35f, y - 0.03f, zz + 0.1f), 0.04f, MetalD);
                    Rod(mb, f.P(0.35f, 0, zz + 0.2f), f.P(0.35f, y - 0.03f, zz - 0.1f), 0.04f, MetalD);
                    LB(mb, f, 0, y - 0.05f, zz, 0.78f, 0.05f, 0.06f, MetalD);
                }
            else foreach (float zz in new[] { -0.9f, 0.9f }) foreach (float s in new[] { -0.28f, 0.28f }) LB(mb, f, s, 0.04f, zz, 0.06f, 0.08f, 0.06f, MetalD);
            LCol(f, 0, y / 2 + 0.1f, 0, 0.7f, y + 0.2f, 2.3f, "Stretcher");
        }

        static void Oxygen(MB mb, LF f)
        {
            LB(mb, f, 0, 0.05f, 0, 1.4f, 0.1f, 0.4f, MetalD);
            LB(mb, f, 0, 0.9f, -0.2f, 1.4f, 0.06f, 0.05f, MetalD);
            for (int i = 0; i < 5; i++)
            {
                var b = f.P(-0.56f + i * 0.28f, 0.1f, 0);
                mb.Prism(b, 10, 0.11f, 0.11f, 1.15f, OxyGreen);
                mb.Prism(b + Vector3.up * 1.15f, 10, 0.11f, 0.05f, 0.12f, White);
                mb.Prism(b + Vector3.up * 1.27f, 6, 0.03f, 0.03f, 0.1f, Metal);
                mb.Box(b + Vector3.up * 1.36f, new Vector3(0.09f, 0.04f, 0.04f), Metal, f.r);
            }
            LCol(f, 0, 0.7f, 0, 1.4f, 1.4f, 0.4f, "Oxygen");
        }

        static void FlagPole(MB mb, Vector3 p)
        {
            mb.Prism(p, 8, 0.25f, 0.2f, 0.3f, StoneD);
            mb.Prism(p + Vector3.up * 0.3f, 8, 0.06f, 0.045f, 7.2f, White);
            mb.Sphere(p + Vector3.up * 7.55f, 0.1f, 8, 4, (i, j) => Gold);
            var R = Quaternion.Euler(0, 20f, 0);
            var fc = p + Vector3.up * 6.75f + R * Vector3.right * 0.95f;
            mb.Box(fc, new Vector3(1.8f, 1.2f, 0.02f), White, R);
            mb.Box(fc, new Vector3(1.2f, 0.34f, 0.036f), Red, R);
            mb.Box(fc, new Vector3(0.34f, 1.0f, 0.037f), Red, R);
            cols.Add((p + Vector3.up * 1.5f, new Vector3(0.5f, 3f, 0.5f), Quaternion.identity, "Flag Pole"));
        }

        static void Sign(MB mb, LF f)
        {
            foreach (float s in new[] { -1f, 1f })
            {
                var rr = f.r * Quaternion.Euler(s * 14f, 0, 0);
                var cen = f.P(0, 0.62f, s * 0.16f);
                mb.Box(cen, new Vector3(0.9f, 1.28f, 0.04f), White, rr);
                var nrm = rr * Vector3.forward * s;
                mb.Box(cen + nrm * 0.025f + Vector3.up * 0.15f, new Vector3(0.55f, 0.17f, 0.012f), Red, rr);
                mb.Box(cen + nrm * 0.026f + Vector3.up * 0.15f, new Vector3(0.17f, 0.55f, 0.012f), Red, rr);
                mb.Box(cen + nrm * 0.025f - Vector3.up * 0.38f, new Vector3(0.7f, 0.1f, 0.012f), Red, rr);
            }
            LCol(f, 0, 0.6f, 0, 0.9f, 1.2f, 0.5f, "Sign");
        }

        static void Generator(MB mb, LF f)
        {
            LB(mb, f, 0, 0.6f, 0, 1.7f, 0.9f, 1.0f, GenY);
            LB(mb, f, 0, 1.07f, 0, 1.6f, 0.06f, 0.9f, MetalD);
            LB(mb, f, -0.55f, 0.65f, 0.505f, 0.45f, 0.5f, 0.02f, MetalD);
            for (int i = 0; i < 5; i++) LB(mb, f, 0.3f, 0.4f + i * 0.1f, 0.505f, 0.7f, 0.04f, 0.02f, Iron);
            LB(mb, f, -0.55f, 0.75f, 0.52f, 0.1f, 0.1f, 0.02f, Red);
            mb.Prism(f.P(0.6f, 1.1f, -0.2f), 8, 0.06f, 0.06f, 0.6f, Iron);
            foreach (float a in new[] { -0.6f, 0.6f }) foreach (float b in new[] { -0.52f, 0.52f })
                    mb.Prism(f.P(a, 0.2f, b + (b > 0 ? -0.07f : 0.07f)), 10, 0.2f, 0.2f, 0.14f, Iron, f.r * Quaternion.Euler(b > 0 ? 90 : -90, 0, 0));
            LCol(f, 0, 0.6f, 0, 1.7f, 1.2f, 1.1f, "Generator");
        }

        static void WaterTank(MB mb, LF f)
        {
            foreach (float a in new[] { -0.6f, 0.6f }) foreach (float b in new[] { -0.6f, 0.6f }) LB(mb, f, a, 0.5f, b, 0.08f, 1.0f, 0.08f, MetalD);
            LB(mb, f, 0, 1.02f, 0, 1.5f, 0.06f, 1.5f, MetalD);
            mb.Prism(f.P(0, 1.05f, 0), 12, 0.92f, 0.92f, 1.5f, C(0.30f, 0.44f, 0.55f, 0.4f));
            mb.Prism(f.P(0, 2.55f, 0), 12, 0.92f, 0.2f, 0.2f, C(0.26f, 0.38f, 0.48f, 0.4f));
            LB(mb, f, 0, 0.75f, 0.95f, 0.06f, 0.5f, 0.06f, Metal);
            LB(mb, f, 0, 0.6f, 1.0f, 0.12f, 0.06f, 0.12f, Metal);
            LCol(f, 0, 1.3f, 0, 2.0f, 2.6f, 2.0f, "Water Tank");
        }

        static void Sandbags(MB mb, Vector3 center, float radius, float a0, float span)
        {
            for (int layer = 0; layer < 3; layer++)
            {
                int n = Mathf.RoundToInt(span * Mathf.Deg2Rad * radius / 0.62f);
                for (int i = 0; i < n; i++)
                {
                    float a = a0 - span / 2 + (i + (layer % 2) * 0.5f) * span / n;
                    if (layer % 2 == 1 && i == n - 1) continue;
                    var R = Quaternion.Euler(0, a, 0);
                    var p = center + R * Vector3.forward * radius + Vector3.up * (0.13f + layer * 0.24f);
                    mb.Box(p, new Vector3(0.58f, 0.24f, 0.36f), Vary(layer % 2 == 0 ? Sand : SandD, 0.05f), R * Quaternion.Euler(0, 0, Rf(-3f, 3f)));
                    mb.Box(p + Vector3.up * 0.04f, new Vector3(0.48f, 0.22f, 0.3f), Vary(Sand, 0.05f), R);
                }
            }
            for (int i = 0; i < 6; i++)
            {
                var R = Quaternion.Euler(0, a0 - span / 2 + (i + 0.5f) * span / 6, 0);
                cols.Add((center + R * Vector3.forward * radius + Vector3.up * 0.4f, new Vector3(span * Mathf.Deg2Rad * radius / 6 + 0.2f, 0.8f, 0.4f), R, "Sandbags"));
            }
        }

        static void Wheelchair(MB mb, LF f)
        {
            LB(mb, f, 0, 0.5f, 0, 0.46f, 0.05f, 0.44f, BoxBlue);
            LB(mb, f, 0, 0.78f, -0.22f, 0.44f, 0.5f, 0.04f, BoxBlue);
            foreach (float s in new[] { -1f, 1f })
            {
                LB(mb, f, s * 0.25f, 0.68f, 0, 0.04f, 0.04f, 0.42f, Metal);
                LB(mb, f, s * 0.25f, 0.58f, -0.2f, 0.03f, 0.62f, 0.03f, Metal);
                LB(mb, f, s * 0.25f, 0.92f, -0.3f, 0.03f, 0.03f, 0.16f, Iron);
                mb.Prism(f.P(s * 0.28f, 0.3f, -0.05f), 14, 0.3f, 0.3f, 0.03f, MetalD, f.r * Quaternion.Euler(0, 0, s > 0 ? -90 : 90));
                mb.Prism(f.P(s * 0.2f, 0.07f, 0.26f), 8, 0.07f, 0.07f, 0.03f, Iron, f.r * Quaternion.Euler(0, 0, s > 0 ? -90 : 90));
                LB(mb, f, s * 0.2f, 0.3f, 0.26f, 0.025f, 0.4f, 0.025f, Metal);
            }
            LB(mb, f, 0, 0.12f, 0.36f, 0.4f, 0.02f, 0.14f, Iron);
            LCol(f, 0, 0.5f, 0, 0.65f, 1.0f, 0.75f, "Wheelchair");
        }

        static void WaitingChairs(MB mb, LF f, int n)
        {
            LB(mb, f, 0, 0.36f, 0, n * 0.52f, 0.04f, 0.06f, MetalD);
            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) / 2f) * 0.52f;
                LB(mb, f, x, 0.44f, 0.02f, 0.46f, 0.05f, 0.44f, BoxBlue);
                LB(mb, f, x, 0.72f, -0.2f, 0.46f, 0.42f, 0.04f, BoxBlue);
            }
            foreach (float s in new[] { -1f, 1f })
            {
                float x = s * (n * 0.26f - 0.08f);
                LB(mb, f, x, 0.18f, 0, 0.04f, 0.36f, 0.04f, MetalD);
                LB(mb, f, x, 0.02f, 0, 0.06f, 0.04f, 0.5f, MetalD);
            }
            LCol(f, 0, 0.45f, 0, n * 0.52f, 0.9f, 0.5f, "Waiting Chairs");
        }

        static void FieldLamp(MB mb, Transform g, Vector3 p)
        {
            foreach (int i in new[] { 0, 1, 2 }) { var d = Quaternion.Euler(0, i * 120f, 0) * Vector3.forward; Rod(mb, p + Vector3.up * 1.2f, p + d * 0.7f, 0.04f, MetalD); }
            mb.Prism(p, 6, 0.04f, 0.04f, 3.0f, MetalD);
            mb.Prism(p + Vector3.up * 2.85f, 8, 0.12f, 0.3f, 0.22f, MetalD);
            glowMB.Prism(p + Vector3.up * 2.75f, 8, 0.1f, 0.12f, 0.12f, GLamp);
            AddLight(g, "Field Lamp Light", p + Vector3.up * 2.4f, LightType.Point, new Color(1f, 0.82f, 0.58f), 2.2f, 7f);
            cols.Add((p + Vector3.up * 1.5f, new Vector3(0.3f, 3f, 0.3f), Quaternion.identity, "Field Lamp"));
        }

        // ---------- Cypresses ----------

        static void BuildCypresses()
        {
            var g = Group("Cypresses");
            var pm = new MB();
            var mb = new MB();
            var spots = new List<(Vector3 p, float h, bool pot)>();
            for (int i = 0; i < 8; i++)
            {
                if (i == 4 || i == 5) continue;   // leave the side towards the hospital open
                var d = Quaternion.Euler(0, i * 45f + 22.5f, 0) * Vector3.forward;
                spots.Add((FountainPos + d * 8.4f, Rf(6.5f, 8f), true));
            }
            // East row: the pot in line with the lever-gate tunnel moves just north of the gate.
            for (float z = -20f; z < PZ - 10f; z += 7f) spots.Add((new Vector3(PX - 2.6f, 0, z > TunnelZ0 - 1.5f && z < TunnelZ1 + 1.5f ? TunnelZ1 + 2.0f : z), Rf(6.5f, 8.5f), true));
            for (float x = -2f; x < PX - 4f; x += 7.5f) spots.Add((new Vector3(x, 0, -PZ + 2.6f), Rf(6.5f, 8.5f), true));
            spots.Add((new Vector3(-6.5f, 0, -20.5f), 7f, true));
            spots.Add((new Vector3(-6.5f, 0, 13.5f), 7.5f, true));
            spots.Add((new Vector3(PX - 9.2f, 0, PZ - 3.0f), 9f, true));
            // Tall ones behind the houses, showing over the roofs.
            var behind = new[] { new Vector3(-20, 0, PZ + D + 3), new Vector3(-6, 0, PZ + D + 5), new Vector3(9, 0, PZ + D + 3.5f), new Vector3(24, 0, PZ + D + 6),
                new Vector3(-PX - D - 3, 0, -10), new Vector3(-PX - D - 5, 0, 8), new Vector3(PX + D + 4, 0, -24), new Vector3(PX + D + 3, 0, 4),   // -24: clear of the tunnel exit yard
                new Vector3(-14, 0, -PZ - D - 4), new Vector3(4, 0, -PZ - D - 3), new Vector3(20, 0, -PZ - D - 5), new Vector3(-28, 0, PZ + D + 4) };
            foreach (var b in behind) spots.Add((b, Rf(24f, 29f), false));
            int pi = 0;
            foreach (var s in spots)
            {
                if (!s.pot) { Cypress(mb, s.p, s.h); continue; }
                // Three planter variants in turn: terracotta pot with a cypress, stone box with a ball topiary,
                // tall terracotta urn with a flowering oleander.
                switch (pi++ % 3)
                {
                    case 0: PotCypress(pm, s.p, s.h); break;
                    case 1: BoxTopiary(pm, s.p); break;
                    default: UrnOleander(pm, s.p); break;
                }
                cols.Add((s.p + Vector3.up * 1.5f, new Vector3(1.4f, 3f, 1.4f), Quaternion.identity, "Planter"));
            }
            Piece(g, "Cypresses", mb, solid);
            Piece(g, "Planters", pm, solid);
        }

        static void PotCypress(MB mb, Vector3 p, float h)
        {
            mb.Prism(p, 8, 0.52f, 0.7f, 0.85f, Pot, 22.5f);
            mb.Prism(p + Vector3.up * 0.85f, 8, 0.78f, 0.78f, 0.12f, Vary(Pot, 0.05f), 22.5f);
            mb.Prism(p + Vector3.up * 0.2f, 8, 0.6f, 0.68f, 0.12f, Vary(Pot, 0.08f) * 0.9f, 22.5f);
            mb.Prism(p + Vector3.up * 0.9f, 8, 0.66f, 0.66f, 0.04f, Soil, 22.5f);
            Cypress(mb, p + Vector3.up * 0.97f, h);
        }

        static void BoxTopiary(MB mb, Vector3 p)
        {
            var R = Quaternion.Euler(0, Rf(0, 90), 0);
            mb.Box(p + Vector3.up * 0.5f, new Vector3(1.5f, 1.0f, 1.5f), Stone, R, SkipBottom);
            mb.Box(p + Vector3.up * 1.06f, new Vector3(1.66f, 0.12f, 1.66f), StoneL, R);
            mb.Box(p + Vector3.up * 0.06f, new Vector3(1.62f, 0.12f, 1.62f), StoneD, R, SkipBottom);
            mb.Box(p + Vector3.up * 1.13f, new Vector3(1.3f, 0.04f, 1.3f), Soil, R);
            // Clipped bay laurel: a trunk with three stacked balls, smaller towards the top.
            mb.Prism(p + Vector3.up * 1.12f, 6, 0.09f, 0.06f, 4.75f, Trunk);   // up into the top ball
            float y = 1.95f;
            foreach (float r in new[] { 0.85f, 0.68f, 0.5f })
            {
                var col = Vary(rnd.NextDouble() < 0.5 ? Leaf : LeafD, 0.06f);
                y += r;
                mb.Sphere(p + Vector3.up * y, r, 9, 6, (i, j) => j >= 4 ? col * 1.12f : col, Rf(0f, 360f));
                for (int k = 0; k < 7; k++)
                {
                    float a = Rf(0, Mathf.PI * 2), e = Rf(-0.6f, 0.9f);
                    var d = new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
                    var lc = Vary(Leaf, 0.08f);
                    mb.Sphere(p + Vector3.up * y + d * r * 0.85f, r * 0.3f, 5, 3, (i, j) => lc, Rf(0f, 360f));
                }
                y += r + 0.12f;
            }
        }

        static void UrnOleander(MB mb, Vector3 p)
        {
            // Tall bellied terracotta urn.
            mb.Prism(p, 10, 0.42f, 0.5f, 0.15f, Vary(Pot, 0.05f) * 0.9f);
            mb.Prism(p + Vector3.up * 0.15f, 10, 0.36f, 0.62f, 0.45f, Pot);
            mb.Prism(p + Vector3.up * 0.6f, 10, 0.62f, 0.58f, 0.45f, Pot);
            mb.Prism(p + Vector3.up * 1.05f, 10, 0.58f, 0.5f, 0.15f, Vary(Pot, 0.06f));
            mb.Prism(p + Vector3.up * 1.2f, 10, 0.62f, 0.64f, 0.12f, Vary(Pot, 0.04f) * 1.05f);
            mb.Prism(p + Vector3.up * 1.33f, 10, 0.54f, 0.54f, 0.03f, Soil);   // 1 cm above the rim top (1.32), never coplanar
            // Oleander: a few stems and a tall, loose cloud of leaf clumps with pink and white flowers.
            var top = p + Vector3.up * 1.3f;
            for (int k = 0; k < 5; k++)
            {
                float a = k * 72f * Mathf.Deg2Rad + Rf(-0.3f, 0.3f);
                Rod(mb, top, top + new Vector3(Mathf.Sin(a) * 0.45f, Rf(1.4f, 1.9f), Mathf.Cos(a) * 0.45f), 0.045f, Trunk);
            }
            var flower = rnd.NextDouble() < 0.6 ? FlowerP : FlowerW;
            for (int k = 0; k < 26; k++)
            {
                float t = Rf(0f, 1f), a = Rf(0, Mathf.PI * 2);
                float yy = 1.4f + t * 2.0f, rr = Mathf.Lerp(0.45f, 0.95f, Mathf.Sin(Mathf.Min(1f, t * 1.25f) * Mathf.PI * 0.9f)) * Rf(0.5f, 1f);
                var c = p + Vector3.up * (1.3f + yy) + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * rr;
                var col = Vary(rnd.NextDouble() < 0.5 ? Leaf : LeafD, 0.07f);
                mb.Sphere(c, Rf(0.32f, 0.48f), 6, 4, (i, j) => j == 3 ? col * 1.12f : col, Rf(0f, 360f));
                if (rnd.NextDouble() < 0.55)
                {
                    var fc = Vary(flower, 0.05f);
                    mb.Sphere(c + new Vector3(Mathf.Sin(a), 0.6f, Mathf.Cos(a)) * 0.35f, 0.13f, 5, 3, (i, j) => fc, Rf(0f, 360f));
                }
            }
        }

        static void Cypress(MB mb, Vector3 b, float h)
        {
            // Italian cypress: a narrow dark column, rounded at the foot, widest low down, tapering to a pointed flame,
            // with lumpy clumps of foliage all over it.
            float rmax = h * 0.072f;
            mb.Prism(b, 6, rmax * 0.22f, rmax * 0.18f, h * 0.08f, Trunk);
            float y = h * 0.05f;
            float R(float t) => t < 0.12f ? rmax * Mathf.Lerp(0.55f, 1f, Mathf.Sqrt(t / 0.12f)) : t < 0.35f ? rmax : rmax * Mathf.Pow(Mathf.Max(0.0f, 1 - (t - 0.35f) / 0.65f), 0.75f);
            int tiers = Mathf.Clamp(Mathf.RoundToInt(h / 0.6f), 10, 40);
            for (int i = 0; i < tiers; i++)
            {
                float t0 = (float)i / tiers, t1 = (float)(i + 1) / tiers;
                float y0 = Mathf.Lerp(y, h, t0), y1 = Mathf.Lerp(y, h, t1);
                var off = new Vector3(Rf(-1f, 1f), 0, Rf(-1f, 1f)) * rmax * 0.05f;
                float r0 = R(t0) * Rf(0.94f, 1.04f), r1 = R(t1) * Rf(0.94f, 1.0f);
                var col = Vary(CypressCols[rnd.Next(CypressCols.Length)], 0.05f);
                mb.Prism(b + off + Vector3.up * y0, 8, Mathf.Max(0.03f, r0), Mathf.Max(0.02f, r1), (y1 - y0) * 1.1f, col, Rf(0, 360));
            }
            int lumps = Mathf.RoundToInt(h * 5f);
            for (int k = 0; k < lumps; k++)
            {
                float t = Rf(0.04f, 0.9f), rt = R(t);
                if (rt < 0.05f) continue;
                float a = Rf(0, Mathf.PI * 2);
                var p = b + Vector3.up * Mathf.Lerp(y, h, t) + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * rt * 0.82f;
                var col = Vary(CypressCols[rnd.Next(CypressCols.Length)], 0.07f);
                mb.Sphere(p, Mathf.Min(rmax * Rf(0.24f, 0.38f), rt * 0.65f), 5, 3, (i, j) => j == 2 ? col * 1.12f : col, Mathf.Repeat(p.x * 37f + p.y * 91f + p.z * 53f, 360f));
            }
        }

        // ---------- Street props: lamp posts and benches ----------

        static void BuildStreetProps()
        {
            var g = Group("Props");
            var mb = new MB();
            var lamps = new[] { new Vector3(6.5f, 0, -4f), new Vector3(21.5f, 0, -4f), new Vector3(6.5f, 0, 10f), new Vector3(21.5f, 0, 10f), new Vector3(0f, 0, -22f), new Vector3(28f, 0, -22f), new Vector3(-4f, 0, 22f), new Vector3(16f, 0, 22f) };
            foreach (var p in lamps)
            {
                mb.Prism(p, 8, 0.24f, 0.2f, 0.5f, Iron, 22.5f);
                mb.Prism(p + Vector3.up * 0.5f, 8, 0.09f, 0.07f, 3.3f, Iron, 22.5f);
                mb.Prism(p + Vector3.up * 2.0f, 8, 0.12f, 0.12f, 0.12f, Iron, 22.5f);
                mb.Prism(p + Vector3.up * 3.8f, 4, 0.1f, 0.16f, 0.12f, Iron, 45f);
                glowMB.Prism(p + Vector3.up * 3.92f, 4, 0.17f, 0.24f, 0.46f, GLampDim, 45f);
                foreach (int i in new[] { 0, 1, 2, 3 }) { var d = Quaternion.Euler(0, 45 + i * 90, 0) * Vector3.forward; Rod(mb, p + Vector3.up * 3.92f + d * 0.17f, p + Vector3.up * 4.38f + d * 0.245f, 0.025f, Iron); }
                mb.Prism(p + Vector3.up * 4.38f, 4, 0.33f, 0f, 0.26f, Iron, 45f);
                mb.Sphere(p + Vector3.up * 4.72f, 0.06f, 6, 3, (i, j) => Iron);
                cols.Add((p + Vector3.up * 2f, new Vector3(0.45f, 4f, 0.45f), Quaternion.identity, "Lamp Post"));
            }
            for (int i = 0; i < 4; i++)
            {
                var R = Quaternion.Euler(0, i * 90f + 45f, 0); var d = R * Vector3.forward;
                var f = new LF(FountainPos + d * 7.0f, i * 90f + 45f);
                LB(mb, f, 0, 0.44f, 0, 1.9f, 0.1f, 0.5f, StoneL);
                LB(mb, f, -0.72f, 0.2f, 0, 0.26f, 0.4f, 0.4f, Stone);
                LB(mb, f, 0.72f, 0.2f, 0, 0.26f, 0.4f, 0.4f, Stone);
                LCol(f, 0, 0.25f, 0, 1.9f, 0.5f, 0.5f, "Bench");
            }
            Piece(g, "Lamp Posts and Benches", mb, solid);
        }

        // ---------- Colliders, light, sky, fog, post ----------

        static void BuildColliders()
        {
            var g = Group("Colliders");
            foreach (var c in cols)
            {
                var go = new GameObject(c.n + " Collider");
                go.transform.SetParent(g, false);
                go.transform.position = c.c; go.transform.rotation = c.r;
                go.AddComponent<BoxCollider>().size = c.s;
            }
            // Map border: tall invisible walls 1 m behind the house fronts (the houses already block, these stop jumps over roofs).
            var b = new GameObject("Map Border"); b.transform.SetParent(g, false);
            void Wall(string n, Vector3 c, Vector3 s) { var go = new GameObject(n); go.transform.SetParent(b.transform, false); go.transform.position = c; go.AddComponent<BoxCollider>().size = s; }
            Wall("Border South", new Vector3(0, 30, -PZ - 1.5f), new Vector3(2 * PX + 6, 60, 1));
            Wall("Border North", new Vector3(0, 30, PZ + 1.5f), new Vector3(2 * PX + 6, 60, 1));
            Wall("Border West", new Vector3(-PX - 1.5f, 30, 0), new Vector3(1, 60, 2 * PZ + 6));
            // East border has a gap at the lever-gate tunnel; behind the house a small walled exit yard (ground reaches x = PX + D + 8).
            float zs = -PZ - 3f, zn = PZ + 3f, z0 = TunnelZ0, z1 = TunnelZ1;
            Wall("Border East South", new Vector3(PX + 1.5f, 30, (zs + z0) / 2), new Vector3(1, 60, z0 - zs));
            Wall("Border East North", new Vector3(PX + 1.5f, 30, (z1 + zn) / 2), new Vector3(1, 60, zn - z1));
            float yx0 = PX + D, yx1 = PX + D + 6f, yz0 = z0 - 2.6f, yz1 = z1 + 2.6f;
            Wall("Tunnel Yard South", new Vector3((yx0 + yx1) / 2, 30, yz0 - 0.5f), new Vector3(yx1 - yx0 + 1, 60, 1));
            Wall("Tunnel Yard North", new Vector3((yx0 + yx1) / 2, 30, yz1 + 0.5f), new Vector3(yx1 - yx0 + 1, 60, 1));
            Wall("Tunnel Yard East", new Vector3(yx1 + 0.5f, 30, (yz0 + yz1) / 2), new Vector3(1, 60, yz1 - yz0 + 2));
            Wall("Tunnel Yard Ceiling", new Vector3((yx0 + yx1) / 2, 60.5f, (yz0 + yz1) / 2), new Vector3(yx1 - yx0 + 1, 1, yz1 - yz0 + 2));
            Wall("Border Ceiling", new Vector3(0, 60.5f, 0), new Vector3(2 * PX + 6, 1, 2 * PZ + 6));
        }

        static string BuildEnvironment()
        {
            var sun = AddLight(root, "Sun", Vector3.up * 40f, LightType.Directional, new Color(1f, 0.85f, 0.64f), 1.75f, 0f, LightShadows.Soft);
            sun.transform.rotation = Quaternion.Euler(31f, 38f, 0f);
            sun.shadowStrength = 0.85f;

            var getter = typeof(RenderSettings).GetMethod("GetRenderSettings", BindingFlags.NonPublic | BindingFlags.Static);
            if (getter != null && getter.Invoke(null, null) is Object rs) Undo.RecordObject(rs, "City environment");
            RenderSettings.sun = sun;
            RenderSettings.skybox = skyMat;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.60f, 0.70f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.54f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.38f, 0.31f, 0.25f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.90f, 0.80f, 0.68f);
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 420f;
            DynamicGI.UpdateEnvironment();

            // Volume
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
            var tm = Comp<Tonemapping>(profile); tm.mode.Override(TonemappingMode.Neutral);
            var ca = Comp<ColorAdjustments>(profile);
            ca.postExposure.Override(-0.1f); ca.contrast.Override(14f); ca.saturation.Override(12f); ca.colorFilter.Override(new Color(1f, 0.97f, 0.9f));
            var bl = Comp<Bloom>(profile);
            bl.threshold.Override(0.95f); bl.intensity.Override(0.55f); bl.scatter.Override(0.7f); bl.tint.Override(new Color(1f, 0.85f, 0.68f));
            var vg = Comp<Vignette>(profile);
            vg.color.Override(new Color(0.16f, 0.08f, 0.05f)); vg.intensity.Override(0.24f); vg.smoothness.Override(0.45f);
            var sp = Comp<SplitToning>(profile);
            sp.shadows.Override(new Color(0.40f, 0.46f, 0.62f)); sp.highlights.Override(new Color(1f, 0.80f, 0.56f)); sp.balance.Override(-12f);
            EditorUtility.SetDirty(profile);
            var vgo = new GameObject("Post Processing"); vgo.transform.SetParent(root, false);
            var v = vgo.AddComponent<Volume>(); v.isGlobal = true; v.priority = 1f; v.sharedProfile = profile;

            // Camera (made once, kept outside the root so a rebuild does not move it).
            var cam = Camera.main;
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
                Undo.RegisterCreatedObjectUndo(cgo, "City camera");
                cam = cgo.AddComponent<Camera>(); cgo.AddComponent<AudioListener>();
                cgo.transform.position = new Vector3(-22f, 7.5f, -23f);
                cgo.transform.rotation = Quaternion.LookRotation(new Vector3(6f, 5f, 8f) - cgo.transform.position);
                cam.fieldOfView = 60f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 800f;
            }
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            return "Sun, sky, trilight ambient, fog, volume " + ProfilePath + ", camera post on.";
        }

        static T Comp<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (!p.TryGet<T>(out var c))
            {
                c = p.Add<T>(false);
                c.name = typeof(T).Name;
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, p);
            }
            c.active = true;
            return c;
        }

        static Light AddLight(Transform parent, string name, Vector3 pos, LightType type, Color c, float intensity, float range, LightShadows shadows = LightShadows.None)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = pos;
            var l = go.AddComponent<Light>(); l.type = type; l.color = c; l.intensity = intensity; if (range > 0) l.range = range; l.shadows = shadows;
            go.AddComponent<UniversalAdditionalLightData>();
            lightCount++;
            return l;
        }

        // ---------- Helpers: objects, materials, meshes, random ----------

        static Transform Group(string name)
        {
            var t = root.Find(name); if (t != null) return t;
            t = new GameObject(name).transform; t.SetParent(root, false); return t;
        }

        static GameObject Piece(Transform parent, string name, MB mb, Material mat, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            string baseName = "City_" + name.Replace(' ', '_'), mname = baseName; int k = 1;
            while (!usedNames.Add(mname)) mname = baseName + "_" + (++k);
            var mesh = Save(mb.Done(mname));
            vertTotal += mesh.vertexCount;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            if (!shadows) mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static void MakeMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Materials", "StyleCity");
            var baseSolid = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/StyleTest/Stylized_Solid.mat");
            var baseGlow = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/StyleTest/Stylized_Glow.mat");
            solid = CopyMat("City_Solid", baseSolid);
            solid.SetFloat("_GradBottom", 0f); solid.SetFloat("_GradTop", 24f);
            solid.SetColor("_ShadowColor", new Color(0.46f, 0.38f, 0.58f));
            glow = CopyMat("City_Glow", baseGlow);
            // Cobbles: copied from City_Solid only when created. Re-copying an existing one (CopyPropertiesFromMaterial) now and then
            // left the saved .mat without its _Tile* values, so the piazza lost its tiles after an Editor restart.
            cobbleMat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/City_Cobbles.mat") ?? CopyMat("City_Cobbles", solid);
            cobbleMat.SetFloat("_TileSize", 0.85f); cobbleMat.SetFloat("_TileAspect", 1f); cobbleMat.SetFloat("_BrickOffset", 0.5f);   // 0.85 m: half as many tiles as the first 0.6 m grid
            cobbleMat.SetFloat("_GroutWidth", 0.02f); cobbleMat.SetColor("_GroutColor", new Color(0.52f, 0.46f, 0.40f));
            cobbleMat.SetFloat("_TileVariation", 0.07f); cobbleMat.SetColor("_CheckerColor", new Color(0.94f, 0.93f, 0.92f));
            // Write the tile grid to disk right away and give the other materials explicit "tiles off" values, so a later
            // CopyPropertiesFromMaterial never leaves the cobbles without their _Tile* entries (the piazza then turns plain).
            foreach (var m in new[] { solid, glow }) { m.SetFloat("_TileSize", 0f); EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); }
            EditorUtility.SetDirty(cobbleMat); AssetDatabase.SaveAssetIfDirty(cobbleMat);
            string sp = MatDir + "/City_Sky.mat";
            skyMat = AssetDatabase.LoadAssetAtPath<Material>(sp);
            if (skyMat == null) { skyMat = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(skyMat, sp); }
            skyMat.SetFloat("_SunDisk", 2f); skyMat.SetFloat("_SunSize", 0.035f); skyMat.SetFloat("_SunSizeConvergence", 6f);
            skyMat.SetFloat("_AtmosphereThickness", 0.8f); skyMat.SetColor("_SkyTint", new Color(0.52f, 0.58f, 0.70f));
            skyMat.SetColor("_GroundColor", new Color(0.66f, 0.58f, 0.50f)); skyMat.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(skyMat);
        }

        static Material CopyMat(string name, Material src)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); } else m.CopyPropertiesFromMaterial(src);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh Save(Mesh m)
        {
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Meshes", "StyleCity");
            string path = MeshDir + "/" + m.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { string n = m.name; existing.Clear(); EditorUtility.CopySerialized(m, existing); existing.name = n; EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static float Rf(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        static Color Vary(Color c, float amt) { float k = 1 + Rf(-amt, amt); return new Color(c.r * k, c.g * k, c.b * k, c.a); }
        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        static float Hash(int x, int y) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xffff) / 65535f; } }

        static void Rod(MB mb, Vector3 a, Vector3 b, float t, Color col)
        {
            var d = b - a; if (d.sqrMagnitude < 1e-6f) return;
            var up = Mathf.Abs(d.normalized.y) > 0.95f ? Vector3.forward : Vector3.up;
            mb.Box((a + b) / 2, new Vector3(t, t, d.magnitude), col, Quaternion.LookRotation(d, up));
        }

        /// <summary>Wall frame: x along the facade (u), y up, d out of the wall (n); o is the bottom-left corner on the wall plane.</summary>
        struct WF
        {
            public Vector3 o, u, n; public Quaternion R;
            public WF(Vector3 o, Vector3 n) { this.o = o; this.n = n; R = Quaternion.LookRotation(n, Vector3.up); u = R * Vector3.right; }
            public Vector3 P(float x, float y, float d) => o + u * x + Vector3.up * y + n * d;
        }

        static void WBox(MB mb, WF w, float x0, float x1, float y0, float y1, float d0, float d1, Color col, bool embedded = true)
        {
            if (x1 - x0 < 0.002f || y1 - y0 < 0.002f) return;
            mb.Box(w.P((x0 + x1) / 2, (y0 + y1) / 2, (d0 + d1) / 2), new Vector3(x1 - x0, y1 - y0, d1 - d0), col, w.R, embedded && d0 < -0.001f ? SkipBack : 0);
        }

        /// <summary>Flat-shaded, vertex-coloured mesh builder (every face has its own vertices).</summary>
        class MB
        {
            readonly List<Vector3> v = new List<Vector3>(); readonly List<Color32> c = new List<Color32>(); readonly List<int> t = new List<int>();

            public void Tri(Vector3 a, Vector3 b, Vector3 d, Color col, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, d - a), outward) < 0) { var x = b; b = d; d = x; }
                int i = v.Count; v.Add(a); v.Add(b); v.Add(d); Color32 cc = col; c.Add(cc); c.Add(cc); c.Add(cc); t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col, Vector3 n)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, cc - a), n) < 0) { var x = b; b = d; d = x; }
                int i = v.Count; v.Add(a); v.Add(b); v.Add(cc); v.Add(d); Color32 k = col; c.Add(k); c.Add(k); c.Add(k); c.Add(k);
                t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            public void Box(Vector3 cen, Vector3 s, Color col, Quaternion r, int skip = 0)
            {
                // Grow every box by its own 0..2 mm so faces of neighbouring boxes never sit exactly in one plane (no z-fighting).
                float e = 0.00025f * (boxCount++ % 9);
                Vector3 h = s * 0.5f + new Vector3(e, e, e);
                Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward };
                for (int a = 0; a < 3; a++)
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        if ((skip & (1 << (a * 2 + (sg > 0 ? 1 : 0)))) != 0) continue;
                        Vector3 n = ax[a] * sg, u = ax[(a + 1) % 3], w = ax[(a + 2) % 3];
                        Vector3 P(float su, float sw) => cen + r * Vector3.Scale(n + u * su + w * sw, h);
                        Quad(P(-1, -1), P(1, -1), P(1, 1), P(-1, 1), col, r * n);
                    }
            }

            public void Prism(Vector3 b, int sides, float r0, float r1, float h, Color col, float startDeg = 0f) => Prism(b, sides, r0, r1, h, col, Quaternion.identity, startDeg);

            /// <summary>N-sided prism / frustum / cone from b along r*up (r1 = 0 gives a point).</summary>
            public void Prism(Vector3 b, int sides, float r0, float r1, float h, Color col, Quaternion r, float startDeg = 0f)
            {
                Vector3 up = r * Vector3.up, top = b + up * h;
                Vector3 P(int i, float rad, float y) { float a = startDeg * Mathf.Deg2Rad + i * Mathf.PI * 2f / sides; return b + r * new Vector3(Mathf.Sin(a) * rad, y, Mathf.Cos(a) * rad); }
                Vector3 Out(Vector3 m) => m - (b + up * Vector3.Dot(m - b, up));
                for (int i = 0; i < sides; i++)
                {
                    var a0 = P(i, r0, 0); var a1 = P(i + 1, r0, 0);
                    if (r1 > 0)
                    {
                        var b0 = P(i, r1, h); var b1 = P(i + 1, r1, h);
                        Quad(a0, a1, b1, b0, col, Out((a0 + a1 + b0 + b1) / 4f));
                        Tri(top, b0, b1, col, up);
                    }
                    else Tri(a0, a1, top, col, Out((a0 + a1 + top) / 3f));
                    Tri(b, a0, a1, col, -up);
                }
            }

            public void Sphere(Vector3 cen, float rad, int seg, int rings, System.Func<int, int, Color> colf, float yawDeg = 0f)
            {
                float y0 = yawDeg * Mathf.Deg2Rad;
                Vector3 P(int i, int k) { float a = y0 + i * Mathf.PI * 2f / seg, phi = -Mathf.PI / 2 + k * Mathf.PI / rings; float rr = Mathf.Cos(phi) * rad; return cen + new Vector3(Mathf.Sin(a) * rr, Mathf.Sin(phi) * rad, Mathf.Cos(a) * rr); }
                for (int k = 0; k < rings; k++)
                    for (int i = 0; i < seg; i++)
                    {
                        var a = P(i, k); var b = P(i + 1, k); var cc = P(i + 1, k + 1); var d = P(i, k + 1); var col = colf(i, k);
                        if (k == 0) Tri(a, cc, d, col, (a + cc + d) / 3f - cen);
                        else if (k == rings - 1) Tri(a, b, cc, col, (a + b + cc) / 3f - cen);
                        else Quad(a, b, cc, d, col, (a + b + cc + d) / 4f - cen);
                    }
            }

            /// <summary>Convex planar polygon (world points) extruded along dir by len.</summary>
            public void ExtrudePoly(Vector3[] poly, Vector3 dir, float len, Color col)
            {
                dir = dir.normalized; var off = dir * len;
                var cen = Vector3.zero; foreach (var p in poly) cen += p; cen /= poly.Length;
                for (int i = 0; i < poly.Length; i++)
                {
                    var p0 = poly[i]; var p1 = poly[(i + 1) % poly.Length];
                    Tri(cen, p0, p1, col, -dir);
                    Tri(cen + off, p0 + off, p1 + off, col, dir);
                    var m = (p0 + p1) / 2 - cen; m -= dir * Vector3.Dot(m, dir);
                    Quad(p0, p1, p1 + off, p0 + off, col, m);
                }
            }

            public Mesh Done(string name)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }
    }
}
