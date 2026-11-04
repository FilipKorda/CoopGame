using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// A2_Start: shapes the terrain under the "level the ground" marker to the marker's (possibly tilted) top face and
    /// raises a sand beach under the "Add more beach" marker to its top, sloping gently into the sea.
    /// Changed triangles take the colour of the nearest untouched terrain at a similar height and slope, and the sea
    /// next to the new beach gets the shallow-water/foam colours the old shore has, so nothing looks pasted on.
    /// Edited copies live in Ground_A2.asset; the original meshes are never touched, a rerun restores them first.
    /// </summary>
    public static class IslandGroundBuilder
    {
        const string CopyPath = "Assets/Meshes/StyleTestIsland/Ground_A2.asset";
        const string LevelMarker = "level the ground";
        const string BeachMarker = "Add more beach";
        const float BeachSlope = 5f;     // metres from the beach edge to ShoreFoot under the water
        const float ShoreFoot = -1.5f;   // height the shore slope reaches, then it blends into the old sea floor
        const float SeaBlend = 3f;
        const float SteepSlope = 0.6f;   // face normal.y below this counts as a steep face

        struct Ref { public Vector3 mid; public bool steep; public float depth; public Color colour; }

        [MenuItem("Tools/Coop Game/Island Ground/Level + Beach (A2_Start)")]
        static void Menu() => Debug.Log(Build());

        public static string Build()
        {
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            var island = roots.FirstOrDefault(g => g.name == "Style Test Island");
            var level = roots.FirstOrDefault(g => g.name == LevelMarker);
            var beach = roots.FirstOrDefault(g => g.name == BeachMarker);
            if (island == null || level == null || beach == null) return "Missing 'Style Test Island', '" + LevelMarker + "' or '" + BeachMarker + "'.";

            levelT = level.transform; var bb = MarkerBounds(beach);
            Restore(island);
            Physics.SyncTransforms();

            var area = bb; area.Expand(new Vector3(2 * (BeachSlope + SeaBlend + 1), 0, 2 * (BeachSlope + SeaBlend + 1))); area.Encapsulate(MarkerBounds(level));
            area.min = new Vector3(area.min.x, -1000, area.min.z); area.max = new Vector3(area.max.x, 1000, area.max.z);
            var near = area; near.Expand(new Vector3(60, 0, 60));

            var filters = island.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null && f.GetComponent<Renderer>() != null).ToList();
            var tiles = filters.Where(f => f.name.StartsWith("Terrain") && f.GetComponent<Renderer>().bounds.Intersects(near)).ToList();
            var seas = filters.Where(f => f.name.StartsWith("Water") && f.GetComponent<Renderer>().bounds.Intersects(near)).ToList();

            // The beach must end under water before the terrain tiles do, otherwise the tile edge becomes the shoreline.
            var under = tiles.Where(t => t.GetComponent<Renderer>().bounds.Intersects(new Bounds(bb.center, new Vector3(bb.size.x, 2000, bb.size.z)))).ToList();
            var land = under[0].GetComponent<Renderer>().bounds; foreach (var t in under) land.Encapsulate(t.GetComponent<Renderer>().bounds);
            const float m = 3f;   // at 3 m from its edge the shore slope is already ~0.8 m under water
            beachB = new Bounds(); beachB.SetMinMax(
                new Vector3(Mathf.Max(bb.min.x, land.min.x + m), bb.min.y, Mathf.Max(bb.min.z, land.min.z + m)),
                new Vector3(Mathf.Min(bb.max.x, land.max.x - m), bb.max.y, Mathf.Min(bb.max.z, land.max.z - m)));
            // Snap the edges onto the 2 m terrain grid: then the waterline falls mid-cell instead of on a vertex row (no zig-zag).
            beachB.SetMinMax(
                new Vector3(Mathf.Ceil(beachB.min.x / 2f) * 2f, beachB.min.y, Mathf.Ceil(beachB.min.z / 2f) * 2f),
                new Vector3(Mathf.Floor(beachB.max.x / 2f) * 2f, beachB.max.y, Mathf.Floor(beachB.max.z / 2f) * 2f));

            // Colour references: untouched terrain triangles nearby.
            var refs = new List<Ref>();
            foreach (var f in tiles) Triangles(f.transform, f.sharedMesh, (mid, steep, colour, untouched) => { if (untouched) refs.Add(new Ref { mid = mid, steep = steep, colour = colour }); });

            var copies = new List<(Mesh copy, Mesh orig)>();
            foreach (var f in tiles)
            {
                if (!f.GetComponent<Renderer>().bounds.Intersects(area)) continue;
                var orig = f.sharedMesh;
                var copy = Shape(f.transform, orig, refs);
                if (copy == null) continue;
                copy.name = orig.name; copies.Add((copy, orig)); Assign(f, copy);
            }
            Physics.SyncTransforms();

            // Sea: the island's water uses four colours (deep, mid, shallow, foam); repaint them along the new shore.
            var seaColours = seas.SelectMany(f => f.sharedMesh.colors).Distinct().OrderBy(c => c.r + c.g + c.b).ToArray();
            int seaCount = 0;
            foreach (var f in seas)
            {
                if (seaColours.Length < 4 || !f.GetComponent<Renderer>().bounds.Intersects(area)) continue;
                var orig = f.sharedMesh;
                var copy = PaintSea(f.transform, orig, seaColours);
                if (copy == null) continue;
                copy.name = orig.name; copies.Add((copy, orig)); Assign(f, copy); seaCount++;
            }

            if (copies.Count > 0)
            {
                var map = new TextAsset(string.Join("\n", copies.Select(c => c.copy.name + "\t" + GlobalObjectId.GetGlobalObjectIdSlow(c.orig)))) { name = "Ground Originals" };
                AssetDatabase.CreateAsset(map, CopyPath);
                foreach (var c in copies) AssetDatabase.AddObjectToAsset(c.copy, map);
                AssetDatabase.ImportAsset(CopyPath);
            }

            foreach (var g in new[] { level, beach }) { Undo.RecordObject(g, "Hide ground marker"); g.SetActive(false); }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(island.scene);
            return $"Edited: {string.Join(", ", copies.Select(c => c.copy.name))} (sea tiles repainted {seaCount}); beach top {bb.max.y:0.00}, beach area {beachB.min.x:0.0},{beachB.min.z:0.0}..{beachB.max.x:0.0},{beachB.max.z:0.0}. Markers switched off.";
        }

        static Bounds MarkerBounds(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>(); var t = go.transform;
            var local = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            var b = new Bounds(t.TransformPoint(local.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                b.Encapsulate(t.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(i % 2 * 2 - 1, i / 2 % 2 * 2 - 1, i / 4 * 2 - 1))));
            return b;
        }

        static Transform levelT;
        static Bounds beachB;

        /// <summary>Height of the level marker's top face above (x, z), which may be tilted; false outside its footprint.</summary>
        static bool LevelTop(Vector3 p, out float y)
        {
            var p0 = levelT.TransformPoint(new Vector3(0, 0.5f, 0)); var n = levelT.up;
            y = p0.y - (n.x * (p.x - p0.x) + n.z * (p.z - p0.z)) / n.y;
            var l = levelT.InverseTransformPoint(new Vector3(p.x, y, p.z));
            return Mathf.Abs(l.x) <= 0.5f && Mathf.Abs(l.z) <= 0.5f;
        }

        static bool InXZ(Bounds b, Vector3 p) => p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z;

        static float DistXZ(Bounds b, Vector3 p)
        {
            float dx = Mathf.Max(b.min.x - p.x, 0, p.x - b.max.x), dz = Mathf.Max(b.min.z - p.z, 0, p.z - b.max.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>New height for a terrain point, or NaN to leave it.</summary>
        static float Target(Vector3 w)
        {
            if (LevelTop(w, out float lt)) return lt;
            float top = beachB.max.y, y;
            float d = DistXZ(beachB, w);
            if (d <= 0) y = top;
            else if (d < BeachSlope)
            {
                // Straight gentle shore, set exactly (also lowering old sea-floor bumps) so the waterline is a clean line.
                y = Mathf.Lerp(top, ShoreFoot, d / BeachSlope);
                if (w.y < 0f) return Mathf.Abs(w.y - y) < 0.001f ? float.NaN : y;
            }
            else if (d < BeachSlope + SeaBlend) y = Mathf.Lerp(ShoreFoot, w.y, (d - BeachSlope) / SeaBlend);
            else return float.NaN;
            return w.y < y ? y : float.NaN;   // the beach only fills up, it never digs into land
        }

        /// <summary>Calls back for every triangle: world centroid, steep or not, its colour, and whether this builder leaves it alone.</summary>
        static void Triangles(Transform tr, Mesh mesh, System.Action<Vector3, bool, Color, bool> each)
        {
            var v = mesh.vertices; var col = mesh.colors; var tris = mesh.triangles;
            if (col.Length != v.Length) return;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = tr.TransformPoint(v[tris[i]]), b = tr.TransformPoint(v[tris[i + 1]]), c = tr.TransformPoint(v[tris[i + 2]]);
                var n = Vector3.Cross(b - a, c - a).normalized;
                bool untouched = float.IsNaN(Target(a)) && float.IsNaN(Target(b)) && float.IsNaN(Target(c));
                each((a + b + c) / 3f, Mathf.Abs(n.y) < SteepSlope, col[tris[i]], untouched);
            }
        }

        static Mesh Shape(Transform tr, Mesh orig, List<Ref> refs)
        {
            var v = orig.vertices; var col = orig.colors; bool any = false;
            var moved = new bool[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var w = tr.TransformPoint(v[i]); float y = Target(w);
                if (float.IsNaN(y) || Mathf.Abs(y - w.y) < 0.001f) continue;
                w.y = y; v[i] = tr.InverseTransformPoint(w); moved[i] = any = true;
            }
            if (!any) return null;

            // Every moved triangle copies the colour (and shine in alpha) of the closest untouched terrain
            // with the same slope class at a similar height, so the new ground continues the old beach and grass.
            if (col.Length == v.Length)
            {
                var tris = orig.triangles;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    if (!moved[a] && !moved[b] && !moved[c]) continue;
                    Vector3 wa = tr.TransformPoint(v[a]), wb = tr.TransformPoint(v[b]), wc = tr.TransformPoint(v[c]);
                    var n = Vector3.Cross(wb - wa, wc - wa).normalized; bool steep = Mathf.Abs(n.y) < SteepSlope;
                    var mid = (wa + wb + wc) / 3f;
                    float best = float.PositiveInfinity; Color colour = col[a];
                    foreach (var r in refs)
                    {
                        if (r.steep != steep) continue;
                        // Dry flat ground never takes the dark brown sea-floor/dirt colour (that made a brown patch on the beach).
                        if (!steep && mid.y > -0.3f && r.colour.r > r.colour.g && Mathf.Max(r.colour.r, r.colour.g, r.colour.b) < 0.6f) continue;
                        float dx = r.mid.x - mid.x, dz = r.mid.z - mid.z;
                        float s = Mathf.Sqrt(dx * dx + dz * dz) + Mathf.Abs(r.mid.y - mid.y) * (steep ? 2f : 12f);
                        if (s < best) { best = s; colour = r.colour; }
                    }
                    col[a] = col[b] = col[c] = colour;
                }
            }

            var m = Object.Instantiate(orig); m.vertices = v; if (col.Length == v.Length) m.colors = col;
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Water depth above the terrain at p (negative where the land is above the surface).</summary>
        static float Depth(Vector3 p)
        {
            float ground = -50f;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, 60f, p.z), Vector3.down, 200f))
                if (!h.collider.isTrigger && h.collider.name.StartsWith("Terrain")) ground = Mathf.Max(ground, h.point.y);
            return p.y - ground;
        }

        /// <summary>Foam where the triangle touches the shore, then shallow, mid and deep water by the shallowest point under it.
        /// Only the sea next to the new beach / levelled ground is repainted (the old shore by the dock stays as it was).</summary>
        static Mesh PaintSea(Transform tr, Mesh orig, Color[] pal)
        {
            var v = orig.vertices; var col = orig.colors; var tris = orig.triangles;
            if (col.Length != v.Length) return null;
            Color deep = pal[0], mid1 = pal[1], shallow = pal[pal.Length - 2], foam = pal[pal.Length - 1];
            bool any = false;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = tr.TransformPoint(v[tris[i]]), b = tr.TransformPoint(v[tris[i + 1]]), c = tr.TransformPoint(v[tris[i + 2]]);
                var mid = (a + b + c) / 3f;
                if (DistXZ(beachB, mid) > BeachSlope + SeaBlend + 2f || mid.x > beachB.max.x + 6f) continue;
                float shallowest = float.PositiveInfinity;
                for (int s = 0; s <= 6; s++)
                    for (int t = 0; s + t <= 6; t++)
                        shallowest = Mathf.Min(shallowest, Depth(a + (b - a) * (s / 6f) + (c - a) * (t / 6f)));
                var colour = shallowest < 0.05f ? foam : shallowest < 1.2f ? shallow : shallowest < 2.5f ? mid1 : deep;
                if (colour == col[tris[i]]) continue;
                col[tris[i]] = col[tris[i + 1]] = col[tris[i + 2]] = colour; any = true;
            }
            if (!any) return null;
            var m = Object.Instantiate(orig); m.colors = col;
            return m;
        }


        /// <summary>Puts the original meshes back on tiles a previous run edited and drops the old copies.</summary>
        static void Restore(GameObject island)
        {
            var mapAsset = AssetDatabase.LoadAllAssetsAtPath(CopyPath).OfType<TextAsset>().FirstOrDefault();
            if (mapAsset == null) return;
            var originals = new Dictionary<string, Mesh>();
            foreach (var line in mapAsset.text.Split('\n'))
            {
                var parts = line.Split('\t');
                if (parts.Length == 2 && GlobalObjectId.TryParse(parts[1], out var id)) originals[parts[0]] = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Mesh;
            }
            foreach (var mf in island.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && AssetDatabase.GetAssetPath(mf.sharedMesh) == CopyPath && originals.TryGetValue(mf.sharedMesh.name, out var o) && o != null)
                    Assign(mf, o);
            AssetDatabase.DeleteAsset(CopyPath);
        }

        static void Assign(MeshFilter mf, Mesh m)
        {
            Undo.RecordObject(mf, "Ground mesh"); mf.sharedMesh = m;
            var mc = mf.GetComponent<MeshCollider>();
            if (mc != null) { Undo.RecordObject(mc, "Ground mesh"); mc.sharedMesh = null; mc.sharedMesh = m; }
        }

        static Color[] Hex(params string[] hex) => hex.Select(h => ColorUtility.TryParseHtmlString("#" + h, out var c) ? c : Color.magenta).ToArray();
    }
}
