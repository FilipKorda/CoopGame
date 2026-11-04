using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// A2_Start island water: turns the greybox marker cubes into real geometry.
    /// - "water and a ditch*": digs the ditch into the island terrain meshes (edited copies saved to
    ///   Assets/Meshes/StyleTestIsland/Ditches_A2.asset, originals untouched), cuts grass/dressing out of it,
    ///   and fills it with swimmable water (Water.mat surface + WaterVolume triggers, little falls where levels step).
    /// - "Bridge*": a wooden bridge with steel straps, bands, braces and bolts.
    /// - "Water wheel construction": a small mill house on the bank with a medium water wheel fed by a wooden flume.
    /// Markers are only switched off; re-running rebuilds everything under "Island Waterworks".
    /// </summary>
    public static class IslandWaterworksBuilder
    {
        const string RootName = "Island Waterworks";
        const string IslandName = "Style Test Island";
        const string TerrainCopyPath = "Assets/Meshes/StyleTestIsland/Ditches_A2.asset";
        const string MeshDir = "Assets/Meshes/Waterworks";
        const string MatDir = "Assets/Materials/Waterworks";

        const float WaterBelowRim = 0.8f;    // water surface this far under the lowest bank
        const float MaxDepth = 3.5f;         // ditch floor at most this far under the surface
        const float ShelfWidth = 1.2f;       // shallow shelf along the banks (stand and climb out)
        const float ShelfDepth = 0.6f;
        const float BankWidth = 1.6f;        // banks slope down over this width outside the ditch
        const float PitWaterLevel = 4.3f;    // the existing pit (floor already below 0): filled almost to the surrounding ground (~5 m)
        const float CascadeStep = 0.4f;      // ditches on a slope step down in these increments

        static Material wood, woodLight, woodDark, metal, metalDark, stone, plaster, roof, windowGlow, water, waterFall;

        class Seg { public Vector3 c; public Quaternion rot; public float hx, hz, level, bottom; public Ditch d; }
        class Ditch { public GameObject go; public Vector3 c; public Quaternion rot; public float hx, hz, minY; public bool pit, cascade; public List<Seg> segs = new List<Seg>(); public int comp; }

        [MenuItem("Tools/Coop Game/Island Waterworks/Build (A2_Start)")]
        public static string Build()
        {
            var report = new System.Text.StringBuilder();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var island = roots.FirstOrDefault(g => g.name == IslandName);
            if (island == null) return "No '" + IslandName + "' in the open scene.";
            var ditchGos = roots.Where(g => g.name.StartsWith("water and a ditch")).ToList();
            var bridgeGos = roots.Where(g => g.name.StartsWith("Bridge")).ToList();
            var wheelGo = roots.FirstOrDefault(g => g.name.StartsWith("Water wheel construction"));

            MakeMaterials();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build Island Waterworks");

            var old = roots.FirstOrDefault(g => g.name == RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Island Waterworks");

            // Markers off (they stay in the scene as the source of truth for re-runs).
            foreach (var g in ditchGos.Concat(bridgeGos).Append(wheelGo))
                if (g != null && g.activeSelf) { Undo.RecordObject(g, "Hide marker"); g.SetActive(false); }

            var jobs = RestoreOriginals(island);   // measure the banks on the untouched terrain
            Physics.SyncTransforms();
            var ditches = MakeDitches(ditchGos);
            report.AppendLine(CarveTerrain(island, ditches, jobs));
            BuildWater(root.transform, ditches, report);
            foreach (var b in bridgeGos) BuildBridge(root.transform, b, ditches, report);
            if (wheelGo != null) BuildMill(root.transform, wheelGo, ditches, report);

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<Spinner>() == null && t.GetComponentInParent<Spinner>() == null && t.GetComponent<Light>() == null) t.gameObject.isStatic = true;
            AssetDatabase.SaveAssets();
            return report.ToString();
        }

        // ---------- Ditches: footprints, levels ----------

        static bool Inside(Seg s, Vector3 p, float ex, float ez, out float lateral)
        {
            var l = Quaternion.Inverse(s.rot) * (p - s.c);
            lateral = s.hx - Mathf.Abs(l.x);
            return Mathf.Abs(l.x) <= s.hx + ex && Mathf.Abs(l.z) <= s.hz + ez;
        }

        static bool InsideDitch(Ditch d, Vector3 p, float ex, float ez)
        {
            var l = Quaternion.Inverse(d.rot) * (p - d.c);
            return Mathf.Abs(l.x) <= d.hx + ex && Mathf.Abs(l.z) <= d.hz + ez;
        }

        static float TerrainHeight(Vector3 p)
        {
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, 120f, p.z), Vector3.down, 300f))
                if (h.collider.name.StartsWith("Terrain") && h.point.y > best) best = h.point.y;
            return best;
        }

        static List<Ditch> MakeDitches(List<GameObject> gos)
        {
            var list = new List<Ditch>();
            foreach (var go in gos)
            {
                var t = go.transform; var s = t.lossyScale;
                var d = new Ditch { go = go, c = new Vector3(t.position.x, 0, t.position.z), rot = Quaternion.Euler(0, t.eulerAngles.y, 0), hx = Mathf.Abs(s.x) / 2, hz = Mathf.Abs(s.z) / 2, minY = t.position.y - Mathf.Abs(s.y) / 2 };
                // Already a depression (the pit puzzle): ground inside is below zero.
                d.pit = TerrainHeight(t.position) < 0.5f;
                int n = Mathf.Max(1, Mathf.RoundToInt(d.hz * 2 / 4f));
                var fwd = d.rot * Vector3.forward; var right = d.rot * Vector3.right;
                for (int i = 0; i < n; i++)
                {
                    var seg = new Seg { d = d, rot = d.rot, hx = d.hx, hz = d.hz / n, c = d.c + fwd * (-d.hz + (i + 0.5f) * 2 * d.hz / n) };
                    float rim = float.PositiveInfinity;
                    for (int k = -1; k <= 1; k++)
                        foreach (float side in new[] { -1f, 1f })
                        {
                            float h = TerrainHeight(seg.c + fwd * (k * seg.hz) + right * side * (d.hx + 1.5f));
                            if (!float.IsNegativeInfinity(h)) rim = Mathf.Min(rim, h);
                        }
                    seg.level = rim - WaterBelowRim;
                    d.segs.Add(seg);
                }
                float lo = d.segs.Min(x => x.level), hi = d.segs.Max(x => x.level);
                d.cascade = !d.pit && hi - lo > 0.8f;
                if (d.cascade) foreach (var seg in d.segs) seg.level = Mathf.Floor(seg.level / CascadeStep) * CascadeStep;
                else foreach (var seg in d.segs) seg.level = lo;
                list.Add(d);
            }
            // Connected ditches (touching footprints) share one level; cascades never rise above it.
            for (int i = 0; i < list.Count; i++) list[i].comp = i;
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                        if (list[i].comp != list[j].comp && Touch(list[i], list[j]))
                        { int a = Mathf.Min(list[i].comp, list[j].comp); list[i].comp = list[j].comp = a; changed = true; }
            }
            foreach (var grp in list.GroupBy(d => d.comp))
            {
                if (grp.Any(d => d.pit)) foreach (var d in grp) { d.pit = true; d.cascade = false; }
                var flat = grp.Where(d => !d.cascade && !d.pit).ToList();
                float level = flat.Count > 0 ? flat.Min(d => d.segs[0].level) : float.PositiveInfinity;
                foreach (var d in grp)
                    foreach (var s in d.segs)
                    {
                        if (d.pit) s.level = PitWaterLevel;
                        else if (d.cascade) s.level = Mathf.Min(s.level, level);
                        else s.level = level;
                        s.bottom = Mathf.Max(d.minY, s.level - MaxDepth);
                    }
            }
            return list;
        }

        static bool Touch(Ditch a, Ditch b)
        {
            for (float x = -a.hx; x <= a.hx + 0.01f; x += Mathf.Max(0.5f, a.hx / 4))
                for (float z = -a.hz; z <= a.hz + 0.01f; z += Mathf.Max(0.5f, a.hz / 6))
                    if (InsideDitch(b, a.c + a.rot * new Vector3(x, 0, z), 0.5f, 0.5f)) return true;
            return false;
        }

        // ---------- Terrain + dressing ----------

        /// <summary>Puts the original meshes back on everything a previous run edited, and drops the old copies.</summary>
        static List<(MeshFilter mf, Mesh orig)> RestoreOriginals(GameObject island)
        {
            var mapAsset = AssetDatabase.LoadAllAssetsAtPath(TerrainCopyPath).OfType<TextAsset>().FirstOrDefault();
            var originals = new Dictionary<string, Mesh>();
            if (mapAsset != null)
                foreach (var line in mapAsset.text.Split('\n'))
                {
                    var parts = line.Split('\t');
                    if (parts.Length == 2 && GlobalObjectId.TryParse(parts[1], out var id)) originals[parts[0]] = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Mesh;
                }
            var jobs = new List<(MeshFilter mf, Mesh orig)>();
            foreach (var mf in island.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (m == null || mf.name == "Island_Glow") continue;
                bool isCopy = AssetDatabase.GetAssetPath(m) == TerrainCopyPath;
                var orig = isCopy && originals.TryGetValue(m.name, out var o) ? o : (isCopy ? null : m);
                if (orig != null) jobs.Add((mf, orig));
            }
            foreach (var j in jobs) if (j.mf.sharedMesh != j.orig) Assign(j.mf, j.orig);
            AssetDatabase.DeleteAsset(TerrainCopyPath);
            return jobs;
        }

        static string CarveTerrain(GameObject island, List<Ditch> ditches, List<(MeshFilter mf, Mesh orig)> all)
        {
            var segs = ditches.SelectMany(d => d.segs).ToList();
            var area = new Bounds(segs[0].c, Vector3.zero);
            foreach (var s in segs) { area.Encapsulate(s.c + Vector3.one * (s.hx + s.hz + 3)); area.Encapsulate(s.c - Vector3.one * (s.hx + s.hz + 3)); }
            area.min = new Vector3(area.min.x, -1000, area.min.z); area.max = new Vector3(area.max.x, 1000, area.max.z);
            var jobs = all.Where(j => { var r = j.mf.GetComponent<Renderer>(); return r != null && r.bounds.Intersects(area); }).ToList();

            var copies = new List<(Mesh copy, Mesh orig)>();
            int terrainCount = 0, dressingCount = 0;
            foreach (var (mf, orig) in jobs)
            {
                var r = mf.GetComponent<Renderer>(); var col = mf.GetComponent<Collider>();
                bool terrain = mf.name.StartsWith("Terrain");
                bool flat = !terrain && r != null && r.bounds.size.y < 4f && (col == null || mf.name.StartsWith("Concrete Pads"));
                if (!terrain && !flat) continue;
                var copy = terrain ? Dig(mf.transform, orig, segs) : Cut(mf.transform, orig, segs);
                if (copy == null) continue;
                copy.name = orig.name;
                copies.Add((copy, orig));
                Assign(mf, copy);
                if (terrain) terrainCount++; else dressingCount++;
            }
            if (copies.Count > 0)
            {
                var map = new TextAsset(string.Join("\n", copies.Select(c => c.copy.name + "\t" + GlobalObjectId.GetGlobalObjectIdSlow(c.orig)))) { name = "Ditch Originals" };
                AssetDatabase.CreateAsset(map, TerrainCopyPath);
                foreach (var c in copies) AssetDatabase.AddObjectToAsset(c.copy, map);
                AssetDatabase.ImportAsset(TerrainCopyPath);
            }

            // Small loose props standing in a ditch (bushes, palms, grass tufts): switch them off.
            var hidden = new List<string>();
            foreach (var r in island.GetComponentsInChildren<Renderer>(false))
            {
                var t = r.transform;
                if (r.bounds.size.magnitude > 12f || t.name.StartsWith("Terrain") || jobs.Any(j => j.mf.transform == t)) continue;
                if (segs.Any(s => Inside(s, t.position, 0.5f, 0.2f, out _) && t.position.y > s.bottom - 1f))
                { Undo.RecordObject(t.gameObject, "Hide prop in ditch"); t.gameObject.SetActive(false); hidden.Add(t.name); }
            }
            // Anything else (gameplay objects) inside a ditch: just report.
            var others = new HashSet<string>();
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (c.transform.IsChildOf(island.transform) || c.isTrigger || ditches.Any(d => c.transform.IsChildOf(d.go.transform))) continue;
                var p = c.bounds.center;
                if (segs.Any(s => Inside(s, p, 0, 0, out _) && p.y < s.level + 2.5f)) others.Add(c.name);
            }
            return $"Ditches: {ditches.Count} ({ditches.Count(d => d.pit)} pit, {ditches.Count(d => d.cascade)} cascade); terrain meshes dug {terrainCount}, dressing meshes cut {dressingCount}; hidden props: {(hidden.Count == 0 ? "none" : string.Join(", ", hidden))}" +
                   $"\nLevels: " + string.Join(", ", ditches.Select(d => d.go.name.Replace("water and a ditch", "d") + "=" + string.Join("/", d.segs.Select(s => s.level.ToString("0.0")).Distinct()))) +
                   $"\nOther colliders inside ditches (left as is): {(others.Count == 0 ? "none" : string.Join(", ", others))}";
        }

        static void Assign(MeshFilter mf, Mesh m)
        {
            Undo.RecordObject(mf, "Ditch mesh"); mf.sharedMesh = m;
            var mc = mf.GetComponent<MeshCollider>();
            if (mc != null) { Undo.RecordObject(mc, "Ditch mesh"); mc.sharedMesh = null; mc.sharedMesh = m; }
        }

        /// <summary>Lowers the terrain inside the ditch footprints: shelf near the banks, floor further in.</summary>
        static Mesh Dig(Transform tr, Mesh orig, List<Seg> segs)
        {
            var v = orig.vertices; bool any = false;
            for (int i = 0; i < v.Length; i++)
            {
                var w = tr.TransformPoint(v[i]); float target = float.PositiveInfinity;
                foreach (var s in segs)
                {
                    // Ends reach 0.4 m further so neighbouring ditches meet without a ridge between them.
                    if (Inside(s, w, 0, 0.4f, out float lat)) target = Mathf.Min(target, lat < ShelfWidth ? s.level - ShelfDepth : s.bottom);
                    // Just outside: slope the bank down towards the water (no jagged teeth).
                    else if (lat > -BankWidth && Inside(s, w, BankWidth, 0.4f, out _))
                        target = Mathf.Min(target, Mathf.Lerp(s.level + 0.35f, w.y, -lat / BankWidth));
                }
                if (target < w.y) { w.y = target; v[i] = tr.InverseTransformPoint(w); any = true; }
            }
            if (!any) return null;
            var m = Object.Instantiate(orig); m.vertices = v; m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Removes grass/dressing triangles lying over a ditch (they'd float above the water).</summary>
        static Mesh Cut(Transform tr, Mesh orig, List<Seg> segs)
        {
            var v = orig.vertices; bool any = false;
            var m = Object.Instantiate(orig);
            for (int sm = 0; sm < orig.subMeshCount; sm++)
            {
                var tris = orig.GetTriangles(sm); var keep = new List<int>(tris.Length);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var w = tr.TransformPoint((v[tris[i]] + v[tris[i + 1]] + v[tris[i + 2]]) / 3f);
                    if (segs.Any(s => Inside(s, w, BankWidth + 0.2f, 0.4f, out _))) { any = true; continue; }
                    keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
                }
                m.SetTriangles(keep, sm);
            }
            if (!any) { Object.DestroyImmediate(m); return null; }
            m.RecalculateBounds();
            return m;
        }

        // ---------- Water ----------

        static void BuildWater(Transform root, List<Ditch> ditches, System.Text.StringBuilder report)
        {
            var g = new GameObject("Ditch Water").transform; g.SetParent(root, false);
            var segs = ditches.SelectMany(d => d.segs).ToList();
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var s in segs) { float r = s.hx + s.hz + 6; minX = Mathf.Min(minX, s.c.x - r); maxX = Mathf.Max(maxX, s.c.x + r); minZ = Mathf.Min(minZ, s.c.z - r); maxZ = Mathf.Max(maxZ, s.c.z + r); }
            const float cell = 0.5f, core = 0.6f, reach = 5f;
            var level = new Dictionary<(int, int), float>();
            int x0 = Mathf.FloorToInt(minX / cell), x1 = Mathf.CeilToInt(maxX / cell), z0 = Mathf.FloorToInt(minZ / cell), z1 = Mathf.CeilToInt(maxZ / cell);
            for (int i = x0; i < x1; i++)
                for (int j = z0; j < z1; j++)
                {
                    var p = new Vector3((i + 0.5f) * cell, 0, (j + 0.5f) * cell);
                    // Level of the closest segment (ties: the lower one), by distance outside its footprint.
                    float bestDist = float.PositiveInfinity, best = float.PositiveInfinity;
                    foreach (var s in segs)
                    {
                        var l = Quaternion.Inverse(s.rot) * (p - s.c);
                        float dx = Mathf.Max(0, Mathf.Abs(l.x) - s.hx), dz = Mathf.Max(0, Mathf.Abs(l.z) - s.hz);
                        float dist = Mathf.Sqrt(dx * dx + dz * dz);
                        if (dist < bestDist - 0.01f || (Mathf.Abs(dist - bestDist) <= 0.01f && s.level < best)) { bestDist = dist; best = s.level; }
                    }
                    if (bestDist > reach) continue;
                    // Core of the ditch always; around it wherever the ground lies under the water line,
                    // so the surface closes up against the banks with no gaps.
                    if (bestDist <= core || TerrainHeight(p) < best + 0.05f) level[(i, j)] = best;
                }
            // One continuous surface: grid corners are shared, and each corner's height is the average level of the
            // water cells around it (3 m radius), so cascade steps become a gentle slope instead of vertical falls.
            var verts = new List<Vector3>(); var surf = new List<int>(); var uvs = new List<Vector2>();
            var corner = new Dictionary<(int, int), int>();
            const int blur = 6;
            int Corner(int ci, int cj)
            {
                if (corner.TryGetValue((ci, cj), out int idx)) return idx;
                float sum = 0; int cnt = 0;
                for (int a = -blur; a < blur; a++)
                    for (int b = -blur; b < blur; b++)
                        if (level.TryGetValue((ci + a, cj + b), out float lv)) { sum += lv; cnt++; }
                var p = new Vector3(ci * cell, sum / cnt, cj * cell);
                idx = verts.Count; verts.Add(p); uvs.Add(new Vector2(p.x, p.z)); corner[(ci, cj)] = idx;
                return idx;
            }
            foreach (var kv in level)
            {
                var (i, j) = kv.Key;
                int a0 = Corner(i, j), a1 = Corner(i, j + 1), a2 = Corner(i + 1, j + 1), a3 = Corner(i + 1, j);
                surf.AddRange(new[] { a0, a1, a2, a0, a2, a3 });
            }
            var mesh = new Mesh { name = "Ditch Water Surface", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(surf, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh = SaveMesh(mesh, "Ditch Water Surface");
            var surface = new GameObject("Water Surface"); surface.transform.SetParent(g, false);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = surface.AddComponent<MeshRenderer>(); mr.sharedMaterial = water;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;

            // Swimmable volumes, one per segment (WaterVolume reads the collider's top as the surface).
            foreach (var s in segs)
            {
                var v = new GameObject("WaterVolume " + s.d.go.name.Replace("water and a ditch", "").Trim()); v.transform.SetParent(g, false);
                v.transform.SetPositionAndRotation(new Vector3(s.c.x, (s.level + s.bottom) * 0.5f, s.c.z), s.rot);
                var bc = v.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(s.hx * 2 + 1.0f, s.level - s.bottom, s.hz * 2);
                v.AddComponent<WaterVolume>();
            }
            report.AppendLine($"Water: {level.Count * cell * cell:0} m² of surface, {segs.Count} WaterVolumes.");
        }

        static Seg SegAt(List<Ditch> ditches, Vector3 p)
        {
            Seg best = null; float bd = float.MaxValue;
            foreach (var s in ditches.SelectMany(d => d.segs)) { float dd = (new Vector3(s.c.x, 0, s.c.z) - new Vector3(p.x, 0, p.z)).sqrMagnitude; if (dd < bd) { bd = dd; best = s; } }
            return best;
        }

        // ---------- Bridge ----------

        static void BuildBridge(Transform root, GameObject marker, List<Ditch> ditches, System.Text.StringBuilder report)
        {
            var t = marker.transform; var s = t.lossyScale;
            float span = Mathf.Abs(s.x), W = Mathf.Abs(s.z), L = span + 1.6f;
            var rot = Quaternion.Euler(0, t.eulerAngles.y, 0); var right = rot * Vector3.right;
            float hA = TerrainHeight(t.position - right * (L * 0.5f - 0.4f)), hB = TerrainHeight(t.position + right * (L * 0.5f - 0.4f));
            float deck = Mathf.Max(hA, hB) + 0.12f;
            var seg = SegAt(ditches, t.position);
            float floor = seg != null ? seg.bottom : deck - 4f;

            var g = new GameObject(marker.name + " (Wood)").transform; g.SetParent(root, false);
            g.SetPositionAndRotation(new Vector3(t.position.x, deck, t.position.z), rot);
            var deckCol = g.gameObject.AddComponent<BoxCollider>(); deckCol.center = new Vector3(0, -0.15f, 0); deckCol.size = new Vector3(L, 0.3f, W);

            // Deck planks across the width, every fourth one held by a flush steel strap.
            int n = Mathf.RoundToInt(L / 0.5f);
            for (int i = 0; i < n; i++)
            {
                float x = -L / 2 + (i + 0.5f) * (L / n);
                var p = Box(g, "Plank", new Vector3(x, -0.06f, 0), new Vector3(L / n - 0.05f, 0.12f, W), i % 3 == 0 ? woodLight : wood);
                p.transform.localRotation = Quaternion.Euler(0, (i * 37 % 5 - 2) * 0.6f, 0);   // slightly uneven boards
                if (i % 4 == 1) Box(g, "Deck Steel Strap", new Vector3(x, 0.005f, 0), new Vector3(0.14f, 0.03f, W + 0.04f), metalDark);
            }
            // Stringers and steel cross braces underneath.
            foreach (float z in new[] { -W / 2 + 0.3f, 0f, W / 2 - 0.3f })
                Box(g, "Stringer", new Vector3(0, -0.32f, z), new Vector3(L, 0.38f, 0.26f), woodDark);
            for (float x = -L / 2 + 1f; x < L / 2 - 0.5f; x += 2f)
                Box(g, "Steel Cross Brace", new Vector3(x, -0.55f, 0), new Vector3(0.1f, 0.1f, W - 0.3f), metal);
            // Piles at the ditch edges, with steel caps and knee braces up to the stringers.
            float pileX = span / 2 - 1.6f, pileH = deck - 0.5f - floor + 0.3f;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float z in new[] { -W / 2 + 0.3f, W / 2 - 0.3f })
                {
                    Box(g, "Pile", new Vector3(sx * pileX, -0.5f - pileH / 2, z), new Vector3(0.32f, pileH, 0.32f), woodDark);
                    Box(g, "Pile Steel Cap", new Vector3(sx * pileX, -0.52f, z), new Vector3(0.38f, 0.08f, 0.38f), metal);
                    Box(g, "Pile Steel Band", new Vector3(sx * pileX, -1.3f, z), new Vector3(0.36f, 0.08f, 0.36f), metal);
                    var brace = Box(g, "Steel Knee Brace", new Vector3(sx * (pileX - 0.6f), -0.95f, z), new Vector3(0.07f, 1.5f, 0.07f), metal);
                    brace.transform.localRotation = Quaternion.Euler(0, 0, sx * 45f);
                }
            // Railings: wooden posts and rails with steel bands, caps and bolts.
            int posts = Mathf.Max(2, Mathf.RoundToInt(L / 2f) + 1);
            foreach (float sz in new[] { -1f, 1f })
            {
                float z = sz * (W / 2 - 0.09f);
                for (int i = 0; i < posts; i++)
                {
                    float x = -L / 2 + 0.15f + i * (L - 0.3f) / (posts - 1);
                    Box(g, "Rail Post", new Vector3(x, 0.55f, z), new Vector3(0.16f, 1.1f, 0.16f), woodDark);
                    Box(g, "Post Steel Cap", new Vector3(x, 1.13f, z), new Vector3(0.22f, 0.05f, 0.22f), metal);
                    Box(g, "Post Steel Band", new Vector3(x, 0.12f, z), new Vector3(0.2f, 0.07f, 0.2f), metal);
                    Box(g, "Rail Bolt", new Vector3(x, 1.0f, z + sz * 0.1f), new Vector3(0.07f, 0.07f, 0.04f), metalDark);
                    Box(g, "Rail Bolt", new Vector3(x, 0.55f, z + sz * 0.1f), new Vector3(0.07f, 0.07f, 0.04f), metalDark);
                }
                Box(g, "Top Rail", new Vector3(0, 1.0f, z + sz * 0.02f), new Vector3(L, 0.12f, 0.14f), wood);
                Box(g, "Mid Rail", new Vector3(0, 0.55f, z + sz * 0.02f), new Vector3(L, 0.08f, 0.09f), wood);
                Box(g, "Rail Steel Strip", new Vector3(0, 1.065f, z + sz * 0.02f), new Vector3(L, 0.02f, 0.06f), metal);
                var rc = g.gameObject.AddComponent<BoxCollider>(); rc.center = new Vector3(0, 0.6f, z); rc.size = new Vector3(L, 1.2f, 0.2f);
            }
            foreach (var c in g.GetComponentsInChildren<BoxCollider>()) if (c.gameObject != g.gameObject) Object.DestroyImmediate(c);
            report.AppendLine($"{g.name}: {L:0.0} x {W:0.0} m, deck at {deck:0.00}.");
        }

        // ---------- Mill ----------

        static void BuildMill(Transform root, GameObject marker, List<Ditch> ditches, System.Text.StringBuilder report)
        {
            var mb = marker.GetComponent<Renderer>().bounds;
            var seg = SegAt(ditches, mb.center); var d = seg.d;
            var rot = d.rot; var right = rot * Vector3.right; var fwd = rot * Vector3.forward;
            var lm = Quaternion.Inverse(rot) * (new Vector3(mb.center.x, 0, mb.center.z) - d.c);   // marker in ditch space
            float level = seg.level;
            const float R = 4.0f, wheelWidth = 1.6f, flumeWidth = 1.8f;

            var g = new GameObject("Water Mill").transform; g.SetParent(root, false);
            g.SetPositionAndRotation(d.c, rot);

            // Wheel on the east side of the ditch, just inside the bank; house on the bank beside it.
            // Wheel clear of the bank shelf, axle running east into the house wall.
            float wheelX = d.hx - ShelfWidth - wheelWidth / 2 - 0.4f, wheelZ = 0f, wallX = d.hx + 1.0f, houseX = wallX + 2.5f;
            float wheelY = level + R - 0.9f;
            float ground = TerrainHeight(d.c + right * houseX);

            var wheel = new GameObject("Water Wheel").transform; wheel.SetParent(g, false);
            wheel.localPosition = new Vector3(wheelX, wheelY, wheelZ);
            BuildWheel(wheel, R, wheelWidth, wallX - wheelX + 0.2f);
            var sp = wheel.gameObject.AddComponent<Spinner>(); sp.Setup(Vector3.right, -16f, true);
            // Far bearing post in the water.
            float postX = wheelX - wheelWidth / 2 - 0.5f;
            Box(g, "Wheel Bearing Post", new Vector3(postX, (wheelY + seg.bottom) / 2, wheelZ), new Vector3(0.5f, wheelY - seg.bottom + 0.4f, 0.5f), woodDark);
            Box(g, "Wheel Bearing", new Vector3(postX, wheelY, wheelZ), new Vector3(0.6f, 0.55f, 0.7f), metalDark);
            Box(g, "Wall Bearing Plate", new Vector3(wallX - 0.05f, wheelY, wheelZ), new Vector3(0.12f, 1.0f, 1.0f), metalDark);

            BuildHouse(g, new Vector3(houseX, ground, 0), wheelY);

            // Flume: along x under the marker (catches the pipes), then along z over the wheel top.
            float flumeY = wheelY + R + 0.55f, fw = flumeWidth / 2;
            float zA = lm.z, xStart = lm.x - mb.size.x / 2;
            Flume(g, new Vector3(xStart, flumeY, zA), new Vector3(wheelX + fw, flumeY, zA), seg.bottom, flumeWidth);
            Flume(g, new Vector3(wheelX, flumeY, zA - fw), new Vector3(wheelX, flumeY, wheelZ + 0.6f), seg.bottom, flumeWidth);
            var fall = Box(g, "Flume Spill", new Vector3(wheelX, flumeY - 0.45f, wheelZ + 0.55f), new Vector3(flumeWidth - 0.3f, 0.9f, 0.05f), waterFall);
            Object.DestroyImmediate(fall.GetComponent<Collider>());
            report.AppendLine($"Water Mill: wheel Ø{R * 2:0} m at y {wheelY:0.0} (water {level:0.0}), house on the bank at ground {ground:0.0}, flume at {flumeY:0.0}.");
        }

        static void BuildWheel(Transform w, float R, float width, float axleLen)
        {
            int n = 24; float segLen = 2 * Mathf.PI * R / n * 1.04f, hw = width / 2;
            Vector3 Radial(float a) => new Vector3(0, Mathf.Cos(a), Mathf.Sin(a));
            Vector3 Tangent(float a) => new Vector3(0, -Mathf.Sin(a), Mathf.Cos(a));
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2 / n; var q = Quaternion.LookRotation(Tangent(a), Radial(a));
                foreach (float sx in new[] { -1f, 1f })
                {
                    Part(w, "Rim", new Vector3(sx * hw, 0, 0) + Radial(a) * R, q, new Vector3(0.2f, 0.3f, segLen), wood);
                    Part(w, "Rim Steel Band", new Vector3(sx * hw, 0, 0) + Radial(a) * (R + 0.17f), q, new Vector3(0.24f, 0.05f, segLen), metal);
                    Part(w, "Inner Rim", new Vector3(sx * hw, 0, 0) + Radial(a) * (R - 1.1f), q, new Vector3(0.16f, 0.2f, segLen * (R - 1.1f) / R), wood);
                }
                float b = a + Mathf.PI / n; var qb = Quaternion.LookRotation(Tangent(b), Radial(b));
                Part(w, "Paddle", Radial(b) * (R - 0.55f), qb, new Vector3(width - 0.1f, 1.1f, 0.09f), k % 2 == 0 ? woodLight : wood);
                Part(w, "Paddle Steel Edge", Radial(b) * (R + 0.04f), qb, new Vector3(width - 0.1f, 0.06f, 0.12f), metalDark);
            }
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 6; var q = Quaternion.LookRotation(Tangent(a), Radial(a));
                foreach (float sx in new[] { -1f, 1f }) Part(w, "Spoke", new Vector3(sx * (hw - 0.05f), 0, 0), q, new Vector3(0.16f, R * 2 - 0.3f, 0.2f), woodDark);
            }
            var hub = Prism(12, 0.6f, 0.6f, width + 0.4f, "Hub");
            Mesh(w, "Hub", hub, new Vector3(-(width + 0.4f) / 2, 0, 0), Quaternion.Euler(0, 0, -90), metalDark);
            Mesh(w, "Axle", Prism(8, 0.12f, 0.12f, axleLen + 1.2f, "Axle"), new Vector3(-1.2f, 0, 0), Quaternion.Euler(0, 0, -90), metal);
        }

        static void BuildHouse(Transform g, Vector3 o, float axleY)
        {
            var h = new GameObject("Mill House").transform; h.SetParent(g, false); h.localPosition = o;
            const float hw = 2.5f, hd = 3.0f, wallTop = 3.6f, rise = 2.0f;
            Box(h, "Foundation", new Vector3(0, -0.2f, 0), new Vector3(hw * 2 + 0.4f, 1.6f, hd * 2 + 0.4f), stone, true);   // -1.0 .. 0.6, covers the sloped bank
            Box(h, "Foundation Steel Band", new Vector3(0, 0.62f, 0), new Vector3(hw * 2 + 0.44f, 0.08f, hd * 2 + 0.44f), metalDark);
            Box(h, "Walls", new Vector3(0, 2.1f, 0), new Vector3(hw * 2, 3.0f, hd * 2), plaster, true);
            // Timber frame.
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    Box(h, "Corner Post", new Vector3(sx * hw, 2.1f, sz * hd), new Vector3(0.28f, 3.0f, 0.28f), woodDark);
                    Box(h, "Steel Corner Bracket", new Vector3(sx * hw, wallTop - 0.25f, sz * hd), new Vector3(0.34f, 0.3f, 0.34f), metal);
                    Box(h, "Steel Corner Shoe", new Vector3(sx * hw, 0.75f, sz * hd), new Vector3(0.34f, 0.25f, 0.34f), metal);
                }
            foreach (float y in new[] { 0.75f, wallTop - 0.1f })
            {
                foreach (float sx in new[] { -1f, 1f }) Box(h, "Beam", new Vector3(sx * hw, y, 0), new Vector3(0.24f, 0.22f, hd * 2 + 0.2f), woodDark);
                foreach (float sz in new[] { -1f, 1f }) Box(h, "Beam", new Vector3(0, y, sz * hd), new Vector3(hw * 2 + 0.2f, 0.22f, 0.24f), woodDark);
            }
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    var br = Box(h, "Wall Brace", new Vector3(sx * (hw + 0.02f), 2.15f, sz * 1.6f), new Vector3(0.14f, 3.2f, 0.16f), woodDark);
                    br.transform.localRotation = Quaternion.Euler(sz * 32f, 0, 0);
                    Box(h, "Brace Steel Plate", new Vector3(sx * (hw + 0.1f), 2.15f, sz * 1.6f), new Vector3(0.03f, 0.3f, 0.3f), metal);
                }
            // Roof: two slopes, steel ridge, gable ends.
            float half = hw + 0.45f, ang = Mathf.Atan2(rise, hw) * Mathf.Rad2Deg, len = half / Mathf.Cos(ang * Mathf.Deg2Rad);
            foreach (float sx in new[] { -1f, 1f })
            {
                var r = Box(h, "Roof Slope", new Vector3(sx * half / 2, wallTop + rise - half / 2 * rise / hw + 0.1f, 0), new Vector3(len, 0.16f, hd * 2 + 0.8f), roof, true);
                r.transform.localRotation = Quaternion.Euler(0, 0, -sx * ang);
            }
            Box(h, "Roof Steel Ridge", new Vector3(0, wallTop + rise + 0.17f, 0), new Vector3(0.32f, 0.12f, hd * 2 + 0.9f), metal);
            var gable = Gable(hw * 2, rise, 0.2f, "MillGable");
            foreach (float sz in new[] { -1f, 1f })
            {
                Mesh(h, "Gable", gable, new Vector3(0, wallTop, sz * hd), Quaternion.identity, plaster);
                Box(h, "Gable King Post", new Vector3(0, wallTop + rise * 0.45f, sz * (hd + 0.1f)), new Vector3(0.18f, rise * 0.9f, 0.06f), woodDark);
            }
            // Door (south, -z) with steel straps and a lantern; small windows.
            Box(h, "Door", new Vector3(0.9f, 1.7f, -hd - 0.05f), new Vector3(1.2f, 2.1f, 0.1f), woodDark);
            Box(h, "Door Frame", new Vector3(0.9f, 1.72f, -hd - 0.02f), new Vector3(1.45f, 2.3f, 0.06f), wood);
            foreach (float y in new[] { 1.1f, 2.3f }) Box(h, "Door Steel Strap", new Vector3(0.9f, y, -hd - 0.11f), new Vector3(1.15f, 0.09f, 0.03f), metalDark);
            Box(h, "Door Handle", new Vector3(1.3f, 1.65f, -hd - 0.13f), new Vector3(0.06f, 0.18f, 0.05f), metal);
            Window(h, new Vector3(-1.1f, 2.3f, -hd - 0.03f), Vector3.back);
            Window(h, new Vector3(hw + 0.03f, 2.3f, 0f), Vector3.right);
            Window(h, new Vector3(0f, 2.3f, hd + 0.03f), Vector3.forward);
            Box(h, "Lantern", new Vector3(1.85f, 2.6f, -hd - 0.2f), new Vector3(0.22f, 0.3f, 0.22f), windowGlow);
            Box(h, "Lantern Arm", new Vector3(1.85f, 2.82f, -hd - 0.1f), new Vector3(0.05f, 0.05f, 0.25f), metalDark);
            var lg = new GameObject("Lantern Light"); lg.transform.SetParent(h, false); lg.transform.localPosition = new Vector3(1.85f, 2.6f, -hd - 0.45f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.78f, 0.45f); l.intensity = 1.5f; l.range = 5f; l.shadows = LightShadows.None;
        }

        static void Window(Transform h, Vector3 pos, Vector3 facing)
        {
            var q = Quaternion.LookRotation(-facing);
            Part(h, "Window", pos, q, new Vector3(0.8f, 0.8f, 0.06f), windowGlow);
            Part(h, "Window Frame", pos - facing * 0.02f, q, new Vector3(1.0f, 1.0f, 0.05f), woodDark);
            Part(h, "Window Steel Bar", pos + facing * 0.02f, q, new Vector3(0.05f, 0.8f, 0.03f), metalDark);
            Part(h, "Window Steel Bar", pos + facing * 0.02f, q, new Vector3(0.8f, 0.05f, 0.03f), metalDark);
        }

        static void Flume(Transform g, Vector3 a, Vector3 b, float floorY, float width)
        {
            var dir = b - a; float len = dir.magnitude; var q = Quaternion.LookRotation(dir.normalized, Vector3.up); var mid = (a + b) / 2;
            float hw = width / 2, sideH = width * 0.5f;
            var f = new GameObject("Flume").transform; f.SetParent(g, false); f.localPosition = mid; f.localRotation = q;
            Part(f, "Flume Bottom", Vector3.zero, Quaternion.identity, new Vector3(width, 0.14f, len), wood);
            foreach (float sx in new[] { -1f, 1f }) Part(f, "Flume Side", new Vector3(sx * hw, sideH / 2, 0), Quaternion.identity, new Vector3(0.14f, sideH, len), woodLight);
            Part(f, "Flume Water", new Vector3(0, sideH * 0.45f, 0), Quaternion.identity, new Vector3(width - 0.2f, 0.05f, len), waterFall);
            for (float z = -len / 2 + 0.5f; z < len / 2; z += 1.6f)
            {
                Part(f, "Flume Steel Band", new Vector3(0, -0.09f, z), Quaternion.identity, new Vector3(width + 0.2f, 0.06f, 0.14f), metalDark);
                foreach (float sx in new[] { -1f, 1f }) Part(f, "Flume Steel Band", new Vector3(sx * (hw + 0.08f), sideH / 2, z), Quaternion.identity, new Vector3(0.05f, sideH + 0.1f, 0.14f), metalDark);
            }
            // Support trestles down to the ditch floor, with steel cross ties.
            for (float z = -len / 2 + 0.5f; z < len / 2; z += 2.4f)
            {
                foreach (float sx in new[] { -1f, 1f })
                {
                    var wp = f.localPosition + f.localRotation * new Vector3(sx * (hw - 0.1f), 0, z); float hgt = wp.y - floorY;
                    Box(g, "Flume Trestle", new Vector3(wp.x, floorY + hgt / 2 - 0.07f, wp.z), new Vector3(0.28f, hgt, 0.28f), woodDark);
                }
                var c = f.localPosition + f.localRotation * new Vector3(0, -1.2f, z);
                Part(g, "Trestle Steel Tie", c, q, new Vector3(width, 0.1f, 0.1f), metal);
            }
        }

        // ---------- Helpers ----------

        static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            return go;
        }

        static GameObject Part(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 size, Material mat)
        {
            var go = Box(parent, name, pos, size, mat); go.transform.localRotation = rot; return go;
        }

        static GameObject Mesh(Transform parent, string name, Mesh mesh, Vector3 pos, Quaternion rot, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static void MakeMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Materials", "Waterworks");
            wood = Mat("WW_Wood", new Color(0.5f, 0.32f, 0.18f), 0f, 0.25f);
            woodLight = Mat("WW_WoodLight", new Color(0.66f, 0.46f, 0.27f), 0f, 0.25f);
            woodDark = Mat("WW_WoodDark", new Color(0.3f, 0.19f, 0.11f), 0f, 0.2f);
            metal = Mat("WW_Metal", new Color(0.6f, 0.62f, 0.66f), 0.85f, 0.45f);
            metalDark = Mat("WW_MetalDark", new Color(0.26f, 0.27f, 0.3f), 0.8f, 0.35f);
            stone = Mat("WW_Stone", new Color(0.56f, 0.54f, 0.5f), 0f, 0.1f);
            plaster = Mat("WW_Plaster", new Color(0.86f, 0.8f, 0.66f), 0f, 0.1f);
            roof = Mat("WW_RoofTiles", new Color(0.5f, 0.22f, 0.15f), 0f, 0.2f);
            windowGlow = Mat("WW_WindowGlow", new Color(0.3f, 0.22f, 0.12f), 0f, 0.5f, new Color(0.9f, 0.6f, 0.28f));
            water = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Water.mat");
            waterFall = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WaterStream.mat");
        }

        static Material Mat(string name, Color c, float metallic, float smoothness, Color? emission = null)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh SaveMesh(Mesh m, string name)
        {
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Meshes", "Waterworks");
            m.name = name.Replace(',', '.');
            string path = MeshDir + "/" + m.name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        class MB
        {
            readonly List<Vector3> v = new List<Vector3>(); readonly List<int> t = new List<int>(); public Vector3 Centre;
            public void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - Centre) < 0) { var x = b; b = c; c = x; }
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }
            public Mesh Done() { var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m; }
        }

        static Mesh Prism(int sides, float r0, float r1, float h, string name)
        {
            var b = new MB { Centre = new Vector3(0, h * 0.5f, 0) };
            Vector3 P(int i, float r, float y) { float a = i * Mathf.PI * 2f / sides; return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r); }
            for (int i = 0; i < sides; i++)
            {
                var a0 = P(i, r0, 0); var a1 = P(i + 1, r0, 0); var b0 = P(i, r1, h); var b1 = P(i + 1, r1, h);
                b.Tri(Vector3.zero, a0, a1); b.Quad(a0, a1, b1, b0); b.Tri(new Vector3(0, h, 0), b0, b1);
            }
            return SaveMesh(b.Done(), $"WW_{name}_{sides}_{r0:0.##}_{r1:0.##}_{h:0.##}");
        }

        static Mesh Gable(float w, float h, float d, string name)
        {
            var b = new MB { Centre = new Vector3(0, h / 3f, 0) };
            Vector3 L(float z) => new Vector3(-w / 2, 0, z);
            Vector3 R(float z) => new Vector3(w / 2, 0, z);
            Vector3 T(float z) => new Vector3(0, h, z);
            float f = -d / 2, k = d / 2;
            b.Tri(L(f), R(f), T(f)); b.Tri(L(k), R(k), T(k));
            b.Quad(L(f), L(k), R(k), R(f)); b.Quad(L(f), T(f), T(k), L(k)); b.Quad(R(f), R(k), T(k), T(f));
            return SaveMesh(b.Done(), $"WW_{name}_{w:0.##}_{h:0.##}_{d:0.##}");
        }
    }
}
