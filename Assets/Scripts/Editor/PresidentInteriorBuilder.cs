using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Builds the president's house interior in A8_Start under "President House Interior": a hall of stone, wood and
    /// metal (brick walls, wood wainscot, steel pilasters and ceiling panels, wooden beams, a skylight well with a
    /// chandelier) with the war table exactly in the middle (0, floor, 0), the president's desk, fireplace, bookshelves
    /// and other furniture, interior lights, trilight ambient and a global post-processing Volume.
    /// Faceted vertex-coloured meshes (alpha = shine) on the StylizedLowPoly materials, saved under
    /// Assets/Meshes/PresidentHouseInterior. The floor sits at y 0.5 like the old greybox, so the cutscene markers stay valid.
    /// The old "Greybox" root is only switched off. Re-running rebuilds the interior.
    /// </summary>
    public static class PresidentInteriorBuilder
    {
        const string RootName = "President House Interior";
        const string MeshDir = "Assets/Meshes/PresidentHouseInterior";
        const string MatDir = "Assets/Materials/PresidentHouse/Interior";
        const string ProfilePath = "Assets/Settings/A8_PresidentInterior_Volume.asset";

        // Room: inner wall faces at x ±HX, z ±HZ (door wall at -z), floor top at F, ceiling at F + H.
        const float F = 0.5f, HX = 9.9f, HZ = 9.4f, H = 6.0f, T = 0.6f;
        const float DoorHalf = 1.6f, DoorTop = 4.2f;
        const float SkyX = 3.3f, SkyZ = 2.35f, SkyH = 2.2f;   // skylight well over the table
        static readonly float[] SideZ = { -7.05f, -2.35f, 2.35f, 7.05f };   // pilasters on the side walls
        static readonly float[] EndX = { -6.6f, -3.3f, 3.3f, 6.6f };        // pilasters on the front/back walls

        static Material solid, glow, stoneFloor, stoneWall, woodPanel, metalPanel;
        static Transform root;
        static readonly HashSet<string> usedNames = new HashSet<string>();
        static int lights, boxCount;

        // Palette (alpha = shine: 0 stone .. 1 gold).
        static readonly Color StoneL = new Color(0.74f, 0.70f, 0.64f, 0.05f), StoneM = new Color(0.62f, 0.58f, 0.54f, 0.05f), StoneD = new Color(0.40f, 0.37f, 0.35f, 0.05f),
            Wood = new Color(0.56f, 0.34f, 0.18f, 0.15f), WoodD = new Color(0.34f, 0.20f, 0.10f, 0.12f), WoodL = new Color(0.70f, 0.48f, 0.28f, 0.18f),
            Steel = new Color(0.42f, 0.45f, 0.50f, 0.7f), SteelD = new Color(0.27f, 0.29f, 0.33f, 0.6f), Chrome = new Color(0.74f, 0.77f, 0.82f, 0.85f),
            Gold = new Color(0.95f, 0.72f, 0.30f, 1f), Red = new Color(0.58f, 0.11f, 0.10f, 0.03f), RedD = new Color(0.40f, 0.07f, 0.08f, 0.03f),
            Blue = new Color(0.14f, 0.24f, 0.52f, 0.05f), Paper = new Color(0.90f, 0.84f, 0.68f, 0.02f), Green = new Color(0.32f, 0.54f, 0.25f, 0.05f),
            GreenD = new Color(0.20f, 0.38f, 0.18f, 0.05f), Leather = new Color(0.45f, 0.20f, 0.10f, 0.3f), LeatherD = new Color(0.32f, 0.13f, 0.07f, 0.3f),
            Soot = new Color(0.12f, 0.10f, 0.09f, 0f), White = new Color(0.92f, 0.92f, 0.90f, 0.1f), Teal = new Color(0.25f, 0.55f, 0.55f, 0.6f);
        // Glow colours (unlit, multiplied by the glow material's boost).
        static readonly Color GSky = new Color(0.2f, 0.29f, 0.42f, 1f), GWarm = new Color(1f, 0.78f, 0.45f, 1f), GFire = new Color(1f, 0.45f, 0.12f, 1f), GFireY = new Color(1f, 0.78f, 0.3f, 1f);

        [MenuItem("Tools/Coop Game/President House/Build Interior (A8_Start)")]
        public static string Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.path.Contains("A8_Start")) return "Open A8_Start first (active scene is " + scene.path + ").";

            MakeMaterials();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build President House Interior");
            int group = Undo.GetCurrentGroup();

            // Old greybox room: keep it, just switch it off.
            var greybox = GameObject.Find("Greybox");
            if (greybox != null && greybox.activeSelf) { Undo.RecordObject(greybox, "Hide greybox"); greybox.SetActive(false); }

            var old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            usedNames.Clear(); windowFills.Clear(); floorLamps.Clear(); lights = 0; boxCount = 0;

            var go = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(go, "Build President House Interior");
            root = go.transform;

            BuildShell();
            BuildDoor();
            BuildWindows();
            BuildSkylightAndChandelier();
            BuildWarTable();
            BuildPresidentCorner();
            BuildFireplace();
            BuildBookshelves();
            BuildCornerPieces();
            BuildSideFurniture();
            BuildDecor();
            BuildLights();
            var vol = BuildVolume();
            string amb = SetAmbient();
            string cam = EnableCameraPost();

            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<Light>() == null && t.GetComponent<Volume>() == null) t.gameObject.isStatic = true;

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = go;
            return $"Built '{RootName}': {go.GetComponentsInChildren<MeshRenderer>(true).Length} meshes, {lights} lights, volume '{vol}'. {amb} {cam} Greybox hidden: {(greybox != null)}.";
        }

        // ---------- Shell: floor, walls, trims, pilasters, ceiling ----------

        static void BuildShell()
        {
            var s = Group("Structure");

            var mb = new MB(); mb.Box(new Vector3(0, F - 0.25f, 0), new Vector3(2 * HX + 2 * T, 0.5f, 2 * HZ + 2 * T), StoneM);
            Piece(s, "Floor", mb, stoneFloor, collider: true);

            mb = new MB(); float bw = 0.7f, by = F + 0.01f;
            mb.Box(new Vector3(0, by, HZ - bw / 2), new Vector3(2 * HX, 0.02f, bw), StoneD);
            mb.Box(new Vector3(0, by, -HZ + bw / 2), new Vector3(2 * HX, 0.02f, bw), StoneD);
            mb.Box(new Vector3(-HX + bw / 2, by, 0), new Vector3(bw, 0.02f, 2 * HZ - 2 * bw), StoneD);
            mb.Box(new Vector3(HX - bw / 2, by, 0), new Vector3(bw, 0.02f, 2 * HZ - 2 * bw), StoneD);
            Piece(s, "Floor Border", mb, solid);

            float wy = F + H / 2, fl = HX + T, wl = fl - DoorHalf, th = H - DoorTop;
            Wall(s, "Wall Back", new Vector3(0, wy, HZ + T / 2), new Vector3(2 * fl, H, T));
            Wall(s, "Wall Left", new Vector3(-HX - T / 2, wy, 0), new Vector3(T, H, 2 * HZ));
            Wall(s, "Wall Right", new Vector3(HX + T / 2, wy, 0), new Vector3(T, H, 2 * HZ));
            Wall(s, "Wall Front L", new Vector3(-(DoorHalf + wl / 2), wy, -HZ - T / 2), new Vector3(wl, H, T));
            Wall(s, "Wall Front R", new Vector3(DoorHalf + wl / 2, wy, -HZ - T / 2), new Vector3(wl, H, T));
            Wall(s, "Wall Front Top", new Vector3(0, F + DoorTop + th / 2, -HZ - T / 2), new Vector3(2 * DoorHalf, th, T));

            // Wood wainscot (planks via the material's world tiles), stone plinth, steel rail with a gold line.
            mb = new MB(); Band(mb, F + 0.3f, F + 1.7f, 0.08f, Wood, DoorHalf + 0.35f);
            Piece(s, "Wainscot", mb, woodPanel);
            mb = new MB();
            Band(mb, F, F + 0.3f, 0.14f, StoneD, DoorHalf + 0.35f);
            Band(mb, F + 1.7f, F + 1.82f, 0.14f, Steel, DoorHalf + 0.35f);
            Band(mb, F + 1.82f, F + 1.86f, 0.11f, Gold, DoorHalf + 0.35f);
            // Cornice under the ceiling: steel, gold line, plain steel frieze.
            Band(mb, F + H - 0.45f, F + H, 0.3f, SteelD, 0);
            Band(mb, F + H - 0.52f, F + H - 0.45f, 0.24f, Gold, 0);
            Band(mb, F + H - 0.9f, F + H - 0.52f, 0.12f, Steel, 0);
            Piece(s, "Wall Trims", mb, solid);

            // Steel pilasters on stone bases, riveted face plates, gold capitals.
            mb = new MB(); var cols = new List<(Vector3, Vector3)>();
            foreach (var z in SideZ) { Pilaster(mb, new Vector3(-HX, F, z), Vector3.right, cols); Pilaster(mb, new Vector3(HX, F, z), Vector3.left, cols); }
            foreach (var x in EndX) { Pilaster(mb, new Vector3(x, F, HZ), Vector3.back, cols); Pilaster(mb, new Vector3(x, F, -HZ), Vector3.forward, cols); }
            var pil = Piece(s, "Pilasters", mb, solid);
            foreach (var (c, sz) in cols) { var bc = pil.AddComponent<BoxCollider>(); bc.center = c; bc.size = sz; }

            // Ceiling: steel panels around the skylight opening.
            mb = new MB(); float cy = F + H + 0.2f, ex = HX + T, ez = HZ + T;
            mb.Box(new Vector3(0, cy, (SkyZ + ez) / 2), new Vector3(2 * ex, 0.4f, ez - SkyZ), SteelD);
            mb.Box(new Vector3(0, cy, -(SkyZ + ez) / 2), new Vector3(2 * ex, 0.4f, ez - SkyZ), SteelD);
            mb.Box(new Vector3(-(SkyX + ex) / 2, cy, 0), new Vector3(ex - SkyX, 0.4f, 2 * SkyZ), SteelD);
            mb.Box(new Vector3((SkyX + ex) / 2, cy, 0), new Vector3(ex - SkyX, 0.4f, 2 * SkyZ), SteelD);
            Piece(s, "Ceiling", mb, metalPanel, collider: true);

            // Dark wooden beams on the pilaster lines, steel plates where they cross.
            mb = new MB();
            foreach (var z in SideZ) mb.Box(new Vector3(0, F + H - 0.25f, z), new Vector3(2 * HX, 0.5f, 0.4f), WoodD);
            foreach (var x in EndX) mb.Box(new Vector3(x, F + H - 0.23f, 0), new Vector3(0.38f, 0.46f, 2 * HZ), WoodD);
            foreach (var z in SideZ) foreach (var x in EndX)
            {
                mb.Box(new Vector3(x, F + H - 0.26f, z), new Vector3(0.56f, 0.56f, 0.56f), Steel);
                for (int i = 0; i < 4; i++)
                {
                    var d = Quaternion.Euler(0, 45 + 90 * i, 0) * Vector3.forward * 0.2f;
                    mb.Box(new Vector3(x + d.x, F + H - 0.55f, z + d.z), new Vector3(0.07f, 0.03f, 0.07f), Gold);
                }
            }
            Piece(s, "Ceiling Beams", mb, solid);
        }

        static void Wall(Transform p, string name, Vector3 c, Vector3 size)
        {
            var mb = new MB(); mb.Box(c, size, StoneM); Piece(p, name, mb, stoneWall, collider: true);
        }

        /// <summary>A band along the four inner walls (y0..y1, sticking out d); the front run leaves a gap of ±gap round the door.</summary>
        static void Band(MB mb, float y0, float y1, float d, Color col, float gap)
        {
            float cy = (y0 + y1) / 2, hh = y1 - y0;
            mb.Box(new Vector3(0, cy, HZ - d / 2), new Vector3(2 * HX, hh, d), col);
            mb.Box(new Vector3(-HX + d / 2, cy, 0), new Vector3(d, hh, 2 * HZ - 2 * d), col);
            mb.Box(new Vector3(HX - d / 2, cy, 0), new Vector3(d, hh, 2 * HZ - 2 * d), col);
            if (gap <= 0) mb.Box(new Vector3(0, cy, -HZ + d / 2), new Vector3(2 * HX, hh, d), col);
            else
            {
                float w = HX - gap;
                mb.Box(new Vector3(-(gap + w / 2), cy, -HZ + d / 2), new Vector3(w, hh, d), col);
                mb.Box(new Vector3(gap + w / 2, cy, -HZ + d / 2), new Vector3(w, hh, d), col);
            }
        }

        /// <summary>Size of a box on a wall with inward normal n: w along the wall, h up, d out of the wall.</summary>
        static Vector3 Sz(Vector3 n, float w, float h, float d) => Mathf.Abs(n.x) > 0.5f ? new Vector3(d, h, w) : new Vector3(w, h, d);
        static Vector3 Along(Vector3 n) => Mathf.Abs(n.x) > 0.5f ? Vector3.forward : Vector3.right;

        static void Pilaster(MB mb, Vector3 o, Vector3 n, List<(Vector3, Vector3)> cols)
        {
            var u = Along(n);
            var core = o + n * 0.125f + Vector3.up * (H / 2); var coreSz = Sz(n, 0.6f, H, 0.25f);
            mb.Box(core, coreSz, SteelD);
            mb.Box(o + n * 0.27f + Vector3.up * (H / 2), Sz(n, 0.4f, H - 1.6f, 0.06f), Steel);
            for (int k = 0; k < 8; k++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                    mb.Box(o + n * 0.31f + u * (0.14f * sgn) + Vector3.up * (1.2f + k * 0.5f), new Vector3(0.05f, 0.05f, 0.05f), Chrome);
            var bas = o + n * 0.225f + Vector3.up * 0.3f; var basSz = Sz(n, 0.85f, 0.6f, 0.45f);
            mb.Box(bas, basSz, StoneL);
            mb.Box(o + n * 0.24f + Vector3.up * 0.625f, Sz(n, 0.88f, 0.05f, 0.48f), Gold);
            mb.Box(o + n * 0.2f + Vector3.up * (H - 1.0f), Sz(n, 0.8f, 0.2f, 0.4f), Gold);
            cols.Add((core, coreSz)); cols.Add((bas, basSz));
        }

        // ---------- Door ----------

        static void BuildDoor()
        {
            var g = Group("Door");
            var mb = new MB(); float z = -HZ;
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Box(new Vector3(s * (DoorHalf + 0.15f), F + DoorTop / 2, z + 0.05f), new Vector3(0.38f, DoorTop, 0.3f), SteelD);
                mb.Box(new Vector3(s * (DoorHalf + 0.02f), F + DoorTop / 2, z + 0.12f), new Vector3(0.05f, DoorTop, 0.2f), Gold);
                for (int k = 0; k < 6; k++) mb.Box(new Vector3(s * (DoorHalf + 0.17f), F + 0.5f + k * 0.65f, z + 0.21f), new Vector3(0.06f, 0.06f, 0.04f), Chrome);
            }
            mb.Box(new Vector3(0, F + DoorTop + 0.18f, z + 0.05f), new Vector3(2 * DoorHalf + 0.68f, 0.44f, 0.32f), SteelD);
            mb.Box(new Vector3(0, F + DoorTop + 0.02f, z + 0.12f), new Vector3(2 * DoorHalf + 0.1f, 0.05f, 0.2f), Gold);
            var toRoom = Quaternion.Euler(90, 0, 0);   // prism axis -> +z (into the room)
            mb.Prism(new Vector3(0, F + DoorTop + 0.75f, z), 16, 0.42f, 0.42f, 0.15f, SteelD, toRoom);
            mb.Prism(new Vector3(0, F + DoorTop + 0.75f, z), 16, 0.34f, 0.34f, 0.19f, Gold, toRoom);
            mb.ExtrudeXY(StarPts(5, 0.26f, 0.11f), 0.19f, 0.24f, Blue, new Vector3(0, F + DoorTop + 0.75f, z));
            // Steel straps, rivets and gold ring handles on the room side of the leaves.
            for (int s = -1; s <= 1; s += 2)
            {
                float cx = s * DoorHalf / 2;
                foreach (var y in new[] { 0.6f, 2.1f, 3.6f })
                {
                    mb.Box(new Vector3(cx, F + y, z + 0.05f), new Vector3(DoorHalf - 0.15f, 0.14f, 0.04f), Steel);
                    for (int k = -2; k <= 2; k++) mb.Box(new Vector3(cx + k * 0.3f, F + y, z + 0.075f), new Vector3(0.04f, 0.04f, 0.02f), Chrome);
                }
                mb.Box(new Vector3(s * 0.28f, F + 1.9f, z + 0.08f), new Vector3(0.07f, 0.45f, 0.06f), Gold);
                mb.Box(new Vector3(s * 0.28f, F + 2.15f, z + 0.07f), new Vector3(0.16f, 0.07f, 0.05f), Gold);
            }
            Piece(g, "Door Frame", mb, solid);

            mb = new MB();
            for (int s = -1; s <= 1; s += 2) mb.Box(new Vector3(s * DoorHalf / 2, F + DoorTop / 2, z - 0.05f), new Vector3(DoorHalf - 0.02f, DoorTop, 0.16f), Wood);
            Piece(g, "Door Leaves", mb, woodPanel, collider: true);
        }

        // ---------- Windows (glowing panes on the wall, steel frames, stone sills, curtains) ----------

        static void BuildWindows()
        {
            var g = Group("Windows");
            MB glass = new MB(), frame = new MB(), cloth = new MB();
            // Sill at F + 2.1 buries the wainscot's gold line; the tops stay under the frieze (F + 5.1), so nothing shares a plane with the trims.
            foreach (var z in new[] { -4.7f, 4.7f }) Window(glass, frame, cloth, new Vector3(-HX, F + 2.1f, z), Vector3.right, 1.6f, 2.6f);
            foreach (var z in new[] { -4.7f, 0f, 4.7f }) Window(glass, frame, cloth, new Vector3(HX, F + 2.1f, z), Vector3.left, 1.6f, 2.6f);
            foreach (var x in new[] { -4.95f, 4.95f }) Window(glass, frame, cloth, new Vector3(x, F + 2.1f, -HZ), Vector3.forward, 1.3f, 2.4f);
            Piece(g, "Window Glass", glass, glow, shadows: false);
            Piece(g, "Window Frames", frame, solid);
            Piece(g, "Curtains", cloth, solid);
        }

        static readonly List<Vector3> windowFills = new List<Vector3>();

        static void Window(MB glass, MB frame, MB cloth, Vector3 sill, Vector3 n, float w, float h)
        {
            var u = Along(n); var c = sill + Vector3.up * (h / 2);
            glass.Box(c + n * 0.02f, Sz(n, w + 0.06f, h + 0.06f, 0.04f), GSky);
            for (int s = -1; s <= 1; s += 2)
            {
                frame.Box(c + u * (s * (w / 2 + 0.07f)) + n * 0.06f, Sz(n, 0.14f, h + 0.28f, 0.12f), SteelD);
                frame.Box(c + Vector3.up * (s * (h / 2 + 0.07f)) + n * 0.06f, Sz(n, w + 0.28f, 0.14f, 0.12f), SteelD);
                frame.Box(c + Vector3.up * (s * h / 6) + n * 0.05f, Sz(n, w, 0.07f, 0.07f), Steel);
            }
            frame.Box(c + n * 0.05f, Sz(n, 0.07f, h, 0.07f), Steel);
            frame.Box(c - Vector3.up * (h / 2 + 0.2f) + n * 0.15f, Sz(n, w + 0.5f, 0.12f, 0.3f), StoneL);
            frame.Box(c + Vector3.up * (h / 2 + 0.3f) + n * 0.08f, Sz(n, w + 0.5f, 0.22f, 0.16f), Steel);
            frame.Box(c + Vector3.up * (h / 2 + 0.17f) + n * 0.1f, Sz(n, w + 0.5f, 0.04f, 0.16f), Gold);
            // Curtains: three folds a side, a gold rod with finials, gold tie-backs.
            for (int s = -1; s <= 1; s += 2)
            {
                for (int f = 0; f < 3; f++)
                {
                    var pos = c + u * (s * (w / 2 + 0.17f + f * 0.14f)) + n * (0.22f + (f % 2) * 0.04f) + Vector3.up * 0.1f;
                    cloth.Box(pos, Sz(n, 0.15f, h + 0.9f + 0.05f * f, 0.08f), f % 2 == 0 ? Red : RedD);
                }
                cloth.Box(c + u * (s * (w / 2 + 0.31f)) + n * 0.3f - Vector3.up * (h * 0.15f), Sz(n, 0.48f, 0.06f, 0.06f), Gold);
                cloth.Box(c + u * (s * (w / 2 + 0.78f)) + n * 0.3f + Vector3.up * (h / 2 + 0.55f), Sz(n, 0.1f, 0.1f, 0.1f), Gold);
            }
            cloth.Box(c + n * 0.3f + Vector3.up * (h / 2 + 0.55f), Sz(n, w + 1.46f, 0.05f, 0.05f), Gold);
            windowFills.Add(c + n * 0.9f);
        }

        // ---------- Skylight well over the table + chandelier ----------

        static void BuildSkylightAndChandelier()
        {
            var g = Group("Skylight");
            // Well walls start inside the ceiling slab and sit 2 cm back from the opening edge (no shared faces with the ceiling).
            float y0 = F + H, wh = SkyH - 0.1f, ym = y0 + 0.1f + wh / 2;
            var mb = new MB();
            mb.Box(new Vector3(0, ym, SkyZ + 0.17f), new Vector3(2 * SkyX + 0.64f, wh, 0.3f), StoneM);
            mb.Box(new Vector3(0, ym, -SkyZ - 0.17f), new Vector3(2 * SkyX + 0.64f, wh, 0.3f), StoneM);
            mb.Box(new Vector3(-SkyX - 0.17f, ym, 0), new Vector3(0.3f, wh, 2 * SkyZ + 0.04f), StoneM);
            mb.Box(new Vector3(SkyX + 0.17f, ym, 0), new Vector3(0.3f, wh, 2 * SkyZ + 0.04f), StoneM);
            Piece(g, "Skylight Well", mb, stoneWall);

            mb = new MB(); float gy = y0 + SkyH - 0.1f;
            for (float x = -SkyX; x <= SkyX + 0.01f; x += SkyX / 3) mb.Box(new Vector3(x, gy, 0), new Vector3(0.12f, 0.15f, 2 * SkyZ), SteelD);
            for (float z = -SkyZ; z <= SkyZ + 0.01f; z += SkyZ / 2) mb.Box(new Vector3(0, gy, z), new Vector3(2 * SkyX, 0.15f, 0.12f), SteelD);
            // Gold line round the well just above the ceiling.
            mb.Box(new Vector3(0, y0 + 0.5f, SkyZ), new Vector3(2 * SkyX, 0.08f, 0.06f), Gold);
            mb.Box(new Vector3(0, y0 + 0.5f, -SkyZ), new Vector3(2 * SkyX, 0.08f, 0.06f), Gold);
            mb.Box(new Vector3(-SkyX, y0 + 0.5f, 0), new Vector3(0.06f, 0.08f, 2 * SkyZ), Gold);
            mb.Box(new Vector3(SkyX, y0 + 0.5f, 0), new Vector3(0.06f, 0.08f, 2 * SkyZ), Gold);
            Piece(g, "Skylight Frame", mb, solid);

            mb = new MB(); mb.Box(new Vector3(0, y0 + SkyH, 0), new Vector3(2 * SkyX + 0.3f, 0.06f, 2 * SkyZ + 0.3f), GSky);
            Piece(g, "Skylight Glass", mb, glow, shadows: false);

            // Chandelier: steel outer ring with candles, gold inner ring, spokes, chains to the skylight frame.
            mb = new MB(); var bulbs = new MB(); float ry = F + 4.3f, hook = F + 5.6f;
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f, ar = a * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(ar) * 1.2f, ry, Mathf.Cos(ar) * 1.2f);
                mb.Box(p, new Vector3(0.1f, 0.1f, 0.66f), Steel, Quaternion.Euler(0, a + 15f + 90f, 0));
                mb.Prism(p + Vector3.up * 0.05f, 6, 0.08f, 0.06f, 0.07f, Gold);
                mb.Prism(p + Vector3.up * 0.12f, 6, 0.04f, 0.04f, 0.18f, White);
                bulbs.Prism(p + Vector3.up * 0.3f, 5, 0.04f, 0f, 0.1f, GWarm);
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f, ar = a * Mathf.Deg2Rad;
                mb.Box(new Vector3(Mathf.Sin(ar) * 0.55f, ry + 0.25f, Mathf.Cos(ar) * 0.55f), new Vector3(0.08f, 0.08f, 0.44f), Gold, Quaternion.Euler(0, a + 22.5f + 90f, 0));
            }
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f, ar = a * Mathf.Deg2Rad;
                var a0 = new Vector3(Mathf.Sin(ar) * 0.55f, ry + 0.25f, Mathf.Cos(ar) * 0.55f); var a1 = new Vector3(Mathf.Sin(ar) * 1.2f, ry, Mathf.Cos(ar) * 1.2f);
                Rod(mb, a0, a1, 0.05f, SteelD);
            }
            mb.Box(new Vector3(0, ry + 0.2f, 0), new Vector3(0.12f, 0.7f, 0.12f), Steel);
            mb.Prism(new Vector3(0, ry - 0.15f, 0), 8, 0.18f, 0f, 0.45f, Gold, Quaternion.Euler(180, 0, 0));
            mb.Sphere(new Vector3(0, ry + 0.62f, 0), 0.13f, 8, 4, (i, k) => Gold);
            for (int i = 0; i < 4; i++)
            {
                float ar = (45 + i * 90) * Mathf.Deg2Rad;
                Rod(mb, new Vector3(Mathf.Sin(ar) * 1.2f, ry, Mathf.Cos(ar) * 1.2f), new Vector3(0, hook, 0), 0.035f, SteelD);
            }
            mb.Box(new Vector3(0, hook, 0), new Vector3(0.16f, 0.16f, 0.16f), Gold);
            Rod(mb, new Vector3(0, hook, 0), new Vector3(0, y0 + SkyH - 0.1f, 0), 0.05f, SteelD);
            Piece(g, "Chandelier", mb, solid);
            Piece(g, "Chandelier Flames", bulbs, glow, shadows: false);
        }

        static void Rod(MB mb, Vector3 a, Vector3 b, float t, Color col)
        {
            var d = b - a; mb.Box((a + b) / 2, new Vector3(t, d.magnitude, t), col, Quaternion.FromToRotation(Vector3.up, d));
        }

        // ---------- War table, exactly in the middle of the house ----------

        static void BuildWarTable()
        {
            var g = Group("Furniture");
            var mb = new MB();
            mb.Prism(new Vector3(0, 0, 0), 8, 0.62f, 0.62f, 0.12f, StoneD, 22.5f);
            mb.Prism(new Vector3(0, 0.12f, 0), 8, 0.42f, 0.34f, 0.62f, StoneL, 22.5f);
            mb.Prism(new Vector3(0, 0.3f, 0), 8, 0.43f, 0.43f, 0.06f, Gold, 22.5f);
            mb.Prism(new Vector3(0, 0.72f, 0), 8, 0.5f, 0.5f, 0.08f, SteelD, 22.5f);
            for (int i = 0; i < 4; i++) mb.Box(new Vector3(0, 0.79f, 0), new Vector3(0.08f, 0.06f, 1.8f), Steel, Quaternion.Euler(0, 45 * i + 22.5f, 0));
            mb.Prism(new Vector3(0, 0.84f, 0), 8, 1.03f, 1.03f, 0.07f, Steel, 22.5f);
            mb.Prism(new Vector3(0, 0.82f, 0), 8, 1.0f, 1.0f, 0.11f, Wood, 22.5f);
            mb.Prism(new Vector3(0, 0.93f, 0), 8, 0.93f, 0.93f, 0.008f, Gold, 22.5f);
            mb.Prism(new Vector3(0, 0.93f, 0), 8, 0.88f, 0.88f, 0.012f, WoodL, 22.5f);
            for (int i = 0; i < 8; i++) { float ar = (i * 45) * Mathf.Deg2Rad; mb.Box(new Vector3(Mathf.Sin(ar) * 0.98f, 0.875f, Mathf.Cos(ar) * 0.98f), new Vector3(0.06f, 0.06f, 0.06f), Gold); }
            // Map of the island, tokens, compass, papers.
            var r = Quaternion.Euler(0, 8, 0);
            mb.Box(new Vector3(0, 0.947f, 0), new Vector3(1.1f, 0.01f, 0.75f), Paper, r);
            mb.Box(new Vector3(0, 0.954f, 0), new Vector3(0.98f, 0.004f, 0.63f), new Color(0.55f, 0.7f, 0.78f, 0.05f), r);
            mb.Box(new Vector3(-0.12f, 0.958f, 0.04f), new Vector3(0.38f, 0.004f, 0.26f), Green, Quaternion.Euler(0, 20, 0));
            mb.Box(new Vector3(0.28f, 0.958f, -0.13f), new Vector3(0.18f, 0.004f, 0.15f), Green, Quaternion.Euler(0, -12, 0));
            mb.Box(new Vector3(-0.1f, 0.961f, 0.05f), new Vector3(0.12f, 0.004f, 0.1f), GreenD, Quaternion.Euler(0, 35, 0));
            foreach (var (p, col) in new[] { (new Vector2(-0.2f, 0.1f), Red), (new Vector2(0.25f, -0.1f), Red), (new Vector2(0.05f, -0.22f), Blue), (new Vector2(-0.33f, -0.1f), Blue) })
            { mb.Prism(new Vector3(p.x, 0.96f, p.y), 6, 0.035f, 0.02f, 0.07f, col); mb.Sphere(new Vector3(p.x, 1.045f, p.y), 0.025f, 6, 3, (i, k) => col); }
            mb.Prism(new Vector3(0.62f, 0.94f, 0.38f), 12, 0.12f, 0.12f, 0.025f, Gold);
            mb.Prism(new Vector3(0.62f, 0.965f, 0.38f), 12, 0.1f, 0.1f, 0.006f, White);
            mb.Box(new Vector3(0.62f, 0.973f, 0.38f), new Vector3(0.02f, 0.006f, 0.16f), Red, Quaternion.Euler(0, 30, 0));
            mb.Box(new Vector3(-0.55f, 0.952f, -0.42f), new Vector3(0.28f, 0.02f, 0.38f), White, Quaternion.Euler(0, 22, 0));
            mb.Box(new Vector3(-0.52f, 0.966f, -0.4f), new Vector3(0.28f, 0.006f, 0.38f), Paper, Quaternion.Euler(0, 10, 0));
            mb.Box(new Vector3(0.45f, 0.95f, -0.5f), new Vector3(0.02f, 0.015f, 0.2f), Gold, Quaternion.Euler(0, -40, 0));
            Piece(g, "War Table", mb, solid, collider: true, pos: new Vector3(0, F, 0));

        }

        // ---------- President's corner: desk, chair, emblem, banners ----------

        static void BuildPresidentCorner()
        {
            var g = Group("President Corner");
            var mb = new MB();
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Box(new Vector3(s * 1.05f, 0.42f, 0), new Vector3(0.8f, 0.8f, 1.0f), Wood);
                foreach (var y in new[] { 0.17f, 0.42f, 0.67f })
                {
                    mb.Box(new Vector3(s * 1.05f, y, -0.505f), new Vector3(0.7f, 0.21f, 0.02f), WoodD);
                    mb.Box(new Vector3(s * 1.05f, y, -0.525f), new Vector3(0.18f, 0.04f, 0.03f), Gold);
                }
                mb.Box(new Vector3(s * 1.05f, 0.02f, 0), new Vector3(0.84f, 0.04f, 1.04f), SteelD);
            }
            mb.Box(new Vector3(0, 0.5f, -0.42f), new Vector3(1.32f, 0.6f, 0.06f), WoodD);
            mb.Prism(new Vector3(0, 0.5f, -0.45f), 12, 0.2f, 0.2f, 0.03f, Gold, Quaternion.Euler(-90, 0, 0));
            mb.Box(new Vector3(0, 0.805f, 0), new Vector3(3.14f, 0.05f, 1.19f), Steel);
            mb.Box(new Vector3(0, 0.85f, 0), new Vector3(3.1f, 0.05f, 1.15f), WoodL);
            mb.Box(new Vector3(0, 0.879f, 0.05f), new Vector3(1.0f, 0.008f, 0.6f), new Color(0.15f, 0.3f, 0.2f, 0.2f));
            mb.Box(new Vector3(0.1f, 0.887f, 0.0f), new Vector3(0.3f, 0.008f, 0.4f), White, Quaternion.Euler(0, -6, 0));
            mb.Box(new Vector3(0.32f, 0.89f, 0.05f), new Vector3(0.015f, 0.015f, 0.18f), Gold, Quaternion.Euler(0, 25, 0));
            // Desk lamp (green shade), red phone, little flags.
            mb.Prism(new Vector3(-1.1f, 0.875f, 0.2f), 8, 0.12f, 0.1f, 0.04f, Steel);
            mb.Box(new Vector3(-1.1f, 1.08f, 0.2f), new Vector3(0.03f, 0.38f, 0.03f), Steel);
            mb.Prism(new Vector3(-1.1f, 1.22f, 0.12f), 8, 0.17f, 0.07f, 0.13f, new Color(0.12f, 0.42f, 0.24f, 0.6f));
            mb.Box(new Vector3(1.0f, 0.915f, 0.1f), new Vector3(0.22f, 0.08f, 0.18f), Red);
            mb.Box(new Vector3(1.0f, 0.97f, 0.1f), new Vector3(0.26f, 0.04f, 0.06f), RedD);
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Box(new Vector3(0.55f + s * 0.06f, 1.0f, 0.4f), new Vector3(0.012f, 0.26f, 0.012f), Gold);
                mb.Box(new Vector3(0.55f + s * 0.06f + 0.05f, 1.08f, 0.4f), new Vector3(0.1f, 0.07f, 0.005f), s < 0 ? Blue : White);
            }
            mb.Box(new Vector3(0.55f, 0.885f, 0.4f), new Vector3(0.2f, 0.02f, 0.08f), WoodD);
            Piece(g, "President Desk", mb, solid, collider: true, pos: new Vector3(0, F, 7.95f));
            var bulb = new MB(); bulb.Prism(new Vector3(-1.1f, 1.14f, 0.12f), 6, 0.06f, 0.06f, 0.06f, GWarm);
            Piece(g, "Desk Lamp Bulb", bulb, glow, shadows: false, pos: new Vector3(0, F, 7.95f));

            // High-backed president chair, facing the room.
            mb = new MB();
            mb.Prism(new Vector3(0, 0, 0), 5, 0.35f, 0.35f, 0.06f, SteelD);
            mb.Box(new Vector3(0, 0.25f, 0), new Vector3(0.1f, 0.4f, 0.1f), Chrome);
            mb.Box(new Vector3(0, 0.47f, 0), new Vector3(0.7f, 0.06f, 0.62f), SteelD);
            mb.Box(new Vector3(0, 0.56f, 0.02f), new Vector3(0.64f, 0.14f, 0.58f), Red);
            mb.Box(new Vector3(0, 1.15f, -0.27f), new Vector3(0.66f, 1.2f, 0.12f), Red);
            mb.Box(new Vector3(0, 1.15f, -0.32f), new Vector3(0.74f, 1.3f, 0.05f), SteelD);
            mb.Box(new Vector3(0, 1.82f, -0.3f), new Vector3(0.78f, 0.08f, 0.1f), Gold);
            mb.Prism(new Vector3(0, 1.86f, -0.3f), 4, 0.08f, 0f, 0.16f, Gold, 45f);
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Box(new Vector3(s * 0.38f, 0.78f, 0.02f), new Vector3(0.08f, 0.06f, 0.6f), Wood);
                mb.Box(new Vector3(s * 0.38f, 0.65f, 0.25f), new Vector3(0.05f, 0.22f, 0.05f), Chrome);
                for (int k = 0; k < 5; k++) mb.Box(new Vector3(s * 0.3f, 0.7f + k * 0.22f, -0.205f), new Vector3(0.04f, 0.04f, 0.02f), Gold);
            }
            Piece(g, "President Chair", mb, solid, collider: true, pos: new Vector3(0, F, 8.85f), rotY: 180f);

            // Emblem on the back wall: steel ring, gold ring, blue disc, gold star.
            mb = new MB(); var ep = new Vector3(0, F + 3.9f, HZ); var toRoom = Quaternion.Euler(-90, 0, 0);   // prism axis -> -z
            mb.Prism(ep, 16, 1.25f, 1.25f, 0.15f, SteelD, toRoom);
            mb.Prism(ep, 16, 1.12f, 1.12f, 0.2f, Gold, toRoom);
            mb.Prism(ep, 16, 0.98f, 0.98f, 0.24f, Blue, toRoom);
            mb.ExtrudeXY(StarPts(5, 0.8f, 0.33f), -0.24f, -0.32f, Gold, ep);
            for (int i = 0; i < 16; i++) { float ar = i * 22.5f * Mathf.Deg2Rad; mb.Box(ep + new Vector3(Mathf.Sin(ar) * 1.19f, Mathf.Cos(ar) * 1.19f, -0.165f), new Vector3(0.06f, 0.06f, 0.04f), Chrome); }
            // Banners on gold rods either side.
            foreach (var x in new[] { -2.4f, 2.4f })
            {
                float top = F + 5.4f, bot = F + 2.2f, bc = (top + bot) / 2;
                mb.Box(new Vector3(x, top + 0.1f, HZ - 0.2f), new Vector3(1.4f, 0.06f, 0.06f), Gold);
                for (int s = -1; s <= 1; s += 2) mb.Sphere(new Vector3(x + s * 0.74f, top + 0.1f, HZ - 0.2f), 0.06f, 6, 3, (i, k) => Gold);
                for (int s = -1; s <= 1; s += 2) mb.Box(new Vector3(x + s * 0.4f, top + 0.05f, HZ - 0.2f), new Vector3(0.03f, 0.12f, 0.03f), Gold);
                mb.Box(new Vector3(x, bc, HZ - 0.15f), new Vector3(1.1f, top - bot, 0.04f), Blue);
                mb.ExtrudeXY(new[] { new Vector2(-0.55f, 0), new Vector2(0, -0.5f), new Vector2(0.55f, 0) }, -0.13f, -0.17f, Blue, new Vector3(x, bot, HZ));
                for (int s = -1; s <= 1; s += 2) mb.Box(new Vector3(x + s * 0.38f, bc, HZ - 0.175f), new Vector3(0.07f, top - bot, 0.01f), Gold);
                mb.ExtrudeXY(StarPts(5, 0.28f, 0.12f), -0.17f, -0.2f, White, new Vector3(x, F + 4.5f, HZ));
                mb.Box(new Vector3(x, F + 3.5f, HZ - 0.175f), new Vector3(0.6f, 0.06f, 0.01f), White);
            }
            Piece(g, "Emblem And Banners", mb, solid);
        }

        // ---------- Fireplace on the left wall ----------

        static void BuildFireplace()
        {
            var g = Group("Fireplace");
            var pos = new Vector3(-HX, F, 0); const float rot = 90f;   // local +z -> into the room (+x)
            var mb = new MB();
            mb.Box(new Vector3(0, 0.1f, 0.75f), new Vector3(3.6f, 0.2f, 1.5f), StoneD);
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Box(new Vector3(s * 1.35f, 1.15f, 0.45f), new Vector3(0.7f, 1.9f, 0.9f), StoneL);
                mb.Box(new Vector3(s * 1.35f, 0.35f, 0.47f), new Vector3(0.76f, 0.3f, 0.94f), StoneM);
            }
            mb.Box(new Vector3(0, 1.0f, 0.1f), new Vector3(2.0f, 1.8f, 0.2f), Soot);
            mb.Box(new Vector3(0, 0.21f, 0.5f), new Vector3(2.0f, 0.02f, 0.8f), Soot);
            mb.Box(new Vector3(0, 1.95f, 0.45f), new Vector3(2.0f, 0.3f, 0.9f), StoneM);
            mb.Box(new Vector3(0, 1.95f, 0.915f), new Vector3(2.1f, 0.18f, 0.03f), Steel);
            for (int k = -3; k <= 3; k++) mb.Box(new Vector3(k * 0.3f, 1.95f, 0.935f), new Vector3(0.04f, 0.04f, 0.02f), Chrome);
            mb.Box(new Vector3(0, 2.21f, 0.55f), new Vector3(3.9f, 0.22f, 1.1f), WoodD);
            mb.Box(new Vector3(0, 2.09f, 1.09f), new Vector3(3.84f, 0.04f, 0.03f), Gold);
            // Grate and logs.
            for (int k = -2; k <= 2; k++) mb.Box(new Vector3(k * 0.3f, 0.38f, 0.75f), new Vector3(0.04f, 0.36f, 0.04f), SteelD);
            mb.Box(new Vector3(0, 0.5f, 0.75f), new Vector3(1.3f, 0.04f, 0.04f), SteelD);
            mb.Box(new Vector3(0, 0.3f, 0.5f), new Vector3(1.2f, 0.18f, 0.18f), WoodD, Quaternion.Euler(0, 12, 0));
            mb.Box(new Vector3(0.1f, 0.42f, 0.45f), new Vector3(1.0f, 0.16f, 0.16f), Wood, Quaternion.Euler(0, -18, 8));
            // Mantel decor in front of the chimney breast (which is 0.7 deep): candlesticks and a clock.
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Prism(new Vector3(s * 1.55f, 2.325f, 0.9f), 6, 0.08f, 0.04f, 0.05f, Gold);
                mb.Prism(new Vector3(s * 1.55f, 2.37f, 0.9f), 6, 0.03f, 0.03f, 0.3f, Gold);
                mb.Prism(new Vector3(s * 1.55f, 2.67f, 0.9f), 6, 0.035f, 0.035f, 0.16f, White);
            }
            mb.Box(new Vector3(0, 2.575f, 0.88f), new Vector3(0.5f, 0.5f, 0.25f), Wood);
            mb.Prism(new Vector3(0, 2.6f, 1.0f), 12, 0.18f, 0.18f, 0.025f, Gold, Quaternion.Euler(90, 0, 0));
            mb.Prism(new Vector3(0, 2.6f, 1.01f), 12, 0.15f, 0.15f, 0.025f, White, Quaternion.Euler(90, 0, 0));
            mb.Box(new Vector3(0, 2.65f, 1.04f), new Vector3(0.02f, 0.11f, 0.01f), SteelD);
            mb.Box(new Vector3(0.04f, 2.6f, 1.045f), new Vector3(0.09f, 0.02f, 0.01f), SteelD);
            // Portrait of the president above the mantel.
            mb.Box(new Vector3(0, 3.75f, 0.73f), new Vector3(1.7f, 2.1f, 0.08f), Gold);
            mb.Box(new Vector3(0, 3.75f, 0.78f), new Vector3(1.45f, 1.85f, 0.04f), new Color(0.12f, 0.14f, 0.22f, 0.05f));
            mb.Box(new Vector3(0, 3.12f, 0.805f), new Vector3(1.0f, 0.6f, 0.02f), new Color(0.16f, 0.16f, 0.2f, 0.1f));
            mb.Box(new Vector3(0, 3.32f, 0.815f), new Vector3(0.3f, 0.2f, 0.01f), White);
            mb.Box(new Vector3(0, 3.18f, 0.825f), new Vector3(0.08f, 0.4f, 0.01f), Red);
            mb.Box(new Vector3(0, 3.75f, 0.81f), new Vector3(0.42f, 0.55f, 0.02f), new Color(0.85f, 0.66f, 0.5f, 0.05f));
            mb.Box(new Vector3(0, 4.05f, 0.82f), new Vector3(0.48f, 0.14f, 0.02f), new Color(0.32f, 0.26f, 0.2f, 0.05f));
            Piece(g, "Fireplace Stone", mb, solid, collider: true, pos: pos, rotY: rot);

            mb = new MB(); mb.Box(new Vector3(0, 2.32f + (H - 2.32f) / 2, 0.35f), new Vector3(2.8f, H - 2.32f, 0.7f), StoneL);
            Piece(g, "Chimney Breast", mb, stoneWall, pos: pos, rotY: rot);

            mb = new MB();
            mb.Box(new Vector3(0, 0.235f, 0.55f), new Vector3(1.3f, 0.04f, 0.55f), new Color(0.8f, 0.25f, 0.05f, 1f));
            var rnd = new System.Random(7);
            for (int i = 0; i < 7; i++)
            {
                float x = -0.5f + i * 0.17f, hgt = 0.45f + (float)rnd.NextDouble() * 0.35f;
                mb.Prism(new Vector3(x, 0.35f, 0.5f + (float)rnd.NextDouble() * 0.1f), 5, 0.13f, 0f, hgt, GFire, i * 17f);
                mb.Prism(new Vector3(x, 0.36f, 0.58f), 5, 0.07f, 0f, hgt * 0.6f, GFireY, i * 31f);
            }
            for (int s = -1; s <= 1; s += 2) mb.Prism(new Vector3(s * 1.55f, 2.83f, 0.9f), 5, 0.03f, 0f, 0.08f, GWarm);
            Piece(g, "Fire", mb, glow, shadows: false, pos: pos, rotY: rot);
        }

        // ---------- Bookshelves on the back wall ----------

        static void BuildBookshelves()
        {
            var g = Group("Bookshelves");
            var rnd = new System.Random(1234);
            foreach (var x in new[] { -4.95f, 4.95f })
            {
                var mb = new MB(); float w = 2.0f, hgt = 3.6f, d = 0.55f;
                for (int s = -1; s <= 1; s += 2) mb.Box(new Vector3(s * (w / 2 - 0.03f), hgt / 2, 0), new Vector3(0.06f, hgt, d), WoodD);
                mb.Box(new Vector3(0, hgt / 2, -d / 2 + 0.02f), new Vector3(w - 0.06f, hgt, 0.04f), new Color(0.27f, 0.16f, 0.08f, 0.1f));
                mb.Box(new Vector3(0, hgt + 0.06f, 0.02f), new Vector3(w + 0.1f, 0.12f, d + 0.07f), Wood);
                mb.Box(new Vector3(0, hgt - 0.05f, d / 2 + 0.01f), new Vector3(w, 0.08f, 0.02f), Steel);
                mb.Box(new Vector3(0, hgt + 0.005f, d / 2 + 0.04f), new Vector3(w + 0.1f, 0.03f, 0.02f), Gold);
                mb.Box(new Vector3(0, 0.08f, 0.02f), new Vector3(w + 0.04f, 0.16f, d + 0.04f), SteelD);
                float[] shelves = { 0.16f, 0.86f, 1.56f, 2.26f, 2.96f };
                foreach (var y in shelves) mb.Box(new Vector3(0, y + 0.025f, 0), new Vector3(w - 0.08f, 0.05f, d - 0.04f), Wood);
                for (int k = 0; k < shelves.Length; k++)
                {
                    float y = shelves[k] + 0.05f, maxH = (k < shelves.Length - 1 ? shelves[k + 1] : hgt - 0.1f) - y - 0.06f;
                    Books(mb, rnd, -w / 2 + 0.08f, w / 2 - 0.08f, y, Mathf.Min(maxH, 0.55f), -d / 2 + 0.05f, 0.38f, k == 2);
                }
                Piece(g, x < 0 ? "Bookshelf L" : "Bookshelf R", mb, solid, collider: true, pos: new Vector3(x, F, HZ - 0.15f - d / 2), rotY: 180f);
            }
        }

        static readonly Color[] BookColors =
        {
            new Color(0.55f, 0.12f, 0.10f, 0.1f), new Color(0.15f, 0.28f, 0.50f, 0.1f), new Color(0.20f, 0.40f, 0.22f, 0.1f), new Color(0.50f, 0.36f, 0.15f, 0.1f),
            new Color(0.70f, 0.55f, 0.25f, 0.2f), new Color(0.30f, 0.18f, 0.12f, 0.1f), new Color(0.75f, 0.70f, 0.60f, 0.1f), new Color(0.35f, 0.15f, 0.35f, 0.1f)
        };

        static void Books(MB mb, System.Random rnd, float x0, float x1, float y, float maxH, float zBack, float depth, bool withDecor)
        {
            float x = x0;
            while (x < x1 - 0.06f)
            {
                double roll = rnd.NextDouble();
                if (withDecor && roll < 0.08 && x + 0.3f < x1)
                {
                    // A small gold-banded vase or a stack of lying books.
                    if (rnd.NextDouble() < 0.5) { mb.Prism(new Vector3(x + 0.15f, y, zBack + 0.2f), 8, 0.09f, 0.11f, 0.2f, Teal); mb.Prism(new Vector3(x + 0.15f, y + 0.2f, zBack + 0.2f), 8, 0.11f, 0.05f, 0.1f, Teal); mb.Prism(new Vector3(x + 0.15f, y + 0.15f, zBack + 0.2f), 8, 0.105f, 0.105f, 0.03f, Gold); }
                    else for (int i = 0; i < 3; i++) mb.Box(new Vector3(x + 0.15f, y + 0.03f + i * 0.06f, zBack + 0.19f), new Vector3(0.26f - i * 0.03f, 0.06f, 0.34f), BookColors[rnd.Next(BookColors.Length)], Quaternion.Euler(0, i * 7f, 0));
                    x += 0.32f; continue;
                }
                if (roll < 0.12) { x += 0.12f + (float)rnd.NextDouble() * 0.15f; continue; }
                float w = 0.05f + (float)rnd.NextDouble() * 0.06f; if (x + w > x1) break;
                float h = maxH * (0.62f + (float)rnd.NextDouble() * 0.36f), d = depth * (0.78f + (float)rnd.NextDouble() * 0.22f);
                var col = BookColors[rnd.Next(BookColors.Length)];
                mb.Box(new Vector3(x + w / 2, y + h / 2, zBack + d / 2), new Vector3(w, h, d), col);
                if (rnd.NextDouble() < 0.35) mb.Box(new Vector3(x + w / 2, y + h * 0.8f, zBack + d + 0.002f), new Vector3(w * 0.8f, 0.02f, 0.004f), Gold);
                x += w + 0.004f;
            }
        }

        // ---------- Back corners: gold safe, trophy shelf ----------

        static void BuildCornerPieces()
        {
            var g = Group("Furniture");
            // Safe in the back-right corner, turned towards the table.
            var mb = new MB();
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) mb.Box(new Vector3(sx * 0.58f, 0.04f, sz * 0.38f), new Vector3(0.16f, 0.08f, 0.16f), SteelD);
            mb.Box(new Vector3(0, 1.0f, 0), new Vector3(1.4f, 1.84f, 1.0f), SteelD);
            mb.Box(new Vector3(0, 1.0f, 0.52f), new Vector3(1.2f, 1.62f, 0.04f), Steel);
            for (int k = 0; k < 7; k++) for (int s = -1; s <= 1; s += 2)
            {
                mb.Box(new Vector3(s * 0.54f, 0.3f + k * 0.23f, 0.55f), new Vector3(0.04f, 0.04f, 0.02f), Chrome);
                mb.Box(new Vector3(-0.42f + k * 0.14f, 1.0f + s * 0.75f, 0.55f), new Vector3(0.04f, 0.04f, 0.02f), Chrome);
            }
            var face = Quaternion.Euler(90, 0, 0);
            mb.Prism(new Vector3(0.18f, 1.1f, 0.54f), 16, 0.22f, 0.22f, 0.05f, Gold, face);
            mb.Prism(new Vector3(0.18f, 1.1f, 0.59f), 16, 0.12f, 0.1f, 0.04f, Chrome, face);
            for (int i = 0; i < 3; i++) mb.Box(new Vector3(0.18f, 1.1f, 0.65f), new Vector3(0.48f, 0.04f, 0.04f), Chrome, Quaternion.Euler(0, 0, i * 60f));
            for (int s = -1; s <= 1; s += 2) mb.Box(new Vector3(-0.64f, 1.0f + s * 0.5f, 0.55f), new Vector3(0.08f, 0.25f, 0.08f), Chrome);
            for (int i = 0; i < 3; i++) GoldBar(mb, new Vector3(-0.3f + i * 0.3f, 1.92f, 0));
            for (int i = 0; i < 2; i++) GoldBar(mb, new Vector3(-0.15f + i * 0.3f, 2.0f, 0));
            GoldBar(mb, new Vector3(0, 2.08f, 0));
            Piece(g, "Gold Safe", mb, solid, collider: true, pos: new Vector3(8.3f, F, 8.4f), rotY: Mathf.Atan2(-8.3f, -8.4f) * Mathf.Rad2Deg);

            // Open steel trophy shelf in the back-left corner: vases, gold bars, a stone bust.
            mb = new MB(); float w = 1.6f, hgt = 2.4f, d = 0.6f;
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) mb.Box(new Vector3(sx * (w / 2 - 0.03f), hgt / 2, sz * (d / 2 - 0.03f)), new Vector3(0.06f, hgt, 0.06f), SteelD);
            float[] ys = { 0.1f, 0.8f, 1.5f, 2.2f };
            foreach (var y in ys) { mb.Box(new Vector3(0, y, 0), new Vector3(w - 0.04f, 0.05f, d - 0.04f), Wood); mb.Box(new Vector3(0, y, d / 2 - 0.02f), new Vector3(w, 0.07f, 0.03f), Steel); }
            mb.Prism(new Vector3(0, hgt, 0), 4, 0.2f, 0f, 0.15f, Gold, 45f);
            Vase(mb, new Vector3(-0.45f, 0.125f, 0), 0.5f, Teal);
            Vase(mb, new Vector3(0.4f, 0.125f, 0), 0.38f, new Color(0.7f, 0.38f, 0.22f, 0.15f));
            for (int i = 0; i < 3; i++) GoldBar(mb, new Vector3(-0.35f + i * 0.28f, 0.84f, 0));
            for (int i = 0; i < 2; i++) GoldBar(mb, new Vector3(-0.21f + i * 0.28f, 0.92f, 0));
            mb.Prism(new Vector3(0.5f, 0.825f, 0), 12, 0.15f, 0.15f, 0.02f, Gold);
            mb.Box(new Vector3(0, 1.525f, 0), new Vector3(0.3f, 0.12f, 0.25f), StoneD);
            mb.Prism(new Vector3(0, 1.645f, 0), 6, 0.2f, 0.1f, 0.18f, StoneL);
            mb.Box(new Vector3(0, 1.93f, 0), new Vector3(0.17f, 0.24f, 0.2f), StoneL);
            mb.Box(new Vector3(0, 2.04f, -0.01f), new Vector3(0.18f, 0.05f, 0.2f), StoneM);
            Vase(mb, new Vector3(-0.55f, 1.525f, 0), 0.3f, Blue);
            Vase(mb, new Vector3(0.55f, 1.525f, 0), 0.3f, Blue);
            Piece(g, "Trophy Shelf", mb, solid, collider: true, pos: new Vector3(-8.3f, F, 8.4f), rotY: Mathf.Atan2(8.3f, -8.4f) * Mathf.Rad2Deg);
        }

        static void GoldBar(MB mb, Vector3 p)
        {
            mb.Box(p + Vector3.up * 0.04f, new Vector3(0.26f, 0.08f, 0.12f), Gold);
            mb.Box(p + Vector3.up * 0.085f, new Vector3(0.2f, 0.01f, 0.08f), new Color(1f, 0.82f, 0.42f, 1f));
        }

        static void Vase(MB mb, Vector3 p, float h, Color col)
        {
            mb.Prism(p, 8, h * 0.18f, h * 0.32f, h * 0.45f, col);
            mb.Prism(p + Vector3.up * h * 0.45f, 8, h * 0.32f, h * 0.12f, h * 0.35f, col);
            mb.Prism(p + Vector3.up * h * 0.8f, 8, h * 0.12f, h * 0.17f, h * 0.2f, col);
            mb.Prism(p + Vector3.up * h * 0.42f, 8, h * 0.325f, h * 0.325f, h * 0.06f, Gold);
        }

        // ---------- Side furniture: sideboard, armchair corners, globe ----------

        static void BuildSideFurniture()
        {
            var g = Group("Furniture");
            // Sideboard under the middle right window: wood body, stone top, steel feet; radio, decanter, vases.
            var mb = new MB();
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) mb.Box(new Vector3(sx * 1.4f, 0.06f, sz * 0.22f), new Vector3(0.08f, 0.12f, 0.08f), SteelD);
            mb.Box(new Vector3(0, 0.52f, 0), new Vector3(3.0f, 0.8f, 0.6f), Wood);
            for (int i = -1; i <= 1; i++)
            {
                mb.Box(new Vector3(i * 0.98f, 0.52f, 0.305f), new Vector3(0.9f, 0.66f, 0.02f), WoodD);
                mb.Box(new Vector3(i * 0.98f, 0.52f, 0.32f), new Vector3(0.05f, 0.05f, 0.03f), Gold);
            }
            mb.Box(new Vector3(0, 0.935f, 0), new Vector3(3.1f, 0.03f, 0.66f), Steel);
            mb.Box(new Vector3(0, 0.97f, 0), new Vector3(3.06f, 0.05f, 0.64f), StoneL);
            mb.Box(new Vector3(-0.9f, 1.15f, -0.02f), new Vector3(0.6f, 0.32f, 0.3f), WoodL);
            mb.Box(new Vector3(-0.98f, 1.15f, 0.135f), new Vector3(0.32f, 0.22f, 0.02f), Chrome);
            for (int k = 0; k < 5; k++) mb.Box(new Vector3(-0.98f, 1.07f + k * 0.04f, 0.15f), new Vector3(0.3f, 0.012f, 0.01f), SteelD);
            for (int s = -1; s <= 1; s += 2) mb.Prism(new Vector3(-0.72f, 1.11f, 0.13f), 8, 0.04f, 0.04f, 0.03f, Gold, Quaternion.Euler(90, 0, 0));
            Vase(mb, new Vector3(0.3f, 0.995f, 0), 0.32f, Teal);
            mb.Prism(new Vector3(-0.25f, 0.995f, 0.05f), 8, 0.06f, 0.08f, 0.14f, new Color(0.55f, 0.75f, 0.8f, 0.9f));
            mb.Prism(new Vector3(-0.25f, 1.135f, 0.05f), 8, 0.08f, 0.02f, 0.12f, new Color(0.55f, 0.75f, 0.8f, 0.9f));
            mb.Sphere(new Vector3(-0.25f, 1.28f, 0.05f), 0.03f, 6, 3, (i, k) => Gold);
            for (int s = -1; s <= 1; s += 2) mb.Prism(new Vector3(-0.25f + s * 0.17f, 0.995f, 0.12f), 6, 0.035f, 0.04f, 0.09f, new Color(0.55f, 0.75f, 0.8f, 0.9f));
            Vase(mb, new Vector3(1.1f, 0.995f, 0), 0.45f, new Color(0.7f, 0.38f, 0.22f, 0.15f));
            Piece(g, "Sideboard", mb, solid, collider: true, pos: new Vector3(HX - 0.48f, F, 0), rotY: -90f);
            var radio = new MB(); radio.Box(new Vector3(-0.7f, 1.22f, 0.135f), new Vector3(0.1f, 0.06f, 0.015f), GWarm);
            Piece(g, "Radio Dial", radio, glow, shadows: false, pos: new Vector3(HX - 0.48f, F, 0), rotY: -90f);

            // Two reading corners: armchairs facing each other with a chess table between.
            ArmchairCorner(g, "Reading Corner R", new Vector3(7.7f, F, -4.7f));
            ArmchairCorner(g, "Reading Corner L", new Vector3(-7.7f, F, 4.7f));

            // Globe on a steel tripod next to the desk.
            mb = new MB();
            for (int i = 0; i < 3; i++)
            {
                float ar = i * 120f * Mathf.Deg2Rad;
                Rod(mb, new Vector3(Mathf.Sin(ar) * 0.32f, 0, Mathf.Cos(ar) * 0.32f), new Vector3(0, 0.6f, 0), 0.04f, SteelD);
            }
            mb.Prism(new Vector3(0, 0.58f, 0), 8, 0.08f, 0.05f, 0.1f, Gold);
            var rnd = new System.Random(42);
            var land = new bool[10, 6]; for (int i = 0; i < 10; i++) for (int k = 0; k < 6; k++) land[i, k] = k > 0 && k < 5 && rnd.NextDouble() < 0.38;
            mb.Sphere(new Vector3(0, 1.0f, 0), 0.32f, 10, 6, (i, k) => land[i % 10, k] ? Green : new Color(0.2f, 0.42f, 0.65f, 0.4f));
            for (int i = 0; i < 16; i++)
            {
                float a0 = i * 22.5f * Mathf.Deg2Rad, a1 = (i + 1) * 22.5f * Mathf.Deg2Rad;
                Rod(mb, new Vector3(Mathf.Sin(a0) * 0.37f, 1.0f + Mathf.Cos(a0) * 0.37f, 0), new Vector3(Mathf.Sin(a1) * 0.37f, 1.0f + Mathf.Cos(a1) * 0.37f, 0), 0.03f, Gold);
            }
            Piece(g, "Globe", mb, solid, collider: true, pos: new Vector3(-2.6f, F, 6.9f), rotY: 30f);
        }

        static void ArmchairCorner(Transform g, string name, Vector3 c)
        {
            var mb = new MB();
            for (int s = -1; s <= 1; s += 2)
            {
                // Chair at local z = s * 1.15, facing the table in the middle.
                var q = Quaternion.Euler(0, s > 0 ? 180f : 0f, 0); var o = new Vector3(0, 0, s * 1.15f);
                Vector3 P(float x, float y, float z) => o + q * new Vector3(x, y, z);
                mb.Box(P(0, 0.1f, 0), new Vector3(0.86f, 0.08f, 0.82f), SteelD, q);
                for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) mb.Box(P(sx * 0.38f, 0.03f, sz * 0.36f), new Vector3(0.06f, 0.06f, 0.06f), Chrome, q);
                mb.Box(P(0, 0.33f, 0.04f), new Vector3(0.66f, 0.36f, 0.7f), Leather, q);
                mb.Box(P(0, 0.52f, 0.06f), new Vector3(0.62f, 0.06f, 0.62f), LeatherD, q);
                mb.Box(P(0, 0.8f, -0.33f), new Vector3(0.8f, 0.9f, 0.16f), Leather, q);
                mb.Box(P(0, 1.27f, -0.33f), new Vector3(0.84f, 0.05f, 0.18f), Gold, q);
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    mb.Box(P(sx * 0.4f, 0.4f, 0.02f), new Vector3(0.14f, 0.5f, 0.78f), LeatherD, q);
                    mb.Box(P(sx * 0.4f, 0.68f, 0.02f), new Vector3(0.16f, 0.06f, 0.82f), Wood, q);
                }
            }
            // Round chess table.
            mb.Prism(Vector3.zero, 8, 0.25f, 0.25f, 0.06f, StoneD);
            mb.Prism(new Vector3(0, 0.06f, 0), 6, 0.05f, 0.05f, 0.5f, Steel);
            mb.Prism(new Vector3(0, 0.56f, 0), 12, 0.36f, 0.36f, 0.05f, Wood);
            mb.Prism(new Vector3(0, 0.58f, 0), 12, 0.38f, 0.38f, 0.02f, Gold);
            for (int i = 0; i < 8; i++) for (int k = 0; k < 8; k++)
                mb.Box(new Vector3(-0.1575f + i * 0.045f, 0.615f, -0.1575f + k * 0.045f), new Vector3(0.045f, 0.01f, 0.045f), (i + k) % 2 == 0 ? White : WoodD);
            var rnd = new System.Random(name.Length);
            for (int n = 0; n < 7; n++)
            {
                int i = rnd.Next(8), k = rnd.Next(8); bool w = n % 2 == 0;
                mb.Prism(new Vector3(-0.1575f + i * 0.045f, 0.62f, -0.1575f + k * 0.045f), 6, 0.015f, 0.008f, 0.04f + (float)rnd.NextDouble() * 0.03f, w ? Chrome : SteelD);
            }
            Piece(g, name, mb, solid, collider: true, pos: c, rotY: 0);
        }

        // ---------- Decor: carpet, runner, planters, floor lamps, sconces ----------

        static readonly List<(Vector3 pos, Vector3 n)> sconces = new List<(Vector3, Vector3)>();
        static readonly List<Vector3> floorLamps = new List<Vector3>();

        static void BuildDecor()
        {
            var g = Group("Decor");
            var mb = new MB(); float cy = F + 0.015f, gy = F + 0.04f;
            mb.Box(new Vector3(0, cy, 0), new Vector3(7.0f, 0.03f, 6.0f), Red);
            CarpetBorder(mb, 0, 0, 6.5f, 5.5f, gy, 0.18f);
            CarpetBorder(mb, 0, 0, 5.6f, 4.6f, gy, 0.05f);
            mb.Box(new Vector3(0, cy, -6.2f), new Vector3(2.2f, 0.03f, 6.4f), RedD);
            for (int s = -1; s <= 1; s += 2) mb.Box(new Vector3(s * 0.9f, gy, -6.2f), new Vector3(0.08f, 0.012f, 6.4f), Gold);
            mb.Box(new Vector3(0, cy, 7.9f), new Vector3(4.6f, 0.03f, 2.6f), Blue);
            CarpetBorder(mb, 0, 7.9f, 4.3f, 2.3f, gy, 0.08f);
            Piece(g, "Carpets", mb, solid, shadows: false);

            // Planters with low-poly cypress trees.
            foreach (var p in new[] { new Vector3(-8.7f, F, -8.7f), new Vector3(8.7f, F, -8.7f), new Vector3(-2.6f, F, 8.5f), new Vector3(2.6f, F, 8.5f) })
            {
                mb = new MB();
                mb.Box(new Vector3(0, 0.3f, 0), new Vector3(0.8f, 0.6f, 0.8f), StoneL);
                mb.Box(new Vector3(0, 0.55f, 0), new Vector3(0.86f, 0.08f, 0.86f), Steel);
                mb.Box(new Vector3(0, 0.08f, 0), new Vector3(0.86f, 0.06f, 0.86f), StoneD);
                mb.Box(new Vector3(0, 0.6f, 0), new Vector3(0.7f, 0.04f, 0.7f), Soot);
                mb.Prism(new Vector3(0, 0.6f, 0), 5, 0.07f, 0.06f, 0.35f, WoodD);
                mb.Prism(new Vector3(0, 0.8f, 0), 7, 0.38f, 0.0f, 0.7f, GreenD, 10f);
                mb.Prism(new Vector3(0, 1.1f, 0), 7, 0.32f, 0.0f, 0.6f, Green, 35f);
                mb.Prism(new Vector3(0, 1.38f, 0), 7, 0.22f, 0.0f, 0.45f, Green, 60f);
                for (int i = 0; i < 4; i++) mb.Box(new Vector3(0, 0.3f, 0), new Vector3(0.83f, 0.04f, 0.05f), Gold, Quaternion.Euler(0, 90 * i, 0) * Quaternion.Euler(0, 0, 0));
                Piece(g, "Planter", mb, solid, collider: true, pos: p, rotY: p.x * 13f);
            }

            // Floor lamps either side of the door.
            var glowMb = new MB();
            foreach (var x in new[] { -2.7f, 2.7f })
            {
                mb = new MB(); var p = new Vector3(x, F, -8.7f);
                mb.Prism(Vector3.zero, 8, 0.22f, 0.2f, 0.05f, SteelD);
                mb.Prism(new Vector3(0, 0.05f, 0), 6, 0.025f, 0.025f, 1.55f, Steel);
                mb.Prism(new Vector3(0, 0.7f, 0), 8, 0.05f, 0.05f, 0.06f, Gold);
                mb.Prism(new Vector3(0, 1.58f, 0), 8, 0.32f, 0.32f, 0.03f, Gold);
                Piece(g, "Floor Lamp", mb, solid, collider: true, pos: p);
                glowMb.Prism(p + new Vector3(0, 1.6f, 0), 8, 0.3f, 0.18f, 0.38f, GWarm);
                floorLamps.Add(p + new Vector3(0, 1.8f, 0.7f));
            }

            // Wall sconces on the side pilasters and the front pilasters next to the door.
            var sc = new MB();
            sconces.Clear();
            foreach (var z in SideZ) { sconces.Add((new Vector3(-HX, F + 3.2f, z), Vector3.right)); sconces.Add((new Vector3(HX, F + 3.2f, z), Vector3.left)); }
            foreach (var x in new[] { -3.3f, 3.3f }) sconces.Add((new Vector3(x, F + 3.2f, -HZ), Vector3.forward));
            foreach (var (pos, n) in sconces)
            {
                var face = pos + n * 0.33f;
                sc.Box(face + n * 0.02f, Sz(n, 0.22f, 0.4f, 0.04f), Gold);
                sc.Box(face + n * 0.16f - Vector3.up * 0.08f, Sz(n, 0.06f, 0.06f, 0.28f), SteelD);
                sc.Prism(face + n * 0.3f - Vector3.up * 0.12f, 8, 0.06f, 0.1f, 0.08f, Gold);
                glowMb.Prism(face + n * 0.3f - Vector3.up * 0.04f, 6, 0.06f, 0.03f, 0.22f, GWarm);
            }
            Piece(g, "Wall Sconces", sc, solid);
            Piece(g, "Lamp Glow", glowMb, glow, shadows: false);
        }

        static void CarpetBorder(MB mb, float cx, float cz, float w, float d, float y, float t)
        {
            mb.Box(new Vector3(cx, y, cz + d / 2), new Vector3(w, 0.012f, t), Gold);
            mb.Box(new Vector3(cx, y, cz - d / 2), new Vector3(w, 0.012f, t), Gold);
            mb.Box(new Vector3(cx - w / 2, y, cz), new Vector3(t, 0.012f, d), Gold);
            mb.Box(new Vector3(cx + w / 2, y, cz), new Vector3(t, 0.012f, d), Gold);
        }

        // ---------- Lights, ambient, post ----------

        static void BuildLights()
        {
            var g = Group("Lights");
            // Evening mood: a cool shaft from the skylight is the key light on the war table, the chandelier and the fire
            // give warm pools, sconces and lamps only light small patches, the windows add a faint cool rim.
            Color warm = new Color(1f, 0.7f, 0.42f), cool = new Color(0.55f, 0.68f, 1f);
            var sky = AddLight(g, "Skylight Spot", new Vector3(0.8f, F + H + 2.0f, 0.6f), LightType.Spot, new Color(0.72f, 0.82f, 1f), 7f, 12f, LightShadows.Soft);
            sky.transform.rotation = Quaternion.LookRotation(new Vector3(0, F, 0) - sky.transform.position);
            sky.spotAngle = 52f; sky.innerSpotAngle = 26f;
            AddLight(g, "Chandelier Light", new Vector3(0, F + 3.9f, 0), LightType.Point, warm, 2.4f, 7.5f, LightShadows.Soft);
            AddLight(g, "Fireplace Light", new Vector3(-HX + 1.4f, F + 0.8f, 0), LightType.Point, new Color(1f, 0.5f, 0.2f), 3.2f, 6.5f, LightShadows.Soft);
            int i = 0;
            foreach (var (pos, n) in sconces) AddLight(g, "Sconce Light " + (++i), pos + n * 0.75f + Vector3.up * 0.1f, LightType.Point, warm, 0.8f, 2.8f);
            i = 0;
            foreach (var p in windowFills) AddLight(g, "Window Fill " + (++i), p, LightType.Point, cool, 0.35f, 3.5f);
            i = 0;
            foreach (var p in floorLamps) AddLight(g, "Floor Lamp Light " + (++i), p, LightType.Point, warm, 0.8f, 3f);
            AddLight(g, "Desk Lamp Light", new Vector3(-1.1f, F + 1.35f, 7.95f), LightType.Point, warm, 1.0f, 2.5f);
        }

        static Light AddLight(Transform parent, string name, Vector3 pos, LightType type, Color c, float intensity, float range, LightShadows shadows = LightShadows.None)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            var l = go.AddComponent<Light>(); l.type = type; l.color = c; l.intensity = intensity; l.range = range; l.shadows = shadows;
            if (go.GetComponent<UniversalAdditionalLightData>() == null) go.AddComponent<UniversalAdditionalLightData>();
            lights++;
            return l;
        }

        static string BuildVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }

            var tm = Comp<Tonemapping>(profile); tm.mode.Override(TonemappingMode.Neutral);
            var ca = Comp<ColorAdjustments>(profile);
            ca.postExposure.Override(-0.25f); ca.contrast.Override(18f); ca.saturation.Override(4f); ca.colorFilter.Override(new Color(1f, 0.96f, 0.92f));
            var bl = Comp<Bloom>(profile);
            bl.threshold.Override(0.9f); bl.intensity.Override(0.55f); bl.scatter.Override(0.7f); bl.tint.Override(new Color(1f, 0.86f, 0.68f));
            var vg = Comp<Vignette>(profile);
            vg.color.Override(new Color(0.05f, 0.03f, 0.03f)); vg.intensity.Override(0.38f); vg.smoothness.Override(0.5f);
            var st = Comp<SplitToning>(profile);
            st.shadows.Override(new Color(0.36f, 0.45f, 0.6f)); st.highlights.Override(new Color(1f, 0.82f, 0.62f)); st.balance.Override(-10f);
            EditorUtility.SetDirty(profile);

            var go = new GameObject("Interior Post Volume"); go.transform.SetParent(root, false);
            var v = go.AddComponent<Volume>(); v.isGlobal = true; v.priority = 1f; v.weight = 1f; v.sharedProfile = profile;
            return ProfilePath;
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

        static string SetAmbient()
        {
            var getter = typeof(RenderSettings).GetMethod("GetRenderSettings", BindingFlags.NonPublic | BindingFlags.Static);
            if (getter != null && getter.Invoke(null, null) is Object rs) Undo.RecordObject(rs, "Interior ambient");
            string before = $"Ambient was {RenderSettings.ambientMode} sky {RenderSettings.ambientSkyColor}.";
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.13f, 0.14f, 0.19f);
            RenderSettings.ambientEquatorColor = new Color(0.12f, 0.1f, 0.085f);
            RenderSettings.ambientGroundColor = new Color(0.06f, 0.05f, 0.045f);
            DynamicGI.UpdateEnvironment();
            return before + " Now Trilight.";
        }

        static string EnableCameraPost()
        {
            var cam = GameObject.Find("LevelEssentials/Main Camera");
            var data = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
            if (data == null) return "Main Camera not found.";
            if (!data.renderPostProcessing) { Undo.RecordObject(data, "Camera post"); data.renderPostProcessing = true; return "Main Camera post switched on."; }
            return "Main Camera post already on.";
        }

        // ---------- Helpers: objects, materials, meshes ----------

        static Transform Group(string name)
        {
            var t = root.Find(name); if (t != null) return t;
            t = new GameObject(name).transform; t.SetParent(root, false); return t;
        }

        static GameObject Piece(Transform parent, string name, MB mb, Material mat, bool collider = false, bool shadows = true, Vector3 pos = default, float rotY = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = Quaternion.Euler(0, rotY, 0);
            string baseName = "PHI_" + name.Replace(' ', '_'), mname = baseName; int k = 1;
            while (!usedNames.Add(mname)) mname = baseName + "_" + (++k);
            var mesh = Save(mb.Done(mname));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            if (!shadows) mr.shadowCastingMode = ShadowCastingMode.Off;
            if (collider) { var bc = go.AddComponent<BoxCollider>(); bc.center = mesh.bounds.center; bc.size = mesh.bounds.size; }
            return go;
        }

        static void MakeMaterials()
        {
            solid = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/StyleTest/Island_Solid.mat");
            glow = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/StyleTest/Island_Glow.mat");
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Materials/PresidentHouse", "Interior");
            stoneFloor = TileMat("PHI_StoneFloor", 1.2f, 1f, 0f, 0.016f, new Color(0.44f, 0.41f, 0.38f), 0.1f, new Color(0.84f, 0.82f, 0.8f));
            stoneWall = TileMat("PHI_StoneWall", 0.7f, 0.45f, 0.5f, 0.022f, new Color(0.42f, 0.38f, 0.35f), 0.12f, Color.white);
            woodPanel = TileMat("PHI_WoodPlanks", 0.32f, 12f, 0f, 0.018f, new Color(0.2f, 0.11f, 0.05f), 0.15f, new Color(0.92f, 0.9f, 0.9f));
            metalPanel = TileMat("PHI_MetalPanels", 1.6f, 1f, 0f, 0.03f, new Color(0.14f, 0.15f, 0.17f), 0.06f, new Color(0.9f, 0.92f, 0.95f));
        }

        static Material TileMat(string name, float tile, float aspect, float offset, float grout, Color groutCol, float variation, Color checker)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(solid); AssetDatabase.CreateAsset(m, path); } else m.CopyPropertiesFromMaterial(solid);
            m.SetFloat("_TileSize", tile); m.SetFloat("_TileAspect", aspect); m.SetFloat("_BrickOffset", offset);
            m.SetFloat("_GroutWidth", grout); m.SetColor("_GroutColor", groutCol); m.SetFloat("_TileVariation", variation); m.SetColor("_CheckerColor", checker);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh Save(Mesh m)
        {
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Meshes", "PresidentHouseInterior");
            string path = MeshDir + "/" + m.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { string n = m.name; existing.Clear(); EditorUtility.CopySerialized(m, existing); existing.name = n; EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Vector2[] StarPts(int points, float rOut, float rIn)
        {
            var pts = new Vector2[points * 2];
            for (int i = 0; i < pts.Length; i++) { float a = i * Mathf.PI / points, r = i % 2 == 0 ? rOut : rIn; pts[i] = new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r); }
            return pts;
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

            public void Sphere(Vector3 cen, float rad, int seg, int rings, System.Func<int, int, Color> colf)
            {
                Vector3 P(int i, int k) { float a = i * Mathf.PI * 2f / seg, phi = -Mathf.PI / 2 + k * Mathf.PI / rings; float rr = Mathf.Cos(phi) * rad; return cen + new Vector3(Mathf.Sin(a) * rr, Mathf.Sin(phi) * rad, Mathf.Cos(a) * rr); }
                for (int k = 0; k < rings; k++)
                    for (int i = 0; i < seg; i++)
                    {
                        var a = P(i, k); var b = P(i + 1, k); var cc = P(i + 1, k + 1); var d = P(i, k + 1); var col = colf(i, k);
                        if (k == 0) Tri(a, cc, d, col, (a + cc + d) / 3f - cen);
                        else if (k == rings - 1) Tri(a, b, cc, col, (a + b + cc) / 3f - cen);
                        else Quad(a, b, cc, d, col, (a + b + cc + d) / 4f - cen);
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
    }
}
