using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Enemy cutter: a small rusty trawler in the enemy-ship style (riveted rust hull with a shark-teeth band, plank
    /// bulwark, spiked wheelhouse with a crow's nest, funnel, aft mast, stern lattice crane, ram spikes, spiked side
    /// boxes, anchor, torn banners). It dresses the greybox "Protagonist Small Boat" (a scaled cube): the boat keeps
    /// its transform, BoxCollider and deck anchors (Deck_P1/P2), only its cube renderer is switched off, and the prefab
    /// Assets/Prefabs/StyleShip/Enemy_Cutter.prefab goes in as child "Enemy Cutter" with the box scale undone (model in
    /// metres). Model frame = the A3_Start box (20.4 x 7.29 x 7.1 m): deck on top of the box, keel at its bottom,
    /// waterline 2.24 m above the keel (A3 water), bow towards local -z (the way A3_Outro sails it). Visual only and not
    /// static (the boat moves). Mesh: Assets/Meshes/StyleShip/Cutter/Ship_EnemyCutter.asset (material Ship_Solid).
    /// </summary>
    public static partial class ProtagonistShipBuilder
    {
        const string CutterBoatName = "Protagonist Small Boat", CutterChildName = "Enemy Cutter";
        const string CutterPrefabPath = PrefabDir + "/Enemy_Cutter.prefab";
        const float CDeck = 3.55f, CWaterY = -2.24f, FM = -6.5f; // FM: fore mast z (middle of the wheelhouse)

        static readonly Color CRustD = C(0.06f, 0.032f, 0.02f, 0.04f), CRust = C(0.17f, 0.07f, 0.03f, 0.05f), CRust2 = C(0.12f, 0.055f, 0.03f, 0.05f),
            CRustL = C(0.26f, 0.11f, 0.045f, 0.04f), CSteelD = C(0.06f, 0.055f, 0.05f, 0.2f), CBoot = C(0.03f, 0.028f, 0.026f, 0.15f), CJaw = C(0.025f, 0.02f, 0.018f, 0.05f),
            CTooth = C(0.62f, 0.59f, 0.52f, 0.1f), CWoodA = C(0.11f, 0.08f, 0.055f, 0.03f), CWoodB = C(0.085f, 0.062f, 0.045f, 0.03f),
            CWoodL = C(0.15f, 0.11f, 0.075f, 0.03f), CDeckA = C(0.20f, 0.16f, 0.115f, 0.03f), CDeckB = C(0.17f, 0.135f, 0.097f, 0.03f),
            CIron = C(0.075f, 0.072f, 0.068f, 0.2f), CIronD = C(0.045f, 0.043f, 0.04f, 0.2f), CCloth = C(0.50f, 0.47f, 0.40f, 0.02f),
            CGlass = C(0.02f, 0.025f, 0.03f, 0.7f), CRopeC = C(0.36f, 0.29f, 0.18f, 0.02f), CCrateC = C(0.26f, 0.18f, 0.10f, 0.03f);

        // Plan at deck level (z from stern to bow, half-width); the side lines run along these stations.
        static readonly float[] CutZ = { 10.15f, 9.85f, 9.2f, 7.5f, 5f, 3.6f, 2.5f, 0f, -0.4f, -2.5f, -5f, -6.6f, -7.9f, -8.9f, -9.6f, -10.15f };
        static readonly float[] CutHw = { 2.6f, 3.25f, 3.55f, 3.6f, 3.6f, 3.6f, 3.6f, 3.6f, 3.6f, 3.6f, 3.6f, 3.4f, 2.85f, 1.9f, 1.0f, 0.22f };
        // Boarding gap in the -x bulwark (the quay side in A3_Outro, where both heroes jump aboard), between stations 3.6 and -0.4.
        const float GapZ0 = -0.45f, GapZ1 = 3.65f;
        static bool InGap(Vector3 a, Vector3 b) => a.x < 0 && b.x < 0 && a.z > GapZ0 && a.z < GapZ1 && b.z > GapZ0 && b.z < GapZ1;
        // Hull levels: y, half-width factor, bow rake (z scale for z < 0), stern rake. Bands between them get one colour:
        // under water, boot top, three rust plates, the black teeth band, wood planks, deck line (index 11), bulwark planks.
        static readonly float[,] CutLev =
        {
            { -3.55f, 0.45f, 0.84f, 0.93f }, { -3.0f, 0.72f, 0.89f, 0.955f }, { CWaterY, 0.88f, 0.93f, 0.975f }, { -1.95f, 0.9f, 0.94f, 0.98f },
            { -1.0f, 0.96f, 0.965f, 0.99f }, { 0f, 0.985f, 0.98f, 0.995f }, { 1.0f, 1f, 0.99f, 1f }, { 1.9f, 1f, 1f, 1f },
            { 2.32f, 1f, 1f, 1f }, { 2.74f, 1f, 1f, 1f }, { 3.15f, 1f, 1f, 1f }, { CDeck, 1f, 1f, 1f }, { 3.88f, 1f, 1f, 1f }, { 4.21f, 1f, 1f, 1f }, { 4.55f, 1f, 1f, 1f }
        };
        const int KBandLo = 6, KBandHi = 7, KDeck = 11;

        [MenuItem("Tools/Coop Game/Style Test/Build Enemy Cutter (Protagonist Small Boat)")]
        static void BuildCutterMenu() => Debug.Log(BuildEnemyCutter());

        /// <summary>Builds the cutter mesh + prefab and swaps it onto the active scene's "Protagonist Small Boat" (Undo, scene not saved).</summary>
        public static string BuildEnemyCutter()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var boat = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.name == CutterBoatName && t.gameObject.scene == scene);
            if (boat == null) return "No '" + CutterBoatName + "' in the active scene " + scene.path + ".";
            solid = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Ship_Solid.mat");
            if (solid == null) return "Missing " + MatDir + "/Ship_Solid.mat (build the protagonist ship once).";

            rnd = new System.Random(20261112); boxCount = 0; zFixCount = 0;
            var mb = new MB();
            var rings = CutterHull(mb);
            CutterWheelhouse(mb);
            CutterDeckItems(mb);
            CutterCrane(mb);
            CutterSpikes(mb, rings);
            var mesh = SaveMesh(mb.Done("Ship_EnemyCutter"), "Cutter/Ship_EnemyCutter");

            var go = new GameObject("Enemy_Cutter");
            var mc = new GameObject("Mesh"); mc.transform.SetParent(go.transform, false);
            mc.AddComponent<MeshFilter>().sharedMesh = mesh;
            mc.AddComponent<MeshRenderer>().sharedMaterial = solid;
            EnsureFolder(PrefabDir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, CutterPrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();

            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Build Enemy Cutter"); int group = Undo.GetCurrentGroup();
            var old = boat.Find(CutterChildName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, boat);
            Undo.RegisterCreatedObjectUndo(inst, "Build Enemy Cutter");
            inst.name = CutterChildName;
            var ls = boat.lossyScale;
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
            var cube = boat.GetComponent<MeshRenderer>();
            if (cube != null && cube.enabled) { Undo.RecordObject(cube, "Build Enemy Cutter"); cube.enabled = false; }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            return $"Enemy cutter on '{CutterBoatName}' ({boat.position:F2}, box {ls:F2}): {mesh.vertexCount} vertices, {zFixCount} coplanar faces lifted. " +
                   "Cube renderer off, BoxCollider and Deck_P1/P2 kept.";
        }

        // ---------- Hull ----------

        static float CutHwAt(float z)
        {
            if (z >= CutZ[0]) return CutHw[0];
            for (int i = 1; i < CutZ.Length; i++)
                if (z >= CutZ[i]) return Mathf.Lerp(CutHw[i], CutHw[i - 1], (z - CutZ[i]) / (CutZ[i - 1] - CutZ[i]));
            return CutHw[CutHw.Length - 1];
        }

        /// <summary>Half-width of the outer hull at height y and station z.</summary>
        static float HullHw(float y, float z)
        {
            int nl = CutLev.GetLength(0), k = 0;
            while (k < nl - 2 && y > CutLev[k + 1, 0]) k++;
            float t = Mathf.InverseLerp(CutLev[k, 0], CutLev[k + 1, 0], y);
            float f = Mathf.Lerp(CutLev[k, 1], CutLev[k + 1, 1], t), kb = Mathf.Lerp(CutLev[k, 2], CutLev[k + 1, 2], t), ks = Mathf.Lerp(CutLev[k, 3], CutLev[k + 1, 3], t);
            return CutHwAt(z / (z < 0 ? kb : ks)) * f;
        }

        static float StemZ(float y)
        {
            int nl = CutLev.GetLength(0), k = 0;
            while (k < nl - 2 && y > CutLev[k + 1, 0]) k++;
            return CutZ[CutZ.Length - 1] * Mathf.Lerp(CutLev[k, 2], CutLev[k + 1, 2], Mathf.InverseLerp(CutLev[k, 0], CutLev[k + 1, 0], y));
        }

        /// <summary>Closed ring at level k: +x side stern to bow (0..n-1), then -x side bow to stern (n..2n-1).</summary>
        static Vector3[] CutRing(int k)
        {
            float y = CutLev[k, 0], f = CutLev[k, 1], kb = CutLev[k, 2], ks = CutLev[k, 3];
            int n = CutZ.Length; var r = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                float z = CutZ[i] * (CutZ[i] < 0 ? kb : ks), hw = CutHw[i] * f;
                r[i] = V(hw, y, z); r[2 * n - 1 - i] = V(-hw, y, z);
            }
            return r;
        }

        static Vector3 EdgeOut(Vector3 a, Vector3 b)
        {
            var e = b - a; e.y = 0; var n = new Vector3(e.z, 0, -e.x).normalized;
            var m = (a + b) * 0.5f; m.y = 0;
            return Vector3.Dot(n, m) < 0 ? -n : n;
        }

        static Vector3[] Inset(Vector3[] r, float d)
        {
            int n = r.Length; var o = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var n1 = EdgeOut(r[(i + n - 1) % n], r[i]); var n2 = EdgeOut(r[i], r[(i + 1) % n]);
                o[i] = r[i] - (n1 + n2) / Mathf.Max(1f + Vector3.Dot(n1, n2), 0.2f) * d;
            }
            return o;
        }

        static bool SpanZ(Vector3[] r, float x, out float z0, out float z1)
        {
            z0 = float.MaxValue; z1 = float.MinValue;
            for (int i = 0; i < r.Length; i++)
            {
                var a = r[i]; var b = r[(i + 1) % r.Length];
                if ((a.x - x) * (b.x - x) > 0 || Mathf.Abs(a.x - b.x) < 1e-5f) continue;
                float z = Mathf.Lerp(a.z, b.z, (x - a.x) / (b.x - a.x));
                z0 = Mathf.Min(z0, z); z1 = Mathf.Max(z1, z);
            }
            return z1 > z0;
        }

        static Color CutBand(int k, int i)
        {
            float y = CutLev[k, 0], h = Hash(i, k * 7 + 3);
            if (y < CWaterY - 0.01f) return CRustD;
            if (y < -1.96f) return CBoot;
            if (y < 0.99f) return h < 0.25f ? Shade(CSteelD, 0.8f + 0.8f * h) : Shade(k % 2 == 0 ? CRust : CRust2, 0.75f + 0.4f * h);
            if (y < 1.89f) return CJaw;
            return Shade(k % 2 == 0 ? CWoodA : CWoodB, 0.9f + 0.2f * h);
        }

        static Vector3[][] CutterHull(MB mb)
        {
            int nl = CutLev.GetLength(0); var R = new Vector3[nl][];
            for (int k = 0; k < nl; k++) R[k] = CutRing(k);
            int N = R[0].Length, n = CutZ.Length;
            for (int k = 0; k + 1 < nl; k++)
                for (int i = 0; i < N; i++)
                {
                    int j = (i + 1) % N; if (k >= KDeck && InGap(R[k][i], R[k][j])) continue;
                    var o = (R[k][i] + R[k][j] + R[k + 1][i] + R[k + 1][j]) * 0.25f; o.y = 0;
                    mb.Quad(R[k][i], R[k][j], R[k + 1][j], R[k + 1][i], CutBand(k, i), o);
                }
            mb.Cap(R[0], CRustD, Vector3.down);

            // bulwark inside (planks, darker) and its top; deck planks inside it
            var I = new Vector3[nl - KDeck][];
            for (int k = KDeck; k < nl; k++) I[k - KDeck] = Inset(R[k], 0.18f);
            for (int k = KDeck; k + 1 < nl; k++)
                for (int i = 0; i < N; i++)
                {
                    int j = (i + 1) % N; if (InGap(R[k][i], R[k][j])) continue;
                    var a = I[k - KDeck]; var b = I[k + 1 - KDeck]; var o = (a[i] + a[j]) * 0.5f; o.y = 0;
                    mb.Quad(a[i], a[j], b[j], b[i], Shade(CutBand(k, i), 0.75f), -o);
                }
            var top = R[nl - 1]; var tin = I[nl - 1 - KDeck];
            for (int i = 0; i < N; i++) { int j = (i + 1) % N; if (!InGap(top[i], top[j])) mb.Quad(top[i], top[j], tin[j], tin[i], CWoodB, Vector3.up); }
            CutterDeck(mb, I[0]);

            // boarding gap: hull top at deck level across it, bulwark ends closed
            var rd = R[KDeck];
            for (int i = 0; i < N; i++)
            {
                int j = (i + 1) % N;
                if (InGap(rd[i], rd[j])) mb.Quad(rd[i], rd[j], I[0][j], I[0][i], CWoodL, Vector3.up);
                if (rd[i].x < 0 && (Mathf.Abs(rd[i].z - 3.6f) < 0.01f || Mathf.Abs(rd[i].z + 0.4f) < 0.01f))
                    mb.Quad(rd[i], top[i], tin[i], I[0][i], CWoodL, rd[i].z > 1f ? Vector3.back : Vector3.forward);
            }

            // bulwark posts on the inside
            var ib = I[0];
            for (int i = 0; i < N; i++)
            {
                int j = (i + 1) % N; if (InGap(ib[i], ib[j])) continue;
                float L = (ib[j] - ib[i]).magnitude; int m = Mathf.FloorToInt(L / 1.7f);
                var nrm = EdgeOut(ib[i], ib[j]); var q = Quaternion.LookRotation((ib[j] - ib[i]).normalized, Vector3.up);
                for (int s = 1; s <= m; s++)
                    mb.Box(Vector3.Lerp(ib[i], ib[j], s / (m + 1f)) - nrm * 0.07f + Vector3.up * 0.5f, V(0.14f, 1.0f, 0.14f), Shade(CWoodB, 0.8f), q);
            }

            // rub rails (above the teeth band, at the deck line) and the cap rail
            foreach (int k in new[] { KBandHi, KDeck })
                for (int i = 0; i < N; i++)
                {
                    int j = (i + 1) % N; var a = R[k][i]; var b = R[k][j]; var d = (b - a).normalized * 0.06f; var o = EdgeOut(a, b) * 0.06f;
                    Strip(mb, a - d + o, b + d + o, 0.16f, 0.14f, CWoodB);
                }
            for (int i = 0; i < N; i++)
            {
                int j = (i + 1) % N; if (InGap(top[i], top[j])) continue;
                var a = (top[i] + tin[i]) * 0.5f + Vector3.up * 0.04f; var b = (top[j] + tin[j]) * 0.5f + Vector3.up * 0.04f; var d = (b - a).normalized * 0.08f;
                Strip(mb, a - d, b + d, 0.08f, 0.3f, CWoodL);
            }

            // shark teeth on the black band: upper row hangs down, lower row stands up between them, black gaps around each
            for (int i = 0; i < N; i++)
            {
                int j = (i + 1) % N; if (i == n - 1) continue; // stem face
                var lo = R[KBandLo]; var hi = R[KBandHi]; float L = (lo[j] - lo[i]).magnitude; if (L < 0.4f) continue;
                var nrm = EdgeOut(lo[i], lo[j]); int m = Mathf.Max(1, Mathf.RoundToInt(L / 0.75f)); float g = 0.09f / m;
                Vector3 P(float u, float v) => Vector3.Lerp(Vector3.Lerp(lo[i], lo[j], u), Vector3.Lerp(hi[i], hi[j], u), v) + nrm * 0.03f;
                for (int t = 0; t <= m; t++)
                {
                    float uc = t / (float)m;
                    if (t < m) { float u0 = uc + g, u1 = (t + 1f) / m - g; mb.Tri(P(u0, 0.95f), P(u1, 0.95f), P((u0 + u1) * 0.5f, 0.3f), Shade(CTooth, 0.75f + 0.3f * Hash(i, t)), nrm); }
                    float l0 = Mathf.Max(0f, uc - 0.5f / m + g), l1 = Mathf.Min(1f, uc + 0.5f / m - g);
                    mb.Tri(P(l0, 0.05f), P(l1, 0.05f), P(uc, 0.64f), Shade(CTooth, 0.75f + 0.3f * Hash(t, i + 40)), nrm);
                }
            }

            // plate seams and rust streaks on the steel
            for (int i = 0; i < N; i++)
            {
                if (i == 0 || i == n - 1 || i == n || i == N - 1) continue;
                var nv = (EdgeOut(R[4][(i + N - 1) % N], R[4][i]) + EdgeOut(R[4][i], R[4][(i + 1) % N])).normalized * 0.02f;
                for (int k = 2; k < KBandLo; k++) mb.Rod(R[k][i] + nv, R[k + 1][i] + nv, 0.06f, Shade(CRust2, 0.55f));
            }
            for (int s = 0; s < 26; s++)
            {
                int i = rnd.Next(N), j = (i + 1) % N; float u = Rf(0.15f, 0.85f), w = Rf(0.1f, 0.26f), reach = Rf(0.1f, 0.9f);
                if (i == n - 1 || i == N - 1) continue;
                float L = (R[KBandLo][j] - R[KBandLo][i]).magnitude; if (L < 1.2f) continue;
                var nrm = EdgeOut(R[KBandLo][i], R[KBandLo][j]) * 0.018f; float hw = w / L; var col = Shade(CRustL, Rf(0.8f, 1.15f));
                Vector3 Pk(int k, float uu) => Vector3.Lerp(R[k][i], R[k][j], uu) + nrm;
                mb.Quad(Pk(KBandLo, u - hw), Pk(KBandLo, u + hw), Pk(5, u + hw * 0.6f), Pk(5, u - hw * 0.6f), col, nrm);
                mb.Tri(Pk(5, u - hw * 0.6f), Pk(5, u + hw * 0.6f), Vector3.Lerp(Pk(5, u), Pk(4, u), reach), col, nrm);
            }
            return R;
        }

        static void CutterDeck(MB mb, Vector3[] rim)
        {
            const float pw = 0.42f, gap = 0.035f, xmax = 3.4f, lift = 0.012f;
            mb.Cap(rim, Shade(CDeckB, 0.5f), Vector3.up);
            int row = 0;
            for (float x0 = -xmax; x0 < xmax - 0.05f; x0 += pw, row++)
            {
                float x1 = Mathf.Min(x0 + pw - gap, xmax);
                if (!SpanZ(rim, x0, out float a0, out float b0) || !SpanZ(rim, x1, out float a1, out float b1)) continue;
                float z0 = Mathf.Max(a0, a1) + 0.02f, z1 = Mathf.Min(b0, b1) - 0.02f, z = z0; int seg = 0;
                while (z < z1 - 0.05f)
                {
                    float ze = Mathf.Min(z1, z + Rf(2.2f, 4.2f)); if (z1 - ze < 0.8f) ze = z1;
                    var col = Shade((row + seg) % 2 == 0 ? CDeckA : CDeckB, 0.85f + 0.3f * Hash(row, seg + 11));
                    float y = CDeck + lift;
                    mb.Quad(V(x0, y, z), V(x1, y, z), V(x1, y, ze - gap), V(x0, y, ze - gap), col, Vector3.up);
                    z = ze; seg++;
                }
            }
        }

        // ---------- Superstructure ----------

        static void Spike(MB mb, Vector3 b, Vector3 dir, float len, float r, Color col) =>
            mb.Prism(b, 4, r, 0f, len, col, Quaternion.FromToRotation(Vector3.up, dir.normalized), 45f);

        static void Cloth(MB mb, Vector3 origin, Vector3 right, Vector3 normal, float w, float h)
        {
            // torn banner hanging from origin (top-left corner), ragged bottom
            var pts = new System.Collections.Generic.List<Vector3> { origin, origin + right * w };
            int cuts = Mathf.Max(3, Mathf.RoundToInt(w / 0.2f));
            for (int c = 0; c <= cuts; c++)
            {
                float x = w * (1f - c / (float)cuts), drop = h * (c % 2 == 0 ? Rf(0.72f, 0.85f) : Rf(0.9f, 1.05f));
                pts.Add(origin + right * x + Vector3.down * drop);
            }
            mb.ExtrudePoly(pts.ToArray(), normal, 0.025f, Shade(CCloth, Rf(0.8f, 1.05f)));
        }

        static void CutterWheelhouse(MB mb)
        {
            float x0 = -1.75f, x1 = 1.75f, z0 = -8.4f, z1 = -4.6f, y0 = CDeck, y1 = 6.45f, t = 0.14f; var I = Quaternion.identity;
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f, W = x1 - x0, D = z1 - z0; int rows = 8; float ph = (y1 - y0) / rows;
            for (int r = 0; r < rows; r++)
            {
                float ym = y0 + (r + 0.5f) * ph; var col = Shade(r % 2 == 0 ? CWoodA : CWoodB, 0.9f + 0.2f * Hash(r, 77));
                mb.Box(V(cx, ym, z0 + t * 0.5f), V(W, ph, t), col, I);
                mb.Box(V(cx, ym, z1 - t * 0.5f), V(W, ph, t), col, I);
                mb.Box(V(x0 + t * 0.5f, ym, cz), V(t, ph, D - 2 * t), col, I);
                mb.Box(V(x1 - t * 0.5f, ym, cz), V(t, ph, D - 2 * t), col, I);
            }
            mb.Box(V(cx, y0 + 0.05f, cz), V(W - 0.1f, 0.1f, D - 0.1f), CWoodB, I);
            foreach (var x in new[] { x0, x1 }) foreach (var z in new[] { z0, z1 }) mb.Box(V(x, (y0 + y1) * 0.5f, z), V(0.2f, y1 - y0, 0.2f), Shade(CWoodB, 0.7f), I);

            void Window(Vector3 c, Vector3 n, float w, float h)
            {
                var q = Quaternion.LookRotation(n, Vector3.up);
                mb.Box(c + n * 0.03f, V(w + 0.14f, h + 0.14f, 0.06f), CWoodL, q);
                mb.Box(c + n * 0.06f, V(w, h, 0.04f), CGlass, q);
                mb.Box(c + n * 0.09f + Vector3.up * 0.02f, V(0.05f, h, 0.03f), CWoodL, q);
            }
            foreach (var x in new[] { -1.1f, 0f, 1.1f }) Window(V(x, 5.45f, z0), Vector3.back, 0.75f, 0.7f);
            foreach (var z in new[] { -7.5f, -5.65f }) { Window(V(x0, 5.45f, z), Vector3.left, 0.95f, 0.65f); Window(V(x1, 5.45f, z), Vector3.right, 0.95f, 0.65f); }
            Window(V(-0.85f, 5.45f, z1), Vector3.forward, 0.75f, 0.65f);
            mb.Box(V(0.85f, y0 + 1.05f, z1 + 0.04f), V(0.9f, 2.1f, 0.08f), Shade(CWoodB, 0.6f), I);             // door
            mb.Box(V(0.5f, y0 + 1.05f, z1 + 0.1f), V(0.08f, 0.08f, 0.08f), CIron, I);                         // handle

            // roof, railing, spike crown, jagged corner plates
            float ry = y1 + 0.09f; mb.Box(V(cx, ry, cz), V(W + 0.5f, 0.18f, D + 0.5f), CRust2, I);
            float rx0 = x0 - 0.15f, rx1 = x1 + 0.15f, rz0 = z0 - 0.15f, rz1 = z1 + 0.15f, rt = ry + 0.09f;
            var rc = new[] { V(rx0, rt, rz0), V(rx1, rt, rz0), V(rx1, rt, rz1), V(rx0, rt, rz1) };
            for (int i = 0; i < 4; i++)
            {
                var a = rc[i]; var b = rc[(i + 1) % 4]; float L = (b - a).magnitude; int m = Mathf.Max(1, Mathf.RoundToInt(L / 1.1f));
                mb.Rod(a + Vector3.up * 0.75f, b + Vector3.up * 0.75f, 0.06f, CIron);
                mb.Rod(a + Vector3.up * 0.4f, b + Vector3.up * 0.4f, 0.04f, CIron);
                for (int s = 0; s < m; s++) mb.Box(Vector3.Lerp(a, b, s / (float)m) + Vector3.up * 0.38f, V(0.06f, 0.76f, 0.06f), CIron, I);
                int sp = Mathf.Max(2, Mathf.RoundToInt(L / 0.7f));
                for (int s = 0; s < sp; s++)
                {
                    var p = Vector3.Lerp(a, b, (s + 0.5f) / sp) + Vector3.up * 0.7f; var o = p - V(cx, p.y, cz); o.y = 0;
                    Spike(mb, p, o.normalized * 0.55f + Vector3.up, Rf(0.7f, 1.25f), 0.07f, s % 3 == 0 ? CIronD : CIron);
                }
                // jagged two-pointed plate at each corner, leaning out
                var pu = (rc[i] - V(cx, rt, cz)).normalized * 0.5f + Vector3.up; var side = (b - a).normalized;
                Vector3 Pl(float x, float y) => rc[i] + side * x + pu * y;
                mb.ExtrudePoly(new[] { Pl(0f, 0f), Pl(-0.1f, 1.5f), Pl(0.35f, 0.8f), Pl(0.55f, 1.1f), Pl(0.8f, 0f) }, Vector3.Cross(side, Vector3.up), 0.04f, CRust);
            }
            Cloth(mb, V(0.2f, y1 - 0.05f, z0 - 0.12f), Vector3.right, Vector3.back, 1.2f, 1.9f);
            Cloth(mb, V(x1 + 0.12f, y1 - 0.05f, z1 - 0.4f), Vector3.back, Vector3.right, 1.1f, 1.6f);

            // fore mast with crow's nest and a spiked yard
            var mp = V(0f, rt, FM);
            mb.Cyl(mp, V(0f, 11.2f, FM), 0.11f, 6, CIron);
            mb.Prism(V(0f, 9.0f, FM), 8, 0.75f, 0.75f, 0.12f, CWoodB, 22.5f);
            for (int s = 0; s < 8; s++)
            {
                float a0 = (s * 45f + 22.5f) * Mathf.Deg2Rad, a1 = ((s + 1) * 45f + 22.5f) * Mathf.Deg2Rad;
                var p0 = V(Mathf.Sin(a0) * 0.72f, 9.12f, FM + Mathf.Cos(a0) * 0.72f); var p1 = V(Mathf.Sin(a1) * 0.72f, 9.12f, FM + Mathf.Cos(a1) * 0.72f);
                mb.Box(p0 + Vector3.up * 0.3f, V(0.05f, 0.6f, 0.05f), CIron, I);
                mb.Rod(p0 + Vector3.up * 0.6f, p1 + Vector3.up * 0.6f, 0.05f, CIron);
                var o = p0 - V(0, p0.y, FM); Spike(mb, p0 + Vector3.up * 0.6f, o * 0.7f + Vector3.up, 0.55f, 0.05f, CIron);
            }
            mb.Rod(V(-1.3f, 10.3f, FM), V(1.3f, 10.3f, FM), 0.08f, CIron);
            foreach (var x in new[] { -1.3f, 1.3f }) Spike(mb, V(x, 10.3f, FM), V(x * 0.2f, 1f, 0f), 0.6f, 0.06f, CIron);
            Spike(mb, V(0f, 11.2f, FM), Vector3.up, 0.6f, 0.08f, CIron);
        }

        static void CutterDeckItems(MB mb)
        {
            var I = Quaternion.identity;
            // funnel on its casing
            mb.Box(V(1.2f, CDeck + 0.4f, 4.6f), V(1.7f, 0.8f, 1.7f), CRust2, I);
            mb.Cyl(V(1.2f, CDeck + 0.8f, 4.6f), V(1.2f, 8.6f, 4.6f), 0.5f, 10, CRust);
            foreach (var y in new[] { 6.2f, 7.9f }) mb.Prism(V(1.2f, y, 4.6f), 10, 0.53f, 0.53f, 0.18f, CIronD);
            mb.Prism(V(1.2f, 8.45f, 4.6f), 10, 0.57f, 0.57f, 0.16f, CIronD);
            mb.Prism(V(1.2f, 8.6f, 4.6f), 10, 0.42f, 0.42f, 0.02f, C(0.01f, 0.01f, 0.01f, 0f));

            // hatch with a crate, crates and a barrel to starboard, rope coil
            mb.Box(V(-1.7f, CDeck + 0.2f, 5.0f), V(1.9f, 0.4f, 2.0f), CWoodB, I);
            for (int s = 0; s < 4; s++) mb.Box(V(-1.7f, CDeck + 0.43f, 4.25f + s * 0.5f), V(1.8f, 0.06f, 0.45f), Shade(s % 2 == 0 ? CDeckA : CDeckB, 0.8f), I);
            Crate(mb, V(-1.9f, CDeck + 0.81f, 5.3f), V(0.75f, 0.7f, 0.75f), CCrateC, true);
            Crate(mb, V(2.35f, CDeck + 0.4f, 0.6f), V(1.0f, 0.8f, 1.0f), CCrateC, true);
            Crate(mb, V(2.45f, CDeck + 0.35f, 1.95f), V(0.85f, 0.7f, 0.9f), Shade(CCrateC, 0.85f), false);
            Crate(mb, V(2.3f, CDeck + 1.08f, 0.7f), V(0.6f, 0.55f, 0.6f), Shade(CCrateC, 1.1f), false);
            mb.Prism(V(2.6f, CDeck, -1.7f), 8, 0.32f, 0.32f, 0.95f, CRust);
            foreach (var y in new[] { 0.18f, 0.77f }) mb.Prism(V(2.6f, CDeck + y, -1.7f), 8, 0.34f, 0.34f, 0.06f, CIronD);
            mb.Prism(V(-2.3f, CDeck, -1.8f), 10, 0.5f, 0.5f, 0.14f, CRopeC);
            mb.Prism(V(-2.3f, CDeck + 0.14f, -1.8f), 10, 0.42f, 0.42f, 0.1f, Shade(CRopeC, 0.8f));
            mb.Prism(V(-2.3f, CDeck + 0.2f, -1.8f), 10, 0.2f, 0.2f, 0.06f, C(0.03f, 0.025f, 0.02f, 0f));

            // aft mast with a spiked yard, stays to the bulwark and the fore mast
            var mt = V(-0.5f, 12.8f, 7.4f);
            mb.Cyl(V(-0.5f, CDeck, 7.4f), mt, 0.14f, 6, CIron);
            mb.Rod(V(-2.2f, 11.2f, 7.4f), V(1.2f, 11.2f, 7.4f), 0.09f, CIron);
            foreach (var x in new[] { -2.2f, -1.35f, 0.35f, 1.2f }) Spike(mb, V(x, 11.25f, 7.4f), Vector3.up, 0.55f, 0.06f, CIron);
            Spike(mb, mt, Vector3.up, 0.7f, 0.09f, CIron);
            var rope = C(0.05f, 0.042f, 0.035f, 0.02f);
            // aft stays only to +x: on the -x side they would cross Cam_06_DeckWide (A3_Outro) right in front of the lens
            mb.Rod(mt + Vector3.down * 0.2f, V(3.35f, 4.6f, 6.2f), 0.035f, rope);
            mb.Rod(mt + Vector3.down * 0.2f, V(3.3f, 4.6f, 8.7f), 0.035f, rope);
            foreach (var s in new[] { -1f, 1f }) mb.Rod(V(0f, 11.0f, FM), V(s * 2.8f, 4.6f, -7.9f), 0.035f, rope);
            mb.Rod(mt + Vector3.down * 0.3f, V(0f, 11.0f, FM), 0.035f, rope);
            mb.Rod(V(0f, 11.0f, FM), V(0f, 4.6f, StemZ(4.5f) + 0.4f), 0.035f, rope);

            // anchor hanging off the bow (+x side) from its hawse
            float az = -8.9f, ax = HullHw(3.0f, az);
            mb.Cyl(V(ax - 0.1f, 3.0f, az), V(ax + 0.06f, 3.0f, az), 0.2f, 8, CIronD);
            var ring = V(ax + 0.2f, 2.55f, az);
            Chain(mb, V(ax + 0.08f, 3.0f, az), ring, 0.16f, CIron);
            mb.Box(ring + Vector3.down * 0.7f, V(0.12f, 1.3f, 0.12f), CIron, I);
            mb.Box(ring + Vector3.down * 0.15f, V(0.1f, 0.1f, 0.85f), CIron, I);
            var crown = ring + Vector3.down * 1.35f;
            foreach (var s in new[] { -1f, 1f })
            {
                var tip = crown + V(0f, 0.5f, s * 0.55f);
                mb.Rod(crown, tip, 0.11f, CIron);
                mb.Box(tip + V(0f, -0.05f, -s * 0.06f), V(0.08f, 0.3f, 0.22f), CIron, Quaternion.Euler(s * 35f, 0f, 0f));
            }

            // chains hanging over the sides
            foreach (var s in new[] { -1f, 1f })
                foreach (var z in new[] { -1.4f, 5.2f })
                {
                    float hx = s * (CutHwAt(z) + 0.12f);
                    Chain(mb, V(hx, 4.5f, z), V(hx + s * 0.05f, 2.1f, z + 0.3f), 0.2f, CIron);
                    mb.Rod(V(hx + s * 0.05f, 2.1f, z + 0.3f), V(hx + s * 0.05f, 1.9f, z + 0.45f), 0.06f, CIron);
                }
        }

        static void CutterCrane(MB mb)
        {
            var I = Quaternion.identity;
            var pb = V(1.6f, CDeck, 8.9f);
            mb.Cyl(pb, pb + Vector3.up * 1.1f, 0.42f, 8, CIronD);
            mb.Box(pb + V(0f, 1.45f, 0f), V(0.9f, 0.7f, 1.0f), CRust2, I);
            var p0 = pb + V(0f, 1.6f, 0.2f); var tip = p0 + V(1.4f, 5.6f, 2.6f);
            var d = (tip - p0).normalized; var u = Vector3.Cross(d, Vector3.up).normalized; var w = Vector3.Cross(u, d);
            const int NS = 7; var c = new Vector3[NS + 1, 4];
            for (int k = 0; k <= NS; k++)
            {
                float s = Mathf.Lerp(0.32f, 0.12f, k / (float)NS); var m = Vector3.Lerp(p0, tip, k / (float)NS);
                c[k, 0] = m + (u + w) * s; c[k, 1] = m + (-u + w) * s; c[k, 2] = m + (-u - w) * s; c[k, 3] = m + (u - w) * s;
            }
            for (int k = 0; k <= NS; k++)
                for (int q = 0; q < 4; q++)
                {
                    mb.Rod(c[k, q], c[k, (q + 1) % 4], 0.04f, CRust2);
                    if (k == NS) continue;
                    mb.Rod(c[k, q], c[k + 1, q], 0.08f, CRust);
                    mb.Rod(k % 2 == 0 ? c[k, q] : c[k, (q + 1) % 4], k % 2 == 0 ? c[k + 1, (q + 1) % 4] : c[k + 1, q], 0.04f, CRust2);
                }
            var cable = C(0.03f, 0.03f, 0.03f, 0.1f);
            mb.Rod(tip, pb + V(0f, 1.85f, -0.4f), 0.035f, cable);
            var hb = tip + Vector3.down * 3.4f;
            mb.Rod(tip, hb, 0.03f, cable);
            mb.Box(hb + Vector3.down * 0.12f, V(0.22f, 0.28f, 0.16f), CIronD, I);
            var h0 = hb + Vector3.down * 0.3f; var h1 = h0 + Vector3.down * 0.35f; var h2 = h1 + V(0f, -0.08f, 0.2f); var h3 = h2 + V(0f, 0.2f, 0.06f);
            mb.Rod(h0, h1, 0.05f, CIron); mb.Rod(h1, h2, 0.05f, CIron); mb.Rod(h2, h3, 0.05f, CIron);
        }

        static void CutterSpikes(MB mb, Vector3[][] R)
        {
            // along the sides just above the water
            foreach (var s in new[] { -1f, 1f })
                foreach (var z in new[] { -7.2f, -4.6f, -2.0f, 0.6f, 4.4f, 8.0f })
                    Spike(mb, V(s * (HullHw(-1.45f, z) - 0.1f), -1.45f, z), V(s, -0.18f, z < 0 ? -0.15f : 0.1f), 0.85f, 0.1f, CIron);
            // ram at the bow
            Spike(mb, V(0f, -1.5f, StemZ(-1.5f) + 0.35f), V(0f, -0.08f, -1f), 2.0f, 0.26f, CIron);
            foreach (var s in new[] { -1f, 1f }) Spike(mb, V(s * 0.15f, -0.5f, StemZ(-0.5f) + 0.3f), V(s * 0.35f, 0.05f, -1f), 1.4f, 0.18f, CIronD);
            Spike(mb, V(0f, 2.4f, StemZ(2.4f) + 0.3f), V(0f, 0.15f, -1f), 1.2f, 0.15f, CIron);
            // crown on the bow bulwark
            int nl = R.Length, N = R[0].Length, n = CutZ.Length; var top = R[nl - 1];
            for (int i = n - 4; i <= n + 3; i++)
            {
                var p = top[i] + Vector3.up * 0.06f; var o = EdgeOut(top[(i + N - 1) % N], top[i]) + EdgeOut(top[i], top[(i + 1) % N]);
                Spike(mb, p, o.normalized + Vector3.up * 0.7f + Vector3.back * 0.3f, Rf(0.9f, 1.3f), 0.08f, i % 2 == 0 ? CIron : CIronD);
            }
            // stern
            foreach (var s in new[] { -1f, 1f }) Spike(mb, V(s * 1.1f, 0.4f, CutZ[0] * 0.99f), V(s * 0.25f, 0f, 1f), 1.0f, 0.13f, CIron);
            // spiked boxes with a spiked wheel on both sides
            foreach (var s in new[] { -1f, 1f })
                foreach (var z in new[] { 2.6f, 6.25f })
                {
                    float hx = HullHw(-0.55f, z); var bc = V(s * (hx + 0.2f), -0.55f, z);
                    mb.Box(bc, V(0.6f, 1.3f, 1.5f), CRust2, Quaternion.identity);
                    mb.Box(bc + V(s * 0.31f, 0f, 0f), V(0.04f, 1.15f, 1.35f), CIronD, Quaternion.identity);
                    var wc = bc + V(s * 0.33f, 0f, 0f);
                    mb.Cyl(wc, wc + V(s * 0.12f, 0f, 0f), 0.38f, 8, CIron, 22.5f);
                    Spike(mb, wc + V(s * 0.1f, 0f, 0f), V(s, 0f, 0f), 0.45f, 0.08f, CIron);
                    for (int a = 0; a < 8; a++)
                    {
                        float ang = a * 45f * Mathf.Deg2Rad; var dir = V(0f, Mathf.Cos(ang), Mathf.Sin(ang));
                        Spike(mb, wc + V(s * 0.06f, 0f, 0f) + dir * 0.32f, dir, 0.3f, 0.05f, CIron);
                    }
                }
        }
    }
}
