using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Builds the island president's house (A2_Start) under the "President house" object: a chrome palace on a
    /// two-tier podium with entrance stairs, a columned portico with a gold emblem, side wings, a gold dome and a tall
    /// tower with a flag mast. Low-poly faceted meshes (flat normals) are saved under Assets/Meshes/PresidentHouse,
    /// metal URP Lit materials under Assets/Materials/PresidentHouse. The old greybox cubes are only switched off.
    /// Model space: x = right, z = depth (front face at z 0, the building grows backwards), y = up from the ground.
    /// </summary>
    public static class PresidentHouseBuilder
    {
        const string RootName = "President house";
        const string ModelName = "President House Model";
        const string MeshDir = "Assets/Meshes/PresidentHouse";
        const string MatDir = "Assets/Materials/PresidentHouse";

        // Front-centre of the podium in world space (keeps the outro cutscene area in front of the house free).
        static readonly Vector3 Origin = new Vector3(-11.5f, 3.05f, 48.45f);
        const float Scale = 0.8f;   // whole house, model space below is in unscaled metres

        static Material chrome, steel, gold, glass, lamp;
        static Transform model;

        [MenuItem("Tools/Coop Game/President House/Build (A2_Start)")]
        public static string Build()
        {
            var root = GameObject.Find(RootName);
            if (root == null) return "No '" + RootName + "' object in the open scene.";

            MakeMaterials();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build President House");

            // Old greybox: keep it, just switch it off.
            foreach (Transform c in root.transform)
                if (c.name.StartsWith("President House") && c.name != ModelName && c.gameObject.activeSelf)
                { Undo.RecordObject(c.gameObject, "Hide greybox"); c.gameObject.SetActive(false); }

            var old = root.transform.Find(ModelName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var go = new GameObject(ModelName);
            Undo.RegisterCreatedObjectUndo(go, "Build President House");
            go.transform.SetParent(root.transform, false);
            go.transform.SetPositionAndRotation(Origin, Quaternion.Euler(0, root.transform.eulerAngles.y, 0));
            go.transform.localScale = Vector3.one * Scale;   // scales round the front-centre, so the front line stays put
            go.isStatic = true;
            model = go.transform;

            BuildPodium();
            BuildHall();
            BuildPortico();
            BuildWings();
            BuildDome();
            BuildTower();
            BuildLamps();
            BuildReflectionProbe();

            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.GetComponent<Light>() == null) t.gameObject.isStatic = true;
            Selection.activeGameObject = go;
            return Report();
        }

        // ---------- Parts ----------

        static void BuildPodium()
        {
            var g = Group("Podium");
            Box(g, "Base", new Vector3(0, -0.75f, 7.6f), new Vector3(21.0f, 2.0f, 15.2f), steel);          // y -1.75 .. 0.25 (sunk into the ground), z 0 .. 15.2
            Box(g, "Base Gold Band", new Vector3(0, 0.3f, 7.6f), new Vector3(21.1f, 0.12f, 15.3f), gold);
            Box(g, "Upper", new Vector3(0, 0.85f, 8.2f), new Vector3(19.6f, 1.1f, 12.8f), chrome);          // top at y 1.4, z 1.8 .. 14.6
            Box(g, "Upper Gold Edge", new Vector3(0, 1.43f, 8.2f), new Vector3(19.7f, 0.08f, 12.9f), gold);
            // Entrance stairs: 5 steps from the front edge up to the upper tier.
            for (int i = 0; i < 5; i++)
            {
                float top = 0.25f + (i + 1) * 0.23f, depth = 1.8f - i * 0.36f;
                Box(g, "Step " + (i + 1), new Vector3(0, (top - 0.4f) * 0.5f, i * 0.36f + depth * 0.5f),
                    new Vector3(8.0f, top + 0.4f, depth), i % 2 == 0 ? chrome : steel);
            }
            // Gold handrail blocks either side of the stairs.
            foreach (float s in new[] { -1f, 1f })
                Box(g, s < 0 ? "Stair Cheek L" : "Stair Cheek R", new Vector3(s * 4.3f, 0.9f, 0.9f), new Vector3(0.6f, 1.0f, 1.8f), gold);
        }

        static void BuildHall()
        {
            var g = Group("Main Hall");
            Box(g, "Hall", new Vector3(0, 5.85f, 7.4f), new Vector3(14.0f, 8.9f, 8.2f), chrome);            // y 1.4 .. 10.3, z 3.3 .. 11.5
            Box(g, "Roof Slab", new Vector3(0, 10.5f, 7.4f), new Vector3(14.6f, 0.4f, 8.8f), steel);
            Box(g, "Roof Gold Cornice", new Vector3(0, 10.25f, 7.4f), new Vector3(14.8f, 0.18f, 9.0f), gold);
            // Door with a gold frame (front face of the hall at z 3.3).
            Box(g, "Door", new Vector3(0, 3.6f, 3.25f), new Vector3(2.6f, 4.4f, 0.2f), steel);
            Box(g, "Door Frame Top", new Vector3(0, 5.95f, 3.2f), new Vector3(3.3f, 0.35f, 0.3f), gold);
            Box(g, "Door Frame L", new Vector3(-1.48f, 3.6f, 3.2f), new Vector3(0.35f, 4.4f, 0.3f), gold);
            Box(g, "Door Frame R", new Vector3(1.48f, 3.6f, 3.2f), new Vector3(0.35f, 4.4f, 0.3f), gold);
            Box(g, "Door Split", new Vector3(0, 3.6f, 3.13f), new Vector3(0.06f, 4.4f, 0.06f), gold);
            // Tall glowing windows on the front and sides.
            foreach (float x in new[] { -4.6f, -2.8f, 2.8f, 4.6f })
                Window(g, "Front Window", new Vector3(x, 5.2f, 3.27f), new Vector2(1.1f, 4.6f), Vector3.back);
            foreach (float s in new[] { -1f, 1f })
                for (int i = 0; i < 3; i++)
                    Window(g, "Side Window", new Vector3(s * 7.03f, 9.0f, 5.0f + i * 2.4f), new Vector2(1.1f, 1.8f), s < 0 ? Vector3.left : Vector3.right);
            // Pilasters at the hall corners.
            foreach (float s in new[] { -1f, 1f })
                foreach (float z in new[] { 3.3f, 11.5f })
                    Box(g, "Pilaster", new Vector3(s * 7.0f, 5.85f, z), new Vector3(0.7f, 8.9f, 0.7f), steel);
        }

        static void BuildPortico()
        {
            var g = Group("Portico");
            var column = Prism(10, 0.42f, 0.42f, 7.6f, "Column");
            var capital = Prism(10, 0.62f, 0.5f, 0.35f, "ColumnCap");
            foreach (float x in new[] { -6.0f, -3.6f, -1.2f, 1.2f, 3.6f, 6.0f })
            {
                Box(g, "Column Base", new Vector3(x, 1.6f, 2.2f), new Vector3(1.1f, 0.4f, 1.1f), gold);
                var c = Mesh(g, "Column", column, new Vector3(x, 1.8f, 2.2f), Quaternion.identity, chrome);
                var cc = c.AddComponent<CapsuleCollider>(); cc.radius = 0.42f; cc.height = 7.6f; cc.center = new Vector3(0, 3.8f, 0);
                Mesh(g, "Column Capital", capital, new Vector3(x, 9.6f, 2.2f), Quaternion.Euler(180, 0, 0), gold);
            }
            Box(g, "Entablature", new Vector3(0, 9.95f, 2.4f), new Vector3(14.2f, 0.7f, 2.4f), chrome);
            Box(g, "Entablature Gold", new Vector3(0, 9.55f, 1.18f), new Vector3(14.2f, 0.16f, 0.06f), gold);
            // Pediment (triangular gable) with a gold trim and the emblem.
            var ped = Gable(14.2f, 2.6f, 2.4f, "Pediment");
            Mesh(g, "Pediment", ped, new Vector3(0, 10.3f, 2.4f), Quaternion.identity, chrome, ped);
            var trim = Gable(14.6f, 2.75f, 0.2f, "PedimentTrim");
            Mesh(g, "Pediment Gold Trim", trim, new Vector3(0, 10.25f, 1.15f), Quaternion.identity, gold);
            Emblem(g, new Vector3(0, 11.35f, 1.12f), 0.85f);
        }

        static void BuildWings()
        {
            var g = Group("Wings");
            foreach (float s in new[] { -1f, 1f })
            {
                string side = s < 0 ? " L" : " R";
                Box(g, "Wing" + side, new Vector3(s * 8.3f, 4.4f, 7.6f), new Vector3(3.0f, 6.0f, 7.4f), chrome);       // y 1.4 .. 7.4, x 6.8 .. 9.8
                Box(g, "Wing Cornice" + side, new Vector3(s * 8.35f, 7.5f, 7.6f), new Vector3(3.3f, 0.2f, 7.7f), gold);
                Box(g, "Wing Roof" + side, new Vector3(s * 8.3f, 7.75f, 7.6f), new Vector3(3.1f, 0.3f, 7.5f), steel);
                Window(g, "Wing Window" + side, new Vector3(s * 8.3f, 4.6f, 3.88f), new Vector2(1.3f, 3.4f), Vector3.back);
                for (int i = 0; i < 2; i++)
                    Window(g, "Wing Side Window" + side, new Vector3(s * 9.82f, 4.6f, 5.8f + i * 3.4f), new Vector2(1.2f, 3.4f), s < 0 ? Vector3.left : Vector3.right);
                // Small gold obelisks on the wing corners.
                var ob = Prism(4, 0.35f, 0.0f, 1.8f, "Obelisk", 45f);
                Mesh(g, "Wing Obelisk" + side, ob, new Vector3(s * 9.4f, 7.9f, 4.2f), Quaternion.identity, gold);
            }
        }

        static void BuildDome()
        {
            var g = Group("Dome");
            const float z = 7.4f;
            var drum = Prism(16, 4.2f, 4.2f, 1.6f, "Drum");
            Mesh(g, "Drum", drum, new Vector3(0, 10.7f, z), Quaternion.identity, chrome, drum);
            Mesh(g, "Drum Gold Ring", Prism(16, 4.45f, 4.45f, 0.25f, "DrumRing"), new Vector3(0, 12.3f, z), Quaternion.identity, gold);
            for (int i = 0; i < 8; i++)   // glowing slots round the drum
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / 8f;
                var dir = new Vector3(Mathf.Sin(a), 0, -Mathf.Cos(a));
                Window(g, "Drum Window", new Vector3(dir.x * 4.08f, 11.5f, z + dir.z * 4.08f), new Vector2(0.7f, 1.0f), dir);
            }
            var dome = Dome(16, 5, 4.2f, 3.0f, "Dome");
            Mesh(g, "Dome", dome, new Vector3(0, 12.55f, z), Quaternion.identity, gold, dome);
            Mesh(g, "Lantern", Prism(8, 0.65f, 0.65f, 1.0f, "Lantern"), new Vector3(0, 15.4f, z), Quaternion.identity, chrome);
            Mesh(g, "Lantern Cap", Prism(8, 0.85f, 0.0f, 1.0f, "LanternCap"), new Vector3(0, 16.4f, z), Quaternion.identity, gold);
        }

        static void BuildTower()
        {
            var g = Group("Tower");
            const float z = 12.0f, w = 4.6f, top = 20f;
            Box(g, "Tower Base", new Vector3(0, 1.4f + 0.5f, z), new Vector3(w + 1.2f, 1.0f, w + 1.2f), steel);
            Box(g, "Tower Shaft", new Vector3(0, (1.4f + top) * 0.5f, z), new Vector3(w, top - 1.4f, w), chrome);
            foreach (float sx in new[] { -1f, 1f })   // steel corner ribs break up the big chrome faces
                foreach (float sz in new[] { -1f, 1f })
                    Box(g, "Tower Corner Rib", new Vector3(sx * w * 0.5f, (1.4f + top) * 0.5f, z + sz * w * 0.5f), new Vector3(0.55f, top - 1.4f, 0.55f), steel, collider: false);
            foreach (float y in new[] { 10.6f, 16.9f })
                Box(g, "Tower Gold Band", new Vector3(0, y, z), new Vector3(w + 0.25f, 0.3f, w + 0.25f), gold);
            // Lit window slots on the sides and back (the front holds the emblem).
            foreach (var d in new[] { Vector3.forward, Vector3.left, Vector3.right })
                Window(g, "Tower Window", new Vector3(0, 0, z) + d * (w * 0.5f + 0.03f) + Vector3.up * 14.5f, new Vector2(1.0f, 2.6f), d);
            // Gold emblem on the tower front, just above the dome.
            Emblem(g, new Vector3(0, 18.5f, z - w * 0.5f - 0.05f), 1.0f);
            Box(g, "Tower Cornice", new Vector3(0, top + 0.2f, z), new Vector3(w + 0.8f, 0.4f, w + 0.8f), gold);
            // Corner pinnacles.
            var pin = Prism(4, 0.35f, 0.0f, 1.6f, "Pinnacle", 45f);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Mesh(g, "Pinnacle", pin, new Vector3(sx * (w * 0.5f + 0.1f), top + 0.4f, z + sz * (w * 0.5f + 0.1f)), Quaternion.identity, gold);
            var spire = Prism(4, w * 0.62f, 0.0f, 3.5f, "Spire", 45f);
            Mesh(g, "Spire", spire, new Vector3(0, top + 0.4f, z), Quaternion.identity, chrome, spire);
            Mesh(g, "Spire Gold Tip", Prism(8, 0.35f, 0.0f, 0.8f, "SpireTip"), new Vector3(0, top + 3.5f, z), Quaternion.identity, gold);
        }

        static void BuildLamps()
        {
            var g = Group("Lights");
            var pole = Prism(8, 0.16f, 0.12f, 3.4f, "LampPole");
            foreach (float s in new[] { -1f, 1f })
            {
                string side = s < 0 ? " L" : " R";
                var basePos = new Vector3(s * 5.4f, 0.25f, 0.9f);
                Box(g, "Lamp Base" + side, basePos + Vector3.up * 0.25f, new Vector3(0.7f, 0.5f, 0.7f), gold);
                Mesh(g, "Lamp Pole" + side, pole, basePos + Vector3.up * 0.5f, Quaternion.identity, steel);
                Box(g, "Lamp Glow" + side, basePos + Vector3.up * 4.2f, new Vector3(0.55f, 0.75f, 0.55f), lamp, collider: false);
                Mesh(g, "Lamp Cap" + side, Prism(4, 0.5f, 0.0f, 0.5f, "LampCap", 45f), basePos + Vector3.up * 4.58f, Quaternion.identity, gold);
                AddLight(g, "Lamp Light" + side, basePos + Vector3.up * 4.2f + Vector3.back * 0.3f, LightType.Point, new Color(1f, 0.78f, 0.45f), 2.5f, 7f);
                // Warm uplight washing the metal facade.
                var up = AddLight(g, "Facade Uplight" + side, new Vector3(s * 4.8f, 1.6f, 0.4f), LightType.Spot, new Color(1f, 0.85f, 0.6f), 12f, 13f);
                up.transform.localRotation = Quaternion.LookRotation(new Vector3(-s * 0.15f, 1.0f, 0.35f));
                up.spotAngle = 70f;
            }
            AddLight(g, "Portico Light", new Vector3(0, 8.6f, 2.0f), LightType.Point, new Color(1f, 0.8f, 0.5f), 2f, 6f);
            AddLight(g, "Tower Emblem Light", new Vector3(0, 18.5f, 9.2f), LightType.Point, new Color(1f, 0.82f, 0.5f), 2f, 5f);
        }

        static void BuildReflectionProbe()
        {
            // The scene has no skybox, so chrome needs its own reflections: a baked probe around the house.
            var go = new GameObject("President House Reflections");
            go.transform.SetParent(model, false);
            go.transform.localPosition = new Vector3(0, 16f, -6f);   // high up: sees more sky than grass, so chrome reads silver
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
            p.size = new Vector3(40f, 60f, 46f);
            p.center = new Vector3(0, 0f, 12f);
            p.boxProjection = false;
            p.resolution = 256;
            p.importance = 2;
            p.intensity = 0.8f;
            p.nearClipPlane = 0.3f;
            p.farClipPlane = 600f;
            p.clearFlags = UnityEngine.Rendering.ReflectionProbeClearFlags.SolidColor;
            p.backgroundColor = new Color(0.55f, 0.68f, 0.85f);
            string path = MatDir + "/PresidentHouse_Reflection.exr";
            if (Lightmapping.BakeReflectionProbe(p, path))
            {
                AssetDatabase.ImportAsset(path);
                p.customBakedTexture = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            }
        }

        // ---------- Helpers: objects ----------

        static Transform Group(string name)
        {
            var t = new GameObject(name).transform; t.SetParent(model, false); return t;
        }

        static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            return go;
        }

        static GameObject Mesh(Transform parent, string name, Mesh mesh, Vector3 pos, Quaternion rot, Material mat, Mesh convexCollider = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (convexCollider != null) { var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = convexCollider; mc.convex = true; }
            return go;
        }

        static void Window(Transform parent, string name, Vector3 pos, Vector2 size, Vector3 facing)
        {
            var go = Box(parent, name, pos, new Vector3(size.x, size.y, 0.08f), glass, collider: false);
            go.transform.localRotation = Quaternion.LookRotation(-facing);
            var frame = Box(go.transform.parent, name + " Frame", pos - facing * 0.02f, new Vector3(size.x + 0.24f, size.y + 0.24f, 0.06f), gold, collider: false);
            frame.transform.localRotation = go.transform.localRotation;
        }

        static void Emblem(Transform parent, Vector3 pos, float r)
        {
            // Gold ring + star facing the front (-z), glowing core behind the star.
            var rot = Quaternion.Euler(-90, 0, 0);
            Mesh(parent, "Emblem Ring", Prism(20, r, r, 0.12f, "EmblemDisc"), pos, rot, gold);
            Mesh(parent, "Emblem Core", Prism(20, r * 0.82f, r * 0.82f, 0.16f, "EmblemCore"), pos, rot, lamp);
            Mesh(parent, "Emblem Star", Star(5, r * 0.74f, r * 0.3f, 0.14f, "EmblemStar"), pos + Vector3.back * 0.16f, Quaternion.identity, gold);
        }

        static Light AddLight(Transform parent, string name, Vector3 pos, LightType type, Color c, float intensity, float range)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            var l = go.AddComponent<Light>(); l.type = type; l.color = c; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        // ---------- Helpers: materials ----------

        static void MakeMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Materials", "PresidentHouse");
            // Brushed metal rather than mirror chrome: still metallic, but softer, less glare.
            chrome = Mat("PH_Chrome", new Color(0.78f, 0.8f, 0.84f), 0.85f, 0.5f);
            steel = Mat("PH_DarkSteel", new Color(0.36f, 0.38f, 0.43f), 0.8f, 0.4f);
            gold = Mat("PH_Gold", new Color(0.92f, 0.72f, 0.36f), 0.85f, 0.5f);
            glass = Mat("PH_WindowGlow", new Color(0.3f, 0.22f, 0.12f), 0.1f, 0.6f, new Color(0.75f, 0.52f, 0.25f));   // warm lit interior vs cool metal
            lamp = Mat("PH_LampGlow", new Color(1f, 0.85f, 0.55f), 0f, 0.4f, new Color(1.6f, 1.15f, 0.55f));
        }

        static Material Mat(string name, Color baseColor, float metallic, float smoothness, Color? emission = null)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_EnvironmentReflections", 1f);
            m.SetFloat("_SpecularHighlights", 1f);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- Helpers: faceted meshes ----------

        class MB
        {
            readonly List<Vector3> v = new List<Vector3>(); readonly List<int> t = new List<int>();
            public Vector3 Centre;
            public void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                // Wind so the face points away from the shape centre (all shapes here are star-shaped around it).
                var n = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(n, (a + b + c) / 3f - Centre) < 0) { var x = b; b = c; c = x; }
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }
            public Mesh Done(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }

        static Mesh Save(Mesh m)
        {
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Meshes", "PresidentHouse");
            m.name = m.name.Replace(',', '.');
            string path = MeshDir + "/" + m.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { existing.Clear(); EditorUtility.CopySerialized(m, existing); existing.name = m.name; EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>N-sided prism / frustum / pyramid along +y from y 0 to h (r1 = 0 gives a point).</summary>
        static Mesh Prism(int sides, float r0, float r1, float h, string name, float startDeg = 0f)
        {
            var b = new MB { Centre = new Vector3(0, h * (r1 > 0 ? 0.5f : 0.25f), 0) };
            Vector3 P(int i, float r, float y) { float a = (startDeg * Mathf.Deg2Rad) + i * Mathf.PI * 2f / sides; return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r); }
            for (int i = 0; i < sides; i++)
            {
                var a0 = P(i, r0, 0); var a1 = P(i + 1, r0, 0);
                b.Tri(Vector3.zero, a0, a1);
                if (r1 > 0) { var b0 = P(i, r1, h); var b1 = P(i + 1, r1, h); b.Quad(a0, a1, b1, b0); b.Tri(new Vector3(0, h, 0), b0, b1); }
                else b.Tri(a0, a1, new Vector3(0, h, 0));
            }
            return Save(b.Done($"PH_{name}_{sides}_{r0:0.##}_{r1:0.##}_{h:0.##}"));
        }

        /// <summary>Faceted half-sphere (flattened to height h) sitting on y 0.</summary>
        static Mesh Dome(int sides, int rings, float r, float h, string name)
        {
            var b = new MB { Centre = new Vector3(0, h * 0.35f, 0) };
            Vector3 P(int i, int k) { float a = i * Mathf.PI * 2f / sides, phi = k * Mathf.PI * 0.5f / rings; float rr = Mathf.Cos(phi) * r; return new Vector3(Mathf.Sin(a) * rr, Mathf.Sin(phi) * h, Mathf.Cos(a) * rr); }
            for (int i = 0; i < sides; i++)
            {
                b.Tri(Vector3.zero, P(i, 0), P(i + 1, 0));
                for (int k = 0; k < rings - 1; k++) b.Quad(P(i, k), P(i + 1, k), P(i + 1, k + 1), P(i, k + 1));
                b.Tri(P(i, rings - 1), P(i + 1, rings - 1), new Vector3(0, h, 0));
            }
            return Save(b.Done($"PH_{name}_{sides}x{rings}_{r:0.##}_{h:0.##}"));
        }

        /// <summary>Triangular gable: base width w on y 0, apex at height h, depth d centred on z.</summary>
        static Mesh Gable(float w, float h, float d, string name)
        {
            var b = new MB { Centre = new Vector3(0, h / 3f, 0) };
            Vector3 L(float z) => new Vector3(-w / 2, 0, z);
            Vector3 R(float z) => new Vector3(w / 2, 0, z);
            Vector3 T(float z) => new Vector3(0, h, z);
            float f = -d / 2, k = d / 2;
            b.Tri(L(f), R(f), T(f)); b.Tri(L(k), R(k), T(k));
            b.Quad(L(f), L(k), R(k), R(f)); b.Quad(L(f), T(f), T(k), L(k)); b.Quad(R(f), R(k), T(k), T(f));
            return Save(b.Done($"PH_{name}_{w:0.##}_{h:0.##}_{d:0.##}"));
        }

        /// <summary>Extruded star in the XY plane, thickness along z (front face at -z).</summary>
        static Mesh Star(int points, float rOut, float rIn, float depth, string name)
        {
            var b = new MB { Centre = Vector3.zero };
            int n = points * 2; var ring = new Vector3[n];
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI / points; float r = i % 2 == 0 ? rOut : rIn; ring[i] = new Vector3(Mathf.Sin(a) * r, Mathf.Cos(a) * r, 0); }
            var f = Vector3.back * depth / 2; var k = Vector3.forward * depth / 2;
            for (int i = 0; i < n; i++)
            {
                var a = ring[i]; var c = ring[(i + 1) % n];
                b.Tri(f, a + f, c + f); b.Tri(k, a + k, c + k);
                // Side faces: wind against the ring's midpoint direction, not the centre (concave notches).
                b.Centre = (a + c) * 0.5f + Vector3.Cross(c - a, Vector3.forward).normalized * 0.01f;   // a point just inside this edge
                b.Quad(a + f, c + f, c + k, a + k);
                b.Centre = Vector3.zero;
            }
            return Save(b.Done($"PH_{name}_{points}_{rOut:0.##}_{rIn:0.##}_{depth:0.##}"));
        }

        // ---------- Report ----------

        static string Report()
        {
            var sb = new System.Text.StringBuilder();
            var bounds = new Bounds(model.position, Vector3.zero);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            sb.AppendLine($"Built '{ModelName}': bounds min {bounds.min:F1} max {bounds.max:F1}, renderers {model.GetComponentsInChildren<Renderer>().Length}.");
            // Ground heights under the podium corners (ignoring the house itself).
            foreach (var lp in new[] { new Vector3(-10.5f, 0, 0), new Vector3(10.5f, 0, 0), new Vector3(-10.5f, 0, 14), new Vector3(10.5f, 0, 14), new Vector3(0, 0, 7) })
            {
                var wp = model.TransformPoint(lp) + Vector3.up * 40f;
                foreach (var h in Physics.RaycastAll(wp, Vector3.down, 80f))
                    if (!h.collider.transform.IsChildOf(model)) { sb.AppendLine($"ground under {lp}: {h.point.y - model.position.y:F2} m ({h.collider.name})"); break; }
            }
            // Scene objects poking into the house.
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
