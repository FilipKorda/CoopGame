using System.Collections.Generic;
using System.IO;
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
    /// Dresses the greybox protagonist warship in StyleTest_ProtagonistShip (low poly, steel + wood) without touching its gameplay.
    /// Everything visual goes under "Protagonist Ship/Ship Style" (in the ship frame = pose of the greybox floor "Protagonist Ship  (6)"),
    /// so moving the "Protagonist Ship" root to another scene carries the look along. The greybox keeps every collider and position:
    /// plain cubes only get the "Stylized Ship Surface" materials (deck planks / riveted plates), some plain blocks (stern castle,
    /// engine tower, cabin pillars, deck obstacles, bow cannon) get a visual wrapper of exactly their size and their own renderer
    /// switched off. Outside: hull with a ram bow and a galleon stern around the greybox box, casemates, masts, funnels, turret,
    /// lifeboats, anchors, railings (with colliders, see railColliders). Inside: ribs, deck beams, lamps with lights, pipes,
    /// props, hammocks, chains. Environment root "Ship Environment": low-poly ocean, islets, sun, sky, fog, Volume.
    /// Meshes: Assets/Meshes/StyleShip, prefabs: Assets/Prefabs/StyleShip, materials: Assets/Materials/StyleShip.
    /// Re-running rebuilds "Ship Style" and "Ship Environment"; the scene itself is never saved.
    /// </summary>
    public static partial class ProtagonistShipBuilder
    {
        public const string ScenePath = "Assets/Scenes/Test Scene/StyleTest_ProtagonistShip.unity";
        const string ShipRootName = "Protagonist Ship", StyleRootName = "Ship Style", EnvRootName = "Ship Environment";
        const string FrameRefName = "Protagonist Ship  (6)";
        const string MeshDir = "Assets/Meshes/StyleShip", MatDir = "Assets/Materials/StyleShip", PrefabDir = "Assets/Prefabs/StyleShip";
        const string ProfilePath = "Assets/Settings/StyleTest_Ship_Volume.asset";
        const string BackupPath = "Assets/Prefabs/StyleShip/GreyboxOriginalMaterials.txt";

        /// <summary>Interior clutter (crates, barrels, bunks...) is visual only unless this is on.</summary>
        public static bool propColliders = false;
        /// <summary>Deck railings, the bow breastwork lining and the small stern deck get colliders (players can't walk off the deck).</summary>
        public static bool railColliders = true;
        /// <summary>Regenerate prefabs/meshes on every build (off: reuse existing assets with the same name).</summary>
        public static bool rebuildPrefabs = true;
        /// <summary>Water level in the ship frame (floor of the greybox hold is at 0.7).</summary>
        public const float WaterY = 10.5f;

        static Material solid, inner, glow, steelSurf, deckSurf, grateSurf, oceanMat, skyMat;
        static Transform ship, style;
        static System.Random rnd;
        static int boxCount, vertTotal, prefabCount, lightCount, instanceCount, zFixCount;
        static readonly HashSet<string> builtPrefabs = new HashSet<string>();
        static readonly List<string> report = new List<string>();

        // ---------- Palette (alpha = shine) ----------
        static Color C(float r, float g, float b, float a = 0.05f) => new Color(r, g, b, a);
        static readonly Color HullRed = C(0.30f, 0.07f, 0.05f, 0.08f), HullRedD = C(0.25f, 0.06f, 0.045f, 0.08f), BootBlack = C(0.05f, 0.05f, 0.055f, 0.25f),
            Steel = C(0.16f, 0.19f, 0.22f, 0.3f), Steel2 = C(0.145f, 0.175f, 0.205f, 0.3f), SteelD = C(0.085f, 0.10f, 0.115f, 0.34f), SteelL = C(0.34f, 0.37f, 0.40f, 0.45f),
            Iron = C(0.14f, 0.14f, 0.15f, 0.4f), RivetC = C(0.56f, 0.58f, 0.60f, 0.55f), Brass = C(0.80f, 0.60f, 0.27f, 0.85f), Copper = C(0.52f, 0.27f, 0.14f, 0.6f),
            Wood = C(0.30f, 0.165f, 0.08f, 0.06f), WoodD = C(0.15f, 0.085f, 0.045f, 0.05f), WoodL = C(0.45f, 0.29f, 0.16f, 0.06f), Deck = C(0.58f, 0.46f, 0.32f, 0.05f), DeckD = C(0.50f, 0.39f, 0.26f, 0.05f),
            Glass = C(0.10f, 0.15f, 0.19f, 0.8f), Cream = C(0.74f, 0.70f, 0.60f, 0.1f), White = C(0.92f, 0.92f, 0.90f, 0.12f), RedPaint = C(0.62f, 0.14f, 0.11f, 0.12f),
            Yellow = C(0.88f, 0.70f, 0.20f, 0.12f), Rope = C(0.70f, 0.60f, 0.42f, 0.03f), RopeD = C(0.27f, 0.22f, 0.15f, 0.05f), Canvas = C(0.64f, 0.60f, 0.48f, 0.02f), CanvasG = C(0.34f, 0.39f, 0.29f, 0.02f),
            Black = C(0.07f, 0.07f, 0.08f, 0.3f), FunnelCol = C(0.17f, 0.18f, 0.19f, 0.25f), Machine = C(0.25f, 0.33f, 0.30f, 0.3f), MachineD = C(0.18f, 0.24f, 0.22f, 0.3f),
            Mattress = C(0.78f, 0.76f, 0.68f, 0.02f), BlanketR = C(0.55f, 0.18f, 0.16f, 0.02f), BlanketB = C(0.24f, 0.32f, 0.45f, 0.02f), BlanketG = C(0.36f, 0.42f, 0.31f, 0.02f),
            Coal = C(0.09f, 0.09f, 0.10f, 0.15f), Rust = C(0.46f, 0.25f, 0.14f, 0.05f), Green = C(0.23f, 0.37f, 0.31f, 0.2f), Rock = C(0.42f, 0.40f, 0.37f, 0.03f), RockD = C(0.32f, 0.30f, 0.28f, 0.03f),
            Grass = C(0.33f, 0.47f, 0.23f, 0.02f), GrassD = C(0.24f, 0.37f, 0.18f, 0.02f), Sand = C(0.80f, 0.72f, 0.54f, 0.02f), Foam = C(0.86f, 0.93f, 0.95f, 0.2f), FoamD = C(0.70f, 0.82f, 0.86f, 0.2f);
        static readonly Color FrameC = C(0.17f, 0.17f, 0.16f, 0.25f), FrameD = C(0.11f, 0.11f, 0.105f, 0.3f);
        static readonly Color GLamp = new Color(1f, 0.80f, 0.50f, 1f), GFire = new Color(1f, 0.42f, 0.12f, 1f), GRed = new Color(1f, 0.25f, 0.18f, 1f), GGreen = new Color(0.25f, 1f, 0.45f, 1f), GWhite = new Color(0.95f, 0.95f, 0.9f, 1f);
        static readonly Color LampLight = new Color(1f, 0.74f, 0.46f);

        // ---------- Entry points ----------

        [MenuItem("Tools/Coop Game/Style Test/Build Protagonist Ship (StyleTest_ProtagonistShip)")]
        static void BuildMenu() => Debug.Log(Build());

        [MenuItem("Tools/Coop Game/Style Test/Restore Protagonist Ship Greybox")]
        static void RestoreMenu() => Debug.Log(RestoreGreybox());

        public static string Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) return "Open " + ScenePath + " first (active scene is " + scene.path + ").";
            var shipGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == ShipRootName);
            if (shipGo == null) return "No root object '" + ShipRootName + "'.";
            ship = shipGo.transform;
            var frameRef = ship.Find(FrameRefName);
            if (frameRef == null) return "No '" + FrameRefName + "' (the greybox floor that defines the ship frame).";

            MakeMaterials();
            AssetDatabase.SaveAssets();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build Protagonist Ship");
            int group = Undo.GetCurrentGroup();

            var oldStyle = ship.Find(StyleRootName);
            if (oldStyle != null) Undo.DestroyObjectImmediate(oldStyle.gameObject);
            var oldEnv = scene.GetRootGameObjects().FirstOrDefault(g => g.name == EnvRootName);
            if (oldEnv != null) Undo.DestroyObjectImmediate(oldEnv);

            boxCount = 0; vertTotal = 0; prefabCount = 0; lightCount = 0; instanceCount = 0; zFixCount = 0;
            builtPrefabs.Clear(); report.Clear(); placedProps.Clear(); placedLights.Clear();
            rnd = new System.Random(20261110);

            var sgo = new GameObject(StyleRootName);
            Undo.RegisterCreatedObjectUndo(sgo, "Build Protagonist Ship");
            style = sgo.transform;
            style.SetParent(ship, false);
            style.SetPositionAndRotation(frameRef.position, Quaternion.Euler(0f, frameRef.eulerAngles.y, 0f));
            Physics.SyncTransforms();

            CollectGreybox();
            CollectGameplay();
            SwapSurfaces();
            Physics.SyncTransforms();

            BuildHull();
            BuildWraps();
            BuildBridgeHousing();
            BuildDeckItems();
            BuildInterior();
            string env = BuildEnvironment(scene);

            foreach (var t in sgo.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<Light>() == null) t.gameObject.isStatic = true;

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = sgo;
            return $"Built '{StyleRootName}': {prefabCount} prefabs, {instanceCount} instances, {sgo.GetComponentsInChildren<MeshRenderer>(true).Length} renderers, " +
                   $"{lightCount} lights, {vertTotal} vertices, {zFixCount} coplanar faces lifted (z-fighting). {env}\n" + string.Join("\n", report);
        }

        /// <summary>Puts the original greybox materials / meshes / renderers back and removes the generated roots.</summary>
        public static string RestoreGreybox()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var shipGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == ShipRootName);
            if (shipGo == null) return "No root '" + ShipRootName + "'.";
            ship = shipGo.transform;
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Restore Protagonist Ship Greybox"); int group = Undo.GetCurrentGroup();
            var map = new Dictionary<string, string>();
            if (File.Exists(BackupPath)) foreach (var line in File.ReadAllLines(BackupPath)) { var p = line.Split('|'); if (p.Length == 2) map[p[0]] = p[1]; }
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            int n = 0;
            foreach (Transform t in ship)
            {
                var mr = t.GetComponent<MeshRenderer>(); var mf = t.GetComponent<MeshFilter>();
                if (mr == null) continue;
                if (map.TryGetValue(t.name, out var matPath) && IsShipMat(mr.sharedMaterial))
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (m != null) { Undo.RecordObject(mr, "Restore"); mr.sharedMaterial = m; n++; }
                }
                if (!mr.enabled && IsWrapped(t.name)) { Undo.RecordObject(mr, "Restore"); mr.enabled = true; n++; }
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name == "Ship_ExplosiveCrate") { Undo.RecordObject(mf, "Restore"); mf.sharedMesh = cube; n++; }
            }
            var st = ship.Find(StyleRootName); if (st != null) Undo.DestroyObjectImmediate(st.gameObject);
            var env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == EnvRootName); if (env != null) Undo.DestroyObjectImmediate(env);
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            return "Restored " + n + " greybox settings, removed generated roots (lighting / volume settings are left as they are).";
        }

        static bool IsShipMat(Material m) => m != null && (m == steelSurf || m == deckSurf || m == grateSurf || m.name.StartsWith("Ship_Surface") || m.name == "Ship_Solid" || m.name == "Ship_Interior");

        // ---------- Greybox inventory ----------

        static readonly List<Transform> hullPieces = new List<Transform>(), platformPieces = new List<Transform>(), obstaclePieces = new List<Transform>(),
            dynamitPieces = new List<Transform>(), gameplayRoots = new List<Transform>();

        /// <summary>Blocks that get a visual wrapper of exactly their size (their own renderer goes off).</summary>
        static readonly HashSet<string> WrappedNames = new HashSet<string> {
            "Protagonist Ship  (7)", "Protagonist Ship  (38)", "Protagonist Ship  (39)", "Protagonist Ship  (40)", "Protagonist Ship  (41)", "Protagonist Ship  (42)",
            "Protagonist Ship  (43)", "Protagonist Ship  (44)", "Protagonist Ship  (45)", "Protagonist Ship  (46)", "Protagonist Ship  (47)", "Protagonist Ship  (48)",
            "Protagonist Ship  (49)", "Protagonist Ship  (50)", "Protagonist Ship  (51)", "Protagonist Ship  (52)", "Protagonist Ship  (53)", "Protagonist Ship  (54)",
            "Platform (26)", "Platform (27)", "Platform (28)", "Platform (29)", "Platform (30)", "Platform (31)", "Cannon", "Cannon (1)", "Cannon (2)" };
        static bool IsWrapped(string name) => WrappedNames.Contains(name) || name.StartsWith("Obstacle");
        static readonly string[] DeckNames = { "Protagonist Ship  (5)", "Protagonist Ship  (8)", "Protagonist Ship  (9)", "Protagonist Ship  (97)" };

        static bool IsPlain(Transform t)
        {
            if (t.childCount > 0) return false;
            foreach (var c in t.GetComponents<Component>())
                if (!(c is Transform || c is MeshFilter || c is MeshRenderer || c is BoxCollider)) return false;
            return t.GetComponent<BoxCollider>() != null && t.GetComponent<MeshRenderer>() != null;
        }

        static void CollectGreybox()
        {
            hullPieces.Clear(); platformPieces.Clear(); obstaclePieces.Clear(); dynamitPieces.Clear(); gameplayRoots.Clear();
            foreach (Transform t in ship)
            {
                if (t == style) continue;
                bool plain = IsPlain(t);
                if (plain && t.name.StartsWith("Protagonist Ship")) hullPieces.Add(t);
                else if (plain && t.name.StartsWith("Platform")) platformPieces.Add(t);
                else if (plain && t.name.StartsWith("Obstacle")) obstaclePieces.Add(t);
                else { gameplayRoots.Add(t); if (plain && t.name.StartsWith("Dynamit")) dynamitPieces.Add(t); }
            }
            report.Add($"Greybox: {hullPieces.Count} hull cubes, {platformPieces.Count} platforms, {obstaclePieces.Count} deck obstacles, {gameplayRoots.Count} gameplay objects (untouched).");
        }

        static void SwapSurfaces()
        {
            var lines = new Dictionary<string, string>();
            if (File.Exists(BackupPath)) foreach (var line in File.ReadAllLines(BackupPath)) { var p = line.Split('|'); if (p.Length == 2) lines[p[0]] = p[1]; }
            int swapped = 0, off = 0;
            void Swap(Transform t, Material m)
            {
                var mr = t.GetComponent<MeshRenderer>();
                if (!IsShipMat(mr.sharedMaterial) && mr.sharedMaterial != null) lines[t.name] = AssetDatabase.GetAssetPath(mr.sharedMaterial);
                if (mr.sharedMaterial != m) { Undo.RecordObject(mr, "Ship surfaces"); mr.sharedMaterial = m; swapped++; }
            }
            foreach (var t in hullPieces) Swap(t, DeckNames.Contains(t.name) ? deckSurf : steelSurf);
            foreach (var t in platformPieces) Swap(t, grateSurf);
            foreach (var t in hullPieces.Concat(platformPieces).Concat(obstaclePieces).Concat(gameplayRoots))
            {
                if (!IsWrapped(t.name) || !IsPlain(t)) continue;
                var mr = t.GetComponent<MeshRenderer>();
                if (mr.enabled) { Undo.RecordObject(mr, "Ship wraps"); mr.enabled = false; off++; }
            }
            var crate = ExplosiveCrateMesh();
            foreach (var t in dynamitPieces)
            {
                var mf = t.GetComponent<MeshFilter>(); var mr = t.GetComponent<MeshRenderer>();
                if (!IsShipMat(mr.sharedMaterial) && mr.sharedMaterial != null) lines[t.name] = AssetDatabase.GetAssetPath(mr.sharedMaterial);
                Undo.RecordObject(mf, "Explosive crates"); mf.sharedMesh = crate;
                Undo.RecordObject(mr, "Explosive crates"); mr.sharedMaterial = inner;
            }
            EnsureFolder(PrefabDir);
            File.WriteAllLines(BackupPath, lines.Select(kv => kv.Key + "|" + kv.Value).OrderBy(s => s));
            AssetDatabase.ImportAsset(BackupPath);
            report.Add($"Surfaces: {swapped} greybox materials swapped, {off} renderers hidden under wrappers, {dynamitPieces.Count} Dynamit cubes -> explosive crate mesh. Originals in {BackupPath}.");
        }

        // ---------- Gameplay clearance ----------

        static readonly List<Bounds> gameplay = new List<Bounds>();

        static void CollectGameplay()
        {
            gameplay.Clear(); gameplayRaw.Clear();
            foreach (var g in gameplayRoots)
            {
                var parts = PartBounds(g);
                if (parts.Count == 0) parts.Add(new Bounds(g.position, Vector3.one));
                var all = new List<Bounds>(parts);
                foreach (var mb in g.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    var so = new SerializedObject(mb); string tn = mb.GetType().Name; var tr = mb.transform;
                    var sub = PartBounds(tr); if (sub.Count == 0) continue;
                    Vector3 V3(string n) { var p = so.FindProperty(n); return p != null && p.propertyType == SerializedPropertyType.Vector3 ? p.vector3Value : Vector3.zero; }
                    if (tn == "MechanismMover") foreach (var b in sub) all.Add(Shift(b, tr.rotation * V3("openOffset")));
                    if (tn == "SceneObjectMover") foreach (var b in sub) { all.Add(Shift(b, V3("offset"))); all.Add(Shift(b, tr.rotation * V3("offset"))); }
                    if (tn == "CoopSlideBridge") foreach (var b in sub) all.Add(Shift(b, tr.rotation * V3("slideOffset")));
                    if (tn == "Graspable")
                    {
                        var mp = so.FindProperty("motion"); int motion = mp != null ? mp.enumValueIndex : 0;
                        if (motion == 0) foreach (var b in sub) all.Add(Shift(b, tr.rotation * V3("slideOffset")));
                        else if (motion == 1)
                        {
                            float r = 0f;
                            foreach (var b in sub) for (int i = 0; i < 8; i++) r = Mathf.Max(r, Vector3.Distance(tr.position, b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                            all.Add(new Bounds(tr.position, Vector3.one * 2f * r));
                        }
                        else if (motion == 2)
                        {
                            var cr = so.FindProperty("carryRange"); var rv = cr != null && cr.propertyType == SerializedPropertyType.Vector2 ? cr.vector2Value : Vector2.zero;
                            foreach (var b in sub) { var e = b; e.Expand(new Vector3(rv.x > 0 ? rv.x * 2f : 12f, 0.8f, rv.y > 0 ? rv.y * 2f : 12f)); all.Add(e); }
                        }
                    }
                    if (tn == "HeavyObject") foreach (var b in sub) { var e = b; e.Expand(new Vector3(8f, 1f, 8f)); all.Add(e); }
                }
                foreach (var b in all) { gameplayRaw.Add(b); var e = b; e.Expand(1.2f); gameplay.Add(e); }
            }
        }

        static List<Bounds> PartBounds(Transform t)
        {
            var l = new List<Bounds>();
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer)) l.Add(r.bounds);
            foreach (var c in t.GetComponentsInChildren<Collider>(true)) l.Add(c.bounds);
            return l;
        }

        static Bounds Shift(Bounds b, Vector3 d) { b.center += d; return b; }
        static bool NearGameplay(Vector3 w, float r) { float r2 = r * r; foreach (var b in gameplay) if (b.SqrDistance(w) < r2) return true; return false; }
        static bool BoxNearGameplay(Bounds wb) { foreach (var b in gameplay) if (b.Intersects(wb)) return true; return false; }

        /// <summary>Inside the greybox hold (ship frame): the closed box, the stern gallery and the gun alcove.</summary>
        static bool InInterior(Vector3 w)
        {
            var l = style.InverseTransformPoint(w);
            if (l.y < 0.6f || l.y > 31.65f) return false;
            if (l.x > 32.3f && l.y > 28.9f) return false;
            if (l.x > -44.1f && l.x < 43.0f && Mathf.Abs(l.z) < 20.0f) return true;
            if (l.x > -47.9f && l.x <= -44.1f && l.z > 3.8f && l.z < 20.1f && l.y > 13.5f && l.y < 20.9f) return true;
            if (l.x > 1.4f && l.x < 5.5f && l.z > -24.7f && l.z < -19f && l.y > 12.6f && l.y < 15.7f) return true;
            return false;
        }

        // ---------- Hull ----------

        static readonly float[] LevY = { -3.4f, -2.6f, -1f, 1f, 4f, 7f, 9.6f, 11.2f, 13.8f, 15.1f, 19f, 22f, 24f, 24.7f, 25.4f, 26.1f, 26.8f, 27.5f, 28.2f, 28.9f, 29.6f, 30.3f, 31f, 31.7f, 32.4f };
        const float HW = 22f, MidAft = -44.5f, MidFwd = 43.2f, StemTopX = 71.5f, BowDeckY = 25.2f;
        static readonly Vector2[] StemPts = { new Vector2(-3.4f, 56f), new Vector2(0f, 60.5f), new Vector2(4f, 63.5f), new Vector2(9.6f, 65.8f), new Vector2(15.1f, 67.4f), new Vector2(22f, 69.2f), new Vector2(28.8f, 71.5f) };
        const int BowU = 14, SternU = 6;

        static float StemX(float y)
        {
            for (int i = 0; i < StemPts.Length - 1; i++)
                if (y <= StemPts[i + 1].x) return Mathf.Lerp(StemPts[i].y, StemPts[i + 1].y, Mathf.InverseLerp(StemPts[i].x, StemPts[i + 1].x, y));
            return StemPts[StemPts.Length - 1].y;
        }
        static float Hw0(int k) { float y = LevY[k]; float b = y < -3f ? 15f : y < -2f ? 19.8f : y < 0f ? 21.4f : HW; if (y >= 24f && (k % 2) == 1) b += 0.05f; return b; }
        static float BowTop(float x) => BowDeckY + 3.6f * Mathf.Pow(Mathf.Clamp01((x - MidFwd) / (StemTopX - MidFwd)), 1.6f);
        static float BowTopHw(float u) => HW * (1f - Mathf.Pow(Mathf.Clamp01(u), 2.2f));
        static float BowU01(float x) => Mathf.Clamp01((x - MidFwd) / (StemTopX - MidFwd));
        static float BowP(float y) => Mathf.Lerp(1.5f, 2.2f, Mathf.InverseLerp(-3.4f, 25f, y));
        static float SternX(float y) => y >= 6f ? -48.8f - 0.5f * (y - 6f) / 26.4f : -44.5f - 4.3f * Mathf.Pow(Mathf.Clamp01((y + 3.4f) / 9.4f), 0.55f);
        static float SternEnd(int k) { float y = LevY[k], h = Hw0(k); return y >= 6f ? h - 1.4f : h * 0.6f; }
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        static Vector3 M(Vector3 p, float s) => new Vector3(p.x, p.y, p.z * s);
        static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        static Color Band(int k)
        {
            float y = LevY[k];
            if (y < 9.5f) return k % 2 == 0 ? HullRed : HullRedD;
            if (y < 11f) return BootBlack;
            if (y < 23.9f) return k % 2 == 0 ? Steel : Steel2;
            return Shade(Color.Lerp(Wood, WoodL, Hash(k, 3) * 0.45f), 0.92f + Hash(k, 11) * 0.12f);
        }

        static Vector3[] BowRow(int k)
        {
            var r = new Vector3[BowU + 1]; float y = LevY[k], xs = StemX(y), p = BowP(y);
            for (int i = 0; i <= BowU; i++) { float u = i / (float)BowU; r[i] = V(MidFwd + u * (xs - MidFwd), y, Hw0(k) * (1f - Mathf.Pow(u, p))); }
            return r;
        }
        static Vector3[] BowTopRow()
        {
            var r = new Vector3[BowU + 1];
            for (int i = 0; i <= BowU; i++) { float u = i / (float)BowU, x = MidFwd + u * (StemTopX - MidFwd); r[i] = V(x, BowTop(x), BowTopHw(u)); }
            return r;
        }
        static Vector3[] SternRow(int k)
        {
            var r = new Vector3[SternU + 1]; float y = LevY[k], xe = SternX(y), h0 = Hw0(k), he = SternEnd(k);
            for (int i = 0; i <= SternU; i++) { float u = i / (float)SternU; r[i] = V(Mathf.Lerp(MidAft, xe, u), y, Mathf.Lerp(h0, he, Smooth((u - 0.45f) / 0.55f))); }
            return r;
        }

        static void SideQuad(MB mb, float x0, float x1, float y0, float y1, float h0, float h1, float s, Color col) =>
            mb.Quad(V(x0, y0, s * h0), V(x1, y0, s * h0), V(x1, y1, s * h1), V(x0, y1, s * h1), col, new Vector3(0, 0, s));

        static void HullLoft(MB mb)
        {
            int K = LevY.Length;
            // Midbody: vertical sides around the greybox box, with the opening of the gun alcove on the -z side.
            for (int s = -1; s <= 1; s += 2)
                for (int k = 0; k < K - 1; k++)
                {
                    float y0 = LevY[k], y1 = LevY[k + 1]; var col = Band(k);
                    if (s < 0 && y0 > 11f && y1 < 19.05f) { SideQuad(mb, MidAft, 0.6f, y0, y1, Hw0(k), Hw0(k + 1), s, col); SideQuad(mb, 6.4f, MidFwd, y0, y1, Hw0(k), Hw0(k + 1), s, col); }
                    else SideQuad(mb, MidAft, MidFwd, y0, y1, Hw0(k), Hw0(k + 1), s, col);
                }
            SideQuad(mb, 0.6f, 6.4f, 17.6f, 19f, HW, HW, -1, Steel2);
            mb.Quad(V(MidAft, -3.4f, -15f), V(MidFwd, -3.4f, -15f), V(MidFwd, -3.4f, 15f), V(MidAft, -3.4f, 15f), HullRedD, Vector3.down);

            // Bow: waterlines converge on a raked stem, the top row is the sheer of the low prow deck.
            var rows = new List<Vector3[]>(); var cols = new List<Color>();
            for (int k = 0; k < K; k++) if (LevY[k] < BowDeckY - 0.3f) { rows.Add(BowRow(k)); cols.Add(Band(k)); }
            rows.Add(BowTopRow());
            for (int s = -1; s <= 1; s += 2)
                for (int j = 0; j < rows.Count - 1; j++)
                    for (int i = 0; i < BowU; i++)
                        mb.Quad(M(rows[j][i], s), M(rows[j][i + 1], s), M(rows[j + 1][i + 1], s), M(rows[j + 1][i], s), cols[j], new Vector3(0.3f, 0, s));
            for (int i = 0; i < BowU; i++) mb.Quad(M(rows[0][i], -1), M(rows[0][i], 1), M(rows[0][i + 1], 1), M(rows[0][i + 1], -1), HullRedD, Vector3.down);

            // Stern: rounded quarters and a raked transom with an opening for the stern gallery bridge.
            var srows = new Vector3[K][];
            for (int k = 0; k < K; k++) srows[k] = SternRow(k);
            for (int s = -1; s <= 1; s += 2)
                for (int k = 0; k < K - 1; k++)
                    for (int i = 0; i < SternU; i++)
                        mb.Quad(M(srows[k][i], s), M(srows[k][i + 1], s), M(srows[k + 1][i + 1], s), M(srows[k + 1][i], s), Band(k), new Vector3(-0.3f, 0, s));
            for (int k = 0; k < K - 1; k++)
            {
                Vector3 e0 = srows[k][SternU], e1 = srows[k + 1][SternU]; var col = Band(k);
                if (LevY[k] > 13.7f && LevY[k + 1] < 15.2f)
                {
                    TransomPart(mb, e0, e1, -e0.z, 10.9f, col);
                    if (e0.z > 20.45f) TransomPart(mb, e0, e1, 20.45f, e0.z, col);
                }
                else mb.Quad(M(e0, -1), M(e0, 1), M(e1, 1), M(e1, -1), col, Vector3.left);
            }
        }

        static void TransomPart(MB mb, Vector3 e0, Vector3 e1, float z0, float z1, Color col) =>
            mb.Quad(V(e0.x, e0.y, z0), V(e0.x, e0.y, z1), V(e1.x, e1.y, z1), V(e1.x, e1.y, z0), col, Vector3.left);

        static void BuildHull()
        {
            var parent = Group("Exterior");
            Place(Prefab("Exterior", "Ship_Hull", pb => HullLoft(pb.s)), parent, Vector3.zero, Quaternion.identity);
            Place(Prefab("Exterior", "Ship_HullDetails", HullDetails), parent, Vector3.zero, Quaternion.identity);
            Place(Prefab("Exterior", "Ship_ProwDeck", ProwDeck), parent, Vector3.zero, Quaternion.identity);
            Place(Prefab("Exterior", "Ship_Stern", SternDetails), parent, Vector3.zero, Quaternion.identity);
            Place(Prefab("Exterior", railColliders ? "Ship_Railings" : "Ship_Railings_NoCol", Railings), parent, Vector3.zero, Quaternion.identity);
            foreach (float x in new[] { -27.5f, -11.5f, 3.5f, 19.5f })
                for (int s = -1; s <= 1; s += 2)
                {
                    bool alcove = s < 0 && Mathf.Abs(x - 3.5f) < 0.1f;
                    var pf = Prefab("Exterior", alcove ? "Casemate_Alcove" : "Casemate", pb => Casemate(pb, alcove));
                    Place(pf, parent, V(x, 0f, s * HW), Quaternion.Euler(0, s < 0 ? 180f : 0f, 0), alcove ? "Casemate (gun alcove)" : "Casemate");
                }
            report.Add("Hull: ram bow to x 71.5, transom at x -49 (ship frame), water at y " + WaterY + "; casemates at x -27.5 / -11.5 / 3.5 / 19.5 both sides (the -z one at 3.5 wraps the New Weapon alcove).");
        }

        // A box between two points along a horizontal path: thick across, tall vertically.
        static void Strip(MB mb, Vector3 a, Vector3 b, float tall, float thick, Color col)
        {
            var d = b - a; d.y = 0; if (d.sqrMagnitude < 1e-6f) return;
            var r = Quaternion.LookRotation(d.normalized, Vector3.up);
            mb.Box((a + b) * 0.5f, new Vector3(thick, tall, (b - a).magnitude), col, r);
        }

        static void Rivets(MB mb, Vector3 a, Vector3 b, float step, Vector3 outward, float size, Color col)
        {
            float len = (b - a).magnitude; int n = Mathf.Max(1, Mathf.FloorToInt(len / step));
            var r = Quaternion.LookRotation(outward, Mathf.Abs(outward.y) > 0.9f ? Vector3.forward : Vector3.up);
            for (int i = 0; i <= n; i++) mb.Box(Vector3.Lerp(a, b, i / (float)n) + outward * size * 0.4f, new Vector3(size, size, size), col, r);
        }

        static void Porthole(MB mb, Vector3 c, Vector3 n, float r, bool lit = false, MB glowMb = null)
        {
            var q = Quaternion.FromToRotation(Vector3.up, n);
            mb.Prism(c - n * 0.02f, 10, r, r, 0.12f, Brass, q, 18f);
            if (lit && glowMb != null) glowMb.Prism(c + n * 0.06f, 10, r * 0.72f, r * 0.72f, 0.04f, GLamp, q, 18f);
            else mb.Prism(c + n * 0.06f, 10, r * 0.72f, r * 0.72f, 0.04f, Glass, q, 18f);
        }

        static void HullDetails(PB pb)
        {
            var mb = pb.s; int K = LevY.Length;
            float midX = (MidAft + MidFwd) * 0.5f, L = MidFwd - MidAft;
            for (int s = -1; s <= 1; s += 2)
            {
                // Gunwale cap (covers the gap between the greybox walls and the hull) and the wale under the wooden strakes.
                mb.Box(V(midX, 32.375f, s * 21.55f), V(L, 0.25f, 1.2f), WoodD, Quaternion.identity);
                mb.Box(V(midX, 23.9f, s * 22.12f), V(L, 0.6f, 0.25f), WoodD, Quaternion.identity);
                var b24 = BowRow(System.Array.IndexOf(LevY, 24f));
                for (int i = 0; i < BowU; i++) Strip(mb, M(b24[i], s) + V(0, -0.1f, s * 0.1f), M(b24[i + 1], s) + V(0, -0.1f, s * 0.1f), 0.6f, 0.25f, WoodD);
                var s24 = SternRow(System.Array.IndexOf(LevY, 24f));
                for (int i = 0; i < SternU; i++) Strip(mb, M(s24[i], s) + V(0, -0.1f, s * 0.1f), M(s24[i + 1], s) + V(0, -0.1f, s * 0.1f), 0.6f, 0.25f, WoodD);
                var sTop = SternRow(K - 1);
                for (int i = 0; i < SternU; i++) Strip(mb, M(sTop[i], s) + V(0, -0.025f, -s * 0.45f), M(sTop[i + 1], s) + V(0, -0.025f, -s * 0.45f), 0.25f, 1.2f, WoodD);

                // Armour belt along the waterline.
                for (float x = -43.6f; x < 42.4f; x += 4.5f)
                {
                    float x1 = Mathf.Min(x + 4.38f, 42.6f);
                    if (s < 0 && x1 > 0.2f && x < 6.8f) continue;
                    mb.Box(V((x + x1) * 0.5f, 13.15f, s * 22.15f), V(x1 - x, 3.8f, 0.3f), SteelD, Quaternion.identity);
                    Rivets(mb, V(x + 0.2f, 14.85f, s * 22.3f), V(x1 - 0.2f, 14.85f, s * 22.3f), 0.45f, V(0, 0, s), 0.07f, RivetC);
                    Rivets(mb, V(x + 0.2f, 11.45f, s * 22.3f), V(x1 - 0.2f, 11.45f, s * 22.3f), 0.45f, V(0, 0, s), 0.07f, RivetC);
                }
                // Plate seams on the steel band.
                foreach (float y in new[] { 17.05f, 21.0f })
                {
                    if (s < 0 && y < 19f) { mb.Box(V((MidAft + 0.2f) * 0.5f, y, s * 22.01f), V(0.2f - MidAft, 0.07f, 0.03f), SteelD, Quaternion.identity); mb.Box(V((6.8f + MidFwd) * 0.5f, y, s * 22.01f), V(MidFwd - 6.8f, 0.07f, 0.03f), SteelD, Quaternion.identity); }
                    else mb.Box(V(midX, y, s * 22.01f), V(L, 0.07f, 0.03f), SteelD, Quaternion.identity);
                }
                for (float x = -41.8f; x < 42f; x += 5.4f)
                {
                    float yb = (s < 0 && x > 0.2f && x < 6.8f) ? 19.1f : 15.1f;
                    mb.Box(V(x, (yb + 23.6f) * 0.5f, s * 22.01f), V(0.07f, 23.6f - yb, 0.03f), SteelD, Quaternion.identity);
                }
                // Portholes and the closed gun ports of the wooden upper hull, iron straps between them.
                for (float x = -42.0f; x < 41.7f; x += 3.2f) Porthole(mb, V(x, 20.4f, s * 22.0f), V(0, 0, s), 0.36f);
                for (int k = 0; k <= 13; k++)
                {
                    float x = -41.6f + 6.4f * k; if (x > 41.7f) break;
                    mb.Box(V(x, 27.3f, s * 22.08f), V(1.5f, 1.6f, 0.14f), WoodD, Quaternion.identity);
                    mb.Box(V(x, 28.18f, s * 22.12f), V(1.75f, 0.14f, 0.2f), Iron, Quaternion.identity);
                    mb.Box(V(x, 26.42f, s * 22.12f), V(1.75f, 0.14f, 0.2f), Iron, Quaternion.identity);
                    mb.Box(V(x - 0.86f, 27.3f, s * 22.12f), V(0.14f, 1.9f, 0.2f), Iron, Quaternion.identity);
                    mb.Box(V(x + 0.86f, 27.3f, s * 22.12f), V(0.14f, 1.9f, 0.2f), Iron, Quaternion.identity);
                    mb.Box(V(x - 0.4f, 27.75f, s * 22.17f), V(0.12f, 0.75f, 0.06f), Iron, Quaternion.identity);
                    mb.Box(V(x + 0.4f, 27.75f, s * 22.17f), V(0.12f, 0.75f, 0.06f), Iron, Quaternion.identity);
                    float xs = x + 3.2f;
                    if (xs < 42.6f)
                    {
                        mb.Box(V(xs, 28.2f, s * 22.04f), V(0.22f, 8.1f, 0.1f), Iron, Quaternion.identity);
                        Rivets(mb, V(xs, 24.5f, s * 22.1f), V(xs, 32.0f, s * 22.1f), 0.55f, V(0, 0, s), 0.07f, RivetC);
                    }
                }
            }

            // Bow box front: armoured breastwork with the gun port of the greybox cannon (left open), emblem, portholes.
            float bx = MidFwd;
            mb.Box(V(bx + 0.175f, 26.975f, -18.3f), V(0.35f, 4.05f, 7.4f), Steel, Quaternion.identity);
            mb.Box(V(bx + 0.175f, 26.975f, 4.35f), V(0.35f, 4.05f, 35.3f), Steel, Quaternion.identity);
            mb.Box(V(bx + 0.175f, 25.275f, -13.95f), V(0.35f, 0.65f, 1.3f), Steel, Quaternion.identity);
            mb.Box(V(bx + 0.175f, 28.05f, -13.95f), V(0.35f, 1.9f, 1.3f), Steel, Quaternion.identity);
            mb.Box(V(bx - 0.075f, 30.625f, 0f), V(0.6f, 3.25f, 44f), Steel, Quaternion.identity);
            mb.Box(V(bx + 0.45f, 25.48f, -13.95f), V(0.3f, 0.25f, 1.8f), Iron, Quaternion.identity);
            mb.Box(V(bx + 0.45f, 27.22f, -13.95f), V(0.3f, 0.25f, 1.8f), Iron, Quaternion.identity);
            mb.Box(V(bx + 0.45f, 26.35f, -14.73f), V(0.3f, 1.99f, 0.25f), Iron, Quaternion.identity);
            mb.Box(V(bx + 0.45f, 26.35f, -13.17f), V(0.3f, 1.99f, 0.25f), Iron, Quaternion.identity);
            for (int i = 1; i < 16; i++) { float z = -22f + 2.75f * i; if (z > -15.1f && z < -12.8f) continue; mb.Box(V(bx + 0.36f, 28.6f, z), V(0.03f, 7.3f, 0.08f), SteelD, Quaternion.identity); }
            mb.Box(V(bx + 0.36f, 28.6f, 0f), V(0.03f, 0.08f, 44f), SteelD, Quaternion.identity);
            mb.Box(V(bx + 0.36f, 31.0f, 0f), V(0.03f, 0.08f, 44f), SteelD, Quaternion.identity);
            Rivets(mb, V(bx + 0.36f, 32.0f, -21.6f), V(bx + 0.36f, 32.0f, 21.6f), 0.5f, Vector3.right, 0.07f, RivetC);
            Rivets(mb, V(bx + 0.36f, 25.2f, -21.6f), V(bx + 0.36f, 25.2f, -15.2f), 0.5f, Vector3.right, 0.07f, RivetC);
            Rivets(mb, V(bx + 0.36f, 25.2f, -12.7f), V(bx + 0.36f, 25.2f, 21.6f), 0.5f, Vector3.right, 0.07f, RivetC);
            var qx = Quaternion.FromToRotation(Vector3.up, Vector3.right);
            mb.Prism(V(bx + 0.35f, 30.0f, 0f), 8, 1.15f, 1.15f, 0.12f, Brass, qx, 22.5f);
            mb.Prism(V(bx + 0.35f, 30.0f, 0f), 8, 0.8f, 0.8f, 0.2f, RedPaint, qx, 22.5f);
            mb.Prism(V(bx + 0.35f, 30.0f, 0f), 4, 0.42f, 0.42f, 0.26f, Brass, qx, 0f);
            foreach (float z in new[] { -9f, 9f, -17f, 17f }) Porthole(mb, V(bx + 0.35f, 30.1f, z), Vector3.right, 0.38f);

            // Stem bar along the cutwater.
            for (int i = 0; i < StemPts.Length - 1; i++)
            {
                var a = V(StemPts[i].y + 0.04f, StemPts[i].x, 0f); var b = V(StemPts[i + 1].y + 0.04f, StemPts[i + 1].x, 0f);
                var d = b - a; mb.Box((a + b) * 0.5f, V(0.18f, d.magnitude + 0.1f, 0.4f), SteelD, Quaternion.LookRotation(Vector3.forward, d));
            }

            // Anchors at the hawse pipes on both bow sides, chains on the prow deck to the capstans.
            for (int s = -1; s <= 1; s += 2)
            {
                float ax = 60.5f, ay = 22.0f;
                float hw = AnchorHw(ax, ay);
                var hawse = V(ax, ay, s * (hw + 0.05f));
                var nrm = new Vector3(0.45f, 0f, s).normalized;
                mb.Prism(hawse - nrm * 0.1f, 10, 0.55f, 0.55f, 0.3f, Iron, Quaternion.FromToRotation(Vector3.up, nrm));
                mb.Prism(hawse + nrm * 0.15f, 10, 0.3f, 0.3f, 0.06f, Black, Quaternion.FromToRotation(Vector3.up, nrm));
                Anchor(mb, V(ax + 0.2f, ay - 0.6f, s * (hw + 0.75f)), s);
                float dx = 59.6f, dz = s * 6.5f, dy = BowTop(dx);
                mb.Prism(V(dx, dy - 0.1f, dz), 10, 0.45f, 0.4f, 0.35f, Iron);
                Chain(mb, V(dx, dy + 0.18f, dz), V(56.6f, BowTop(56.6f) + 0.18f, s * 3.4f), 0.28f, Iron);
            }
        }

        static float AnchorHw(float x, float y)
        {
            float xs = StemX(y), u = Mathf.Clamp01((x - MidFwd) / (xs - MidFwd));
            return HW * (1f - Mathf.Pow(u, BowP(y)));
        }

        static void Anchor(MB mb, Vector3 top, float s)
        {
            var c = Iron; const float q = 1.5f;
            mb.Box(top + V(0, -2.0f, 0) * q, V(0.32f, 4.0f, 0.32f) * q, c, Quaternion.identity);                 // shank
            mb.Box(top + V(0, -0.25f, 0) * q, V(2.6f, 0.26f, 0.26f) * q, c, Quaternion.identity);                // stock
            mb.Prism(top + V(0, 0.25f, 0) * q, 8, 0.32f * q, 0.32f * q, 0.12f * q, c, Quaternion.FromToRotation(Vector3.up, Vector3.forward)); // ring
            var crown = top + V(0, -4.0f, 0) * q;
            for (int k = -1; k <= 1; k += 2)
            {
                var tip = crown + V(k * 1.45f, 1.25f, 0) * q;
                mb.Rod(crown, tip, 0.3f * q, c);
                mb.Box(tip + V(-k * 0.15f, -0.2f, 0) * q, V(0.55f, 0.75f, 0.18f) * q, c, Quaternion.Euler(0, 0, k * 35f));
            }
        }

        static void Chain(MB mb, Vector3 a, Vector3 b, float link, Color col)
        {
            var d = b - a; float len = d.magnitude; if (len < 0.01f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / link));
            var fwd = d / len; var up = Mathf.Abs(fwd.y) > 0.95f ? Vector3.forward : Vector3.up;
            for (int i = 0; i < n; i++)
            {
                var c = a + d * ((i + 0.5f) / n);
                var r = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(0, 0, (i % 2) * 90f);
                mb.Box(c, new Vector3(link * 0.42f, link * 0.12f, link * 1.05f), col, r);
            }
        }

        static void ProwDeck(PB pb)
        {
            var mb = pb.s; var top = BowTopRow(); const int NJ = 18;
            for (int i = 0; i < BowU; i++)
                for (int j = 0; j < NJ; j++)
                {
                    float f0 = -1f + 2f * j / NJ, f1 = -1f + 2f * (j + 1) / NJ;
                    var a = V(top[i].x, top[i].y + 0.005f, top[i].z * f0); var b = V(top[i + 1].x, top[i + 1].y + 0.005f, top[i + 1].z * f0);
                    var c = V(top[i + 1].x, top[i + 1].y + 0.005f, top[i + 1].z * f1); var d = V(top[i].x, top[i].y + 0.005f, top[i].z * f1);
                    var col = Shade(j % 2 == 0 ? Deck : DeckD, 0.94f + Hash(i, j) * 0.1f);
                    mb.Quad(a, b, c, d, col, Vector3.up);
                }
            // Bulwark around the prow: continues the hull side 1.05 m above the deck, cream inside, dark cap.
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < BowU; i++)
                {
                    Vector3 p0 = M(top[i], s), p1 = M(top[i + 1], s);
                    Vector3 q0 = V(p0.x, p0.y, s * Mathf.Max(0.02f, top[i].z - 0.25f)), q1 = V(p1.x, p1.y, s * Mathf.Max(0.02f, top[i + 1].z - 0.25f));
                    var up = Vector3.up * 1.05f;
                    mb.Quad(p0, p1, p1 + up, p0 + up, Wood, new Vector3(0.3f, 0, s));
                    mb.Quad(q0, q1, q1 + up, q0 + up, Cream, new Vector3(-0.3f, 0, -s));
                    mb.Quad(p0 + up, p1 + up, q1 + up, q0 + up, WoodD, Vector3.up);
                    mb.Box((p0 + p1) * 0.5f + up + V(0, 0.06f, 0), V(0.35f, 0.12f, 0.35f), WoodD, Quaternion.identity);
                }
            // Jackstaff with the bow flag.
            var stem = V(StemTopX - 0.6f, BowTop(StemTopX - 0.6f), 0f);
            mb.Cyl(stem, stem + V(0, 6.5f, 0), 0.08f, 6, WoodD);
            Flag(mb, stem + V(0, 6.4f, 0), 2.4f, 1.5f, RedPaint, Cream, Vector3.back);
        }

        static void Flag(MB mb, Vector3 top, float w, float h, Color a, Color b, Vector3 dir)
        {
            dir.y = 0; dir.Normalize();
            var r = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);
            for (int i = 0; i < 4; i++)
            {
                float x0 = w * i / 4f, x1 = w * (i + 1) / 4f, wave = (i % 2 == 0 ? 0.08f : -0.08f);
                var c0 = top + dir * ((x0 + x1) * 0.5f) + Vector3.Cross(dir, Vector3.up) * wave;
                mb.Box(c0 - Vector3.up * h * 0.25f, new Vector3(0.03f, h * 0.5f, x1 - x0), a, Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up).normalized, Vector3.up) * Quaternion.Euler(0, 90, 0) * Quaternion.Euler(0, 90, 0));
                mb.Box(c0 - Vector3.up * h * 0.75f, new Vector3(0.03f, h * 0.5f, x1 - x0), b, Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up).normalized, Vector3.up) * Quaternion.Euler(0, 90, 0) * Quaternion.Euler(0, 90, 0));
            }
        }

        static void SternDetails(PB pb)
        {
            var mb = pb.s; var g = pb.g;
            float xr(float y) => SternX(y) - 0.02f;
            // Gallery cornices below and above the window row (the bridge opening stays clear).
            float yl = 13.6f, yh = 19.25f;
            mb.Box(V(xr(yl) - 0.15f, yl - 0.15f, -4.85f), V(0.45f, 0.35f, 31.5f), WoodD, Quaternion.identity);
            mb.Box(V(xr(yh) - 0.2f, yh, 0f), V(0.55f, 0.45f, 41.2f), WoodD, Quaternion.identity);
            mb.Box(V(xr(yh) - 0.12f, yh + 0.35f, 0f), V(0.4f, 0.25f, 41.4f), Brass, Quaternion.identity);
            foreach (float z in new[] { -18f, -14f, -10f, -6f, -2f, 2f, 6f })
            {
                float x = xr(17.1f);
                mb.Box(V(x - 0.04f, 17.1f, z), V(0.1f, 3.0f, 1.35f), Glass, Quaternion.identity);
                mb.Box(V(x - 0.1f, 18.65f, z), V(0.16f, 0.16f, 1.6f), WoodL, Quaternion.identity);
                mb.Box(V(x - 0.1f, 15.55f, z), V(0.16f, 0.16f, 1.6f), WoodL, Quaternion.identity);
                mb.Box(V(x - 0.1f, 17.1f, z - 0.74f), V(0.16f, 3.2f, 0.14f), WoodL, Quaternion.identity);
                mb.Box(V(x - 0.1f, 17.1f, z + 0.74f), V(0.16f, 3.2f, 0.14f), WoodL, Quaternion.identity);
                mb.Box(V(x - 0.08f, 17.1f, z), V(0.1f, 3.0f, 0.08f), WoodL, Quaternion.identity);
                mb.Box(V(x - 0.08f, 17.6f, z), V(0.1f, 0.08f, 1.35f), WoodL, Quaternion.identity);
            }
            // Pilasters and the name board on the upper transom, lanterns on the corners, ensign staff.
            foreach (float z in new[] { -19.2f, -12f, 12f, 19.2f })
                mb.Box(V(xr(26f) - 0.1f, 25.9f, z), V(0.25f, 12.6f, 0.6f), WoodD, Quaternion.identity);
            mb.Box(V(xr(28f) - 0.08f, 28f, 0f), V(0.2f, 2.2f, 15f), WoodD, Quaternion.identity);
            mb.Box(V(xr(28f) - 0.14f, 28f, 0f), V(0.2f, 1.7f, 14.4f), Black, Quaternion.identity);
            mb.Box(V(xr(28f) - 0.17f, 29.0f, 0f), V(0.18f, 0.12f, 14.6f), Brass, Quaternion.identity);
            mb.Box(V(xr(28f) - 0.17f, 27.0f, 0f), V(0.18f, 0.12f, 14.6f), Brass, Quaternion.identity);
            for (int i = -5; i <= 5; i++) mb.Box(V(xr(28f) - 0.2f, 28f, i * 1.2f), V(0.1f, 0.9f, 0.55f), Brass, Quaternion.identity);
            foreach (float z in new[] { -19.4f, 19.4f })
            {
                var b = V(SternX(32.4f) + 0.3f, 32.5f, z);
                mb.Cyl(b, b + V(0, 1.6f, 0), 0.09f, 6, Iron);
                mb.Prism(b + V(0, 1.6f, 0), 6, 0.32f, 0.28f, 0.16f, Iron);
                g.Prism(b + V(0, 1.76f, 0), 6, 0.24f, 0.24f, 0.55f, GLamp);
                mb.Prism(b + V(0, 2.31f, 0), 6, 0.34f, 0.05f, 0.3f, Iron);
            }
            var fs = V(SternX(32.4f) + 0.4f, 32.5f, 0f);
            mb.Cyl(fs, fs + V(0, 8.5f, 0), 0.1f, 6, WoodD);
            mb.Prism(fs + V(0, 8.5f, 0), 6, 0.16f, 0.16f, 0.16f, Brass);
            Flag(mb, fs + V(0, 8.3f, 0), 3.6f, 2.2f, RedPaint, Cream, Vector3.left);
        }

        static void Railings(PB pb)
        {
            var mb = pb.s; int K = LevY.Length;
            float xs = SternX(32.4f) + 0.55f;
            var sTop = SternRow(K - 1);
            for (int s = -1; s <= 1; s += 2)
            {
                RailRun(mb, V(sTop[3].x, 32.5f, s * 21.55f), V(MidFwd, 32.5f, s * 21.55f));
                for (int i = 3; i < SternU; i++) RailRun(mb, M(sTop[i], s) + V(0, 0.1f, -s * 0.45f), M(sTop[i + 1], s) + V(0, 0.1f, -s * 0.45f));
                // Lining inside the small stern deck and inside the bow well (the hull mesh is one-sided).
                for (int i = 2; i < SternU; i++) Strip(mb, M(sTop[i], s) + V(0, -0.65f, -s * 0.3f), M(sTop[i + 1], s) + V(0, -0.65f, -s * 0.3f), 1.0f, 0.35f, Cream);
                mb.Box(V(38f, 30.22f, s * 21.2f), V(9.9f, 4.04f, 1.5f), Wood, Quaternion.identity); // top 1 cm under the deck edge (no z-fighting with it)
                for (float x = 33.6f; x < 42.9f; x += 1.6f) mb.Box(V(x, 30.2f, s * 20.43f), V(0.14f, 4.0f, 0.08f), WoodD, Quaternion.identity);
                if (railColliders)
                {
                    pb.boxes.Add((V((sTop[3].x + MidFwd) * 0.5f, 32.95f, s * 21.55f), V(MidFwd - sTop[3].x, 1.4f, 0.4f), Quaternion.identity));
                    pb.boxes.Add((V(38f, 30.225f, s * 21.2f), V(9.9f, 4.05f, 1.5f), Quaternion.identity));
                    for (int i = 3; i < SternU; i++)
                    {
                        Vector3 a = M(sTop[i], s) + V(0, 0.55f, -s * 0.45f), b = M(sTop[i + 1], s) + V(0, 0.55f, -s * 0.45f), d = b - a; d.y = 0;
                        pb.boxes.Add(((a + b) * 0.5f, V(0.4f, 1.4f, d.magnitude + 0.3f), Quaternion.LookRotation(d.normalized, Vector3.up)));
                    }
                }
            }
            RailRun(mb, V(xs, 32.5f, -sTop[SternU].z + 0.45f), V(xs, 32.5f, sTop[SternU].z - 0.45f));
            mb.Box(V(xs - 0.15f, 31.75f, 0f), V(0.35f, 1.0f, 2f * sTop[SternU].z - 0.6f), Cream, Quaternion.identity);
            RailRun(mb, V(MidFwd + 0.2f, 32.5f, -21.55f), V(MidFwd + 0.2f, 32.5f, 21.55f));
            // Small stern deck behind the castle (the greybox ends at x -42.9 there).
            float x0 = xs - 0.1f, x1 = -42.9f;
            for (float z = -20.6f; z < 20.6f; z += 0.4f)
                mb.Box(V((x0 + x1) * 0.5f, 31.475f, z + 0.2f), V(x1 - x0, 0.45f, 0.385f), Shade(Hash(Mathf.RoundToInt(z * 10), 5) > 0.5f ? Deck : DeckD, 0.95f), Quaternion.identity);
            if (railColliders)
            {
                pb.boxes.Add((V(xs, 32.95f, 0f), V(0.4f, 1.4f, 2f * sTop[SternU].z), Quaternion.identity));
                pb.boxes.Add((V(MidFwd + 0.2f, 32.95f, 0f), V(0.4f, 1.4f, 43.1f), Quaternion.identity));
                pb.boxes.Add((V((x0 + x1) * 0.5f, 31.475f, 0f), V(x1 - x0, 0.45f, 41.2f), Quaternion.identity));
            }
        }

        static void RailRun(MB mb, Vector3 a, Vector3 b)
        {
            float len = Vector3.Distance(a, b); if (len < 0.05f) return;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 1.6f));
            for (int i = 0; i <= n; i++) { var p = Vector3.Lerp(a, b, i / (float)n); mb.Box(p + V(0, 0.5f, 0), V(0.07f, 1.0f, 0.07f), Iron, Quaternion.identity); }
            Strip(mb, a + V(0, 1.0f, 0), b + V(0, 1.0f, 0), 0.07f, 0.09f, WoodD);
            Strip(mb, a + V(0, 0.55f, 0), b + V(0, 0.55f, 0), 0.04f, 0.04f, Iron);
            Strip(mb, a + V(0, 0.25f, 0), b + V(0, 0.25f, 0), 0.04f, 0.04f, Iron);
        }

        /// <summary>Armoured casemate on the hull side (local: +z = outboard, origin on the hull plane at ship x). The alcove one is hollow around the greybox gun alcove.</summary>
        static void Casemate(PB pb, bool alcove)
        {
            var mb = pb.s;
            float x0 = -3.2f, x1 = 3.2f, y0 = 11.0f, y1 = 17.6f, zo = 3.9f;
            if (alcove)
            {
                mb.Box(V(0, (y0 + y1) * 0.5f, zo - 0.28f), V(x1 - x0, y1 - y0, 0.55f), Steel, Quaternion.identity);
                mb.Box(V(0, 17.275f, (zo - 0.1f) * 0.5f), V(x1 - x0, 0.65f, zo - 0.1f), Steel, Quaternion.identity);
                mb.Box(V(0, 11.475f, (zo - 0.1f) * 0.5f), V(x1 - x0, 0.95f, zo - 0.1f), Steel, Quaternion.identity);
                mb.Box(V(-2.975f, (y0 + y1) * 0.5f, (zo - 0.1f) * 0.5f), V(0.45f, y1 - y0, zo - 0.1f), Steel, Quaternion.identity);
                mb.Box(V(2.975f, (y0 + y1) * 0.5f, (zo - 0.1f) * 0.5f), V(0.45f, y1 - y0, zo - 0.1f), Steel, Quaternion.identity);
            }
            else mb.Box(V(0, (y0 + y1) * 0.5f, (zo - 0.1f) * 0.5f), V(x1 - x0, y1 - y0, zo + 0.1f), Steel, Quaternion.identity);
            // sloped armour roof and skirt
            mb.ExtrudePoly(new[] { V(x0, y1, -0.1f), V(x0, y1, zo), V(x0, y1 + 0.9f, -0.1f) }, Vector3.right, x1 - x0, Steel2);
            mb.ExtrudePoly(new[] { V(x0, y0, -0.1f), V(x0, y0, zo), V(x0, y0 - 0.7f, -0.1f) }, Vector3.right, x1 - x0, SteelD);
            mb.Box(V(0, y1 - 0.1f, zo + 0.02f), V(x1 - x0 + 0.1f, 0.25f, 0.12f), SteelD, Quaternion.identity);
            mb.Box(V(0, y0 + 0.1f, zo + 0.02f), V(x1 - x0 + 0.1f, 0.25f, 0.12f), SteelD, Quaternion.identity);
            Rivets(mb, V(x0 + 0.3f, y1 - 0.45f, zo + 0.05f), V(x1 - 0.3f, y1 - 0.45f, zo + 0.05f), 0.45f, Vector3.forward, 0.08f, RivetC);
            Rivets(mb, V(x0 + 0.3f, y0 + 0.45f, zo + 0.05f), V(x1 - 0.3f, y0 + 0.45f, zo + 0.05f), 0.45f, Vector3.forward, 0.08f, RivetC);
            // gun port with mantlet and barrel
            float gy = 14.6f;
            mb.Prism(V(0, gy, zo - 0.05f), 12, 1.0f, 0.9f, 0.45f, SteelD, Quaternion.FromToRotation(Vector3.up, Vector3.forward));
            mb.Prism(V(0, gy, zo + 0.35f), 10, 0.42f, 0.34f, 4.2f, Iron, Quaternion.FromToRotation(Vector3.up, Vector3.forward));
            mb.Prism(V(0, gy, zo + 4.3f), 10, 0.4f, 0.4f, 0.35f, Iron, Quaternion.FromToRotation(Vector3.up, Vector3.forward));
            mb.Prism(V(0, gy, zo + 4.62f), 10, 0.2f, 0.2f, 0.06f, Black, Quaternion.FromToRotation(Vector3.up, Vector3.forward));
        }

        // ---------- Deck items, masts, rigging, funnels ----------

        static Vector3 foreTop, mainTop;

        static void BuildDeckItems()
        {
            var parent = Group("Deck");
            // Prow: turret, foremast, capstans, bollards, ventilators.
            float xt = 53.5f;
            Place(Prefab("Deck", "Turret_Main", Turret), parent, V(xt, BowTop(xt) - 0.05f, 0), Quaternion.identity, "Bow Turret");
            float xf = 46.4f, fh = 35f;
            Place(Prefab("Deck", "Mast_H35", pb => Mast(pb, fh, 15f, 10f)), parent, V(xf, BowTop(xf), 0), Quaternion.identity, "Foremast");
            foreTop = V(xf, BowTop(xf) + fh, 0);
            var cap = Prefab("Deck", "Capstan", Capstan);
            var bol = Prefab("Deck", "Bollard", Bollard);
            var vent = Prefab("Deck", "Ventilator", Ventilator);
            for (int s = -1; s <= 1; s += 2)
            {
                Place(cap, parent, V(56.6f, BowTop(56.6f), s * 3.4f), Quaternion.identity);
                foreach (float x in new[] { 49.5f, 62.5f })
                {
                    float hw = BowTopHw(BowU01(x));
                    Place(bol, parent, V(x, BowTop(x), s * (hw - 1.3f)), Quaternion.Euler(0, s * 10f, 0));
                }
                Place(vent, parent, V(45.3f, BowTop(45.3f), s * 11.5f), Quaternion.Euler(0, s > 0 ? 200f : -20f, 0));
            }

            // Funnels on the two centre-line obstacles, deck guns on the two -z side ones.
            var fun = Prefab("Deck", "Funnel", Funnel);
            foreach (var o in obstaclePieces)
            {
                var size = Vector3.Scale(o.GetComponent<BoxCollider>().size, Abs(o.lossyScale));
                var top = o.TransformPoint(o.GetComponent<BoxCollider>().center) + Vector3.up * size.y * 0.5f;
                var lc = style.InverseTransformPoint(top);
                if (o.name == "Obstacle (2)" || o.name == "Obstacle (6)")
                    Place(fun, parent, lc, Quaternion.identity, "Funnel on " + o.name);
                else if (o.name == "Obstacle (1)" || o.name == "Obstacle (5)")
                    Place(Prefab("Deck", "DeckGun", DeckGun), parent, lc, Quaternion.Euler(0, 180f, 0), "Deck Gun on " + o.name);
            }

            // Stern castle top: main mast (castle wrapper builds the rest).
            var castle = ship.Find("Protagonist Ship  (7)");
            if (castle != null)
            {
                var bc = castle.GetComponent<BoxCollider>(); var S = Vector3.Scale(bc.size, Abs(castle.lossyScale));
                var topLocal = style.InverseTransformPoint(castle.TransformPoint(bc.center) + castle.rotation * V(-S.x * 0.5f + 3.2f, S.y * 0.5f, 0f));
                float mh = 34f;
                Place(Prefab("Deck", "Mast_H34", pb => Mast(pb, mh, 18f, 12f)), parent, topLocal, Quaternion.identity, "Main Mast");
                mainTop = topLocal + V(0, mh, 0);
                // Lifeboats on davits along the castle sides.
                var boat = Prefab("Deck", "Lifeboat_Davits", LifeboatDavits);
                float cx = style.InverseTransformPoint(castle.TransformPoint(bc.center)).x;
                foreach (float x in new[] { cx - 3.8f, cx + 3.8f })
                    for (int s = -1; s <= 1; s += 2) Place(boat, parent, V(x, 32.5f, s * 21.55f), Quaternion.Euler(0, s > 0 ? 0f : 180f, 0), "Lifeboat");
            }
            Rigging(parent);
            report.Add($"Deck: bow turret x {xt}, foremast x {xf}, main mast on the castle top at {mainTop.x:F1}, funnels on Obstacle (2)/(6), deck guns on Obstacle (1)/(5), 4 lifeboats on davits by the castle (ship frame).");
        }

        static void Rigging(Transform parent)
        {
            var mb = new MB(); var rc = RopeD;
            int K = LevY.Length;
            var stem = V(StemTopX - 0.6f, BowTop(StemTopX - 0.6f) + 6f, 0f);
            mb.Rod(foreTop + V(0, -0.5f, 0), stem, 0.07f, rc);
            mb.Rod(foreTop + V(0, -1.5f, 0), V(StemTopX - 2.5f, BowTop(StemTopX - 2.5f) + 1f, 0f), 0.06f, rc);
            if (mainTop != Vector3.zero) mb.Rod(mainTop + V(0, -0.5f, 0), foreTop + V(0, -0.8f, 0), 0.07f, rc);
            var sTop = SternRow(K - 1);
            for (int s = -1; s <= 1; s += 2)
            {
                foreach (float x in new[] { 44.6f, 47.6f, 50.4f })
                {
                    float hw = BowTopHw(BowU01(x)) - 0.15f;
                    mb.Rod(foreTop + V(0, -fhShroud, 0), V(x, BowTop(x) + 1.05f, s * hw), 0.05f, rc);
                }
                if (mainTop != Vector3.zero)
                {
                    foreach (float dx in new[] { -4.5f, -2f, 0.5f, 3f })
                        mb.Rod(mainTop + V(0, -14f, 0), V(mainTop.x + dx, 32.62f, s * 21.75f), 0.05f, rc);
                    mb.Rod(mainTop + V(0, -0.8f, 0), V(sTop[SternU].x + 0.6f, 32.62f, s * (sTop[SternU].z - 0.6f)), 0.06f, rc);
                }
            }
            SceneMesh(parent, "Rigging", mb, solid, false);
        }
        const float fhShroud = 10f;

        static void Turret(PB pb)
        {
            var mb = pb.s;
            mb.Prism(V(0, 0, 0), 16, 4.3f, 4.1f, 1.3f, Steel2);
            mb.Prism(V(0, 1.3f, 0), 16, 4.2f, 4.2f, 0.12f, SteelD);
            Rivets(mb, V(-4.15f, 0.9f, 0f), V(-4.15f, 0.9f, 0f), 1f, Vector3.left, 0.08f, RivetC);
            var lo = new[] { V(3.6f, 0, -1.9f), V(3.6f, 0, 1.9f), V(1.6f, 0, 3.4f), V(-3.2f, 0, 3.4f), V(-4.2f, 0, 2.2f), V(-4.2f, 0, -2.2f), V(-3.2f, 0, -3.4f), V(1.6f, 0, -3.4f) };
            var hi = lo.Select(p => new Vector3(p.x * 0.86f - 0.35f, 0, p.z * 0.86f)).ToArray();
            float yb = 1.42f, yh = 3.4f, yt = 4.1f;
            var loB = lo.Select(p => p + V(0, yb, 0)).ToArray(); var loH = lo.Select(p => p + V(0, yh, 0)).ToArray(); var hiT = hi.Select(p => p + V(0, yt, 0)).ToArray();
            mb.Loft(loB, loH, Steel, V(0, yb, 0));
            mb.Loft(loH, hiT, Steel2, V(0, yh, 0));
            mb.Cap(hiT, SteelD, Vector3.up);
            Rivets(mb, loH[0] + V(0.02f, -0.15f, 0), loH[1] + V(0.02f, -0.15f, 0), 0.4f, Vector3.right, 0.08f, RivetC);
            for (int i = 2; i < 7; i++) Rivets(mb, loH[i] + V(0, -0.15f, 0), loH[i + 1] + V(0, -0.15f, 0), 0.6f, (loH[i] + loH[i + 1]).normalized, 0.08f, RivetC);
            // barrels with mantlets
            foreach (float z in new[] { -1.0f, 1.0f })
            {
                var b = V(3.3f, 2.5f, z); var dir = Quaternion.Euler(0, 0, 4f) * Vector3.right;
                mb.Prism(b, 10, 0.55f, 0.5f, 0.9f, SteelD, Quaternion.FromToRotation(Vector3.up, dir));
                mb.Prism(b + dir * 0.9f, 10, 0.34f, 0.27f, 10.5f, Iron, Quaternion.FromToRotation(Vector3.up, dir));
                mb.Prism(b + dir * 11.2f, 10, 0.33f, 0.33f, 0.5f, Iron, Quaternion.FromToRotation(Vector3.up, dir));
                mb.Prism(b + dir * 11.65f, 10, 0.17f, 0.17f, 0.06f, Black, Quaternion.FromToRotation(Vector3.up, dir));
            }
            // rangefinder ears, hatch, periscope hoods
            foreach (float z in new[] { -3.35f, 3.35f }) mb.Prism(V(-1.2f, 3.6f, z), 8, 0.42f, 0.42f, 0.9f * Mathf.Sign(z), SteelD, Quaternion.FromToRotation(Vector3.up, Vector3.forward));
            mb.Prism(V(-2.0f, yt, 0f), 10, 0.75f, 0.7f, 0.18f, SteelD);
            mb.Box(V(0.6f, yt + 0.25f, -1.5f), V(0.8f, 0.5f, 0.6f), SteelD, Quaternion.identity);
            mb.Box(V(0.6f, yt + 0.25f, 1.5f), V(0.8f, 0.5f, 0.6f), SteelD, Quaternion.identity);
        }

        static void Mast(PB pb, float h, float yard1, float yard2)
        {
            var mb = pb.s;
            mb.Prism(V(0, 0, 0), 10, 0.95f, 0.85f, 0.6f, Iron);
            mb.Prism(V(0, 0.6f, 0), 10, 0.6f, 0.34f, h - 0.6f, WoodD);
            for (float y = 2f; y < h - 1f; y += 3.2f) mb.Prism(V(0, y, 0), 10, 0.62f - y / h * 0.27f, 0.62f - y / h * 0.27f, 0.18f, Iron);
            float yt = h * 0.68f;
            mb.Prism(V(0, yt, 0), 12, 1.9f, 1.9f, 0.22f, WoodD);
            mb.Prism(V(0, yt - 0.9f, 0), 12, 0.4f, 1.7f, 0.9f, WoodD);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f; var p = V(Mathf.Sin(a) * 1.8f, yt + 0.22f, Mathf.Cos(a) * 1.8f);
                mb.Box(p + V(0, 0.5f, 0), V(0.06f, 1.0f, 0.06f), Iron, Quaternion.identity);
            }
            mb.Prism(V(0, yt + 1.18f, 0), 12, 1.85f, 1.85f, 0.06f, Iron);
            foreach (var (y, len) in new[] { (h * 0.5f, yard1), (h * 0.84f, yard2) })
            {
                mb.Box(V(0.45f, y, 0), V(0.3f, 0.3f, len), WoodD, Quaternion.identity);
                mb.Box(V(0.45f, y, 0), V(0.36f, 0.36f, 0.5f), Iron, Quaternion.identity);
                mb.Box(V(0.45f, y + 0.25f, -len * 0.5f + 0.2f), V(0.08f, 0.5f, 0.08f), Iron, Quaternion.identity);
                mb.Box(V(0.45f, y + 0.25f, len * 0.5f - 0.2f), V(0.08f, 0.5f, 0.08f), Iron, Quaternion.identity);
            }
            mb.Prism(V(0, h, 0), 8, 0.3f, 0.3f, 0.2f, Brass);
            mb.Cyl(V(0, h, 0), V(0, h + 2.4f, 0), 0.06f, 6, Iron);
            Flag(mb, V(0, h + 2.3f, 0), 4.2f, 0.9f, RedPaint, RedPaint, Vector3.left);
            // searchlight on the top
            mb.Prism(V(0.9f, yt + 0.22f, 0), 8, 0.35f, 0.35f, 0.4f, Iron);
            mb.Prism(V(0.9f, yt + 0.9f, 0), 10, 0.45f, 0.45f, 0.7f, SteelD, Quaternion.FromToRotation(Vector3.up, Vector3.right));
            mb.Prism(V(1.6f, yt + 0.9f, 0), 10, 0.36f, 0.36f, 0.04f, GWhiteSolid, Quaternion.FromToRotation(Vector3.up, Vector3.right));
        }
        static readonly Color GWhiteSolid = C(0.9f, 0.92f, 0.95f, 0.9f);

        static void Capstan(PB pb)
        {
            var mb = pb.s;
            mb.Prism(V(0, 0, 0), 12, 0.9f, 0.85f, 0.25f, Iron);
            mb.Prism(V(0, 0.25f, 0), 12, 0.6f, 0.5f, 0.8f, Iron);
            mb.Prism(V(0, 1.05f, 0), 12, 0.75f, 0.75f, 0.2f, Brass);
            for (int i = 0; i < 6; i++) { float a = i * 60f; var d = Quaternion.Euler(0, a, 0) * Vector3.forward; mb.Box(d * 0.7f + V(0, 0.95f, 0), V(0.14f, 0.14f, 1.3f), WoodL, Quaternion.LookRotation(d)); }
        }

        static void Bollard(PB pb)
        {
            var mb = pb.s;
            mb.Box(V(0, 0.08f, 0), V(0.9f, 0.16f, 2.0f), Iron, Quaternion.identity);
            foreach (float z in new[] { -0.55f, 0.55f }) { mb.Prism(V(0, 0.16f, z), 10, 0.28f, 0.26f, 0.7f, Iron); mb.Prism(V(0, 0.86f, z), 10, 0.34f, 0.34f, 0.1f, Iron); }
        }

        static void Ventilator(PB pb)
        {
            var mb = pb.s;
            mb.Prism(V(0, 0, 0), 10, 0.45f, 0.42f, 1.8f, Cream);
            mb.Prism(V(0, 1.8f, 0), 10, 0.42f, 0.62f, 0.5f, Cream);
            var d = Vector3.forward;
            mb.Prism(V(0, 2.25f, 0.05f), 10, 0.62f, 0.75f, 0.6f, Cream, Quaternion.FromToRotation(Vector3.up, d));
            mb.Prism(V(0, 2.25f, 0.62f), 10, 0.55f, 0.55f, 0.04f, RedPaint, Quaternion.FromToRotation(Vector3.up, d));
        }

        static void Funnel(PB pb)
        {
            var mb = pb.s;
            float H = 17f, rx = 2.0f, rz = 1.65f, rake = 0.11f; int n = 14;
            Vector3[] Ring(float y, float scale) { var r = new Vector3[n]; for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; r[i] = V(Mathf.Cos(a) * rx * scale - y * rake, y, Mathf.Sin(a) * rz * scale); } return r; }
            var bands = new[] { (0f, 0.4f, Iron, 1.12f), (0.4f, 13.2f, FunnelCol, 1f), (13.2f, 14.3f, Brass, 1.02f), (14.3f, 15.3f, FunnelCol, 1f), (15.3f, H, Black, 1f) };
            foreach (var (y0, y1, col, sc) in bands) mb.Loft(Ring(y0, sc), Ring(y1, sc), col, V(-((y0 + y1) * 0.5f) * rake, (y0 + y1) * 0.5f, 0));
            var top = Ring(H, 1f); var innerRing = Ring(H - 0.01f, 0.86f);
            mb.Loft(top.Select(p => p + V(0, 0.18f, 0)).ToArray(), top, Black, V(-H * rake, H + 0.09f, 0));
            mb.Cap(innerRing, C(0.02f, 0.02f, 0.02f, 0f), Vector3.up);
            // steam pipe and whistle at the back, ladder rungs at the front, guy rings
            mb.Cyl(V(-rx - 0.35f, 0.2f, 0.5f), V(-rx - 0.35f - (H + 0.8f) * rake, H + 0.8f, 0.5f), 0.14f, 8, SteelD);
            mb.Prism(V(-rx - 0.35f - (H + 0.8f) * rake, H + 0.8f, 0.5f), 8, 0.2f, 0.14f, 0.5f, Brass);
            for (float y = 1f; y < 13f; y += 0.45f) mb.Box(V(rx + 0.12f - y * rake, y, 0f), V(0.06f, 0.06f, 0.7f), Iron, Quaternion.identity);
            pb.capsules.Add((V(-1.2f, 4f, 0f), 1.9f, 8f));
        }

        static void DeckGun(PB pb)
        {
            var mb = pb.s;
            mb.Prism(V(0, 0, 0), 10, 0.75f, 0.55f, 0.35f, SteelD);
            mb.Prism(V(0, 0.35f, 0), 10, 0.32f, 0.28f, 0.75f, Steel2);
            mb.Box(V(0, 1.2f, 0.1f), V(0.7f, 0.5f, 1.0f), SteelD, Quaternion.identity);
            mb.Prism(V(0, 1.25f, 0.45f), 10, 0.15f, 0.12f, 3.0f, Iron, Quaternion.FromToRotation(Vector3.up, Vector3.forward));
            mb.Prism(V(0, 1.25f, 3.4f), 10, 0.16f, 0.16f, 0.25f, Iron, Quaternion.FromToRotation(Vector3.up, Vector3.forward));
            // curved shield
            for (int i = -2; i <= 2; i++)
            {
                var d = Quaternion.Euler(0, i * 22f, 0) * Vector3.forward;
                mb.Box(d * 0.95f + V(0, 1.25f, 0), V(0.5f, 1.3f, 0.06f), Steel, Quaternion.LookRotation(d));
            }
            mb.Box(V(-0.55f, 0.9f, -0.5f), V(0.35f, 0.08f, 0.35f), Iron, Quaternion.identity);
            mb.Box(V(0.75f, 0.2f, -0.9f), V(0.6f, 0.4f, 0.4f), CanvasG, Quaternion.identity);
        }

        static void LifeboatDavits(PB pb)
        {
            // local: +z = outboard, origin on the gunwale; boat hangs outside the hull.
            var mb = pb.s;
            foreach (float x in new[] { -2.9f, 2.9f })
            {
                var b = V(x, 0f, 0f);
                mb.Prism(b, 8, 0.16f, 0.14f, 3.6f, Iron);
                var p0 = b + V(0, 3.6f, 0); var p1 = b + V(0, 4.4f, 0.9f); var p2 = b + V(0, 4.5f, 2.3f);
                mb.Rod(p0, p1, 0.26f, Iron); mb.Rod(p1, p2, 0.24f, Iron);
                mb.Rod(p2, p2 + V(0, -1.6f, 0), 0.04f, RopeD);
                mb.Box(p2 + V(0, -1.75f, 0), V(0.2f, 0.25f, 0.12f), Iron, Quaternion.identity);
            }
            // boat: hull from lofted rings
            float L = 7.4f, B = 2.2f, D = 1.1f; var c = V(0, 2.0f, 2.3f);
            int n = 9; var rings = new List<Vector3[]>();
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n, x = (t - 0.5f) * L, w = Mathf.Sqrt(Mathf.Max(0.02f, 1f - Mathf.Pow(2f * t - 1f, 2f))) * B * 0.5f, d = D * (0.55f + 0.45f * w / (B * 0.5f));
                rings.Add(new[] { c + V(x, 0, -w), c + V(x, -d * 0.7f, -w * 0.6f), c + V(x, -d, 0), c + V(x, -d * 0.7f, w * 0.6f), c + V(x, 0, w) });
            }
            for (int i = 0; i < n; i++)
                for (int j = 0; j < 4; j++)
                    mb.Quad(rings[i][j], rings[i + 1][j], rings[i + 1][j + 1], rings[i][j + 1], j == 1 || j == 2 ? White : White, (rings[i][j] + rings[i + 1][j + 1]) * 0.5f - (c + V(0, 0.4f, 0)));
            for (int i = 0; i < n; i++) mb.Quad(rings[i][0] + V(0, 0.02f, 0), rings[i + 1][0] + V(0, 0.02f, 0), rings[i + 1][4] + V(0, 0.02f, 0), rings[i][4] + V(0, 0.02f, 0), CanvasG, Vector3.up);
            for (int i = 0; i < n; i++)
            {
                Strip(mb, rings[i][0] + V(0, 0.03f, 0), rings[i + 1][0] + V(0, 0.03f, 0), 0.12f, 0.12f, WoodD);
                Strip(mb, rings[i][4] + V(0, 0.03f, 0), rings[i + 1][4] + V(0, 0.03f, 0), 0.12f, 0.12f, WoodD);
            }
            mb.Box(c + V(0, 0.18f, 0), V(L * 0.8f, 0.32f, 0.1f), CanvasG, Quaternion.identity);
        }

        // ---------- Wrappers (exact size of greybox blocks) ----------

        /// <summary>
        /// Coop_ButtonBridge/Coop_Bridge rests half inside the wall it slides through and sticks out into the room behind it.
        /// Cover that exposed part with a small steel block flush with the wall (same surface material, with a collider, so it
        /// reads as part of the wall). The bridge slides away from the block, so its travel stays clear.
        /// </summary>
        static void BuildBridgeHousing()
        {
            var root = style.parent;
            foreach (Transform bb in root)
            {
                if (!bb.name.StartsWith("Coop_ButtonBridge")) continue;
                var t = bb.Find("Coop_Bridge"); if (t == null) continue;
                var mover = t.GetComponent("MechanismMover"); if (mover == null) continue;
                var off = new SerializedObject(mover).FindProperty("openOffset").vector3Value;
                var dirW = (t.rotation * off).normalized; if (off.sqrMagnitude < 0.01f) continue;
                // bridge box in the ship frame (corners of each part in its own frame, so the yaw does not inflate it)
                var lb = new Bounds(style.InverseTransformPoint(t.position), Vector3.zero);
                foreach (var r in t.GetComponentsInChildren<Renderer>())
                {
                    var rb = r.localBounds;
                    for (int i = 0; i < 8; i++) lb.Encapsulate(style.InverseTransformPoint(r.transform.TransformPoint(rb.center + Vector3.Scale(rb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)))));
                }
                // the wall it slides through: first face hit from the bridge centre along the travel
                float wallD = -1f;
                foreach (var h in Physics.RaycastAll(t.position, dirW, 30f, ~0, QueryTriggerInteraction.Ignore))
                    if (!h.collider.transform.IsChildOf(bb) && !h.collider.transform.IsChildOf(style) && (wallD < 0 || h.distance < wallD)) wallD = h.distance;
                if (wallD < 0) continue;
                var wallL = style.InverseTransformPoint(t.position + dirW * wallD);
                var ax = Quaternion.Inverse(style.rotation) * dirW; ax.y = 0; ax.Normalize();
                bool alongX = Mathf.Abs(ax.x) > Mathf.Abs(ax.z); float sg = alongX ? Mathf.Sign(ax.x) : Mathf.Sign(ax.z);
                float a0 = (alongX ? wallL.x : wallL.z) + sg * 0.03f;                                        // a bit into the wall
                float a1 = (alongX ? (sg < 0 ? lb.max.x : lb.min.x) : (sg < 0 ? lb.max.z : lb.min.z)) - sg * 0.25f; // past the back end
                float c0 = (alongX ? lb.min.z : lb.min.x) - 0.2f, c1 = (alongX ? lb.max.z : lb.max.x) + 0.2f;
                float y0 = lb.min.y - 0.2f, y1 = lb.max.y + 0.2f;
                float lo = Mathf.Min(a0, a1), hi = Mathf.Max(a0, a1);
                var ctr = alongX ? V((lo + hi) * 0.5f, (y0 + y1) * 0.5f, (c0 + c1) * 0.5f) : V((c0 + c1) * 0.5f, (y0 + y1) * 0.5f, (lo + hi) * 0.5f);
                var size = alongX ? V(hi - lo, y1 - y0, c1 - c0) : V(c1 - c0, y1 - y0, hi - lo);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Bridge Cover " + bb.name.Replace("Coop_", "");
                go.transform.SetParent(Group("Wraps"), false);
                go.transform.localPosition = ctr; go.transform.localRotation = Quaternion.identity; go.transform.localScale = size;
                go.GetComponent<Renderer>().sharedMaterial = steelSurf;
                go.isStatic = true;
                var mn = ctr - size * 0.5f; var mx = ctr + size * 0.5f;
                report.Add($"Bridge cover over retracted {bb.name}/{t.name}: ship frame x {mn.x:F2}..{mx.x:F2}, y {mn.y:F2}..{mx.y:F2}, z {mn.z:F2}..{mx.z:F2} (box collider).");
            }
        }

        static void BuildWraps()
        {
            var parent = Group("Wraps");
            int n = 0;
            foreach (var t in hullPieces.Concat(platformPieces).Concat(obstaclePieces).Concat(gameplayRoots))
            {
                if (!IsWrapped(t.name) || !IsPlain(t)) continue;
                var bc = t.GetComponent<BoxCollider>(); var S = Vector3.Scale(bc.size, Abs(t.lossyScale));
                GameObject pf = null; string nm = t.name;
                if (nm == "Protagonist Ship  (7)") pf = Prefab("Wraps", "Castle_" + Cm(S), pb => Castle(pb, S));
                else if (nm == "Protagonist Ship  (42)") pf = Prefab("Wraps", "EngineColumn_" + Cm(S), pb => { pb.interior = true; EngineColumn(pb, S); });
                else if (nm.StartsWith("Protagonist Ship")) pf = S.x > 3f && S.z > 3f ? Prefab("Wraps", "EngineBlock_" + Cm(S), pb => { pb.interior = true; EngineBlock(pb, S); }) : Prefab("Wraps", "Piston_" + Cm(S), pb => { pb.interior = true; Piston(pb, S); });
                else if (nm.StartsWith("Platform")) pf = S.y > 5f ? Prefab("Wraps", "BunkTower_" + Cm(S), pb => { pb.interior = true; BunkTower(pb, S); }) : Prefab("Wraps", "Cabinet_" + Cm(S), pb => { pb.interior = true; Cabinet(pb, S); });
                else if (nm == "Cannon") pf = Prefab("Wraps", "GunCarriage_" + Cm(S), pb => GunCarriage(pb, S));
                else if (nm == "Cannon (1)") pf = Prefab("Wraps", "GunBreech_" + Cm(S), pb => GunBreech(pb, S));
                else if (nm == "Cannon (2)") pf = Prefab("Wraps", "GunBarrel_" + Cm(S), pb => GunBarrel(pb, S));
                else if (nm == "Obstacle (2)" || nm == "Obstacle (6)") pf = Prefab("Wraps", "FunnelCasing_" + Cm(S), pb => FunnelCasing(pb, S));
                else if (nm == "Obstacle (1)" || nm == "Obstacle (5)") pf = Prefab("Wraps", "GunTub_" + Cm(S), pb => GunTub(pb, S));
                else if (nm == "Obstacle (3)") pf = Prefab("Wraps", "Skylight_" + Cm(S), pb => Skylight(pb, S));
                else if (nm == "Obstacle (7)" || nm == "Obstacle (8)") pf = Prefab("Wraps", "Deckhouse_" + Cm(S), pb => Deckhouse(pb, S));
                else if (nm.StartsWith("Obstacle")) pf = Prefab("Wraps", "CargoStack_" + Cm(S), pb => CargoStack(pb, S));
                if (pf == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
                go.transform.SetPositionAndRotation(t.TransformPoint(bc.center), t.rotation);
                go.name = "Wrap " + nm;
                instanceCount++; n++;
            }
            report.Add("Wraps: " + n + " greybox blocks dressed (castle, engine tower, cabin pillars, deck obstacles, bow cannon).");
        }

        static string Cm(Vector3 s) => $"{Mathf.RoundToInt(s.x * 100)}x{Mathf.RoundToInt(s.y * 100)}x{Mathf.RoundToInt(s.z * 100)}";

        /// <summary>Face frame of a box of size S centred at the origin: x along the face (u, left to right seen from outside), y up, d out of the face.</summary>
        struct FF
        {
            public Vector3 o, u, n; public Quaternion R; public float W, H;
            public Vector3 P(float x, float y, float d) => o + u * x + Vector3.up * y + n * d;
        }

        static FF Face(Vector3 S, int dir)
        {
            Vector3 n = dir == 0 ? Vector3.right : dir == 1 ? Vector3.left : dir == 2 ? Vector3.forward : Vector3.back;
            var u = Vector3.Cross(Vector3.up, n);
            float W = Mathf.Abs(Vector3.Dot(S, u)), D = Mathf.Abs(Vector3.Dot(S, n));
            var f = new FF { n = n, u = u, W = W, H = S.y, R = Quaternion.LookRotation(n, Vector3.up) };
            f.o = n * D * 0.5f - u * W * 0.5f - Vector3.up * S.y * 0.5f;
            return f;
        }

        static void FB(MB mb, FF f, float x0, float x1, float y0, float y1, float d0, float d1, Color col)
        {
            if (x1 - x0 < 0.002f || y1 - y0 < 0.002f || d1 - d0 < 0.001f) return;
            mb.Box(f.P((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (d0 + d1) * 0.5f), new Vector3(x1 - x0, y1 - y0, d1 - d0), col, f.R);
        }

        static void FWindow(MB mb, FF f, float cx, float y0, float y1, float w)
        {
            FB(mb, f, cx - w * 0.5f, cx + w * 0.5f, y0, y1, -0.06f, -0.01f, Glass);
            FB(mb, f, cx - w * 0.5f - 0.1f, cx + w * 0.5f + 0.1f, y1, y1 + 0.12f, -0.02f, 0.07f, WoodL);
            FB(mb, f, cx - w * 0.5f - 0.1f, cx + w * 0.5f + 0.1f, y0 - 0.12f, y0, -0.02f, 0.14f, SteelD);
            FB(mb, f, cx - w * 0.5f - 0.1f, cx - w * 0.5f, y0, y1, -0.02f, 0.07f, WoodL);
            FB(mb, f, cx + w * 0.5f, cx + w * 0.5f + 0.1f, y0, y1, -0.02f, 0.07f, WoodL);
            FB(mb, f, cx - 0.035f, cx + 0.035f, y0, y1, -0.04f, 0.02f, WoodL);
            FB(mb, f, cx - w * 0.5f, cx + w * 0.5f, (y0 + y1) * 0.5f - 0.035f, (y0 + y1) * 0.5f + 0.035f, -0.04f, 0.02f, WoodL);
        }

        static void FDoor(MB mb, MB g, FF f, float cx, float w, float h, bool lamp)
        {
            FB(mb, f, cx - w * 0.5f - 0.14f, cx + w * 0.5f + 0.14f, 0f, h + 0.14f, -0.03f, 0.08f, Iron);
            for (int i = 0; i < 6; i++) { float a = cx - w * 0.5f + w * i / 6f; FB(mb, f, a + 0.01f, a + w / 6f - 0.01f, 0.02f, h, -0.02f, 0.1f - (i % 2) * 0.01f, Shade(WoodD, 0.95f + 0.1f * Hash(i, 3))); }
            foreach (float y in new[] { 0.45f, h * 0.5f, h - 0.45f }) FB(mb, f, cx - w * 0.5f + 0.05f, cx + w * 0.5f - 0.05f, y - 0.06f, y + 0.06f, 0.1f, 0.13f, Iron);
            FB(mb, f, cx - 0.2f, cx - 0.08f, h * 0.48f, h * 0.56f, 0.12f, 0.2f, Brass);
            FB(mb, f, cx + 0.08f, cx + 0.2f, h * 0.48f, h * 0.56f, 0.12f, 0.2f, Brass);
            if (lamp)
            {
                FB(mb, f, cx - 0.06f, cx + 0.06f, h + 0.35f, h + 0.5f, 0f, 0.35f, Iron);
                g.Box(f.P(cx, h + 0.32f, 0.35f), new Vector3(0.22f, 0.32f, 0.22f), GLamp, f.R);
                FB(mb, f, cx - 0.16f, cx + 0.16f, h + 0.48f, h + 0.56f, 0.2f, 0.5f, Iron);
            }
        }

        static void Castle(PB pb, Vector3 S)
        {
            var mb = pb.s; var g = pb.g;
            mb.Box(Vector3.zero, S - Vector3.one * 0.1f, SteelD, Quaternion.identity);
            float H = S.y;
            for (int dir = 0; dir < 4; dir++)
            {
                var f = Face(S, dir); float W = f.W;
                FB(mb, f, 0, W, 0, 0.9f, -0.06f, 0.03f, SteelD);
                for (float x = 0; x < W - 0.01f; x += 0.42f) FB(mb, f, x, Mathf.Min(x + 0.41f, W), 0.9f, 8.75f, -0.06f, -0.004f - 0.012f * Hash(Mathf.RoundToInt(x * 10), dir), Shade(Color.Lerp(Wood, WoodL, Hash(dir, Mathf.RoundToInt(x * 7)) * 0.5f), 0.95f));
                FB(mb, f, 0, W, 4.55f, 4.85f, -0.06f, 0.1f, Steel);
                FB(mb, f, 0, W, 8.75f, H, -0.06f, 0.06f, Steel);
                FB(mb, f, -0.1f, W + 0.1f, H - 0.22f, H, -0.06f, 0.18f, SteelD);
                int bays = Mathf.Max(1, Mathf.RoundToInt(W / 4.2f)); float bw = W / bays;
                for (int i = 0; i <= bays; i++)
                {
                    float x = Mathf.Clamp(i * bw, 0.21f, W - 0.21f);
                    FB(mb, f, x - 0.21f, x + 0.21f, 0f, H - 0.22f, -0.06f, 0.09f, Steel);
                    Rivets(mb, f.P(x, 1.2f, 0.1f), f.P(x, H - 0.6f, 0.1f), 0.45f, f.n, 0.06f, RivetC);
                }
                var doors = new List<float>();
                if (dir == 0 && bays >= 4) { doors.Add(Mathf.Floor(bays * 0.32f) * bw + bw * 0.5f); doors.Add((bays - 1 - Mathf.Floor(bays * 0.32f)) * bw + bw * 0.5f); }
                for (int i = 0; i < bays; i++)
                {
                    float cx = (i + 0.5f) * bw;
                    if (doors.Any(d => Mathf.Abs(d - cx) < 0.1f)) FDoor(mb, g, f, cx, 2.2f, 3.3f, true);
                    else FWindow(mb, f, cx, 1.8f, 3.5f, 1.3f);
                    FWindow(mb, f, cx, 5.7f, 7.6f, 1.3f);
                }
                if (dir == 0)
                {
                    // ship's bell and lifebuoys on the front
                    var bell = f.P(W * 0.5f, 3.9f, 0.4f);
                    FB(mb, f, W * 0.5f - 0.05f, W * 0.5f + 0.05f, 4.0f, 4.55f, 0f, 0.45f, Iron);
                    mb.Prism(bell + V(0, -0.45f, 0), 10, 0.32f, 0.2f, 0.45f, Brass);
                    foreach (float x in new[] { W * 0.5f - 2.2f, W * 0.5f + 2.2f }) Lifebuoy(mb, f.P(x, 3.0f, 0.1f), f.n);
                }
            }
            // roof deck, railing, wheelhouse, guns, ventilators
            float ty = S.y * 0.5f, hx = S.x * 0.5f, hz = S.z * 0.5f;
            for (float z = -hz + 0.1f; z < hz - 0.1f; z += 0.36f) mb.Box(V(0, ty - 0.01f, z + 0.17f), V(S.x - 0.3f, 0.04f, 0.34f), Shade(Hash(Mathf.RoundToInt(z * 10), 9) > 0.5f ? Deck : DeckD, 0.97f), Quaternion.identity);
            RailRun(mb, V(-hx + 0.3f, ty, -hz + 0.3f), V(hx - 0.3f, ty, -hz + 0.3f)); RailRun(mb, V(-hx + 0.3f, ty, hz - 0.3f), V(hx - 0.3f, ty, hz - 0.3f));
            RailRun(mb, V(-hx + 0.3f, ty, -hz + 0.3f), V(-hx + 0.3f, ty, hz - 0.3f)); RailRun(mb, V(hx - 0.3f, ty, -hz + 0.3f), V(hx - 0.3f, ty, hz - 0.3f));
            float wx0 = hx - 6.2f, wx1 = hx - 1.6f, wz = 7.5f;
            mb.Box(V((wx0 + wx1) * 0.5f, ty + 0.55f, 0), V(wx1 - wx0, 1.1f, 2 * wz), Steel, Quaternion.identity);
            mb.Box(V((wx0 + wx1) * 0.5f, ty + 1.9f, 0), V(wx1 - wx0 - 0.1f, 1.6f, 2 * wz - 0.1f), Glass, Quaternion.identity);
            for (float z = -wz; z <= wz + 0.01f; z += 1.25f) mb.Box(V(wx1, ty + 1.9f, z), V(0.12f, 1.6f, 0.12f), WoodL, Quaternion.identity);
            for (float x = wx0; x <= wx1 + 0.01f; x += 1.15f) { mb.Box(V(x, ty + 1.9f, wz), V(0.12f, 1.6f, 0.12f), WoodL, Quaternion.identity); mb.Box(V(x, ty + 1.9f, -wz), V(0.12f, 1.6f, 0.12f), WoodL, Quaternion.identity); }
            mb.Box(V((wx0 + wx1) * 0.5f, ty + 2.85f, 0), V(wx1 - wx0, 0.3f, 2 * wz), Steel, Quaternion.identity);
            mb.Box(V((wx0 + wx1) * 0.5f + 0.2f, ty + 3.1f, 0), V(wx1 - wx0 + 0.9f, 0.2f, 2 * wz + 0.8f), SteelD, Quaternion.identity);
            RailRun(mb, V(wx0, ty + 3.2f, -wz), V(wx1, ty + 3.2f, -wz)); RailRun(mb, V(wx0, ty + 3.2f, wz), V(wx1, ty + 3.2f, wz)); RailRun(mb, V(wx1, ty + 3.2f, -wz), V(wx1, ty + 3.2f, wz));
            g.Box(V(wx1 + 0.05f, ty + 3.0f, -wz - 0.1f), V(0.25f, 0.25f, 0.25f), GRed, Quaternion.identity);
            g.Box(V(wx1 + 0.05f, ty + 3.0f, wz + 0.1f), V(0.25f, 0.25f, 0.25f), GGreen, Quaternion.identity);
            foreach (float z in new[] { -4.5f, 4.5f })
            {
                var b = V(wx1 - 1.2f, ty + 3.2f, z);
                mb.Prism(b, 8, 0.3f, 0.3f, 0.5f, Iron);
                mb.Prism(b + V(-0.4f, 0.9f, 0), 10, 0.45f, 0.45f, 0.8f, SteelD, Quaternion.FromToRotation(Vector3.up, Vector3.right));
                mb.Prism(b + V(0.4f, 0.9f, 0), 10, 0.38f, 0.38f, 0.04f, GWhiteSolid, Quaternion.FromToRotation(Vector3.up, Vector3.right));
            }
            mb.Cyl(V(wx0 + 1f, ty + 3.2f, 0), V(wx0 + 1f, ty + 7.5f, 0), 0.07f, 6, Iron);
            mb.Box(V(wx0 + 1f, ty + 6.8f, 0), V(0.08f, 0.08f, 2.4f), Iron, Quaternion.identity);
            mb.Prism(V(wx0 - 0.6f, ty + 3.2f, 1.6f), 8, 0.12f, 0.12f, 1.5f, Brass);
            foreach (float z in new[] { -hz + 4f, hz - 4f })
            {
                var b = V(-1.2f, ty, z); var d = z > 0 ? Vector3.forward : Vector3.back;
                mb.Prism(b, 10, 0.9f, 0.8f, 0.3f, SteelD);
                mb.Prism(b + V(0, 0.3f, 0), 10, 0.3f, 0.3f, 0.8f, Steel2);
                mb.Box(b + V(0, 1.3f, 0), V(0.8f, 0.55f, 1.1f), SteelD, Quaternion.identity);
                mb.Prism(b + V(0, 1.35f, 0) + d * 0.4f, 10, 0.17f, 0.13f, 3.2f, Iron, Quaternion.FromToRotation(Vector3.up, d));
                for (int i = -2; i <= 2; i++) { var dd = Quaternion.Euler(0, i * 22f, 0) * d; mb.Box(dd * 1.0f + b + V(0, 1.3f, 0), V(0.5f, 1.3f, 0.06f), Steel, Quaternion.LookRotation(dd)); }
            }
            foreach (float z in new[] { -6f, 6f })
            {
                var b = V(-hx + 1.6f, ty, z);
                mb.Prism(b, 10, 0.35f, 0.33f, 1.4f, Cream); mb.Prism(b + V(0, 1.4f, 0), 10, 0.33f, 0.5f, 0.4f, Cream);
                mb.Prism(b + V(0, 1.75f, 0.05f), 10, 0.5f, 0.6f, 0.45f, Cream, Quaternion.FromToRotation(Vector3.up, Vector3.left));
            }
            mb.Cyl(V(-hx + 3f, ty, -9f), V(-hx + 3f, ty + 2.4f, -9f), 0.18f, 8, Iron);
            mb.Prism(V(-hx + 3f, ty + 2.4f, -9f), 8, 0.32f, 0.05f, 0.3f, Iron);
        }

        static void Lifebuoy(MB mb, Vector3 c, Vector3 n)
        {
            var r = Quaternion.LookRotation(n, Vector3.up);
            for (int i = 0; i < 12; i++)
            {
                float a0 = i * 30f;
                var d = r * (Quaternion.Euler(0, 0, a0 + 15f) * Vector3.up);
                mb.Box(c + d * 0.42f + n * 0.08f, new Vector3(0.24f, 0.24f, 0.16f), (i / 3) % 2 == 0 ? RedPaint : White, r * Quaternion.Euler(0, 0, a0 + 15f));
            }
        }

        static void EngineBlock(PB pb, Vector3 S)
        {
            var mb = pb.s;
            mb.Box(Vector3.zero, S - Vector3.one * 0.06f, Machine, Quaternion.identity);
            float hx = S.x * 0.5f, hy = S.y * 0.5f, hz = S.z * 0.5f;
            foreach (var sx in new[] { -1, 1 }) foreach (var sz in new[] { -1, 1 }) mb.Box(V(sx * (hx - 0.05f), 0, sz * (hz - 0.05f)), V(0.12f, S.y, 0.12f), Brass, Quaternion.identity);
            for (int dir = 0; dir < 4; dir++)
            {
                var f = Face(S, dir); float W = f.W;
                FB(mb, f, 0.1f, W - 0.1f, 0f, 0.35f, -0.04f, 0.01f, MachineD);
                FB(mb, f, 0.1f, W - 0.1f, S.y - 0.3f, S.y, -0.04f, 0.015f, Steel);
                Rivets(mb, f.P(0.3f, S.y - 0.45f, 0.01f), f.P(W - 0.3f, S.y - 0.45f, 0.01f), 0.4f, f.n, 0.06f, RivetC);
                Rivets(mb, f.P(0.3f, 0.5f, 0.01f), f.P(W - 0.3f, 0.5f, 0.01f), 0.4f, f.n, 0.06f, RivetC);
                if (W > 2.5f && S.y > 1.8f)
                {
                    float px0 = 0.45f, px1 = W - 0.45f, py0 = 0.75f, py1 = S.y - 0.75f;
                    FB(mb, f, px0, px1, py0, py0 + 0.07f, -0.02f, 0.02f, MachineD); FB(mb, f, px0, px1, py1 - 0.07f, py1, -0.02f, 0.02f, MachineD);
                    FB(mb, f, px0, px0 + 0.07f, py0, py1, -0.02f, 0.02f, MachineD); FB(mb, f, px1 - 0.07f, px1, py0, py1, -0.02f, 0.02f, MachineD);
                    float gy = Mathf.Min(S.y * 0.62f, 2.1f);
                    for (int i = 0; i < 3; i++) Gauge(mb, f.P(W * 0.25f + i * 0.55f, gy, 0.0f), f.n, 0.2f);
                    if (S.y > 2.5f) for (int i = 0; i < 6; i++) FB(mb, f, W * 0.62f, W * 0.86f, S.y * 0.3f + i * 0.16f, S.y * 0.3f + i * 0.16f + 0.07f, -0.03f, 0.0f, Black);
                    float py = S.y * 0.82f;
                    if (dir % 2 == 0) { var a = f.P(0.25f, py, -0.02f + 0.11f); var b = f.P(W - 0.25f, py, 0.09f); mb.Cyl(a, b, 0.1f, 8, Copper); for (float x = 1.2f; x < W - 0.5f; x += 1.8f) mb.Prism(f.P(x, py, 0.09f) - f.u * 0.06f, 8, 0.16f, 0.16f, 0.12f, Brass, Quaternion.FromToRotation(Vector3.up, f.u)); }
                    else ValveWheel(mb, f.P(W * 0.75f, Mathf.Min(1.4f, S.y * 0.5f), 0.06f), f.n, 0.35f);
                    if (dir == 2 && S.y > 2.5f && W > 4f)
                    {
                        float fx = W * 0.5f;
                        FB(mb, f, fx - 0.75f, fx + 0.75f, 0.45f, 1.65f, -0.02f, 0.08f, Iron);
                        FB(pb.g, f, fx - 0.55f, fx + 0.55f, 0.6f, 1.45f, 0.06f, 0.1f, GFire);
                        for (int i = 0; i < 4; i++) FB(mb, f, fx - 0.5f + i * 0.33f, fx - 0.44f + i * 0.33f, 0.6f, 1.45f, 0.08f, 0.13f, Iron);
                        FB(mb, f, fx - 0.8f, fx + 0.8f, 1.65f, 1.78f, -0.02f, 0.16f, Brass);
                    }
                }
            }
        }

        static void Gauge(MB mb, Vector3 c, Vector3 n, float r)
        {
            var q = Quaternion.FromToRotation(Vector3.up, n);
            mb.Prism(c, 10, r, r, 0.08f, Brass, q);
            mb.Prism(c + n * 0.08f, 10, r * 0.8f, r * 0.8f, 0.015f, Cream, q);
            var up = Vector3.ProjectOnPlane(Vector3.up, n).normalized;
            mb.Box(c + n * 0.1f + up * r * 0.25f, new Vector3(0.025f, r * 0.55f, 0.01f), Black, Quaternion.LookRotation(n, up) * Quaternion.Euler(0, 0, 35f));
        }

        static void ValveWheel(MB mb, Vector3 c, Vector3 n, float r)
        {
            var R = Quaternion.LookRotation(n, Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up);
            for (int i = 0; i < 10; i++) { float a = i * 36f; mb.Box(c + R * (Quaternion.Euler(0, 0, a) * Vector3.up * r) + n * 0.05f, new Vector3(r * 0.66f, 0.05f, 0.05f), RedPaint, R * Quaternion.Euler(0, 0, a)); }
            for (int i = 0; i < 3; i++) mb.Box(c + n * 0.05f, new Vector3(r * 2f, 0.04f, 0.04f), RedPaint, R * Quaternion.Euler(0, 0, i * 60f));
            mb.Prism(c, 8, 0.07f, 0.07f, 0.1f, Iron, Quaternion.FromToRotation(Vector3.up, n));
        }

        static void Piston(PB pb, Vector3 S)
        {
            var mb = pb.s;
            mb.Box(Vector3.zero, S - Vector3.one * 0.04f, SteelD, Quaternion.identity);
            mb.Box(V(0, S.y * 0.5f - 0.1f, 0), V(S.x - 0.012f, 0.2f, S.z - 0.012f), Brass, Quaternion.identity);
            mb.Box(V(0, -S.y * 0.5f + 0.1f, 0), V(S.x - 0.012f, 0.2f, S.z - 0.012f), Brass, Quaternion.identity);
            for (int dir = 0; dir < 4; dir++)
            {
                var f = Face(S, dir); if (f.W < 1.5f) continue;
                float w = f.W * 0.42f, x0 = (f.W - w) * 0.5f;
                FB(mb, f, x0, x0 + w, 0.35f, S.y - 0.35f, -0.3f, 0.005f, Black);
                mb.Cyl(f.P(f.W * 0.5f, 0.35f, -0.17f), f.P(f.W * 0.5f, S.y - 0.35f, -0.17f), 0.13f, 8, SteelL);
                FB(mb, f, x0 - 0.08f, x0, 0.35f, S.y - 0.35f, -0.02f, 0.03f, Iron);
                FB(mb, f, x0 + w, x0 + w + 0.08f, 0.35f, S.y - 0.35f, -0.02f, 0.03f, Iron);
            }
        }

        static void EngineColumn(PB pb, Vector3 S)
        {
            var mb = pb.s;
            mb.Box(Vector3.zero, S - Vector3.one * 0.04f, Steel2, Quaternion.identity);
            for (float y = -S.y * 0.5f + 0.8f; y < S.y * 0.5f - 0.3f; y += 1.5f) mb.Box(V(0, y, 0), V(S.x + 0.06f, 0.14f, S.z + 0.06f), SteelD, Quaternion.identity);
            for (int dir = 0; dir < 4; dir++)
            {
                var f = Face(S, dir);
                Rivets(mb, f.P(0.2f, 0.4f, 0.01f), f.P(0.2f, S.y - 0.4f, 0.01f), 0.4f, f.n, 0.06f, RivetC);
                Rivets(mb, f.P(f.W - 0.2f, 0.4f, 0.01f), f.P(f.W - 0.2f, S.y - 0.4f, 0.01f), 0.4f, f.n, 0.06f, RivetC);
                if (dir % 2 == 0) mb.Cyl(f.P(f.W * 0.5f, 0.2f, 0.02f), f.P(f.W * 0.5f, S.y - 0.2f, 0.02f), 0.13f, 8, Copper);
            }
        }

        /// <summary>Crew bunk tower over a tall cabin pillar: an open steel frame with two rows of bunks back to back (you can see
        /// through it between the tiers), locker blocks at both ends. The greybox collider stays a solid box.</summary>
        static void BunkTower(PB pb, Vector3 S)
        {
            var mb = pb.s;
            bool alongX = S.x >= S.z;
            float L = alongX ? S.x : S.z, D = alongX ? S.z : S.x;      // along the bunks / across (two rows)
            const float capH = 0.1f;
            float top = S.y - 0.012f, bodyH = top - capH, y0 = -S.y * 0.5f;
            float endD = Mathf.Min(0.6f, L * 0.2f);                     // locker blocks at both ends
            Vector3 P(float a, float y, float c) => alongX ? V(a, y, c) : V(c, y, a);
            Vector3 Sz(float a, float y, float c) => alongX ? V(a, y, c) : V(c, y, a);
            void B(float a0, float a1, float ya, float yb, float c0, float c1, Color col) => mb.Box(P((a0 + a1) * 0.5f, (ya + yb) * 0.5f, (c0 + c1) * 0.5f), Sz(a1 - a0, yb - ya, c1 - c0), col, Quaternion.identity);

            B(-L * 0.5f, L * 0.5f, y0 + top - capH, y0 + top, -D * 0.5f, D * 0.5f, SteelD);                                  // top plate
            for (int e = -1; e <= 1; e += 2)                                                                                // end blocks, outer face 3 cm in for the lockers
            {
                float aOut = e * (L * 0.5f - 0.03f), aIn = e * (L * 0.5f - endD);
                B(Mathf.Min(aOut, aIn), Mathf.Max(aOut, aIn), y0, y0 + bodyH, -D * 0.5f, D * 0.5f, Wood);
                for (int s = -1; s <= 1; s += 2) B(Mathf.Min(aOut, aIn) + 0.04f, Mathf.Max(aOut, aIn) - 0.04f, y0 + 0.1f, y0 + bodyH - 0.1f, s * D * 0.5f - 0.006f, s * D * 0.5f + 0.006f, Shade(Wood, 0.85f));
            }
            float zA = -L * 0.5f + endD, zB = L * 0.5f - endD, Lb = zB - zA;
            int nb = Mathf.Max(1, Mathf.RoundToInt(Lb / 2.2f)); float bw = Lb / nb;
            int nt = Mathf.Max(2, Mathf.FloorToInt(bodyH / 1.25f)); float th = bodyH / nt;
            float cOut = D * 0.5f - 0.04f;
            // corner posts of the frame and posts between the bays (outer edges and the middle)
            for (int b = 0; b <= nb; b++)
            {
                float a = zA + b * bw, a0 = b == 0 ? a : a - 0.03f, a1 = b == nb ? a : a + 0.03f;
                if (b == 0) a1 = a + 0.06f; if (b == nb) a0 = a - 0.06f;
                foreach (float c in new[] { -D * 0.5f + 0.03f, 0f, D * 0.5f - 0.03f })
                    B(a0, a1, y0, y0 + bodyH, c - 0.03f, c + 0.03f, SteelL);
            }
            for (int k = 0; k < nt; k++)
            {
                float tb = y0 + k * th, ty = tb + 0.04f;                                                                    // ty + 0.08 = top of the cross beams
                for (int s = -1; s <= 1; s += 2)                                                                            // steel side rails the beams rest on
                    B(zA, zB, tb + 0.02f, tb + 0.14f, Mathf.Min(s * (D * 0.5f - 0.06f), s * (D * 0.5f - 0.01f)), Mathf.Max(s * (D * 0.5f - 0.06f), s * (D * 0.5f - 0.01f)), SteelD);
                int nbm = Mathf.Max(2, Mathf.RoundToInt(Lb / 0.45f)); float bs = (Lb - 0.08f) / nbm;
                for (int i = 0; i <= nbm; i++) { float a = zA + 0.04f + i * bs; B(a - 0.04f, a + 0.04f, tb, tb + 0.12f, -D * 0.5f + 0.015f, D * 0.5f - 0.015f, i % 2 == 0 ? Wood : WoodL); } // cross beams
                B(zA, zB, ty + 0.08f, ty + 0.2f, -0.05f, 0.05f, SteelL);                                                    // middle rail between the rows
                for (int b = 0; b < nb; b++)
                {
                    float a0 = zA + b * bw + 0.07f, a1 = zA + (b + 1) * bw - 0.07f;
                    for (int r = -1; r <= 1; r += 2)
                    {
                        float hs = Hash(b * 13 + r + 3, k * 5 + (alongX ? 1 : 2));
                        float c0 = 0.07f, c1 = D * 0.5f - 0.065f;
                        B(a0, a1, ty + 0.08f, ty + 0.25f, Mathf.Min(r * c0, r * c1), Mathf.Max(r * c0, r * c1), Mattress);
                        var bl = hs > 0.66f ? BlanketR : hs > 0.33f ? BlanketB : BlanketG;
                        B(a0 + 0.55f, a1 + 0.02f, ty + 0.18f, ty + 0.3f, Mathf.Min(r * (c0 - 0.012f), r * (c1 + 0.012f)), Mathf.Max(r * (c0 - 0.012f), r * (c1 + 0.012f)), bl);
                        B(a0 + 0.08f, a0 + 0.48f, ty + 0.25f, ty + 0.38f, Mathf.Min(r * (c0 + 0.06f), r * (c1 - 0.06f)), Mathf.Max(r * (c0 + 0.06f), r * (c1 - 0.06f)), White);
                        if (k > 0) B(a0 - 0.04f, a1 + 0.04f, ty + 0.36f, ty + 0.41f, Mathf.Min(r * (D * 0.5f - 0.05f), r * (D * 0.5f - 0.012f)), Mathf.Max(r * (D * 0.5f - 0.05f), r * (D * 0.5f - 0.012f)), SteelL);
                    }
                }
            }
            // locker fronts on the two end faces, ladders on the two long faces
            for (int dir = 0; dir < 4; dir++)
            {
                var f = Face(S, dir); bool longFace = alongX ? dir >= 2 : dir < 2;
                if (!longFace) { LockerWall(mb, f, 0.02f, f.W - 0.02f, bodyH); continue; }
                for (int b = 0; b < nb; b++)
                {
                    float bx0 = endD + b * bw, lx0 = bx0 + 0.12f, lx1 = bx0 + 0.58f, ltop = (nt - 1) * th + 0.5f;
                    FB(mb, f, lx0, lx0 + 0.05f, 0, ltop, 0.002f, 0.06f, SteelL);
                    FB(mb, f, lx1 - 0.05f, lx1, 0, ltop, 0.002f, 0.06f, SteelL);
                    for (float y = 0.32f; y < ltop - 0.1f; y += 0.32f) FB(mb, f, lx0 + 0.05f, lx1 - 0.05f, y, y + 0.04f, 0.015f, 0.048f, SteelL);
                    for (int k = 1; k < nt; k++) { float ty = k * th; FB(mb, f, lx0, lx1, ty + 0.01f, ty + 0.06f, -0.02f, 0.002f, SteelD); }
                }
            }
        }

        /// <summary>Lockers, cupboards and boarding across x0..x1 of a face whose backing sits at depth -0.03.</summary>
        static void LockerWall(MB mb, FF f, float x0, float x1, float h)
        {
            float wl = x1 - x0; if (wl < 0.05f) return;
            float boardsFrom = 0f;
            if (wl >= 0.6f)
            {
                int nl = Mathf.Max(1, Mathf.RoundToInt(wl / 0.7f)); float lw = wl / nl;
                float lockTop = Mathf.Min(2.15f, h - 0.1f), cupTop = Mathf.Min(3.3f, h - 0.1f);
                FB(mb, f, x0, x1, 0, cupTop + 0.06f, -0.03f, -0.012f, Shade(Green, 0.62f));        // locker frame
                for (int i = 0; i < nl; i++)
                {
                    float a = x0 + i * lw + 0.025f, b = x0 + (i + 1) * lw - 0.025f;
                    FB(mb, f, a, b, 0.08f, lockTop, -0.012f, 0.0f, Green);
                    for (int k = 0; k < 4; k++) FB(mb, f, a + (b - a) * 0.25f, b - (b - a) * 0.25f, lockTop - 0.42f + k * 0.08f, lockTop - 0.38f + k * 0.08f, 0f, 0.012f, Black);
                    FB(mb, f, b - 0.12f, b - 0.07f, 0.95f, 1.2f, 0f, 0.05f, Brass);
                    FB(mb, f, a + 0.1f, b - 0.1f, 0.25f, 0.33f, 0f, 0.008f, Cream);                     // name plate
                    if (cupTop - lockTop > 0.4f)
                    {
                        FB(mb, f, a, b, lockTop + 0.06f, cupTop, -0.012f, 0.0f, Shade(Green, 1.1f));
                        FB(mb, f, (a + b) * 0.5f - 0.04f, (a + b) * 0.5f + 0.04f, lockTop + 0.14f, lockTop + 0.22f, 0f, 0.04f, Brass);
                    }
                }
                boardsFrom = cupTop + 0.06f;
            }
            if (h - boardsFrom < 0.2f) return;
            int nbd = Mathf.Max(1, Mathf.RoundToInt(wl / 0.45f)); float bwd = wl / nbd;
            for (int i = 0; i < nbd; i++) FB(mb, f, x0 + i * bwd + 0.01f, x0 + (i + 1) * bwd - 0.01f, boardsFrom, h, -0.03f, -0.008f, i % 2 == 0 ? Wood : Shade(Wood, 0.88f));
        }

        static void Cabinet(PB pb, Vector3 S)
        {
            var mb = pb.s;
            mb.Box(Vector3.zero, S - Vector3.one * 0.04f, Wood, Quaternion.identity);
            mb.Box(V(0, S.y * 0.5f - 0.04f, 0), V(S.x + 0.06f, 0.08f, S.z + 0.06f), WoodD, Quaternion.identity);
            mb.Box(V(0, -S.y * 0.5f + 0.06f, 0), V(S.x + 0.02f, 0.12f, S.z + 0.02f), WoodD, Quaternion.identity);
            for (int dir = 0; dir < 4; dir++)
            {
                var f = Face(S, dir); if (f.W < 1.5f) continue;
                bool drawers = S.y < 2.2f;
                int n = Mathf.Max(1, Mathf.RoundToInt(f.W / (drawers ? 1.0f : 0.8f))); float w = f.W / n;
                for (int i = 0; i < n; i++)
                {
                    float x0 = i * w + 0.05f, x1 = (i + 1) * w - 0.05f;
                    if (drawers)
                        for (float y = 0.2f; y < S.y - 0.3f; y += 0.42f) { FB(mb, f, x0, x1, y, y + 0.36f, -0.02f, 0.025f, WoodL); FB(mb, f, (x0 + x1) * 0.5f - 0.12f, (x0 + x1) * 0.5f + 0.12f, y + 0.16f, y + 0.21f, 0.025f, 0.06f, Brass); }
                    else
                    {
                        FB(mb, f, x0, x1, 0.2f, S.y - 0.2f, -0.02f, 0.025f, WoodL);
                        FB(mb, f, x0 + 0.12f, x1 - 0.12f, 0.45f, S.y * 0.45f, 0.025f, 0.04f, Wood);
                        FB(mb, f, x0 + 0.12f, x1 - 0.12f, S.y * 0.52f, S.y - 0.45f, 0.025f, 0.04f, Wood);
                        FB(mb, f, x1 - 0.12f, x1 - 0.06f, S.y * 0.45f, S.y * 0.55f, 0.025f, 0.08f, Brass);
                    }
                }
            }
            // things on top
            float ty = S.y * 0.5f;
            mb.Box(V(-S.x * 0.25f, ty + 0.15f, 0), V(0.5f, 0.3f, 0.35f), WoodD, Quaternion.identity);
            mb.Prism(V(S.x * 0.2f, ty, 0.1f), 8, 0.09f, 0.09f, 0.28f, C(0.18f, 0.32f, 0.22f, 0.7f));
            mb.Prism(V(S.x * 0.2f, ty + 0.28f, 0.1f), 8, 0.04f, 0.04f, 0.1f, C(0.18f, 0.32f, 0.22f, 0.7f));
            for (int i = 0; i < 5; i++) mb.Box(V(S.x * 0.32f + i * 0.07f, ty + 0.14f, -0.2f), V(0.06f, 0.28f, 0.22f), i % 2 == 0 ? RedPaint : BlanketB, Quaternion.identity);
            var lp = V(-S.x * 0.38f, ty, -0.1f);
            mb.Prism(lp, 6, 0.12f, 0.12f, 0.05f, Brass);
            pb.g.Prism(lp + V(0, 0.05f, 0), 6, 0.08f, 0.08f, 0.16f, GLamp);
            mb.Prism(lp + V(0, 0.21f, 0), 6, 0.11f, 0.03f, 0.08f, Brass);
        }

        static void GunCarriage(PB pb, Vector3 S)
        {
            var mb = pb.s; float hx = S.x * 0.5f, hy = S.y * 0.5f, hz = S.z * 0.5f;
            mb.Box(V(0, -hy + 0.2f, 0), V(S.x, 0.4f, S.z), WoodD, Quaternion.identity);
            foreach (float z in new[] { -hz + 0.15f, hz - 0.15f }) mb.Box(V(0, 0.1f, z), V(S.x, S.y - 0.2f, 0.3f), Wood, Quaternion.identity);
            mb.Box(V(0, 0, 0), V(S.x - 0.2f, S.y - 0.3f, S.z - 0.62f), WoodD, Quaternion.identity);
            foreach (float x in new[] { -hx + 0.35f, hx - 0.35f }) foreach (float z in new[] { -hz + 0.05f, hz - 0.05f })
                    mb.Prism(V(x, -hy + 0.3f, z), 10, 0.3f, 0.3f, 0.1f * Mathf.Sign(z), Iron, Quaternion.FromToRotation(Vector3.up, Vector3.forward));
            mb.Box(V(0, hy - 0.06f, 0), V(S.x, 0.12f, S.z), Iron, Quaternion.identity);
        }

        static void GunBreech(PB pb, Vector3 S)
        {
            var mb = pb.s;
            mb.Box(Vector3.zero, S * 0.98f, Iron, Quaternion.identity);
            mb.Prism(V(0, 0, -S.z * 0.5f), 10, S.y * 0.28f, S.y * 0.28f, S.z, SteelD, Quaternion.FromToRotation(Vector3.up, Vector3.forward));
        }

        static void GunBarrel(PB pb, Vector3 S)
        {
            var mb = pb.s; float r = Mathf.Min(S.y, S.z) * 0.48f, hx = S.x * 0.5f;
            var q = Quaternion.FromToRotation(Vector3.up, Vector3.right);
            mb.Prism(V(-hx, 0, 0), 12, r, r * 0.82f, S.x * 0.92f, Iron, q);
            mb.Prism(V(hx - S.x * 0.08f, 0, 0), 12, r * 0.9f, r * 0.9f, S.x * 0.08f, Iron, q);
            mb.Prism(V(hx - 0.01f, 0, 0), 12, r * 0.45f, r * 0.45f, 0.02f, Black, q);
            foreach (float x in new[] { -hx + 0.3f, -hx + 1.1f, hx - 0.9f }) mb.Prism(V(x, 0, 0), 12, r * 1.04f, r * 1.04f, 0.14f, Brass, q);
            mb.Prism(V(-hx, 0, 0), 8, r * 0.35f, r * 0.2f, -0.25f, Iron, q);
        }

        static void FunnelCasing(PB pb, Vector3 S)
        {
            var mb = pb.s;
            mb.Box(Vector3.zero, S - Vector3.one * 0.06f, Steel, Quaternion.identity);
            for (int dir = 0; dir < 4; dir++)
            {
                var f = Face(S, dir);
                FB(mb, f, 0, f.W, 0, 0.35f, -0.04f, 0.02f, SteelD);
                FB(mb, f, 0, f.W, S.y - 0.25f, S.y, -0.04f, 0.03f, SteelD);
                Rivets(mb, f.P(0.2f, S.y - 0.4f, 0.01f), f.P(f.W - 0.2f, S.y - 0.4f, 0.01f), 0.4f, f.n, 0.06f, RivetC);
                for (float x = 0.6f; x < f.W - 0.6f; x += 1.3f) for (int i = 0; i < 7; i++) FB(mb, f, x, x + 0.8f, 0.9f + i * 0.2f, 0.9f + i * 0.2f + 0.08f, -0.03f, 0.0f, Black);
                if (dir == 0) FDoor(mb, pb.g, f, f.W * 0.5f, 1.0f, 2.2f, false);
            }
            mb.Box(V(0, S.y * 0.5f + 0.02f, 0), V(S.x - 0.4f, 0.04f, S.z - 0.4f), SteelD, Quaternion.identity);
        }

        static void GunTub(PB pb, Vector3 S)
        {
            var mb = pb.s;
            mb.Box(Vector3.zero, S - Vector3.one * 0.06f, Steel2, Quaternion.identity);
            for (int dir = 0; dir < 4; dir++)
            {
                var f = Face(S, dir);
                FB(mb, f, 0, f.W, 0, 0.3f, -0.04f, 0.02f, SteelD);
                FB(mb, f, 0, f.W, S.y - 0.3f, S.y, -0.04f, 0.04f, SteelD);
                for (float x = f.W / 3f; x < f.W - 0.1f; x += f.W / 3f) FB(mb, f, x - 0.035f, x + 0.035f, 0.3f, S.y - 0.3f, -0.03f, 0.012f, SteelD);
                Rivets(mb, f.P(0.2f, S.y - 0.45f, 0.01f), f.P(f.W - 0.2f, S.y - 0.45f, 0.01f), 0.35f, f.n, 0.06f, RivetC);
                Rivets(mb, f.P(0.2f, 0.45f, 0.01f), f.P(f.W - 0.2f, 0.45f, 0.01f), 0.35f, f.n, 0.06f, RivetC);
                if (dir == 0) for (float y = 0.4f; y < S.y - 0.2f; y += 0.35f) FB(mb, f, f.W * 0.5f - 0.3f, f.W * 0.5f + 0.3f, y, y + 0.05f, 0f, 0.1f, Iron);
                if (dir == 0) { FB(mb, f, f.W * 0.5f - 0.33f, f.W * 0.5f - 0.27f, 0.2f, S.y, 0f, 0.12f, Iron); FB(mb, f, f.W * 0.5f + 0.27f, f.W * 0.5f + 0.33f, 0.2f, S.y, 0f, 0.12f, Iron); }
            }
            float ty = S.y * 0.5f;
            mb.Box(V(0, ty + 0.01f, 0), V(S.x - 0.3f, 0.03f, S.z - 0.3f), SteelD, Quaternion.identity);
            foreach (var p in new[] { V(-S.x * 0.32f, 0, S.z * 0.3f), V(S.x * 0.32f, 0, S.z * 0.3f) })
                mb.Box(p + V(0, ty + 0.2f, 0), V(0.8f, 0.4f, 0.5f), CanvasG, Quaternion.identity);
        }

        static void Skylight(PB pb, Vector3 S)
        {
            var mb = pb.s; float hx = S.x * 0.5f, hy = S.y * 0.5f, hz = S.z * 0.5f, eave = 1.8f - hy;
            bool alongZ = S.z >= S.x; float W = alongZ ? S.x : S.z, Lg = alongZ ? S.z : S.x;
            var R = alongZ ? Quaternion.identity : Quaternion.Euler(0, 90, 0);
            mb.Box(V(0, (eave - hy) * 0.5f, 0), (alongZ ? V(W, eave + hy, Lg) : V(Lg, eave + hy, W)) - Vector3.one * 0.04f, WoodD, Quaternion.identity);
            for (int dir = 0; dir < 4; dir++)
            {
                var f = Face(S, dir);
                for (float y = 0.3f; y < 1.8f - 0.05f; y += 0.3f) FB(mb, f, 0.05f, f.W - 0.05f, y, y + 0.28f, -0.03f, 0.0f, Shade(Wood, 0.92f + 0.12f * Hash(dir, Mathf.RoundToInt(y * 10))));
                FB(mb, f, 0, f.W, 0, 0.3f, -0.03f, 0.02f, SteelD);
                FB(mb, f, 0, 0.12f, 0, 1.8f, -0.03f, 0.03f, Steel); FB(mb, f, f.W - 0.12f, f.W, 0, 1.8f, -0.03f, 0.03f, Steel);
            }
            // gabled glass roof along the long axis
            for (int s = -1; s <= 1; s += 2)
            {
                var a = R * V(s * W * 0.5f, eave, -Lg * 0.5f); var b = R * V(s * W * 0.5f, eave, Lg * 0.5f); var c = R * V(0, hy, Lg * 0.5f); var d = R * V(0, hy, -Lg * 0.5f);
                mb.Quad(a, b, c, d, Glass, R * V(s, 1, 0));
                for (float t = 0; t <= Lg + 0.01f; t += 1.0f)
                {
                    var p0 = R * V(s * W * 0.5f, eave + 0.03f, -Lg * 0.5f + t); var p1 = R * V(0, hy + 0.03f, -Lg * 0.5f + t);
                    mb.Rod(p0, p1, 0.07f, Brass);
                }
            }
            mb.Box(V(0, hy, 0), V(0.18f, 0.12f, Lg + 0.1f), Brass, R);
            foreach (int s in new[] { -1, 1 })
                mb.ExtrudePoly(new[] { R * V(-W * 0.5f, eave, s * Lg * 0.5f), R * V(W * 0.5f, eave, s * Lg * 0.5f), R * V(0, hy, s * Lg * 0.5f) }, R * V(0, 0, -s), 0.08f, WoodD);
        }

        static void Deckhouse(PB pb, Vector3 S)
        {
            var mb = pb.s; var g = pb.g;
            mb.Box(Vector3.zero, S - Vector3.one * 0.08f, WoodD, Quaternion.identity);
            for (int dir = 0; dir < 4; dir++)
            {
                var f = Face(S, dir); float W = f.W;
                FB(mb, f, 0, W, 0, 0.6f, -0.05f, 0.03f, SteelD);
                for (float x = 0; x < W - 0.01f; x += 0.3f) FB(mb, f, x, Mathf.Min(x + 0.29f, W), 0.6f, S.y - 0.7f, -0.05f, -0.004f - 0.01f * Hash(Mathf.RoundToInt(x * 10), dir + 7), Shade(Color.Lerp(Wood, WoodL, Hash(dir + 3, Mathf.RoundToInt(x * 10)) * 0.4f), 0.97f));
                FB(mb, f, 0, W, S.y - 0.7f, S.y, -0.05f, 0.05f, Steel);
                FB(mb, f, -0.06f, W + 0.06f, S.y - 0.15f, S.y, -0.05f, 0.14f, SteelD);
                FB(mb, f, 0, 0.15f, 0, S.y, -0.05f, 0.06f, Steel); FB(mb, f, W - 0.15f, W, 0, S.y, -0.05f, 0.06f, Steel);
                if (W > 6f)
                {
                    float[] doors = { W * 0.25f, W * 0.75f };
                    foreach (float d in doors) FDoor(mb, g, f, d, 1.1f, 2.3f, false);
                    for (float x = 1.2f; x < W - 0.8f; x += 2.4f) if (doors.All(d => Mathf.Abs(d - x) > 1.0f)) Porthole(mb, f.P(x, 2.7f, 0f), f.n, 0.34f);
                    Lifebuoy(mb, f.P(W * 0.5f, 2.6f, 0.05f), f.n);
                }
                else
                {
                    FB(mb, f, W * 0.5f - 0.35f, W * 0.5f + 0.35f, 1.0f, 2.2f, 0f, 0.12f, RedPaint);
                    mb.Prism(f.P(W * 0.5f, 1.6f, 0.15f), 10, 0.28f, 0.28f, 0.15f, RedPaint, Quaternion.FromToRotation(Vector3.up, f.n));
                }
            }
            mb.Box(V(0, S.y * 0.5f + 0.03f, 0), V(S.x - 0.25f, 0.06f, S.z - 0.25f), Canvas, Quaternion.identity);
            for (float x = -S.x * 0.5f + 2f; x < S.x * 0.5f - 1f; x += 4.5f) { mb.Prism(V(x, S.y * 0.5f, 0), 8, 0.22f, 0.2f, 0.45f, Steel); mb.Prism(V(x, S.y * 0.5f + 0.45f, 0), 8, 0.42f, 0.05f, 0.2f, Steel); }
        }

        static void CargoStack(PB pb, Vector3 S)
        {
            var mb = pb.s;
            int nx = Mathf.Max(1, Mathf.RoundToInt(S.x / 2.4f)), nz = Mathf.Max(1, Mathf.RoundToInt(S.z / 2.4f)), ny = Mathf.Max(1, Mathf.RoundToInt(S.y / 1.5f));
            var cell = V(S.x / nx, S.y / ny, S.z / nz);
            for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) for (int k = 0; k < nz; k++)
                    {
                        var c = V(-S.x * 0.5f + cell.x * (i + 0.5f), -S.y * 0.5f + cell.y * (j + 0.5f), -S.z * 0.5f + cell.z * (k + 0.5f));
                        int v = (i * 7 + j * 3 + k * 5) % 4;
                        Crate(mb, c, cell - Vector3.one * 0.03f, v == 0 ? Wood : v == 1 ? WoodL : v == 2 ? Shade(Wood, 0.85f) : CanvasG, v != 3);
                    }
            // lashing straps
            foreach (float x in new[] { -S.x * 0.25f, S.x * 0.25f }) { mb.Box(V(x, S.y * 0.5f + 0.012f, 0), V(0.1f, 0.025f, S.z), RopeD, Quaternion.identity); foreach (float z in new[] { -1f, 1f }) mb.Box(V(x, 0, z * (S.z * 0.5f + 0.012f)), V(0.1f, S.y, 0.025f), RopeD, Quaternion.identity); }
            mb.Box(V(-S.x * 0.18f, S.y * 0.5f + 0.03f, -S.z * 0.12f), V(S.x * 0.6f, 0.05f, S.z * 0.7f), Canvas, Quaternion.identity);
        }

        static void Crate(MB mb, Vector3 c, Vector3 s, Color col, bool brace)
        {
            mb.Box(c, s - Vector3.one * 0.05f, col, Quaternion.identity);
            var h = s * 0.5f; float t = Mathf.Min(0.1f, Mathf.Min(s.x, Mathf.Min(s.y, s.z)) * 0.12f);
            var dark = Shade(col, 0.62f);
            for (int a = -1; a <= 1; a += 2) for (int b = -1; b <= 1; b += 2)
                {
                    mb.Box(c + V(a * (h.x - t * 0.5f), 0, b * (h.z - t * 0.5f)), V(t, s.y, t), dark, Quaternion.identity);
                    mb.Box(c + V(0, a * (h.y - t * 0.5f), b * (h.z - t * 0.5f)), V(s.x, t, t), dark, Quaternion.identity);
                    mb.Box(c + V(a * (h.x - t * 0.5f), b * (h.y - t * 0.5f), 0), V(t, t, s.z), dark, Quaternion.identity);
                }
            if (brace)
                for (int a = -1; a <= 1; a += 2)
                {
                    var p0 = c + V(-h.x + t, -h.y + t, a * (h.z - 0.01f)); var p1 = c + V(h.x - t, h.y - t, a * (h.z - 0.01f));
                    mb.Rod(p0, p1, t * 0.8f, dark);
                    var q0 = c + V(a * (h.x - 0.01f), -h.y + t, -h.z + t); var q1 = c + V(a * (h.x - 0.01f), h.y - t, h.z - t);
                    mb.Rod(q0, q1, t * 0.8f, dark);
                }
        }

        static Mesh ExplosiveCrateMesh()
        {
            var mb = new MB();
            var red = C(0.62f, 0.17f, 0.12f, 0.08f);
            Crate(mb, Vector3.zero, Vector3.one, red, false);
            mb.Box(Vector3.zero, V(0.97f, 0.14f, 0.97f), Yellow, Quaternion.identity);
            foreach (var n in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                var r = Quaternion.LookRotation(n, Vector3.up);
                mb.Box(n * 0.485f + V(0, 0.24f, 0), V(0.34f, 0.2f, 0.02f), Black, r);
                mb.Box(n * 0.49f + V(0, 0.24f, 0), V(0.22f, 0.08f, 0.02f), Yellow, r);
            }
            return SaveMesh(mb.Done("Ship_ExplosiveCrate"), "Props/Ship_ExplosiveCrate");
        }

        // ---------- Environment ----------

        static string BuildEnvironment(UnityEngine.SceneManagement.Scene scene)
        {
            var go = new GameObject(EnvRootName);
            Undo.RegisterCreatedObjectUndo(go, "Ship environment");
            var env = go.transform;
            env.SetPositionAndRotation(style.position, style.rotation);

            // Ocean: 3 m triangle grid within 300 m (hole where the hull is), flat far ring; foam collar around the hull.
            var om = OceanMesh();
            var ogo = new GameObject("Ocean"); ogo.transform.SetParent(env, false); ogo.transform.localPosition = V(0, WaterY, 0);
            ogo.AddComponent<MeshFilter>().sharedMesh = om; var omr = ogo.AddComponent<MeshRenderer>(); omr.sharedMaterial = oceanMat; omr.shadowCastingMode = ShadowCastingMode.Off;
            SceneMesh(env, "Hull Foam", FoamMesh(), solid, false).transform.localPosition = Vector3.zero;
            var isl = new MB();
            Islet(isl, V(-560f, 0, 420f), 70f, 26f, 11); Islet(isl, V(-470f, 0, 520f), 34f, 14f, 12);
            Islet(isl, V(700f, 0, -520f), 95f, 38f, 13); Islet(isl, V(260f, 0, 880f), 60f, 20f, 14); Islet(isl, V(-980f, 0, -360f), 120f, 46f, 15);
            SceneMesh(env, "Islets", isl, solid, true).transform.localPosition = V(0, WaterY - 0.6f, 0);

            // Sun: the scene's Directional Light.
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional && l.gameObject.scene == scene);
            if (sun != null)
            {
                Undo.RecordObject(sun, "Ship sun"); Undo.RecordObject(sun.transform, "Ship sun");
                sun.transform.rotation = Quaternion.Euler(36f, style.eulerAngles.y + 128f, 0f);
                sun.color = new Color(1f, 0.90f, 0.76f); sun.intensity = 1.5f; sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.85f;
            }
            var getter = typeof(RenderSettings).GetMethod("GetRenderSettings", BindingFlags.NonPublic | BindingFlags.Static);
            if (getter != null && getter.Invoke(null, null) is Object rs) Undo.RecordObject(rs, "Ship environment");
            if (sun != null) RenderSettings.sun = sun;
            RenderSettings.skybox = skyMat;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.50f, 0.60f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.55f, 0.58f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.27f, 0.30f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.66f, 0.76f, 0.84f);
            RenderSettings.fogStartDistance = 140f; RenderSettings.fogEndDistance = 1250f;
            DynamicGI.UpdateEnvironment();

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
            var tm = Comp<Tonemapping>(profile); tm.mode.Override(TonemappingMode.Neutral);
            var ca = Comp<ColorAdjustments>(profile);
            ca.postExposure.Override(0f); ca.contrast.Override(13f); ca.saturation.Override(10f); ca.colorFilter.Override(new Color(1f, 0.98f, 0.94f));
            var bl = Comp<Bloom>(profile); bl.threshold.Override(0.95f); bl.intensity.Override(0.6f); bl.scatter.Override(0.68f); bl.tint.Override(new Color(1f, 0.86f, 0.70f));
            var vg = Comp<Vignette>(profile); vg.color.Override(new Color(0.05f, 0.08f, 0.12f)); vg.intensity.Override(0.24f); vg.smoothness.Override(0.45f);
            var sp = Comp<SplitToning>(profile); sp.shadows.Override(new Color(0.36f, 0.48f, 0.62f)); sp.highlights.Override(new Color(1f, 0.84f, 0.62f)); sp.balance.Override(-10f);
            var smh = Comp<ShadowsMidtonesHighlights>(profile); smh.shadows.Override(new Vector4(0.94f, 0.98f, 1.06f, -0.02f)); smh.highlights.Override(new Vector4(1.04f, 1.0f, 0.95f, 0f));
            EditorUtility.SetDirty(profile);
            var vgo = new GameObject("Post Processing"); vgo.transform.SetParent(env, false);
            var v = vgo.AddComponent<Volume>(); v.isGlobal = true; v.priority = 1f; v.sharedProfile = profile;

            var cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = Undo.AddComponent<UniversalAdditionalCameraData>(cam.gameObject);
                Undo.RecordObject(data, "Camera post"); Undo.RecordObject(cam, "Camera far");
                data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                if (cam.farClipPlane < 1500f) cam.farClipPlane = 1500f;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.GetComponent<Volume>() == null) t.gameObject.isStatic = true;
            return "Environment: ocean (y " + WaterY + " ship frame), islets, sun, Ship_Sky skybox, trilight ambient, linear fog 140-1250, volume " + ProfilePath + ".";
        }

        static Mesh OceanMesh()
        {
            var verts = new List<Vector3>(); var tris = new List<int>();
            const float cell = 3f; const int N = 100; // +-300 m
            var idx = new Dictionary<long, int>();
            int Vx(int i, int j) { long key = (long)(i + 1000) * 4000 + (j + 1000); if (idx.TryGetValue(key, out int id)) return id; id = verts.Count; verts.Add(V(i * cell, 0, j * cell)); idx[key] = id; return id; }
            for (int i = -N; i < N; i++)
                for (int j = -N; j < N; j++)
                {
                    float x0 = i * cell, z0 = j * cell;
                    if (x0 >= -48f && x0 + cell <= 42f && z0 >= -21f && z0 + cell <= 21f) continue;
                    int a = Vx(i, j), b = Vx(i + 1, j), c = Vx(i + 1, j + 1), d = Vx(i, j + 1);
                    if (((i + j) & 1) == 0) { tris.Add(a); tris.Add(d); tris.Add(c); tris.Add(a); tris.Add(c); tris.Add(b); }
                    else { tris.Add(a); tris.Add(d); tris.Add(b); tris.Add(b); tris.Add(d); tris.Add(c); }
                }
            // far flat ring (4 rectangles) out to 3 km
            float r0 = N * cell, r1 = 3000f;
            void Rect(float xa, float za, float xb, float zb) { int s = verts.Count; verts.Add(V(xa, 0, za)); verts.Add(V(xb, 0, za)); verts.Add(V(xb, 0, zb)); verts.Add(V(xa, 0, zb)); tris.Add(s); tris.Add(s + 3); tris.Add(s + 2); tris.Add(s); tris.Add(s + 2); tris.Add(s + 1); }
            Rect(-r1, -r1, r1, -r0); Rect(-r1, r0, r1, r1); Rect(-r1, -r0, -r0, r0); Rect(r0, -r0, r1, r0);
            var m = new Mesh { name = "Ship_Ocean", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetTriangles(tris, 0);
            var n = new Vector3[verts.Count]; for (int i = 0; i < n.Length; i++) n[i] = Vector3.up; m.normals = n;
            m.RecalculateBounds(); m.bounds = new Bounds(Vector3.zero, new Vector3(2 * r1, 20f, 2 * r1));
            vertTotal += verts.Count;
            return SaveMesh(m, "Scene/Ship_Ocean");
        }

        static MB FoamMesh()
        {
            var mb = new MB(); var pts = new List<Vector3>();
            // outline of the hull at the waterline (ship frame), going round
            float y = WaterY;
            for (int i = 0; i <= 12; i++) { float u = i / 12f; float x = MidFwd + u * (StemX(y) - MidFwd); pts.Add(V(x, y, HW * (1f - Mathf.Pow(u, BowP(y))))); }
            for (int i = 12; i >= 0; i--) { float u = i / 12f; float x = MidFwd + u * (StemX(y) - MidFwd); pts.Add(V(x, y, -HW * (1f - Mathf.Pow(u, BowP(y))))); }
            pts.Add(V(-20f, y, -HW)); pts.Add(V(MidAft, y, -HW));
            pts.Add(V(SternX(y), y, -SternEndY(y))); pts.Add(V(SternX(y), y, SternEndY(y)));
            pts.Add(V(MidAft, y, HW)); pts.Add(V(-20f, y, HW)); pts.Add(V(MidFwd, y, HW));
            var cen = V(0, y, 0);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1]; float len = Vector3.Distance(a, b); int n = Mathf.Max(1, Mathf.CeilToInt(len / 2.2f));
                for (int k = 0; k < n; k++)
                {
                    var p0 = Vector3.Lerp(a, b, k / (float)n); var p1 = Vector3.Lerp(a, b, (k + 1) / (float)n);
                    var outw = Vector3.Cross(Vector3.up, p1 - p0).normalized; if (Vector3.Dot(outw, (p0 + p1) * 0.5f - cen) < 0) outw = -outw;
                    float w0 = 0.6f + 1.1f * Hash(i * 31 + k, 1), w1 = 0.6f + 1.1f * Hash(i * 31 + k + 1, 1);
                    var q0 = p0 - outw * 0.1f + V(0, 0.06f, 0); var q1 = p1 - outw * 0.1f + V(0, 0.06f, 0);
                    mb.Quad(q0, q1, q1 + outw * w1, q0 + outw * w0, Hash(i, k) > 0.4f ? Foam : FoamD, Vector3.up);
                }
            }
            return mb;
        }
        static float SternEndY(float y) => y >= 6f ? HW - 1.4f : HW * 0.6f;

        static void Islet(MB mb, Vector3 c, float r, float h, int seed)
        {
            var rr = new System.Random(seed); int n = 11;
            float R(float a, float b) => a + (float)rr.NextDouble() * (b - a);
            var ring0 = new Vector3[n]; var ring1 = new Vector3[n]; var ring2 = new Vector3[n]; var ring3 = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n + R(-0.15f, 0.15f); var d = V(Mathf.Cos(a), 0, Mathf.Sin(a));
                ring0[i] = c + d * r * R(1.05f, 1.2f) + V(0, -1.5f, 0);
                ring1[i] = c + d * r * R(0.92f, 1.02f) + V(0, R(0.6f, 1.4f), 0);
                ring2[i] = c + d * r * R(0.55f, 0.75f) + V(0, h * R(0.45f, 0.7f), 0);
                ring3[i] = c + d * r * R(0.15f, 0.3f) + V(0, h * R(0.85f, 1.0f), 0);
            }
            var top = c + V(R(-r, r) * 0.1f, h * 1.08f, R(-r, r) * 0.1f);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                mb.Quad(ring0[i], ring0[j], ring1[j], ring1[i], Sand, (ring0[i] + ring1[j]) * 0.5f - c);
                mb.Quad(ring1[i], ring1[j], ring2[j], ring2[i], (i % 3 == 0) ? RockD : Rock, (ring1[i] + ring2[j]) * 0.5f - c);
                mb.Quad(ring2[i], ring2[j], ring3[j], ring3[i], (i % 2 == 0) ? Grass : GrassD, (ring2[i] + ring3[j]) * 0.5f - c);
                mb.Tri(ring3[i], ring3[j], top, (i % 2 == 0) ? GrassD : Grass, (ring3[i] + ring3[j] + top) / 3f - c);
            }
        }

        static T Comp<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (!p.TryGet<T>(out var c))
            {
                c = p.Add<T>(false); c.name = typeof(T).Name; c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, p);
            }
            c.active = true;
            return c;
        }

        // ---------- Materials ----------

        static void MakeMaterials()
        {
            EnsureFolder(MatDir);
            var baseSolid = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/StyleTest/Stylized_Solid.mat");
            var baseGlow = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/StyleTest/Stylized_Glow.mat");
            solid = CopyMat("Ship_Solid", baseSolid);
            solid.SetFloat("_GradBottom", -38f); solid.SetFloat("_GradTop", 10f); solid.SetFloat("_TileSize", 0f);
            solid.SetColor("_ShadowColor", new Color(0.42f, 0.40f, 0.58f)); solid.SetColor("_BottomTint", new Color(0.82f, 0.82f, 0.9f));
            inner = CopyMat("Ship_Interior", solid);
            inner.SetFloat("_AmbientStrength", 0.75f); inner.SetFloat("_RimStrength", 0.08f); inner.SetColor("_ShadowColor", new Color(0.45f, 0.40f, 0.42f));
            glow = CopyMat("Ship_Glow", baseGlow);
            var surf = Shader.Find("CoopGame/Stylized Ship Surface");
            steelSurf = SurfMat("Ship_Surface_Steel", surf, m =>
            {
                m.SetFloat("_AmbientStrength", 0.8f); m.SetFloat("_PlankW", 1.28f); m.SetColor("_ShadowColor", new Color(0.42f, 0.38f, 0.42f));
                m.SetColor("_WallA", new Color(0.43f, 0.44f, 0.39f)); m.SetColor("_WallB", new Color(0.39f, 0.41f, 0.36f));
                m.SetColor("_SeamColor", new Color(0.27f, 0.27f, 0.25f)); m.SetColor("_RivetColor", new Color(0.52f, 0.50f, 0.44f));
                m.SetFloat("_PlateW", 3.6f); m.SetFloat("_PlateH", 1.8f); m.SetFloat("_RivetStep", 0.42f);
                m.SetColor("_CeilA", new Color(0.30f, 0.30f, 0.28f)); m.SetColor("_CeilB", new Color(0.27f, 0.27f, 0.25f)); m.SetFloat("_CeilPlate", 3f);
                m.SetColor("_TrimColor", new Color(0.24f, 0.23f, 0.21f));
            });
            deckSurf = SurfMat("Ship_Surface_Deck", surf, m => { m.SetFloat("_AmbientStrength", 1f); m.SetFloat("_PlankW", 1.28f); m.SetColor("_FloorA", new Color(0.60f, 0.48f, 0.34f)); m.SetColor("_FloorB", new Color(0.52f, 0.40f, 0.27f)); m.SetColor("_WallA", new Color(0.20f, 0.23f, 0.26f)); m.SetColor("_WallB", new Color(0.18f, 0.21f, 0.24f)); });
            grateSurf = SurfMat("Ship_Surface_Grate", surf, m =>
            {
                m.SetFloat("_AmbientStrength", 0.8f); m.SetFloat("_FloorMode", 1f);
                m.SetColor("_FloorA", new Color(0.48f, 0.46f, 0.42f)); m.SetColor("_FloorB", new Color(0.43f, 0.41f, 0.38f));
                m.SetColor("_WallA", new Color(0.30f, 0.30f, 0.28f)); m.SetColor("_WallB", new Color(0.27f, 0.27f, 0.25f)); m.SetColor("_ShadowColor", new Color(0.42f, 0.38f, 0.42f));
                m.SetColor("_TrimColor", new Color(0.62f, 0.46f, 0.22f)); m.SetFloat("_TrimShine", 0.7f); m.SetFloat("_TrimW", 0.08f);
                m.SetFloat("_PlateW", 1.25f); m.SetFloat("_PlateH", 0.6f);
            });
            string op = MatDir + "/Ship_Ocean.mat";
            oceanMat = AssetDatabase.LoadAssetAtPath<Material>(op);
            if (oceanMat == null) { oceanMat = new Material(Shader.Find("CoopGame/Stylized Ocean")); AssetDatabase.CreateAsset(oceanMat, op); }
            oceanMat.shader = Shader.Find("CoopGame/Stylized Ocean");
            oceanMat.SetFloat("_SpecStrength", 0.7f); oceanMat.SetFloat("_Gloss", 170f); oceanMat.SetFloat("_FoamLevel", 0.9f);
            EditorUtility.SetDirty(oceanMat);
            string sp = MatDir + "/Ship_Sky.mat";
            skyMat = AssetDatabase.LoadAssetAtPath<Material>(sp);
            if (skyMat == null) { skyMat = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(skyMat, sp); }
            skyMat.SetFloat("_SunDisk", 2f); skyMat.SetFloat("_SunSize", 0.035f); skyMat.SetFloat("_SunSizeConvergence", 6f);
            skyMat.SetFloat("_AtmosphereThickness", 0.75f); skyMat.SetColor("_SkyTint", new Color(0.46f, 0.58f, 0.74f));
            skyMat.SetColor("_GroundColor", new Color(0.30f, 0.42f, 0.50f)); skyMat.SetFloat("_Exposure", 1.3f);
            EditorUtility.SetDirty(skyMat);
            foreach (var m in new[] { solid, inner, glow, steelSurf, deckSurf, grateSurf, oceanMat, skyMat }) { EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); }
        }

        static Material SurfMat(string name, Shader sh, System.Action<Material> setup)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh; setup(m); EditorUtility.SetDirty(m);
            return m;
        }

        static Material CopyMat(string name, Material src)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); } else m.CopyPropertiesFromMaterial(src);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- Prefabs, meshes, objects ----------

        class PB
        {
            public readonly MB s = new MB(), g = new MB();
            public readonly List<(Vector3 p, float range, float intensity)> lights = new List<(Vector3, float, float)>();
            public readonly List<(Vector3 c, Vector3 size, Quaternion r)> boxes = new List<(Vector3, Vector3, Quaternion)>();
            public readonly List<(Vector3 c, float r, float h)> capsules = new List<(Vector3, float, float)>();
            public bool interior;
        }

        static GameObject Prefab(string folder, string name, System.Action<PB> build)
        {
            string path = PrefabDir + "/" + folder + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && (!rebuildPrefabs || builtPrefabs.Contains(path))) return existing;
            var pb = new PB(); build(pb);
            var go = new GameObject(name);
            if (!pb.s.Empty) MeshChild(go.transform, "Mesh", pb.s, folder + "/Ship_" + name, pb.interior ? inner : solid, true);
            if (!pb.s.Empty && !pb.g.Empty) pb.g.FixZFight(pb.s);
            if (!pb.g.Empty) MeshChild(go.transform, "Glow", pb.g, folder + "/Ship_" + name + "_Glow", glow, false);
            int li = 0;
            foreach (var (p, range, intensity) in pb.lights)
            {
                var lgo = new GameObject("Light" + (li++ > 0 ? " " + li : "")); lgo.transform.SetParent(go.transform, false); lgo.transform.localPosition = p;
                var l = lgo.AddComponent<Light>(); l.type = LightType.Point; l.color = LampLight; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
                if (lgo.GetComponent<UniversalAdditionalLightData>() == null) lgo.AddComponent<UniversalAdditionalLightData>();
            }
            int ci = 0;
            foreach (var (c, size, r) in pb.boxes)
            {
                if (r == Quaternion.identity) { var bc = go.AddComponent<BoxCollider>(); bc.center = c; bc.size = size; }
                else { var cgo = new GameObject("Collider " + (++ci)); cgo.transform.SetParent(go.transform, false); cgo.transform.localPosition = c; cgo.transform.localRotation = r; cgo.AddComponent<BoxCollider>().size = size; }
            }
            foreach (var (c, r, h) in pb.capsules) { var cc = go.AddComponent<CapsuleCollider>(); cc.center = c; cc.radius = r; cc.height = h; }
            var pf = SavePrefab(go, path);
            builtPrefabs.Add(path); prefabCount++;
            return pf;
        }

        static void MeshChild(Transform parent, string name, MB mb, string rel, Material mat, bool shadows)
        {
            var c = new GameObject(name); c.transform.SetParent(parent, false);
            var mesh = SaveMesh(mb.Done(Path.GetFileName(rel)), rel);
            vertTotal += mesh.vertexCount;
            c.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = c.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            if (!shadows) mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        static GameObject SceneMesh(Transform parent, string name, MB mb, Material mat, bool shadows)
        {
            var c = new GameObject(name); c.transform.SetParent(parent, false);
            var mesh = SaveMesh(mb.Done("Ship_Scene_" + name.Replace(' ', '_')), "Scene/Ship_Scene_" + name.Replace(' ', '_'));
            vertTotal += mesh.vertexCount;
            c.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = c.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            if (!shadows) mr.shadowCastingMode = ShadowCastingMode.Off;
            return c;
        }

        static Mesh SaveMesh(Mesh m, string rel)
        {
            string path = MeshDir + "/" + rel + ".asset";
            EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { string n = m.name; existing.Clear(); EditorUtility.CopySerialized(m, existing); existing.name = n; EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static GameObject SavePrefab(GameObject go, string path)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = t.GetComponent<Light>() == null;
            EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
            var pf = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return pf;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int k = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, k)); AssetDatabase.CreateFolder(path.Substring(0, k), path.Substring(k + 1));
        }

        static Transform Group(string name)
        {
            var t = style.Find(name); if (t != null) return t;
            t = new GameObject(name).transform; t.SetParent(style, false); return t;
        }

        static GameObject Place(GameObject prefab, Transform parent, Vector3 localPos, Quaternion localRot, string name = null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.localPosition = localPos; go.transform.localRotation = localRot;
            if (name != null) go.name = name;
            instanceCount++;
            return go;
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        static float Hash(int x, int y) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xffff) / 65535f; } }
        static float Rf(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        /// <summary>Flat-shaded, vertex-coloured mesh builder (every face has its own vertices).</summary>
        class MB
        {
            readonly List<Vector3> v = new List<Vector3>(); readonly List<Color32> c = new List<Color32>(); readonly List<int> t = new List<int>();
            public bool Empty => v.Count == 0;
            // primitive id per vertex (one box, prism, loft... = one primitive), used to settle coplanar faces in FixZFight
            readonly List<int> pid = new List<int>(); int depth, cur, next;
            void Begin() { if (depth++ == 0) cur = next++; }
            void End() { depth--; }
            int Id() => depth > 0 ? cur : next++;

            public void Tri(Vector3 a, Vector3 b, Vector3 d, Color col, Vector3 outward)
            {
                var cr = Vector3.Cross(b - a, d - a); if (cr.sqrMagnitude < 1e-10f) return;
                if (Vector3.Dot(cr, outward) < 0) { var x = b; b = d; d = x; }
                int i = v.Count, id = Id(); pid.Add(id); pid.Add(id); pid.Add(id); v.Add(a); v.Add(b); v.Add(d); Color32 cc = col; c.Add(cc); c.Add(cc); c.Add(cc); t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col, Vector3 n)
            {
                var c1 = Vector3.Cross(b - a, cc - a); var c2 = Vector3.Cross(cc - a, d - a);
                if (c1.sqrMagnitude < 1e-10f && c2.sqrMagnitude < 1e-10f) return;
                if (c1.sqrMagnitude < 1e-10f) { Tri(a, cc, d, col, n); return; }
                if (c2.sqrMagnitude < 1e-10f) { Tri(a, b, cc, col, n); return; }
                if (Vector3.Dot(c1 + c2, n) < 0) { var x = b; b = d; d = x; }
                int i = v.Count, id = Id(); pid.Add(id); pid.Add(id); pid.Add(id); pid.Add(id); v.Add(a); v.Add(b); v.Add(cc); v.Add(d); Color32 k = col; c.Add(k); c.Add(k); c.Add(k); c.Add(k);
                t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            public void Box(Vector3 cen, Vector3 s, Color col, Quaternion r, int skip = 0)
            {
                Begin();
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
                End();
            }

            public void Prism(Vector3 b, int sides, float r0, float r1, float h, Color col, float startDeg = 0f) => Prism(b, sides, r0, r1, h, col, Quaternion.identity, startDeg);

            public void Prism(Vector3 b, int sides, float r0, float r1, float h, Color col, Quaternion r, float startDeg = 0f)
            {
                if (h < 0) { b += r * Vector3.up * h; h = -h; var x = r0; r0 = r1; r1 = x; }
                Vector3 up = r * Vector3.up, top = b + up * h;
                Vector3 P(int i, float rad, float y) { float a = startDeg * Mathf.Deg2Rad + i * Mathf.PI * 2f / sides; return b + r * new Vector3(Mathf.Sin(a) * rad, y, Mathf.Cos(a) * rad); }
                Vector3 Out(Vector3 m) => m - (b + up * Vector3.Dot(m - b, up));
                Begin();
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
                End();
            }

            public void Cyl(Vector3 a, Vector3 b, float r, int sides, Color col, float startDeg = 0f)
            {
                var d = b - a; if (d.sqrMagnitude < 1e-8f) return;
                Prism(a, sides, r, r, d.magnitude, col, Quaternion.FromToRotation(Vector3.up, d), startDeg);
            }

            public void Rod(Vector3 a, Vector3 b, float th, Color col)
            {
                var d = b - a; if (d.sqrMagnitude < 1e-6f) return;
                var up = Mathf.Abs(d.normalized.y) > 0.95f ? Vector3.forward : Vector3.up;
                Box((a + b) / 2, new Vector3(th, th, d.magnitude), col, Quaternion.LookRotation(d, up));
            }

            /// <summary>Side quads between two closed rings of the same size (outward from centre).</summary>
            public void Loft(Vector3[] lo, Vector3[] hi, Color col, Vector3 centre)
            {
                int n = lo.Length;
                Begin();
                for (int i = 0; i < n; i++) { int j = (i + 1) % n; var m = (lo[i] + lo[j] + hi[i] + hi[j]) * 0.25f; var o = m - centre; o.y = 0; Quad(lo[i], lo[j], hi[j], hi[i], col, o); }
                End();
            }

            public void Cap(Vector3[] ring, Color col, Vector3 n)
            {
                var cen = Vector3.zero; foreach (var p in ring) cen += p; cen /= ring.Length;
                Begin();
                for (int i = 0; i < ring.Length; i++) Tri(cen, ring[i], ring[(i + 1) % ring.Length], col, n);
                End();
            }

            public void ExtrudePoly(Vector3[] poly, Vector3 dir, float len, Color col)
            {
                dir = dir.normalized; var off = dir * len;
                var cen = Vector3.zero; foreach (var p in poly) cen += p; cen /= poly.Length;
                Begin();
                for (int i = 0; i < poly.Length; i++)
                {
                    var p0 = poly[i]; var p1 = poly[(i + 1) % poly.Length];
                    Tri(cen, p0, p1, col, -dir);
                    Tri(cen + off, p0 + off, p1 + off, col, dir);
                    var m = (p0 + p1) / 2 - cen; m -= dir * Vector3.Dot(m, dir);
                    Quad(p0, p1, p1 + off, p0 + off, col, m);
                }
                End();
            }

            /// <summary>
            /// Removes z-fighting: where faces of two different primitives lie in (almost) the same plane, face the same way,
            /// overlap and have different colours, the later primitive (the detail) is grown out of that plane by 4 mm
            /// (all its vertices on that plane move, so it stays closed). With <paramref name="under"/> given, this mesh's
            /// faces are lifted over the other mesh's faces instead (glow parts over the solid mesh). Returns the faces moved.
            /// </summary>
            public int FixZFight(MB under = null)
            {
                const float Tol = 0.003f, Lift = 0.004f, Shrink = 0.003f;
                int moved = 0;
                for (int pass = 0; pass < 12; pass++)
                {
                    var recs = new List<(Vector3 n, float d, int tri, bool mine)>();
                    var buckets = new Dictionary<(int, int, int, int), List<int>>();
                    void Add(MB m, bool mine)
                    {
                        for (int i = 0; i < m.t.Count; i += 3)
                        {
                            Vector3 a = m.v[m.t[i]], b = m.v[m.t[i + 1]], d = m.v[m.t[i + 2]];
                            var n = Vector3.Cross(b - a, d - a); float ar = n.magnitude; if (ar < 1e-7f) continue; n /= ar;
                            int r = recs.Count; recs.Add((n, Vector3.Dot(n, a), i, mine));
                            var key = (Mathf.RoundToInt(n.x * 60), Mathf.RoundToInt(n.y * 60), Mathf.RoundToInt(n.z * 60), Mathf.FloorToInt(recs[r].d / Tol));
                            if (!buckets.TryGetValue(key, out var l)) buckets[key] = l = new List<int>(); l.Add(r);
                        }
                    }
                    Add(this, true); if (under != null) Add(under, false);
                    MB Of((Vector3 n, float d, int tri, bool mine) q) => q.mine ? this : under;
                    Vector2[] Proj(int r, Vector3 u, Vector3 w)
                    {
                        var q = recs[r]; var m = Of(q); Vector3 a = m.v[m.t[q.tri]], b = m.v[m.t[q.tri + 1]], d = m.v[m.t[q.tri + 2]], cc = (a + b + d) / 3f;
                        Vector2 F(Vector3 p) { p += (cc - p).normalized * Shrink; return new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, w)); }
                        return new[] { F(a), F(b), F(d) };
                    }
                    bool Apart(Vector2[] A, Vector2[] B)
                    {
                        foreach (var P in new[] { A, B })
                            for (int k = 0; k < 3; k++)
                            {
                                var e = P[(k + 1) % 3] - P[k]; var ax = new Vector2(-e.y, e.x);
                                float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
                                foreach (var p in A) { float x = Vector2.Dot(p, ax); a0 = Mathf.Min(a0, x); a1 = Mathf.Max(a1, x); }
                                foreach (var p in B) { float x = Vector2.Dot(p, ax); b0 = Mathf.Min(b0, x); b1 = Mathf.Max(b1, x); }
                                if (a1 <= b0 || b1 <= a0) return true;
                            }
                        return false;
                    }
                    // (primitive, plane) -> normal, plane d of the winner, target d
                    var push = new Dictionary<(int, int, int, int, int), (Vector3 n, float d, float target)>();
                    foreach (var kv in buckets)
                    {
                        var k = kv.Key; var cand = new List<(int r, bool nb)>();
                        foreach (var r in kv.Value) cand.Add((r, false));
                        if (buckets.TryGetValue((k.Item1, k.Item2, k.Item3, k.Item4 + 1), out var up)) foreach (var r in up) cand.Add((r, true));
                        if (cand.Count < 2) continue;
                        var n0 = recs[cand[0].r].n; var u = Vector3.Cross(n0, Mathf.Abs(n0.y) < 0.9f ? Vector3.up : Vector3.right).normalized; var w = Vector3.Cross(n0, u);
                        var pr = new List<(float x0, float x1, int r, bool nb, Vector2[] P)>();
                        foreach (var (r, nb) in cand) { var P = Proj(r, u, w); pr.Add((Mathf.Min(P[0].x, Mathf.Min(P[1].x, P[2].x)), Mathf.Max(P[0].x, Mathf.Max(P[1].x, P[2].x)), r, nb, P)); }
                        pr.Sort((a, b) => a.x0.CompareTo(b.x0));
                        for (int i = 0; i < pr.Count; i++)
                            for (int j = i + 1; j < pr.Count && pr[j].x0 < pr[i].x1; j++)
                            {
                                if (pr[i].nb && pr[j].nb) continue;
                                var A = recs[pr[i].r]; var B = recs[pr[j].r];
                                if (!A.mine && !B.mine) continue;
                                if (Mathf.Abs(A.d - B.d) >= Tol || Vector3.Dot(A.n, B.n) < 0.9995f) continue;
                                MB ma = Of(A), mbb = Of(B); int ia = ma.t[A.tri], ib = mbb.t[B.tri];
                                int pa = ma.pid[ia], pb = mbb.pid[ib];
                                if (under == null) { if (pa == pb || ma.c[ia].Equals(mbb.c[ib])) continue; }
                                else if (A.mine == B.mine) continue;
                                if (Apart(pr[i].P, pr[j].P)) continue;
                                var win = under != null ? (A.mine ? A : B) : (pa > pb ? A : B);
                                var lose = win.Equals(A) ? B : A;
                                int wp = win.Equals(A) ? pa : pb;
                                var key = (wp, Mathf.RoundToInt(win.n.x * 60), Mathf.RoundToInt(win.n.y * 60), Mathf.RoundToInt(win.n.z * 60), Mathf.RoundToInt(win.d / 0.0005f));
                                float target = Mathf.Max(win.d, lose.d) + Lift;
                                if (!push.TryGetValue(key, out var cur0) || cur0.target < target) push[key] = (win.n, win.d, target);
                            }
                    }
                    if (push.Count == 0) break;
                    var byPrim = new Dictionary<int, List<int>>();
                    for (int i = 0; i < v.Count; i++) { if (!byPrim.TryGetValue(pid[i], out var l)) byPrim[pid[i]] = l = new List<int>(); l.Add(i); }
                    foreach (var kv in push)
                    {
                        var (n, d, target) = kv.Value; float dd = target - d;
                        foreach (int i in byPrim[kv.Key.Item1]) if (Mathf.Abs(Vector3.Dot(n, v[i]) - d) < 0.0006f) v[i] += n * dd;
                        moved++;
                    }
                }
                zFixCount += moved;
                return moved;
            }

            public Mesh Done(string name)
            {
                FixZFight();
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }
    }
}
