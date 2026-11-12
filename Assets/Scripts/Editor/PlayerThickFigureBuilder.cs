using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Builds the thick cube figure of the player (bulky barrel torso, thick limbs, fists, boots, small head with eye
    /// sockets). Every body part is filled with small bevelled cubes over a dark core that shows in the gaps.
    /// The meshes are written into the existing Assets/Meshes/PlayerCubes/Cubes_*.asset files, so Player and the
    /// cutscene actors keep their references; the shoulder and hip joints are moved out to fit the wider body.
    /// </summary>
    public static class PlayerThickFigureBuilder
    {
        const string MeshDir = "Assets/Meshes/PlayerCubes";
        static readonly string[] Prefabs =
        {
            "Assets/Prefabs/Player.prefab",
            "Assets/Prefabs/Cutscenes/Actor_Player1.prefab",
            "Assets/Prefabs/Cutscenes/Actor_Player2.prefab",
        };

        // Proportions follow the user's 4-view reference model (front/side/back/three-quarter screenshots), measured
        // with the figure scaled to 2.02 m: deep barrel torso (0.44 deep; chest 0.50 wide, belly 0.60), wide low head
        // (0.33 x 0.22 x 0.33) with big framed eyes, upper arm 0.19 (1.72-1.40 m), bigger forearm 0.23 (1.40-1.10),
        // hand 0.17, thigh 0.27, shin block 0.27, ankle 0.21, tall long boot (0.21 high, 0.36 long). Cubes ~5.5 cm.
        public const float HipX = 0.17f, ShoulderX = 0.30f;
        // Shoulder and elbow heights (UpperBody-local / Shoulder-local y); the elbow sits where the helper model's
        // arm bends (about 1.26 m).
        public const float ShoulderY = 0.56f, ElbowY = -0.42f;
        public const float ArmSpread = 10f, LegSpread = 7f;
        // Player only: the back mount sits on the back of the chest.
        public const float BackMountZ = -0.27f;
        // Hanging from a ledge: how close the body gets to the wall, and the hand spacing; grip point in the hand.
        public const float HangCloser = 0.15f, HangHandSpacing = 0.26f, GripY = -0.32f;

        struct Shape
        {
            public Vector3 center, size; public float round; public bool hole, dark; public int group; public float cube;
            // > 0: super-ellipsoid with this exponent (2 = ellipsoid, higher = fuller, more barrel-like) instead of a box.
            public float ell;
            // A single plain bevelled box of exactly this size (not filled with cubes); dark = grey material.
            public bool slab;
            // Slab material: 0 white, 1 grey (core), 2 dark (eye frames and pupils, head only).
            public int mat;
            // Filled from the helper model: the cubes owned by this tag (see VoxInside).
            public string vox;
        }

        class Part
        {
            public string name; public float cube; public readonly List<Shape> shapes = new List<Shape>();
            // Chance that a 2x2x2 block of cubes becomes one big cube (mixed cube sizes).
            public float merge = 0f;
            // How much each cube is pushed, rotated and resized at random.
            public float jitter = 0f;
            int group; float groupCube;
            public Part(string name, float cube) { this.name = name; this.cube = cube; }
            // Shapes added after this get their own cube lattice.
            public Part NewGroup(float cube = 0f) { group++; groupCube = cube; return this; }
            public Part Box(float cx, float cy, float cz, float sx, float sy, float sz, float round = 0f)
            { shapes.Add(new Shape { center = new Vector3(cx, cy, cz), size = new Vector3(sx, sy, sz), round = round, group = group, cube = groupCube }); return this; }
            public Part Oval(float cx, float cy, float cz, float sx, float sy, float sz, float e = 2.6f)
            { shapes.Add(new Shape { center = new Vector3(cx, cy, cz), size = new Vector3(sx, sy, sz), ell = e, group = group, cube = groupCube }); return this; }
            public Part NoMerge() { merge = 0f; return this; }
            // Cubes filled from the helper model (a cube lattice of its own when it is not the first shape).
            // gridY moves the lattice up so it lines up with another part's (the Pelvis with the UpperBody parts).
            public Part Vox(string tag, float cube = 0f, float gridY = 0f)
            {
                if (shapes.Count > 0) NewGroup(cube);
                shapes.Add(new Shape { center = new Vector3(0f, gridY, 0f), size = Vector3.one * VoxRegion, vox = tag, group = group, cube = groupCube });
                return this;
            }
            public Part Jitter(float j) { jitter = j; return this; }
            public Part Hole(float cx, float cy, float cz, float sx, float sy, float sz)
            { shapes.Add(new Shape { center = new Vector3(cx, cy, cz), size = new Vector3(sx, sy, sz), hole = true, group = group }); return this; }
            public Part Dark(float cx, float cy, float cz, float sx, float sy, float sz)
            { shapes.Add(new Shape { center = new Vector3(cx, cy, cz), size = new Vector3(sx, sy, sz), dark = true }); return this; }
            public Part Slab(float cx, float cy, float cz, float sx, float sy, float sz, int mat = 0)
            { shapes.Add(new Shape { center = new Vector3(cx, cy, cz), size = new Vector3(sx, sy, sz), slab = true, mat = mat }); return this; }
        }

        // Hand (elbow-local, right under the forearm): the palm is a small block of cubes. Under it hang 4 separate
        // fingers side by side from back to front. Each finger has 4 blocks curled into a C: 2 down the outer side,
        // across the bottom, and the tip back up on the inner side. The palm faces the body. In front, on the inner
        // side, a 2-block thumb points down over the finger tips.
        const float HandC = 0.034f;
        // The hand is drawn below at forearm size and scaled down around the top of the palm: it must stay clearly
        // thinner than the forearm (about 2/3 of its width).
        const float HandKX = 0.72f, HandKY = 0.85f;
        const float HandTop = -0.33f; // elbow-local top of the palm, right under the forearm
        static Part Hand(Part p, float s)
        {
            // x in the body-out direction (outer > 0), mirrored per side by s
            void Seg(float x0, float x1, float y0, float y1, float z0, float z1) =>
                p.Slab(s * (0.01f + ((x0 + x1) * 0.5f - 0.01f) * HandKX), HandTop + ((y0 + y1) * 0.5f + 0.30f) * HandKY,
                       (z0 + z1) * 0.5f * HandKX, (x1 - x0) * HandKX, (y1 - y0) * HandKY, (z1 - z0) * HandKX);
            // palm: 4 x 2 x 4 cubes, x -0.08..0.10, y -0.39..-0.30
            p.Box(s * 0.01f, HandTop - 0.045f * HandKY, 0f, 0.18f * HandKX, 0.09f * HandKY, 0.18f * HandKX);
            const float fw = 0.04f, fg = 0.005f;
            for (int k = 0; k < 4; k++) // 0 = back (little finger) .. 3 = front (index)
            {
                float z0 = -0.09f + k * (fw + fg), z1 = z0 + fw;
                float up = (k == 0 || k == 3) ? 0.012f : 0f; // outer fingers a little shorter
                Seg(0.045f, 0.10f, -0.445f + up * 0.5f, -0.39f, z0, z1);        // knuckle, down the outer side
                Seg(0.04f, 0.095f, -0.50f + up, -0.443f + up * 0.5f, z0, z1);   // first joint
                Seg(-0.03f, 0.04f, -0.525f + up, -0.465f + up, z0, z1);  // middle block, across the bottom
                Seg(-0.075f, -0.03f, -0.505f + up, -0.46f + up, z0, z1); // tip, back up on the inner side
            }
            // thumb: from the palm's inner front corner down over the finger tips
            Seg(-0.105f, -0.05f, -0.395f, -0.33f, 0.025f, 0.085f);
            Seg(-0.10f, -0.055f, -0.43f, -0.393f, 0.03f, 0.08f);
            return p;
        }

        // Every body part except the head is filled from the helper model
        // (Assets/Helper Models/Meshy_AI_Cube_Sentinel_20261108_generate.fbx): the model is turned into a grid of
        // 2.75 cm cells (scaled to 2.0 m, feet on the ground), and each cube of a part is kept when its centre, in the
        // rest pose, is inside the model and closest to that part's bone (see VoxInside / Owner). So the cube figure
        // covers the helper model, and each piece still moves with its own joint. The hand gets smaller cubes.
        // One cube size for the whole body (the hand: 0.7 of it), all cubes straight on their grid.
        const float Body = 0.055f;
        static List<Part> Parts()
        {
            var parts = new List<Part>();
            // Pelvis (Hips, 1.029 m) on the same cube grid as Abdomen and Chest (UpperBody, 1.118 m).
            parts.Add(new Part("Pelvis", Body).Vox("Pelvis", 0f, 1.118f - 1.029f));
            foreach (float s in new[] { -1f, 1f })
            {
                string side = s < 0f ? "_L" : "_R";
                parts.Add(new Part("Thigh" + side, Body).Vox("Thigh" + side));
                parts.Add(new Part("Shin" + side, Body).Vox("Shin" + side));
                parts.Add(new Part("Foot" + side, Body).Vox("Foot" + side));
                parts.Add(new Part("UpperArm" + side, Body).Vox("UpperArm" + side));
                parts.Add(new Part("Forearm" + side, Body).Vox("Forearm" + side).Vox("Hand" + side, Body * 0.7f));
            }
            parts.Add(new Part("Abdomen", Body).Vox("Abdomen"));
            parts.Add(new Part("Chest", Body).Vox("Chest"));
            // Neck: a small 2 x 2 block, one cube high, between the top of the chest (1.69 m) and the head.
            parts.Add(new Part("Neck", Body).NoMerge().Box(0f, 1.7225f - 1.118f, -0.03f, 2f * Body, Body, 2f * Body));
            // Head (UpperBody-local): the helper's head box - 0.28 wide, 0.235 tall, 0.28 deep (6 x 5 x 6 cubes),
            // bottom at 1.745 m, set a little back - with two goggle eyes in the upper half of the face: a dark frame
            // standing a little out of the face, a light grey inside set back, a dark square pupil; white bars around.
            float c = 0.047f, hy = (HeadBottom - 1.118f) + 2.5f * c, hz = -0.03f, fz = hz + 2.5f * c, ey = hy + 0.5f * c;
            var head = new Part("Head", c).NoMerge()
                .Box(0f, hy, hz, 6 * c, 5 * c, 6 * c, 0.008f)
                .Hole(0f, ey, fz, 7 * c, 1.9f * c, 0.9f * c);
            float eh = 2f * c - 0.004f, rim = 0.02f, mid = 0.024f, t = 0.018f, pupil = 0.036f;
            float ew = (6f * c - 2f * rim - mid) * 0.5f, ex = mid * 0.5f + ew * 0.5f;
            float back = hz + 2f * c, face = hz + 3f * c;
            head.Slab(0f, ey, fz, mid, eh, 0.9f * c);
            foreach (float s in new[] { -1f, 1f })
            {
                head.Slab(s * (3f * c - rim * 0.5f), ey, fz, rim, eh, 0.9f * c);
                float fd = face + 0.03f - back, fzc = back + fd * 0.5f; // dark frame, 3 cm proud of the face
                head.Slab(s * ex, ey + (eh - t) * 0.5f, fzc, ew, t, fd, 2);
                head.Slab(s * ex, ey - (eh - t) * 0.5f, fzc, ew, t, fd, 2);
                head.Slab(s * ex - (ew - t) * 0.5f, ey, fzc, t, eh - 2f * t, fd, 2);
                head.Slab(s * ex + (ew - t) * 0.5f, ey, fzc, t, eh - 2f * t, fd, 2);
                float id = 0.75f * c; // light grey inside and the dark pupil on it
                head.Slab(s * ex, ey, back + id * 0.5f, ew - 2f * t, eh - 2f * t, id, 1);
                head.Slab(s * ex, ey, back + id + 0.004f, pupil, pupil, 0.012f, 2);
            }
            head.Slab(0f, hy + 0.5f * c, hz - 3f * c - 0.02f, c, 1.2f * c, 0.05f); // bump on the back of the head
            parts.Add(head);
            return parts;
        }

        // ---------- helper model ----------

        const string HelperPath = "Assets/Helper Models/Meshy_AI_Cube_Sentinel_20261108_generate.fbx";
        const float HelperHeight = 2.0f, HeadBottom = 1.745f, VoxRegion = 1.8f;
        const float OccC = 0.0275f;
        static readonly Vector3 OccLo = new Vector3(-0.7f, -0.02f, -0.5f);
        static bool[,,] occ;
        // Rest-pose world matrix of each part mesh, and the bones used to share the helper model out between parts.
        static readonly Dictionary<string, Matrix4x4> restM = new Dictionary<string, Matrix4x4>();
        static readonly List<(string tag, Vector3 a, Vector3 b, float r)> bones = new List<(string, Vector3, Vector3, float)>();

        // Scanline fill of the helper mesh: for every (y, z) row of cells, the crossings of the mesh along x, inside
        // between each pair of crossings. The mesh is closed (every row has an even number of crossings).
        static string LoadHelper()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(HelperPath);
            if (go == null) return "helper model missing: " + HelperPath;
            var mf = go.GetComponentInChildren<MeshFilter>(); var mesh = mf.sharedMesh; var m = mf.transform.localToWorldMatrix;
            var v = mesh.vertices; var tri = mesh.triangles; var wv = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) wv[i] = m.MultiplyPoint3x4(v[i]);
            var bb = new Bounds(wv[0], Vector3.zero); foreach (var p in wv) bb.Encapsulate(p);
            float k = HelperHeight / bb.size.y; var sh = new Vector3(-bb.center.x, -bb.min.y, -bb.center.z);
            for (int i = 0; i < wv.Length; i++) wv[i] = (wv[i] + sh) * k;
            int NX = (int)(1.4f / OccC), NY = (int)(2.06f / OccC), NZ = (int)(1.0f / OccC);
            occ = new bool[NX, NY, NZ];
            var bins = new List<int>[NY, NZ];
            for (int t = 0; t < tri.Length; t += 3)
            {
                Vector3 A = wv[tri[t]], B = wv[tri[t + 1]], C = wv[tri[t + 2]];
                int y0 = Mathf.FloorToInt((Mathf.Min(A.y, Mathf.Min(B.y, C.y)) - OccLo.y) / OccC - 0.5f);
                int y1 = Mathf.CeilToInt((Mathf.Max(A.y, Mathf.Max(B.y, C.y)) - OccLo.y) / OccC - 0.5f);
                int z0 = Mathf.FloorToInt((Mathf.Min(A.z, Mathf.Min(B.z, C.z)) - OccLo.z) / OccC - 0.5f);
                int z1 = Mathf.CeilToInt((Mathf.Max(A.z, Mathf.Max(B.z, C.z)) - OccLo.z) / OccC - 0.5f);
                for (int y = Mathf.Max(0, y0); y <= Mathf.Min(NY - 1, y1); y++)
                    for (int z = Mathf.Max(0, z0); z <= Mathf.Min(NZ - 1, z1); z++)
                    {
                        if (bins[y, z] == null) bins[y, z] = new List<int>();
                        bins[y, z].Add(t);
                    }
            }
            var xs = new List<float>();
            for (int y = 0; y < NY; y++)
                for (int z = 0; z < NZ; z++)
                {
                    if (bins[y, z] == null) continue;
                    float py = OccLo.y + (y + 0.5f) * OccC + 1e-4f, pz = OccLo.z + (z + 0.5f) * OccC + 1.3e-4f;
                    xs.Clear();
                    foreach (int t in bins[y, z])
                    {
                        Vector3 A = wv[tri[t]], B = wv[tri[t + 1]], C = wv[tri[t + 2]];
                        float d = (B.y - A.y) * (C.z - A.z) - (C.y - A.y) * (B.z - A.z);
                        if (Mathf.Abs(d) < 1e-12f) continue;
                        float u = ((py - A.y) * (C.z - A.z) - (C.y - A.y) * (pz - A.z)) / d;
                        float w = ((B.y - A.y) * (pz - A.z) - (py - A.y) * (B.z - A.z)) / d;
                        if (u < 0f || w < 0f || u + w > 1f) continue;
                        xs.Add(A.x + u * (B.x - A.x) + w * (C.x - A.x));
                    }
                    xs.Sort();
                    for (int i = 0; i + 1 < xs.Count; i += 2)
                        for (int x = 0; x < NX; x++)
                        {
                            float px = OccLo.x + (x + 0.5f) * OccC;
                            if (px >= xs[i] && px <= xs[i + 1]) occ[x, y, z] = true;
                        }
                }
            return null;
        }

        static bool Occ(Vector3 w)
        {
            int x = Mathf.FloorToInt((w.x - OccLo.x) / OccC), y = Mathf.FloorToInt((w.y - OccLo.y) / OccC), z = Mathf.FloorToInt((w.z - OccLo.z) / OccC);
            if (x < 0 || y < 0 || z < 0 || x >= occ.GetLength(0) || y >= occ.GetLength(1) || z >= occ.GetLength(2)) return false;
            return occ[x, y, z];
        }

        // Rest pose of the rig (after the joints are moved): part matrices and the bone segments. Each bone has a
        // radius; a point goes to the bone it is nearest to relative to that radius, so the wide torso keeps its
        // sides and the thin arms keep theirs.
        static void ReadRest()
        {
            restM.Clear(); bones.Clear();
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs[0]);
            var f = (GameObject)Object.Instantiate(pf.transform.Find("Visual/CubeFigure").gameObject);
            f.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                f.transform.position = Vector3.zero; f.transform.rotation = Quaternion.identity;
                f.GetComponent<CoopGame.PlayerCubeRig>().PreviewRest();
                foreach (var mfr in f.GetComponentsInChildren<MeshFilter>(true)) restM[mfr.name] = mfr.transform.localToWorldMatrix;
                Vector3 P(string path) => f.transform.Find(path).position;
                bones.Add(("Torso", new Vector3(0f, 1.0f, -0.04f), new Vector3(0f, 1.70f, -0.04f), 0.30f));
                foreach (var sd in new[] { "_L", "_R" })
                {
                    var elT = f.transform.Find("UpperBody/Shoulder" + sd + "/Elbow" + sd);
                    Vector3 sh = P("UpperBody/Shoulder" + sd), el = elT.position, down = -elT.up;
                    Vector3 wr = el + down * 0.25f, he = el + down * 0.38f;
                    bones.Add(("UpperArm" + sd, sh, el, 0.10f));
                    bones.Add(("Forearm" + sd, el, wr, 0.10f));
                    bones.Add(("Hand" + sd, wr, he, 0.08f));
                    Vector3 hip = P("Hips/Hip" + sd), kn = P("Hips/Hip" + sd + "/Knee" + sd), an = P("Hips/Hip" + sd + "/Knee" + sd + "/Ankle" + sd);
                    bones.Add(("Thigh" + sd, hip, kn, 0.12f));
                    bones.Add(("Shin" + sd, kn, an, 0.13f));
                    bones.Add(("Foot" + sd, an, new Vector3(an.x, 0.04f, an.z + 0.10f), 0.15f));
                }
            }
            finally { Object.DestroyImmediate(f); }
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        static string Owner(Vector3 w)
        {
            if (w.y >= HeadBottom) return "Head";
            string best = null; float bd = float.MaxValue;
            foreach (var bn in bones)
            {
                // Compared as seen from the front (depth ignored): the helper's limbs do not sit exactly where the
                // rig's bones are front to back, but they are always beside each other.
                Vector3 q = new Vector3(w.x, w.y, 0f), a = new Vector3(bn.a.x, bn.a.y, 0f), b = new Vector3(bn.b.x, bn.b.y, 0f);
                float d = SegDist(q, a, b) / bn.r; if (d < bd) { bd = d; best = bn.tag; }
            }
            if (best != "Torso") return best;
            return w.y >= 1.69f ? "Neck" : w.y >= 1.44f ? "Chest" : w.y >= 1.06f ? "Abdomen" : "Pelvis";
        }

        // Like Owner, but with half a cube of slack at the borders: each part has its own cube lattice, so without it a
        // cube right on a border would be dropped by both neighbours and leave a hole (knees, ankles, waist).
        static bool Owns(Vector3 w, string tag, float c)
        {
            if (w.y >= HeadBottom) return false;
            bool torsoTag = tag == "Neck" || tag == "Chest" || tag == "Abdomen" || tag == "Pelvis";
            string boneTag = torsoTag ? "Torso" : tag;
            float best = float.MaxValue, mine = float.MaxValue, myR = 1f;
            foreach (var bn in bones)
            {
                Vector3 q = new Vector3(w.x, w.y, 0f), a = new Vector3(bn.a.x, bn.a.y, 0f), b = new Vector3(bn.b.x, bn.b.y, 0f);
                float d = SegDist(q, a, b) / bn.r;
                best = Mathf.Min(best, d);
                if (bn.tag == boneTag) { mine = d; myR = bn.r; }
            }
            if (mine > best + 0.5f * c / myR) return false;
            if (!torsoTag) return true;
            float lo = tag == "Neck" ? 1.69f : tag == "Chest" ? 1.44f : tag == "Abdomen" ? 1.06f : -9f;
            float hi = tag == "Neck" ? HeadBottom : tag == "Chest" ? 1.69f : tag == "Abdomen" ? 1.44f : 1.06f;
            // Hard borders: the torso parts share one cube grid, so each row belongs to exactly one part.
            return w.y >= lo && w.y < hi;
        }

        // The helper model made fat: the torso is wider and its belly bulges well forward (largest at belly height,
        // about 1.2 m), and arms and legs are thicker (any point within FatLimb of the model counts as inside).
        const float FatLimb = 0.022f, TorsoZ = -0.04f;
        static bool FatOcc(Vector3 w, string tag)
        {
            bool torso = tag == "Chest" || tag == "Abdomen" || tag == "Pelvis";
            if (torso)
            {
                float belly = Mathf.Exp(-Mathf.Pow((w.y - 1.2f) / 0.2f, 2f));
                float sx = 1.12f + 0.12f * belly;
                float sz = w.z > TorsoZ ? 1.1f + 0.55f * belly : 1.08f;
                return Occ(new Vector3(w.x / sx, w.y, TorsoZ + (w.z - TorsoZ) / sz));
            }
            if (tag == "Neck" || tag.StartsWith("Hand")) return Occ(w);
            if (Occ(w)) return true;
            for (int i = 0; i < 6; i++)
            {
                var d = Vector3.zero; d[i >> 1] = (i & 1) == 0 ? FatLimb : -FatLimb;
                if (Occ(w + d)) return true;
            }
            return false;
        }

        // Cube of size c at part-local p of part partName: mostly inside the helper model (4 of 9 samples: the centre
        // and the 8 points a quarter cube towards its corners) and owned by tag?
        static bool VoxInside(string partName, string tag, Vector3 p, float c)
        {
            var m = restM[partName];
            int n = FatOcc(m.MultiplyPoint3x4(p), tag) ? 1 : 0;
            for (int k = 0; k < 8; k++)
            {
                var o = new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f) * (c * 0.25f);
                if (FatOcc(m.MultiplyPoint3x4(p + o), tag)) n++;
            }
            // Fingers are thinner than a cube, so the hand keeps any cube at least 2 samples deep in the model.
            return n >= (tag.StartsWith("Hand") ? 2 : 4) && Owns(m.MultiplyPoint3x4(p), tag, c);
        }

        [MenuItem("Tools/Coop/Player/Rebuild Thick Cube Figure")]
        public static void RebuildMenu() => Debug.Log(Rebuild());

        public static string Rebuild()
        {
            var log = new System.Text.StringBuilder();
            int total = 0;
            // Joints first, so the helper model is shared out over the final rest pose.
            foreach (var p in Prefabs) MoveJoints(p);
            string err = LoadHelper(); if (err != null) return err;
            ReadRest();
            foreach (var part in Parts())
            {
                string path = MeshDir + "/Cubes_" + part.name + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) { log.AppendLine("missing " + path); continue; }
                Build(part, mesh);
                total += lastCubes;
                EditorUtility.SetDirty(mesh);
                log.AppendLine(part.name + " cubes=" + lastCubes + " verts=" + mesh.vertexCount + " size=" + mesh.bounds.size.ToString("F3"));
            }
            AssetDatabase.SaveAssets();
            log.AppendLine("TOTAL cubes=" + total);
            foreach (var p in Prefabs) log.AppendLine(MoveJoints(p));
            return log.ToString();
        }

        const string EyeDarkMat = "Assets/Materials/Player_CubesEyeDark.mat";

        // Dark material for the eye frames and pupils (third submesh of the head), made from the core material.
        static Material EnsureEyeDark()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(EyeDarkMat);
            if (m != null) return m;
            AssetDatabase.CopyAsset("Assets/Materials/Player_CubesCore.mat", EyeDarkMat);
            m = AssetDatabase.LoadAssetAtPath<Material>(EyeDarkMat);
            var col = new Color(0.16f, 0.16f, 0.17f, 1f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssets();
            return m;
        }

        static string MoveJoints(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var fig = root.transform.Find("Visual/CubeFigure");
                if (fig == null) return path + ": no CubeFigure";
                void SetX(string p, float x)
                {
                    var t = fig.Find(p); if (t == null) return;
                    var lp = t.localPosition; lp.x = Mathf.Sign(lp.x) * x; t.localPosition = lp;
                }
                SetX("Hips/Hip_L", HipX); SetX("Hips/Hip_R", HipX);
                SetX("UpperBody/Shoulder_L", ShoulderX); SetX("UpperBody/Shoulder_R", ShoulderX);
                foreach (var sd in new[] { "_L", "_R" })
                {
                    var sh = fig.Find("UpperBody/Shoulder" + sd);
                    if (sh != null) { var lp = sh.localPosition; lp.y = ShoulderY; sh.localPosition = lp; }
                    var el = fig.Find("UpperBody/Shoulder" + sd + "/Elbow" + sd);
                    if (el != null) { var lp = el.localPosition; lp.y = ElbowY; el.localPosition = lp; }
                }
                var headR = fig.Find("UpperBody/Head") ? fig.Find("UpperBody/Head").GetComponent<MeshRenderer>() : null;
                if (headR != null && headR.sharedMaterials.Length >= 2)
                {
                    var mats = headR.sharedMaterials;
                    headR.sharedMaterials = new[] { mats[0], mats[1], EnsureEyeDark() };
                }
                var rig = fig.GetComponent<CoopGame.PlayerCubeRig>();
                if (rig != null)
                {
                    var so = new SerializedObject(rig);
                    so.FindProperty("hangCloser").floatValue = HangCloser;
                    so.FindProperty("hangHandSpacing").floatValue = HangHandSpacing;
                    so.FindProperty("armSpread").floatValue = ArmSpread;
                    so.FindProperty("legSpread").floatValue = LegSpread;
                    var gp = so.FindProperty("gripOffset"); var g = gp.vector3Value; g.y = GripY; gp.vector3Value = g;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                if (path.EndsWith("/Player.prefab"))
                {
                    var back = root.transform.Find("Visual/BackMount");
                    if (back != null) { var lp = back.localPosition; lp.z = BackMountZ; back.localPosition = lp; }
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return path + ": joints moved";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ---------- mesh ----------

        static float RoundBox(Vector3 p, Vector3 half, float r)
        {
            r = Mathf.Min(r, Mathf.Min(half.x, Mathf.Min(half.y, half.z)));
            Vector3 q = new Vector3(Mathf.Abs(p.x), Mathf.Abs(p.y), Mathf.Abs(p.z)) - half + Vector3.one * r;
            Vector3 qm = Vector3.Max(q, Vector3.zero);
            return qm.magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f) - r;
        }

        // Cube centre inside the super-ellipsoid (grown by a third of a cube so the outline comes out full).
        static bool InOval(Vector3 p, Shape s, float c)
        {
            Vector3 d = p - s.center, h = s.size * 0.5f + Vector3.one * (c * 0.3f);
            float e = s.ell;
            return Mathf.Pow(Mathf.Abs(d.x) / h.x, e) + Mathf.Pow(Mathf.Abs(d.y) / h.y, e) + Mathf.Pow(Mathf.Abs(d.z) / h.z, e) <= 1f;
        }

        static bool InBox(Vector3 p, Shape s)
        {
            Vector3 d = p - s.center, h = s.size * 0.5f;
            return Mathf.Abs(d.x) <= h.x && Mathf.Abs(d.y) <= h.y && Mathf.Abs(d.z) <= h.z;
        }

        static float Hash(int x, int y, int z, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (uint)(salt * 2654435761u);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        static int lastCubes;

        static void Build(Part part, Mesh mesh)
        {
            lastCubes = 0;
            float baseC = part.cube;
            var allSolid = new List<Shape>(); var holes = new List<Shape>(); var darks = new List<Shape>(); var slabs = new List<Shape>();
            foreach (var s in part.shapes) (s.slab ? slabs : s.hole ? holes : s.dark ? darks : allSolid).Add(s);
            var verts = new List<Vector3>(); var norms = new List<Vector3>();
            var tris = new[] { new List<int>(), new List<int>(), new List<int>() };
            Vector3Int[] dirs = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
            // Each group of shapes (e.g. the fist) gets its own lattice, so small parts still come out whole.
            var groups = new List<int>(); foreach (var s in allSolid) if (!groups.Contains(s.group)) groups.Add(s.group);
            foreach (int g in groups)
            {
            var solid = allSolid.FindAll(s => s.group == g);
            var main = solid[0];
            float c = main.cube > 0f ? main.cube : baseC;
            float gap = 0.05f * c, bevel = 0.1f * c;
            // Lattice: symmetric around the group's first shape (cube centres on it for odd counts, between for even).
            Vector3 offset = Vector3.zero;
            for (int a = 0; a < 3; a++)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(main.size[a] / c));
                offset[a] = main.center[a] + (n % 2 == 0 ? 0.5f * c : 0f);
            }
            Bounds all = new Bounds(solid[0].center, solid[0].size);
            foreach (var s in solid) all.Encapsulate(new Bounds(s.center, s.size));
            Vector3Int lo = Vector3Int.FloorToInt((all.min - offset) / c) - Vector3Int.one;
            Vector3Int hi = Vector3Int.CeilToInt((all.max - offset) / c) + Vector3Int.one;

            Vector3 Center(int x, int y, int z) => offset + new Vector3(x, y, z) * c;
            var filled = new HashSet<Vector3Int>();
            for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                    for (int z = lo.z; z <= hi.z; z++)
                    {
                        Vector3 p = Center(x, y, z);
                        bool inside = false;
                        foreach (var s in solid)
                            if (s.vox != null ? VoxInside(part.name, s.vox, p, c)
                                : s.ell > 0f ? InOval(p, s, c) : RoundBox(p - s.center, s.size * 0.5f, s.round) <= 1e-4f) { inside = true; break; }
                        if (!inside) continue;
                        foreach (var h in holes) if (h.group == g && InBox(p, h)) { inside = false; break; }
                        if (inside) filled.Add(new Vector3Int(x, y, z));
                    }

            // Straighten the helper-filled cubes: each row of cubes (along the part's own y) becomes one even
            // rectangle - as wide and deep as most of that row's cubes, smoothed against the rows above and below,
            // with its corners cut - so the part is built from straight, parallel layers with nothing sticking out.
            // The torso rows are also made symmetric left/right. Hands keep their fingers as they are.
            int vsi = solid.FindIndex(s => s.vox != null); string vtag = vsi >= 0 ? solid[vsi].vox : null;
            if (vtag != null && vtag.StartsWith("Hand"))
            {
                // Hand: nothing above the wrist (forearm-local y -0.25), and no loose cubes hanging off on their own.
                filled.RemoveWhere(v => Center(v.x, v.y, v.z).y > -0.24f);
                for (int pass = 0; pass < 2; pass++)
                {
                    var loose = new List<Vector3Int>();
                    foreach (var v in filled) { int k = 0; foreach (var d in dirs) if (filled.Contains(v + d)) k++; if (k == 0) loose.Add(v); }
                    foreach (var v in loose) filled.Remove(v);
                }
            }
            if (vtag != null && !vtag.StartsWith("Hand") && filled.Count > 0)
            {
                bool sym = vtag == "Pelvis" || vtag == "Abdomen" || vtag == "Chest";
                var rows = new SortedDictionary<int, List<Vector3Int>>();
                foreach (var v in filled) { if (!rows.TryGetValue(v.y, out var l)) rows[v.y] = l = new List<Vector3Int>(); l.Add(v); }
                var ext = new Dictionary<int, int[]>();
                foreach (var kv in rows)
                {
                    int[] Span(System.Func<Vector3Int, int> key)
                    {
                        var cnt = new Dictionary<int, int>(); int mx = 0;
                        foreach (var v in kv.Value) { int k = key(v); cnt.TryGetValue(k, out int n); cnt[k] = ++n; mx = Mathf.Max(mx, n); }
                        int a0 = int.MaxValue, a1 = int.MinValue;
                        foreach (var e in cnt) if (e.Value * 3 >= mx) { a0 = Mathf.Min(a0, e.Key); a1 = Mathf.Max(a1, e.Key); }
                        return new[] { a0, a1 };
                    }
                    var sx = Span(v => v.x); var sz = Span(v => v.z);
                    if (sym) { int m = Mathf.Max(-sx[0], sx[1]); sx[0] = -m; sx[1] = m; }
                    ext[kv.Key] = new[] { sx[0], sx[1], sz[0], sz[1] };
                }
                var ys = new List<int>(ext.Keys);
                filled.Clear();
                for (int i = 0; i < ys.Count; i++)
                {
                    var r = (int[])ext[ys[i]].Clone();
                    if (i > 0 && i < ys.Count - 1 && ys[i - 1] == ys[i] - 1 && ys[i + 1] == ys[i] + 1)
                        for (int a = 0; a < 4; a++)
                        {
                            // Median over the rows around this one (3 for the torso, 5 for arms and legs).
                            var win = new List<int>();
                            for (int d = sym ? -1 : -2; d <= (sym ? 1 : 2); d++)
                                if (ext.TryGetValue(ys[i] + d, out var e)) win.Add(e[a]);
                            win.Sort(); r[a] = win[win.Count / 2];
                        }
                    else if (!sym) // limbs only: the torso keeps its wide top and bottom rows
                    {
                        // End row: no wider or deeper than the row next to it.
                        int nb = i > 0 && ys[i - 1] == ys[i] - 1 ? i - 1 : i < ys.Count - 1 && ys[i + 1] == ys[i] + 1 ? i + 1 : -1;
                        if (nb >= 0)
                        {
                            var n = ext[ys[nb]];
                            r[0] = Mathf.Max(r[0], n[0]); r[1] = Mathf.Min(r[1], n[1]); r[2] = Mathf.Max(r[2], n[2]); r[3] = Mathf.Min(r[3], n[3]);
                            if (r[0] > r[1] || r[2] > r[3]) continue;
                        }
                    }
                    int wx = r[1] - r[0], wz = r[3] - r[2];
                    int ch = wx >= 6 && wz >= 6 ? 2 : wx >= 2 && wz >= 2 ? 1 : 0;
                    for (int x = r[0]; x <= r[1]; x++)
                        for (int z = r[2]; z <= r[3]; z++)
                            if (Mathf.Min(x - r[0], r[1] - x) + Mathf.Min(z - r[2], r[3] - z) >= ch) filled.Add(new Vector3Int(x, ys[i], z));
                }
            }

            // Mixed sizes: some 2x2x2 blocks of cubes are drawn as one big cube.
            var bigOf = new Dictionary<Vector3Int, Vector3Int>();
            var sorted = new List<Vector3Int>(filled);
            sorted.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z));
            if (part.merge > 0f && c >= 0.045f) // small cubes (hands, wrists) stay small
                foreach (var v in sorted)
                {
                    if (Hash(v.x, v.y, v.z, 7 + g) > part.merge) continue;
                    bool ok = true;
                    for (int k = 0; k < 8 && ok; k++)
                    {
                        var q = v + new Vector3Int(k & 1, (k >> 1) & 1, (k >> 2) & 1);
                        if (!filled.Contains(q) || bigOf.ContainsKey(q)) ok = false;
                    }
                    if (!ok) continue;
                    for (int k = 0; k < 8; k++) bigOf[v + new Vector3Int(k & 1, (k >> 1) & 1, (k >> 2) & 1)] = v;
                }
            var bigDone = new HashSet<Vector3Int>();
            System.Func<Vector3Int, bool> always = o => true;

            foreach (var v in sorted)
            {
                var vis = new bool[6]; bool any = false;
                for (int i = 0; i < 6; i++) vis[i] = !filled.Contains(v + dirs[i]);
                // Drawn when any of the 26 neighbours is empty (inner corners of steps show through the gaps too).
                var cell = v;
                System.Func<Vector3Int, bool> empty = o => !filled.Contains(cell + o);
                for (int dx = -1; dx <= 1 && !any; dx++)
                    for (int dy = -1; dy <= 1 && !any; dy++)
                        for (int dz = -1; dz <= 1 && !any; dz++)
                            if ((dx != 0 || dy != 0 || dz != 0) && empty(new Vector3Int(dx, dy, dz))) any = true;
                if (!any) continue;
                Vector3 p = Center(v.x, v.y, v.z);
                // Dark backing in the middle of each outer cube: only the gaps between the cubes show it.
                for (int i = 0; i < 6; i++)
                {
                    if (!vis[i]) continue;
                    Vector3 n = dirs[i];
                    Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                    Vector3 w = Vector3.Cross(n, u);
                    // Full width into the gaps, but kept inside the cube on sides where the cube itself shows.
                    float Ext(Vector3 d) { int j = System.Array.IndexOf(dirs, Vector3Int.RoundToInt(d)); return vis[j] ? c * 0.3f : c * 0.52f; }
                    float up = Ext(u), un = Ext(-u), wp = Ext(w), wn = Ext(-w);
                    Quad(p - u * un - w * wn, p + u * up - w * wn, p + u * up + w * wp, p - u * un + w * wp, n, verts, norms, tris[1]);
                }
                int sub = 0; foreach (var d in darks) if (InBox(p, d)) { sub = 1; break; }
                float j = part.jitter;
                Vector3Int key = v; float span = 1f; bool[] cvis = vis; var cempty = empty;
                if (bigOf.TryGetValue(v, out var bo))
                {
                    if (!bigDone.Add(bo)) continue;
                    // One big cube over the 2x2x2 block; a side shows when any of its cells is open on that side.
                    key = bo; span = 2f; p = Center(bo.x, bo.y, bo.z) + Vector3.one * (c * 0.5f);
                    cvis = new bool[6];
                    for (int k = 0; k < 8; k++)
                    {
                        var q = bo + new Vector3Int(k & 1, (k >> 1) & 1, (k >> 2) & 1);
                        for (int i = 0; i < 6; i++) if (!filled.Contains(q + dirs[i])) cvis[i] = true;
                    }
                    cempty = always;
                }
                // Irregular cubes: each one a different size, pushed off the grid and turned a little.
                float h = (span * c - gap) * 0.5f * (0.98f + 0.07f * j * Hash(key.x, key.y, key.z, 1));
                Vector3 push = Vector3.zero;
                for (int i = 0; i < 6; i++) if (cvis[i]) push += (Vector3)dirs[i];
                Vector3 rnd = new Vector3(Hash(key.x, key.y, key.z, 3) - 0.5f, Hash(key.x, key.y, key.z, 4) - 0.5f, Hash(key.x, key.y, key.z, 5) - 0.5f);
                p += push.normalized * (c * 0.05f * j * Hash(key.x, key.y, key.z, 2)) + rnd * (c * 0.08f * j);
                int start = verts.Count;
                Cube(p, Vector3.one * h, Mathf.Min(bevel * span, h * 0.45f), cvis, cempty, dirs, verts, norms, tris[sub]);
                Vector3 axis = new Vector3(Hash(key.x, key.y, key.z, 8) - 0.5f, Hash(key.x, key.y, key.z, 9) - 0.5f, Hash(key.x, key.y, key.z, 10) - 0.5f);
                if (axis.sqrMagnitude > 1e-4f)
                {
                    var rot = Quaternion.AngleAxis((Hash(key.x, key.y, key.z, 11) - 0.5f) * 10f * j, axis.normalized);
                    for (int k = start; k < verts.Count; k++) { verts[k] = p + rot * (verts[k] - p); norms[k] = rot * norms[k]; }
                }
                lastCubes++;
            }
            }
            // Plain bevelled boxes (eye frames, eye backs, pupils).
            var all6s = new[] { true, true, true, true, true, true };
            System.Func<Vector3Int, bool> yes = o => true;
            foreach (var s in slabs)
            {
                Vector3 hv = s.size * 0.5f;
                Cube(s.center, hv, Mathf.Min(hv.x, Mathf.Min(hv.y, hv.z)) * 0.3f, all6s, yes, dirs, verts, norms, tris[s.mat]);
            }
            // Dark core inside every solid shape, showing through the gaps.
            foreach (var s in allSolid)
            {
                if (s.vox != null) continue; // helper-filled parts: each outer cube already has its dark backing
                float sc = s.cube > 0f ? s.cube : baseC;
                Vector3 half = s.ell > 0f
                    ? s.size * 0.5f * Mathf.Pow(1f / 3f, 1f / s.ell) - Vector3.one * (sc * 0.35f)
                    : s.size * 0.5f - Vector3.one * (sc * 0.55f + s.round * 0.6f);
                half = Vector3.Max(half, Vector3.one * sc * 0.2f);
                var all6 = new[] { true, true, true, true, true, true };
                Box(s.center, half, all6, dirs, verts, norms, tris[1]);
            }

            mesh.Clear();
            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts); mesh.SetNormals(norms);
            mesh.subMeshCount = tris[2].Count > 0 ? 3 : 2;
            mesh.SetTriangles(tris[0], 0); mesh.SetTriangles(tris[1], 1); if (tris[2].Count > 0) mesh.SetTriangles(tris[2], 2);
            mesh.RecalculateBounds();
        }

        static void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector3 n, List<Vector3> v, List<Vector3> nm, List<int> t)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, cc - a), n) < 0f) { var tmp = b; b = d; d = tmp; }
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
            for (int k = 0; k < 4; k++) nm.Add(n);
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        static void Tri(Vector3 a, Vector3 b, Vector3 cc, Vector3 n, List<Vector3> v, List<Vector3> nm, List<int> t)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, cc - a), n) < 0f) { var tmp = b; b = cc; cc = tmp; }
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(cc);
            for (int k = 0; k < 3; k++) nm.Add(n);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        static void Box(Vector3 p, Vector3 half, bool[] vis, Vector3Int[] dirs, List<Vector3> v, List<Vector3> nm, List<int> t)
        {
            for (int i = 0; i < 6; i++)
            {
                if (!vis[i]) continue;
                Vector3 n = dirs[i];
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(n, u);
                float hu = Mathf.Abs(Vector3.Dot(u, half)), hw = Mathf.Abs(Vector3.Dot(w, half)), hn = Mathf.Abs(Vector3.Dot(n, half));
                Vector3 f = p + n * hn;
                Quad(f - u * hu - w * hw, f + u * hu - w * hw, f + u * hu + w * hw, f - u * hu + w * hw, n, v, nm, t);
            }
        }

        // One bevelled cube: the visible faces, the bevel strips between two visible faces and the corner triangles
        // where three meet.
        // Size of the (positive) half-extents hv along an axis direction d.
        static float Ax(Vector3 hv, Vector3 d) => Mathf.Abs(hv.x * d.x) + Mathf.Abs(hv.y * d.y) + Mathf.Abs(hv.z * d.z);

        // One bevelled box (half-extents hv per axis): the visible faces, the bevel strips between two visible faces
        // and the corner triangles where three meet.
        static void Cube(Vector3 p, Vector3 hv, float b, bool[] vis, System.Func<Vector3Int, bool> empty, Vector3Int[] dirs, List<Vector3> v, List<Vector3> nm, List<int> t)
        {
            Vector3 e = hv - Vector3.one * b;
            for (int i = 0; i < 6; i++)
            {
                if (!vis[i]) continue;
                Vector3 n = dirs[i];
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(n, u);
                Vector3 f = p + n * Ax(hv, n);
                float eu = Ax(e, u), ew = Ax(e, w);
                Quad(f - u * eu - w * ew, f + u * eu - w * ew, f + u * eu + w * ew, f - u * eu + w * ew, n, v, nm, t);
            }
            for (int i = 0; i < 6; i++)
                for (int j = i + 1; j < 6; j++)
                {
                    Vector3Int ai = dirs[i], ci = dirs[j];
                    if (ai + ci == Vector3Int.zero) continue;
                    if (!vis[i] && !vis[j] && !empty(ai + ci)) continue;
                    Vector3 a = ai, c2 = ci;
                    Vector3 along = Vector3.Cross(a, c2);
                    float el = Ax(e, along);
                    Vector3 pa = p + a * Ax(hv, a) + c2 * Ax(e, c2), pc = p + c2 * Ax(hv, c2) + a * Ax(e, a);
                    Quad(pa - along * el, pa + along * el, pc + along * el, pc - along * el, (a + c2).normalized, v, nm, t);
                }
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        if (!vis[sx > 0 ? 0 : 1] && !vis[sy > 0 ? 2 : 3] && !vis[sz > 0 ? 4 : 5] && !empty(new Vector3Int(sx, sy, 0)) && !empty(new Vector3Int(sx, 0, sz)) && !empty(new Vector3Int(0, sy, sz)) && !empty(new Vector3Int(sx, sy, sz))) continue;
                        Tri(p + new Vector3(sx * hv.x, sy * e.y, sz * e.z), p + new Vector3(sx * e.x, sy * hv.y, sz * e.z), p + new Vector3(sx * e.x, sy * e.y, sz * hv.z),
                            new Vector3(sx, sy, sz).normalized, v, nm, t);
                    }
        }
    }
}
