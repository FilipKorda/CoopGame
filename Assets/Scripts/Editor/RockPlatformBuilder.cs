using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Builds the rock look of <see cref="RockPlatform"/> for its size, and converts greybox cubes into rock platforms.
    /// Meshes are saved under Assets/Meshes/RockPlatforms, one per size + seed (same size = same mesh, shared).
    /// </summary>
    [InitializeOnLoad]
    public static class RockPlatformBuilder
    {
        const string MeshDir = "Assets/Meshes/RockPlatforms";
        const string MaterialPath = "Assets/Materials/StyleTest/Island_Solid.mat";
        const string PrefabPath = "Assets/Prefabs/StyleMountains/Mountain_Platform.prefab";

        static readonly Color[] Rock = { Hex("#6E5F55"), Hex("#7C6A5C"), Hex("#5F524A"), Hex("#857262") };
        static readonly Color CapA = Hex("#4E8F3E"), CapB = Hex("#5C9E46"), CapSide = Hex("#3F7A35"), BareTop = Hex("#8A7766");

        static RockPlatformBuilder() => RockPlatform.RebuildRequested = p => Rebuild(p);

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); c = c.linear; c.a = 0.05f; return c; }

        // ---------- Menu ----------

        [MenuItem("Tools/Coop Game/Rock Platforms/Convert Selected Cubes")]
        static void ConvertSelected()
        {
            int done = 0;
            foreach (var go in Selection.gameObjects)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name != "Cube" || go.GetComponent<RockPlatform>() != null) continue;
                Convert(go); done++;
            }
            Debug.Log($"Rock Platforms: converted {done} cube(s).");
        }

        [MenuItem("Tools/Coop Game/Rock Platforms/Convert Selected Cubes", true)]
        static bool ConvertSelectedValid() => Selection.gameObjects.Length > 0;

        [MenuItem("Tools/Coop Game/Rock Platforms/Convert Selected Cubes To Rock (walls, entrances)")]
        static void ConvertSelectedRock()
        {
            int done = 0;
            foreach (var go in Selection.gameObjects)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var existing = go.GetComponent<RockPlatform>();
                if (existing != null) { Undo.RecordObject(existing, "Rock Style"); existing.style = RockPlatformStyle.Rock; Rebuild(existing); PrefabUtility.RecordPrefabInstancePropertyModifications(existing); done++; continue; }
                if (mf.sharedMesh.name != "Cube") continue;
                var p = Convert(go).GetComponent<RockPlatform>(); p.style = RockPlatformStyle.Rock; Rebuild(p); PrefabUtility.RecordPrefabInstancePropertyModifications(p); done++;
            }
            Debug.Log($"Rock Platforms: {done} object(s) now in Rock style.");
        }

        [MenuItem("Tools/Coop Game/Rock Platforms/Convert Selected Cubes To Rock (walls, entrances)", true)]
        static bool ConvertSelectedRockValid() => Selection.gameObjects.Length > 0;

        [MenuItem("Tools/Coop Game/Rock Platforms/Rebuild All In Scene")]
        static void RebuildAll()
        {
            foreach (var p in Object.FindObjectsByType<RockPlatform>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Rebuild(p);
        }

        [MenuItem("GameObject/Coop Game/Rock Platform", false, 10)]
        static void Create(MenuCommand cmd)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObjectUtility.SetParentAndAlign(go, cmd.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create Rock Platform");
            Selection.activeGameObject = go;
        }

        /// <summary>Swaps a greybox cube (any rotation around Y) for a rock platform of the same box. Keeps name, parent and sibling order.</summary>
        public static GameObject Convert(GameObject cube)
        {
            var t = cube.transform;
            var s = t.lossyScale; var size = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, cube.scene);
            Undo.RegisterCreatedObjectUndo(go, "Convert To Rock Platform");
            go.name = cube.name;
            go.transform.SetParent(t.parent, false);
            go.transform.SetSiblingIndex(t.GetSiblingIndex());
            go.transform.SetPositionAndRotation(t.position + t.up * (size.y * 0.5f), t.rotation);
            go.transform.localScale = Vector3.one;
            go.layer = cube.layer; go.tag = cube.tag; go.isStatic = cube.isStatic;
            var p = go.GetComponent<RockPlatform>();
            p.size = size;
            p.seed = Mathf.Abs((int)(t.position.x * 73f) ^ (int)(t.position.z * 151f) ^ (int)(t.position.y * 37f)) % 1000 + 1;
            p.SyncCollider();
            Rebuild(p);
            PrefabUtility.RecordPrefabInstancePropertyModifications(p);
            Undo.DestroyObjectImmediate(cube);
            return go;
        }

        // ---------- Mesh ----------

        public static void Rebuild(RockPlatform p)
        {
            if (p == null || EditorUtility.IsPersistent(p)) return;   // prefab asset itself: keep its default mesh
            // Colliders: one box, or three round the tunnel.
            var boxes = p.GetComponents<BoxCollider>(); int want = p.tunnel ? 3 : 1;
            for (int i = boxes.Length; i < want; i++) Undo.AddComponent<BoxCollider>(p.gameObject);
            for (int i = boxes.Length - 1; i >= want; i--) Undo.DestroyObjectImmediate(boxes[i]);
            Mesh mesh;
            if (p.tunnel) { p.TunnelRect(out float ta, out float te, out float th); mesh = GetTunnelMesh(p.size, p.seed, p.grassTop && p.style != RockPlatformStyle.Rock, ta, te, th); }
            else mesh = GetMesh(p.size, p.seed, p.grassTop, p.style == RockPlatformStyle.Rock);
            var mf = p.GetComponent<MeshFilter>();
            if (mf.sharedMesh != mesh) { Undo.RecordObject(mf, "Rebuild Rock Platform"); mf.sharedMesh = mesh; PrefabUtility.RecordPrefabInstancePropertyModifications(mf); }
            var mr = p.GetComponent<MeshRenderer>();
            if (mr.sharedMaterial == null) mr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            p.SyncCollider();
        }

        public static Mesh GetMesh(Vector3 size, int seed, bool grass, bool rock = false)
        {
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Meshes", "RockPlatforms");
            string key = $"Rock4_{size.x:0.##}x{size.y:0.##}x{size.z:0.##}_s{seed}{(rock ? "_rock" : grass ? "" : "_bare")}".Replace(',', '.');
            string path = MeshDir + "/" + key + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            var mesh = Build(size, seed, grass && !rock, rock); mesh.name = key;
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        public static Mesh GetTunnelMesh(Vector3 size, int seed, bool grass, float a, float e, float th)
        {
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Meshes", "RockPlatforms");
            string key = $"RockTunnel_{size.x:0.##}x{size.y:0.##}x{size.z:0.##}_t{a:0.##}_{e:0.##}_{th:0.##}_s{seed}{(grass ? "" : "_bare")}".Replace(',', '.');
            string path = MeshDir + "/" + key + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            var mesh = BuildTunnel(size, seed, grass, a, e, th); mesh.name = key;
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // A block with a straight tunnel along Z. Every face is a grid (~1.2 m cells) whose inner vertices are pushed a
        // little INTO the block (faceted rock look); vertices on edges stay put, so faces meet with no gaps, and neither
        // the block nor the tunnel differs from its colliders. Flat lid on top; the top 0.5 m of the outer sides is grass.
        static Mesh BuildTunnel(Vector3 size, int seed, bool grass, float a, float e, float th)
        {
            var v = new List<Vector3>(); var nrm = new List<Vector3>(); var col = new List<Color>(); var tri = new List<int>();
            void Tri(Vector3 p0, Vector3 p1, Vector3 p2, Color c) { var nn = Vector3.Cross(p1 - p0, p2 - p0).normalized; int i = v.Count; v.Add(p0); v.Add(p1); v.Add(p2); nrm.Add(nn); nrm.Add(nn); nrm.Add(nn); col.Add(c); col.Add(c); col.Add(c); tri.Add(i); tri.Add(i + 1); tri.Add(i + 2); }
            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal, Color c) { if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normal) < 0f) { Tri(p0, p2, p1, c); Tri(p0, p3, p2, c); } else { Tri(p0, p1, p2, c); Tri(p0, p2, p3, c); } }
            float Hash(int x, int y, int f) { unchecked { int hh = x * 73856093 ^ y * 19349663 ^ f * 83492791 ^ seed * 2654435; hh ^= hh >> 13; hh *= 0x5bd1e995; hh ^= hh >> 15; return (hh & 0xffff) / 65535f; } }
            float[] Lines(float from, float to, params float[] must)
            {
                var l = new List<float> { from, to }; int k = Mathf.Max(1, Mathf.RoundToInt((to - from) / 1.2f));
                for (int i = 1; i < k; i++) l.Add(Mathf.Lerp(from, to, (float)i / k));
                foreach (var m0 in must) if (m0 > from + 0.05f && m0 < to - 0.05f) l.Add(m0);
                l.Sort();
                var mustSet = new List<float>(must) { from, to };
                for (int i = l.Count - 1; i > 0; i--) if (l[i] - l[i - 1] < 0.25f) l.RemoveAt(mustSet.Contains(l[i]) ? i - 1 : i);
                return l.ToArray();
            }
            float hx = size.x * 0.5f, hz = size.z * 0.5f, h = size.y, yT = -h + th;
            float maxIn = Mathf.Min(0.35f, Mathf.Min(Mathf.Min(a + hx, hx - e), Mathf.Min(size.z, h - th)) * 0.25f);
            int faceId = 0;
            // One face: origin + u*U[i] + w*W[j] with normal n. skip = cell left open; fixedV = vertex that must not
            // move (tunnel edge); green = cell coloured as grass.
            void Face(Vector3 origin, Vector3 u, Vector3 w, Vector3 n, float[] U, float[] W, System.Func<float, float, bool> skip, System.Func<float, float, bool> fixedV, System.Func<float, bool> green)
            {
                faceId++;
                var P = new Vector3[U.Length, W.Length];
                for (int i = 0; i < U.Length; i++) for (int j = 0; j < W.Length; j++)
                {
                    bool edge = i == 0 || j == 0 || i == U.Length - 1 || j == W.Length - 1 || fixedV(U[i], W[j]);
                    P[i, j] = origin + u * U[i] + w * W[j] - n * (edge ? 0f : Hash(i, j, faceId) * maxIn);
                }
                for (int i = 0; i + 1 < U.Length; i++) for (int j = 0; j + 1 < W.Length; j++)
                {
                    float cu = (U[i] + U[i + 1]) * 0.5f, cw = (W[j] + W[j + 1]) * 0.5f; if (skip(cu, cw)) continue;
                    Color c;
                    if (green(W[j + 1])) c = CapSide;
                    else { c = Rock[(int)(Hash(j / 2, 7, faceId) * Rock.Length) % Rock.Length]; float k = 0.94f + Hash(i, j, faceId + 50) * 0.12f; c = new Color(c.r * k, c.g * k, c.b * k, c.a); }
                    Quad(P[i, j], P[i + 1, j], P[i + 1, j + 1], P[i, j + 1], n, c);
                }
            }
            bool No(float x, float y) => false;
            bool Grass(float y) => grass && y > -0.5f + 0.01f;
            bool NoGrass(float y) => false;
            var Ys = Lines(-h, 0f, -0.5f, yT); var Zs = Lines(-hz, hz); var Xs = Lines(-hx, hx, a, e);
            bool InHole(float x, float y) => x > a && x < e && y < yT;
            bool HoleEdge(float x, float y) => ((Mathf.Abs(x - a) < 0.01f || Mathf.Abs(x - e) < 0.01f) && y <= yT + 0.01f) || (Mathf.Abs(y - yT) < 0.01f && x >= a - 0.01f && x <= e + 0.01f);
            Face(new Vector3(0, 0, -hz), Vector3.right, Vector3.up, Vector3.back, Xs, Ys, InHole, HoleEdge, Grass);       // front, with the hole
            Face(new Vector3(0, 0, hz), Vector3.right, Vector3.up, Vector3.forward, Xs, Ys, InHole, HoleEdge, Grass);     // back, with the hole
            Face(new Vector3(-hx, 0, 0), Vector3.forward, Vector3.up, Vector3.left, Zs, Ys, No, No, Grass);
            Face(new Vector3(hx, 0, 0), Vector3.forward, Vector3.up, Vector3.right, Zs, Ys, No, No, Grass);
            var Yt = Lines(-h, yT); var Xt = Lines(a, e);
            Face(new Vector3(a, 0, 0), Vector3.forward, Vector3.up, Vector3.right, Zs, Yt, No, No, NoGrass);   // tunnel walls and ceiling
            Face(new Vector3(e, 0, 0), Vector3.forward, Vector3.up, Vector3.left, Zs, Yt, No, No, NoGrass);
            Face(new Vector3(0, yT, 0), Vector3.right, Vector3.forward, Vector3.down, Xt, Zs, No, No, NoGrass);
            var lidC = grass ? CapA : Rock[3];
            Quad(new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz), Vector3.up, lidC);
            Quad(new Vector3(-hx, -h, -hz), new Vector3(a, -h, -hz), new Vector3(a, -h, hz), new Vector3(-hx, -h, hz), Vector3.down, Rock[2] * 0.7f);
            Quad(new Vector3(e, -h, -hz), new Vector3(hx, -h, -hz), new Vector3(hx, -h, hz), new Vector3(e, -h, hz), Vector3.down, Rock[2] * 0.7f);
            var mesh = new Mesh { indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(v); mesh.SetNormals(nrm); mesh.SetColors(col); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds();
            return mesh;
        }

        // Rock: the footprint outline (a jittered rounded rectangle, points every ~2.5 m) extruded down in 3 bands,
        // each band a little narrower towards the top, every vertex nudged; a flat grass lid exactly at y 0 with a
        // short skirt over the rock edge. Everything stays inside the footprint (the lid overhangs 4%), so the rock
        // never sticks out past the collider.
        static Mesh Build(Vector3 size, int seed, bool grass, bool rock)
        {
            var rnd = new System.Random(seed);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 d, Color col)
            {
                var nn = Vector3.Cross(b - a, d - a).normalized; int i = v.Count;
                v.Add(a); v.Add(b); v.Add(d); n.Add(nn); n.Add(nn); n.Add(nn); c.Add(col); c.Add(col); c.Add(col); t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
            void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Vector3 outward, Color col)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, d - a), outward) < 0f) { Tri(a, d, b, col); Tri(a, e, d, col); } else { Tri(a, b, d, col); Tri(a, d, e, col); }
            }
            Color Jit(Color col, float amt) { float k = 1f + R(-amt, amt); return new Color(col.r * k, col.g * k, col.b * k, col.a); }

            float hx = size.x * 0.5f, hz = size.z * 0.5f, h = size.y;
            // Rock style: almost square corners and tiny steps, so pillars, lintels and walls meet without gaps.
            float corner = rock ? Mathf.Min(Mathf.Min(hx, hz) * 0.2f, 0.25f) : Mathf.Min(Mathf.Min(hx, hz) * 0.45f, 2.5f);
            // Outline around the rounded rectangle.
            var outline = new List<Vector3>();
            void Edge(Vector3 a, Vector3 b) { int k = Mathf.Max(1, Mathf.RoundToInt((b - a).magnitude / 3.2f)); for (int i = 0; i < k; i++) outline.Add(Vector3.Lerp(a, b, (float)i / k)); }
            void Arc(Vector3 centre, float a0) { for (int i = 0; i < 2; i++) { float a = a0 + i * 45f * Mathf.Deg2Rad; outline.Add(centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * corner); } }
            Edge(new Vector3(-hx + corner, 0, -hz), new Vector3(hx - corner, 0, -hz)); Arc(new Vector3(hx - corner, 0, -hz + corner), -90f * Mathf.Deg2Rad);
            Edge(new Vector3(hx, 0, -hz + corner), new Vector3(hx, 0, hz - corner)); Arc(new Vector3(hx - corner, 0, hz - corner), 0f);
            Edge(new Vector3(hx - corner, 0, hz), new Vector3(-hx + corner, 0, hz)); Arc(new Vector3(-hx + corner, 0, hz - corner), 90f * Mathf.Deg2Rad);
            Edge(new Vector3(-hx, 0, hz - corner), new Vector3(-hx, 0, -hz + corner)); Arc(new Vector3(-hx + corner, 0, -hz + corner), 180f * Mathf.Deg2Rad);
            int m = outline.Count;
            // Bands: y and how far in (0 = on the footprint edge).
            // Small steps only: the blocks often stand flush against each other, deep insets showed as cracks and holes.
            float bandIn = rock ? Mathf.Min(0.14f, Mathf.Min(hx, hz) * 0.1f) : Mathf.Min(0.45f, Mathf.Min(hx, hz) * 0.12f);
            var ys = new[] { -h - 0.3f, -h * R(0.6f, 0.7f), -h * R(0.25f, 0.35f), -0.35f };
            var ins = new[] { 0f, bandIn * 0.35f, bandIn * 0.7f, bandIn };
            if (h < 2.5f) { ys = new[] { -h - 0.2f, -h * 0.5f, -h * 0.5f, -0.3f }; ins = new[] { 0f, bandIn * 0.5f, bandIn * 0.5f, bandIn }; }
            if (rock) { ys[0] = -h; ys[3] = -0.12f; }   // nothing hangs below the box (a lintel must not drop into the door), lid flush
            var ring = new Vector3[4, m];
            for (int k = 0; k < 4; k++)
                for (int i = 0; i < m; i++)
                {
                    var o = outline[i]; var inward = new Vector3(-Mathf.Sign(o.x) * (Mathf.Abs(o.x) > hx - corner - 0.01f ? 1f : 0f), 0, -Mathf.Sign(o.z) * (Mathf.Abs(o.z) > hz - corner - 0.01f ? 1f : 0f)).normalized;
                    float d = ins[k] * R(0.6f, 1.1f) + R(0f, bandIn * 0.5f);
                    ring[k, i] = o + inward * d + new Vector3(0, ys[k] + (k == 1 || k == 2 ? R(-0.8f, 0.8f) * Mathf.Min(1f, h / 6f) : 0f), 0);
                }
            for (int k = 0; k < 3; k++)
            {
                var col = Rock[(k + rnd.Next(2)) % Rock.Length];
                for (int i = 0; i < m; i++)
                {
                    int j = (i + 1) % m; var p0 = ring[k, i]; var p1 = ring[k, j]; var p2 = ring[k + 1, j]; var p3 = ring[k + 1, i];
                    var outw = (p0 + p1 + p2 + p3) * 0.25f; outw.y = 0;
                    var cc = Jit(col, 0.06f) * (k == 0 ? 0.85f : 1f); cc.a = col.a;
                    Quad(p0, p1, p2, p3, outw, cc);
                }
            }
            // Lid: flat top at y 0 (the walkable surface = collider top), skirt down over the rock rim.
            var lid = new Vector3[m]; var skirt = new Vector3[m];
            for (int i = 0; i < m; i++) { var o = outline[i]; lid[i] = new Vector3(o.x, 0f, o.z); skirt[i] = new Vector3(o.x, rock ? -0.12f : -0.55f, o.z); }
            var centreTop = Vector3.zero;
            // One colour for the whole lid: per-triangle shades made a star pattern round the centre.
            var tc = rock ? Rock[3] : grass ? (R(0, 1) < 0.5f ? CapA : CapB) : BareTop;
            for (int i = 0; i < m; i++)
            {
                int j = (i + 1) % m; var outw = (lid[i] + lid[j]) * 0.5f; outw.y = 0;
                Quad(skirt[i], skirt[j], lid[j], lid[i], outw, Jit(grass ? CapSide : Rock[2], 0.05f));
                // Underside of the lid rim, closing the gap down to the rock (seen from below it was a hole).
                Quad(skirt[i], skirt[j], ring[3, j], ring[3, i], outw - Vector3.up * outw.magnitude, Jit(Rock[2], 0.05f) * 0.8f);
                if (Vector3.Dot(Vector3.Cross(lid[i] - centreTop, lid[j] - centreTop), Vector3.up) < 0) Tri(centreTop, lid[j], lid[i], tc); else Tri(centreTop, lid[i], lid[j], tc);
            }
            // Bottom cap (blocks that hang in the air were open underneath).
            { var bc = new Vector3(0f, ring[0, 0].y, 0f); for (int i = 0; i < m; i++) { int j = (i + 1) % m; if (Vector3.Dot(Vector3.Cross(ring[0, j] - bc, ring[0, i] - bc), Vector3.down) > 0) Tri(bc, ring[0, j], ring[0, i], Rock[2] * 0.7f); else Tri(bc, ring[0, i], ring[0, j], Rock[2] * 0.7f); } }
            var mesh = new Mesh { indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetColors(c); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- Inspector ----------

        [CustomEditor(typeof(RockPlatform)), CanEditMultipleObjects]
        class RockPlatformEditor : Editor
        {
            public override void OnInspectorGUI()
            {
                DrawDefaultInspector();
                EditorGUILayout.HelpBox("Pivot = middle of the top surface. Size goes down from it. The collider always fits the size.", MessageType.None);
                if (GUILayout.Button("New Random Shape")) foreach (RockPlatform p in targets) { Undo.RecordObject(p, "Reseed"); p.seed = Random.Range(1, 1000); Rebuild(p); }
                if (GUILayout.Button("Rebuild")) foreach (RockPlatform p in targets) Rebuild(p);
            }
        }
    }
}
