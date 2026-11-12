using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Builds the island president's house (A2_Start) under the "President house" object: a compact stone-and-timber
    /// house. Stone plinth, steps and ground floor, a timber-framed plank upper floor, a wooden
    /// shingle gable roof with a stone chimney, a small porch, shuttered windows. Metal only as details: hinges,
    /// door straps, copper gutters and downpipes, wall lanterns, ridge cap and the gable emblem.
    /// Faceted vertex-coloured meshes (alpha = shine) on Island_Solid / tiled variants, saved under
    /// Assets/Meshes/PresidentHouseStone. Re-running rebuilds the model and keeps its current position and rotation.
    /// Model space: x = right, z = depth (front step edge at z 0, the house grows backwards), y = up from the ground.
    /// </summary>
    public static class PresidentHouseBuilder
    {
        const string RootName = "President house";
        const string ModelName = "President House Model";
        const string MeshDir = "Assets/Meshes/PresidentHouseStone";
        const string MatDir = "Assets/Materials/PresidentHouse/Exterior";

        // Used only when there is no model yet (otherwise the existing model's position / rotation is kept).
        static readonly Vector3 DefaultOrigin = new Vector3(-11.5f, 3.05f, 48.45f);
        const float LayoutZ = 1.2f;   // the layout below starts at the plinth front; the steps stick out 1.2 m in front of it

        // Layout (metres): plinth, ground floor (stone), upper floor (timber), roof.
        const float PX = 5.0f, PZ1 = 9.0f, PY0 = -1.0f, F = 0.7f;
        const float WX = 4.6f, WZ0 = 1.6f, WZ1 = 8.8f, G1 = 3.9f;
        const float UX = 4.75f, UZ0 = 1.45f, UZ1 = 8.95f, U0 = 4.2f, U1 = 6.8f;
        const float RX = 5.35f, E = 6.65f, ZF = 1.0f, ZB = 9.4f, Pitch = 28f;
        static float R => E + RX * Mathf.Tan(Pitch * Mathf.Deg2Rad);   // ridge height (top of the roof boards)

        // Palette (alpha = shine; kept low on stone and wood so flat faces don't flash white).
        static readonly Color StoneW = new Color(0.50f, 0.48f, 0.45f, 0.03f), StoneF = new Color(0.40f, 0.39f, 0.37f, 0.03f),
            StoneL = new Color(0.62f, 0.59f, 0.55f, 0.04f), StoneM = new Color(0.50f, 0.48f, 0.45f, 0.03f),
            Boards = new Color(0.47f, 0.31f, 0.18f, 0.05f), WoodD = new Color(0.32f, 0.20f, 0.11f, 0.05f), WoodM = new Color(0.47f, 0.30f, 0.17f, 0.06f),
            Shutter = new Color(0.44f, 0.29f, 0.17f, 0.05f), Shingle = new Color(0.36f, 0.25f, 0.18f, 0.04f), Soot = new Color(0.10f, 0.09f, 0.08f, 0f),
            Iron = new Color(0.21f, 0.22f, 0.24f, 0.25f), Lead = new Color(0.36f, 0.38f, 0.41f, 0.3f),
            Copper = new Color(0.72f, 0.43f, 0.26f, 0.5f), Brass = new Color(0.86f, 0.66f, 0.32f, 0.7f),
            GlassGlow = new Color(0.36f, 0.26f, 0.15f, 1f), LampGlow = new Color(1f, 0.78f, 0.45f, 1f);

        static Material solid, glow, stoneWall, stoneBlock, boardMat;
        static Transform model, house, colliders;
        static MB mStone, mFound, mTrim, mBoards, mTimber, mShingle, mMetal, mGlow;
        static System.Random rnd;
        static int boxCount, lightCount;

        [MenuItem("Tools/Coop Game/President House/Build (A2_Start)")]
        public static string Build()
        {
            var root = GameObject.Find(RootName);
            if (root == null) return "No '" + RootName + "' object in the open scene.";

            MakeMaterials();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build President House");
            int group = Undo.GetCurrentGroup();

            // Old greybox: keep it, just switch it off.
            foreach (Transform c in root.transform)
                if (c.name.StartsWith("President House") && c.name != ModelName && c.gameObject.activeSelf)
                { Undo.RecordObject(c.gameObject, "Hide greybox"); c.gameObject.SetActive(false); }

            // Keep where the model is now (it may have been moved by hand).
            Vector3 pos = DefaultOrigin; Quaternion rot = Quaternion.Euler(0, root.transform.eulerAngles.y, 0);
            var old = root.transform.Find(ModelName);
            if (old != null) { pos = old.position; rot = old.rotation; Undo.DestroyObjectImmediate(old.gameObject); }

            var go = new GameObject(ModelName);
            Undo.RegisterCreatedObjectUndo(go, "Build President House");
            go.transform.SetParent(root.transform, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one;
            model = go.transform;
            house = new GameObject("House").transform; house.SetParent(model, false); house.localPosition = new Vector3(0, 0, LayoutZ);
            colliders = new GameObject("Colliders").transform; colliders.SetParent(house, false);

            rnd = new System.Random(7); boxCount = 0; lightCount = 0;
            mStone = new MB(); mFound = new MB(); mTrim = new MB(); mBoards = new MB(); mTimber = new MB(); mShingle = new MB(); mMetal = new MB(); mGlow = new MB();

            BuildFoundation();
            BuildGroundFloor();
            BuildUpperFloor();
            BuildRoof();
            BuildChimney();
            BuildPorch();
            BuildLanterns();

            Piece("Stone Foundation", mFound, stoneBlock);
            Piece("Stone Walls", mStone, stoneWall);
            Piece("Stone Trim", mTrim, solid);
            Piece("Wood Boards", mBoards, boardMat);
            Piece("Timber Frame", mTimber, solid);
            Piece("Roof Shingles", mShingle, solid);
            Piece("Metal Details", mMetal, solid);
            Piece("Window Glow", mGlow, glow, shadows: false);

            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.GetComponent<Light>() == null) t.gameObject.isStatic = true;
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(root.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = go;
            return Report();
        }

        // ---------- Parts ----------

        static void BuildFoundation()
        {
            // Plinth (y -1.0 .. 0.72, sunk into the sloping ground), flagstone cap = porch floor.
            mFound.Box(new Vector3(0, (PY0 + 0.6f) / 2, PZ1 / 2), new Vector3(2 * PX, 0.6f - PY0, PZ1), StoneF);
            mTrim.Box(new Vector3(0, 0.65f, PZ1 / 2), new Vector3(2 * PX + 0.12f, 0.14f, PZ1 + 0.12f), StoneL);
            Col("Plinth", new Vector3(0, (PY0 + 0.72f) / 2, PZ1 / 2), new Vector3(2 * PX + 0.12f, 0.72f - PY0, PZ1 + 0.12f));
            // Three stone steps up to the porch.
            for (int i = 0; i < 3; i++)
            {
                float top = i * 0.24f, depth = (3 - i) * 0.4f;
                var c = new Vector3(0, (top - 0.7f) / 2, -depth / 2); var s = new Vector3(2.6f, top + 0.7f, depth);
                mTrim.Box(c, s, i % 2 == 0 ? StoneM : StoneL);
                Col("Step " + (i + 1), c + Vector3.up * 0.01f, s + Vector3.up * 0.02f);
            }
        }

        static void BuildGroundFloor()
        {
            mStone.Box(new Vector3(0, (F + G1) / 2, (WZ0 + WZ1) / 2), new Vector3(2 * WX, G1 - F, WZ1 - WZ0), StoneW);
            // Door + windows.
            BuildDoor();
            foreach (float x in new[] { -3.0f, 3.0f }) Window(new Vector3(x, 2.25f, WZ0), Vector3.back, 0.9f, 1.3f, true);
            foreach (float x in new[] { -2.4f, 2.4f }) Window(new Vector3(x, 2.25f, WZ1), Vector3.forward, 0.9f, 1.3f, true);
            foreach (float s in new[] { -1f, 1f })
                foreach (float z in new[] { 3.4f, 7.0f })
                    Window(new Vector3(s * WX, 2.25f, z), s < 0 ? Vector3.left : Vector3.right, 0.9f, 1.3f, true);
        }

        static void BuildDoor()
        {
            var n = Vector3.back; float z = WZ0, w = 1.3f, h = 2.4f;
            mBoards.Box(new Vector3(0, F + h / 2, z - 0.03f), new Vector3(w, h, 0.08f), WoodM);
            mTimber.Box(new Vector3(-w / 2 - 0.07f, F + (h + 0.14f) / 2, z - 0.05f), new Vector3(0.14f, h + 0.14f, 0.12f), WoodD);
            mTimber.Box(new Vector3(w / 2 + 0.07f, F + (h + 0.14f) / 2, z - 0.05f), new Vector3(0.14f, h + 0.14f, 0.12f), WoodD);
            mTimber.Box(new Vector3(0, F + h + 0.07f, z - 0.05f), new Vector3(w + 0.28f, 0.14f, 0.12f), WoodD);
            // Stone lintel.
            mTrim.Box(new Vector3(0, F + h + 0.3f, z - 0.06f), new Vector3(w + 0.7f, 0.32f, 0.14f), StoneL);
            // Iron hinge straps.
            foreach (float y in new[] { 0.5f, 1.9f })
            {
                mMetal.Box(new Vector3(-0.08f, F + y, z - 0.085f), new Vector3(w - 0.2f, 0.08f, 0.025f), Iron);
            }
        }

        static void BuildUpperFloor()
        {
            float mz = (UZ0 + UZ1) / 2, H = U1 - U0, yc = (U0 + U1) / 2;
            mBoards.Box(new Vector3(0, yc, mz), new Vector3(2 * UX, H, UZ1 - UZ0), Boards);
            // Floor band (the jetty beam), top plate and corner posts.
            mTimber.Box(new Vector3(0, (G1 + U0) / 2, mz), new Vector3(2 * UX + 0.16f, U0 - G1, UZ1 - UZ0 + 0.16f), WoodD);
            mTimber.Box(new Vector3(0, U1 - 0.09f, mz), new Vector3(2 * UX + 0.12f, 0.18f, UZ1 - UZ0 + 0.12f), WoodD);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float z in new[] { UZ0, UZ1 })
                    mTimber.Box(new Vector3(sx * UX, yc, z), new Vector3(0.24f, H, 0.24f), WoodD);
            // Front: posts between the windows.
            foreach (float s in new[] { -1f, 1f })
            {
                mTimber.Box(new Vector3(s * 1.35f, yc, UZ0 - 0.03f), new Vector3(0.2f, H, 0.12f), WoodD);
                var n = s < 0 ? Vector3.left : Vector3.right;
                foreach (float z in new[] { 3.4f, 7.0f }) Window(new Vector3(s * UX, 5.55f, z), n, 0.85f, 1.1f, false);
                Window(new Vector3(s * 2.4f, 5.55f, UZ1), Vector3.forward, 0.85f, 1.1f, false);
            }
            foreach (float x in new[] { -2.7f, 0f, 2.7f }) Window(new Vector3(x, 5.55f, UZ0), Vector3.back, 0.85f, 1.1f, false);
            Col("House Body", new Vector3(0, (F + U1) / 2, mz), new Vector3(2 * UX, U1 - F, UZ1 - UZ0));

            // Plank gables under the roof, with a king post.
            var tri = new[] { new Vector2(-UX, U1), new Vector2(UX, U1), new Vector2(0, R - 0.16f) };
            mBoards.ExtrudeXY(tri, UZ0, UZ0 + 0.3f, Boards, Vector3.zero);
            mBoards.ExtrudeXY(tri, UZ1 - 0.3f, UZ1, Boards, Vector3.zero);
            foreach (var (z, n) in new[] { (UZ0, Vector3.back), (UZ1, Vector3.forward) })
            {
                mTimber.Box(new Vector3(0, (U1 + R - 0.3f) / 2, z + n.z * 0.03f), new Vector3(0.18f, R - 0.3f - U1, 0.12f), WoodD);
            }
            // Presidential emblem on the front gable: iron disc with a brass star.
            mMetal.Prism(new Vector3(0, 7.75f, UZ0 - 0.105f), 16, 0.44f, 0.44f, 0.03f, Iron, Quaternion.Euler(-90, 0, 0));
            mMetal.ExtrudeXY(StarPts(5, 0.32f, 0.13f), UZ0 - 0.135f, UZ0 - 0.175f, Brass, new Vector3(0, 7.75f, 0));
        }

        static void BuildRoof()
        {
            float mz = (ZF + ZB) / 2, len = ZB - ZF;
            foreach (float s in new[] { -1f, 1f })
            {
                var eave = new Vector3(s * RX, E, 0); var ridge = new Vector3(0, R, 0);
                var u = (ridge - eave).normalized; var nrm = Vector3.Cross(Vector3.forward, u).normalized; if (nrm.y < 0) nrm = -nrm;
                float L = (ridge - eave).magnitude; var mid = (eave + ridge) / 2 + Vector3.forward * mz;
                var q = Quaternion.LookRotation(Vector3.forward, nrm);
                mTimber.Box(mid - nrm * 0.07f, new Vector3(L, 0.14f, len), WoodD, q);                       // roof boards
                Shingles(mShingle, eave, ridge, Vector3.forward, ZF - 0.03f, ZB + 0.03f);
                foreach (float z in new[] { ZF - 0.035f, ZB + 0.035f })                                     // bargeboards
                    mTimber.Box(new Vector3(mid.x, mid.y, z) - nrm * 0.06f, new Vector3(L + 0.12f, 0.32f, 0.07f), WoodD, q);
                // Copper gutter along the eave.
                mMetal.Box(new Vector3(s * (RX + 0.12f), E - 0.14f, mz), new Vector3(0.16f, 0.13f, len), Copper);
                // One downpipe per side: drop, dog-leg in to the wall, down to the plinth.
                {
                    const float z = 1.85f;
                    float xo = s * (RX + 0.12f), xi = s * 4.92f;
                    Rod(mMetal, new Vector3(xo, E - 0.12f, z), new Vector3(xo, E - 0.5f, z), 0.11f, Copper);
                    Rod(mMetal, new Vector3(xo, E - 0.5f, z), new Vector3(xi, E - 1.05f, z), 0.11f, Copper);
                    Rod(mMetal, new Vector3(xi, E - 1.05f, z), new Vector3(xi, 0.95f, z), 0.11f, Copper);
                    Rod(mMetal, new Vector3(xi, 0.98f, z), new Vector3(s * 5.15f, 0.8f, z), 0.11f, Copper);
                }
            }
            // Lead ridge cap.
            mMetal.Box(new Vector3(0, R - 0.06f, mz), new Vector3(0.3f, 0.3f, len + 0.12f), Lead, Quaternion.Euler(0, 0, 45));
            // Convex roof collider.
            var cm = new MB(); cm.ExtrudeXY(new[] { new Vector2(-RX - 0.1f, E - 0.2f), new Vector2(RX + 0.1f, E - 0.2f), new Vector2(0, R + 0.1f) }, ZF, ZB, Color.white, Vector3.zero);
            var cgo = new GameObject("Roof"); cgo.transform.SetParent(colliders, false);
            var mc = cgo.AddComponent<MeshCollider>(); mc.sharedMesh = Save(cm.Done("PHS_RoofCollider")); mc.convex = true;
        }

        static void BuildChimney()
        {
            const float x = 3.0f, z = 6.2f, top = 10.4f;
            mStone.Box(new Vector3(x, (6.9f + top) / 2, z), new Vector3(0.9f, top - 6.9f, 0.9f), StoneW);
            mTrim.Box(new Vector3(x, top + 0.07f, z), new Vector3(1.1f, 0.14f, 1.1f), StoneL);
            mTrim.Box(new Vector3(x, top + 0.145f, z), new Vector3(0.6f, 0.02f, 0.6f), Soot);
            Col("Chimney", new Vector3(x, (6.9f + top) / 2, z), new Vector3(0.9f, top - 6.9f, 0.9f));
        }

        static void BuildPorch()
        {
            // Posts, beam, small shingle lean-to roof.
            foreach (float s in new[] { -1f, 1f })
            {
                mTimber.Box(new Vector3(s * 2.1f, (0.72f + 3.3f) / 2, 0.3f), new Vector3(0.22f, 3.3f - 0.72f, 0.22f), WoodM);
                Col("Porch Post " + (s < 0 ? "L" : "R"), new Vector3(s * 2.1f, (0.72f + 3.3f) / 2, 0.3f), new Vector3(0.34f, 3.3f - 0.72f, 0.34f));
            }
            mTimber.Box(new Vector3(0, 3.4f, 0.3f), new Vector3(4.9f, 0.2f, 0.2f), WoodD);
            var eave = new Vector3(0, 3.62f, -0.2f); var top = new Vector3(0, 3.98f, 1.58f);
            var u = (top - eave).normalized; var nrm = Vector3.Cross(Vector3.right, u).normalized; if (nrm.y < 0) nrm = -nrm;
            float L = (top - eave).magnitude; var q = Quaternion.LookRotation(Vector3.right, nrm); var mid = (eave + top) / 2;
            mTimber.Box(mid - nrm * 0.07f, new Vector3(L, 0.14f, 4.9f), WoodD, q);
            Shingles(mShingle, eave, top, Vector3.right, -2.48f, 2.48f);
            foreach (float s in new[] { -1f, 1f })
                mTimber.Box(mid + Vector3.right * s * 2.49f - nrm * 0.06f, new Vector3(L + 0.06f, 0.3f, 0.06f), WoodD, q);
            mMetal.Box(new Vector3(0, 3.5f, -0.3f), new Vector3(4.96f, 0.12f, 0.14f), Copper);
            var cgo = new GameObject("Porch Roof"); cgo.transform.SetParent(colliders, false);
            cgo.transform.localPosition = mid - nrm * 0.02f; cgo.transform.localRotation = q;
            cgo.AddComponent<BoxCollider>().size = new Vector3(L, 0.24f, 4.9f);
        }

        static void BuildLanterns()
        {
            var g = new GameObject("Lights").transform; g.SetParent(house, false);
            foreach (float s in new[] { -1f, 1f })
            {
                float x = s * 1.05f, z = WZ0, lz = z - 0.32f;
                mMetal.Box(new Vector3(x, 3.15f, z - 0.015f), new Vector3(0.12f, 0.24f, 0.03f), Iron);
                Rod(mMetal, new Vector3(x, 3.15f, z), new Vector3(x, 3.15f, z - 0.38f), 0.045f, Iron);
                mMetal.Prism(new Vector3(x, 2.91f, lz), 4, 0.18f, 0f, 0.13f, Brass, 45f);
                mMetal.Box(new Vector3(x, 2.6f, lz), new Vector3(0.22f, 0.04f, 0.22f), Iron);
                mGlow.Box(new Vector3(x, 2.755f, lz), new Vector3(0.17f, 0.26f, 0.17f), LampGlow);
                AddLight(g, "Lantern Light " + (s < 0 ? "L" : "R"), new Vector3(x, 2.72f, z - 0.85f), new Color(1f, 0.78f, 0.48f), 1.6f, 5f);
            }
        }
        // ---------- Details ----------

        /// <summary>Window on a wall: c = opening centre on the wall surface, n = outward normal.</summary>
        static void Window(Vector3 c, Vector3 n, float w, float h, bool stone)
        {
            var q = Quaternion.LookRotation(n); var r = q * Vector3.right;
            Vector3 P(float x, float y, float o) => c + r * x + Vector3.up * y + n * o;
            mGlow.Box(P(0, 0, 0.005f), new Vector3(w, h, 0.02f), GlassGlow, q);
            mTimber.Box(P(-w / 2 - 0.045f, 0, 0.04f), new Vector3(0.09f, h + 0.18f, 0.1f), WoodD, q);
            mTimber.Box(P(w / 2 + 0.045f, 0, 0.04f), new Vector3(0.09f, h + 0.18f, 0.1f), WoodD, q);
            mTimber.Box(P(0, h / 2 + 0.045f, 0.04f), new Vector3(w, 0.09f, 0.1f), WoodD, q);
            mTimber.Box(P(0, -h / 2 - 0.045f, 0.04f), new Vector3(w, 0.09f, 0.1f), WoodD, q);
            if (stone)
            {
                mTrim.Box(P(0, -h / 2 - 0.15f, 0.08f), new Vector3(w + 0.36f, 0.12f, 0.2f), StoneL, q);
                mTrim.Box(P(0, h / 2 + 0.22f, 0.06f), new Vector3(w + 0.5f, 0.26f, 0.14f), StoneL, q);
            }
            // Open shutters (ground floor only).
            if (!stone) return;
            float sw = w / 2 + 0.04f;
            foreach (float s in new[] { -1f, 1f })
            {
                float sx = s * (w / 2 + 0.11f + sw / 2);
                mBoards.Box(P(sx, 0, 0.045f), new Vector3(sw, h + 0.06f, 0.05f), Jit(Shutter, 0.03f), q);
            }
        }
        static void Rod(MB mb, Vector3 a, Vector3 b, float t, Color col)
        {
            var d = b - a; var up = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            mb.Box((a + b) / 2, new Vector3(t, t, d.magnitude + t * 0.5f), col, Quaternion.LookRotation(d.normalized, up));
        }

        /// <summary>Courses of wooden shingles from the eave line up to the ridge line, along axis from a0 to a1.</summary>
        static void Shingles(MB mb, Vector3 eave, Vector3 ridge, Vector3 axis, float a0, float a1)
        {
            var u = (ridge - eave).normalized; float len = (ridge - eave).magnitude;
            var nrm = Vector3.Cross(axis, u).normalized; if (nrm.y < 0) nrm = -nrm;
            // Tilt each course so its lower (butt) edge stands proud of the course below.
            var tn = Quaternion.AngleAxis(6f, axis) * nrm; if (Vector3.Dot(tn, u) < 0) tn = Quaternion.AngleAxis(-6f, axis) * nrm;
            var q = Quaternion.LookRotation(axis, tn);
            const float course = 0.7f, width = 0.8f;
            int n = Mathf.CeilToInt((len + 0.06f - width) / course) + 1;
            for (int k = 0; k < n; k++)
            {
                float s = Mathf.Min(k * course + width / 2 - 0.06f, len + 0.02f - width / 2);
                float x = a0;
                while (x < a1)
                {
                    float l = a1 - a0 + 1f, x0 = Mathf.Max(x, a0), x1 = Mathf.Min(x + l, a1);
                    if (x1 - x0 > 0.08f)
                        mb.Box(eave + u * s + nrm * 0.045f + axis * ((x0 + x1) / 2), new Vector3(width, 0.06f, x1 - x0 - 0.03f), Jit(Shingle, 0.07f), q);
                    x += l;
                }
            }
        }

        static Color Jit(Color c, float amt)
        {
            float k = 1f + ((float)rnd.NextDouble() * 2f - 1f) * amt;
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }

        static Vector2[] StarPts(int points, float rOut, float rIn)
        {
            var pts = new Vector2[points * 2];
            for (int i = 0; i < pts.Length; i++) { float a = i * Mathf.PI / points, r = i % 2 == 0 ? rOut : rIn; pts[i] = new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r); }
            return pts;
        }

        // ---------- Helpers: objects, materials ----------

        static void Piece(string name, MB mb, Material mat, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(house, false);
            go.AddComponent<MeshFilter>().sharedMesh = Save(mb.Done("PHS_" + name.Replace(' ', '_')));
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            if (!shadows) mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void Col(string name, Vector3 c, Vector3 s)
        {
            var go = new GameObject(name); go.transform.SetParent(colliders, false); go.transform.localPosition = c;
            go.AddComponent<BoxCollider>().size = s;
        }

        static void AddLight(Transform parent, string name, Vector3 pos, Color c, float intensity, float range)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            lightCount++;
        }

        static void MakeMaterials()
        {
            solid = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/StyleTest/Island_Solid.mat");
            glow = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/StyleTest/Island_Glow.mat");
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Materials/PresidentHouse", "Exterior");
            stoneWall = TileMat("PHX_StoneWall", 0.95f, 0.45f, 0.4f, 0.035f, new Color(0.30f, 0.28f, 0.26f), 0.16f);
            stoneBlock = TileMat("PHX_StoneBlocks", 1.4f, 0.4f, 0.5f, 0.04f, new Color(0.26f, 0.25f, 0.24f), 0.1f);
            boardMat = TileMat("PHX_WoodBoards", 0.36f, 9f, 0f, 0.015f, new Color(0.22f, 0.13f, 0.07f), 0.12f);
        }

        static Material TileMat(string name, float tile, float aspect, float offset, float grout, Color groutCol, float variation)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(solid); AssetDatabase.CreateAsset(m, path); } else m.CopyPropertiesFromMaterial(solid);
            m.SetFloat("_TileSize", tile); m.SetFloat("_TileAspect", aspect); m.SetFloat("_BrickOffset", offset);
            m.SetFloat("_GroutWidth", grout); m.SetColor("_GroutColor", groutCol); m.SetFloat("_TileVariation", variation); m.SetColor("_CheckerColor", Color.white);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh Save(Mesh m)
        {
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Meshes", "PresidentHouseStone");
            string path = MeshDir + "/" + m.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { string n = m.name; existing.Clear(); EditorUtility.CopySerialized(m, existing); existing.name = n; EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Flat-shaded, vertex-coloured mesh builder (every triangle has its own vertices).</summary>
        class MB
        {
            readonly List<Vector3> v = new List<Vector3>(); readonly List<Color> c = new List<Color>(); readonly List<int> t = new List<int>();

            public void Tri(Vector3 a, Vector3 b, Vector3 d, Color col, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, d - a), outward) < 0) { var x = b; b = d; d = x; }
                int i = v.Count; v.Add(a); v.Add(b); v.Add(d); c.Add(col); c.Add(col); c.Add(col); t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col, Vector3 n) { Tri(a, b, cc, col, n); Tri(a, cc, d, col, n); }

            public void Box(Vector3 cen, Vector3 s, Color col) => Box(cen, s, col, Quaternion.identity);

            public void Box(Vector3 cen, Vector3 s, Color col, Quaternion r)
            {
                // Grow every box by its own 0..4 mm so faces of neighbouring boxes never sit exactly in one plane (no z-fighting).
                float e = 0.0005f * (boxCount++ % 9);
                Vector3 h = s * 0.5f + new Vector3(e, e, e);
                Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward };
                for (int a = 0; a < 3; a++)
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
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

            /// <summary>Star-shaped polygon in the xy plane (around its centre), extruded from z0 to z1, moved by offset.</summary>
            public void ExtrudeXY(Vector2[] pts, float z0, float z1, Color col, Vector3 offset)
            {
                var cen = Vector2.zero; foreach (var p in pts) cen += p; cen /= pts.Length;
                Vector3 V(Vector2 p, float z) => offset + new Vector3(p.x, p.y, z);
                var n0 = Vector3.forward * Mathf.Sign(z0 - z1); var n1 = -n0;
                for (int i = 0; i < pts.Length; i++)
                {
                    var p0 = pts[i]; var p1 = pts[(i + 1) % pts.Length]; var m = (p0 + p1) / 2 - cen;
                    Tri(V(cen, z0), V(p0, z0), V(p1, z0), col, n0);
                    Tri(V(cen, z1), V(p0, z1), V(p1, z1), col, n1);
                    Quad(V(p0, z0), V(p1, z0), V(p1, z1), V(p0, z1), col, new Vector3(m.x, m.y, 0));
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

        // ---------- Report ----------

        static string Report()
        {
            var sb = new System.Text.StringBuilder();
            var bounds = new Bounds(model.position, Vector3.zero);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            int verts = 0; foreach (var f in model.GetComponentsInChildren<MeshFilter>()) verts += f.sharedMesh.vertexCount;
            sb.AppendLine($"Built '{ModelName}' at {model.position:F2}, rot {model.eulerAngles:F1}: world bounds min {bounds.min:F2} max {bounds.max:F2} size {bounds.size:F2}, {verts} verts, {lightCount} lights.");
            foreach (var lp in new[] { new Vector3(-PX, 0, LayoutZ), new Vector3(PX, 0, LayoutZ), new Vector3(-PX, 0, LayoutZ + PZ1), new Vector3(PX, 0, LayoutZ + PZ1), new Vector3(0, 0, 0) })
            {
                var wp = model.TransformPoint(lp) + Vector3.up * 40f;
                foreach (var h in Physics.RaycastAll(wp, Vector3.down, 80f))
                    if (!h.collider.transform.IsChildOf(model)) { sb.AppendLine($"ground under {lp}: {h.point.y - model.position.y:F2} m ({h.collider.name})"); break; }
            }
            var seen = new HashSet<string>();
            foreach (var c in model.GetComponentsInChildren<Collider>())
            {
                var b = c.bounds; b.Expand(-0.1f);
                foreach (var o in Physics.OverlapBox(b.center, b.extents, Quaternion.identity))
                    if (!o.transform.IsChildOf(model) && !o.name.StartsWith("Terrain") && seen.Add(o.name)) sb.AppendLine("overlaps: " + o.name + " (with " + c.name + ")");
            }
            return sb.ToString();
        }
    }
}
