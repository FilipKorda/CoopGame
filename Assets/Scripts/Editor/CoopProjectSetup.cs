using System.Linq;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Generates the placeholder player prefab, the Bootstrap scene (NetworkManager + UnityTransport)
    /// and the Game test level, and registers both scenes in the build scene list.
    /// </summary>
    public static class CoopProjectSetup
    {
        const string ScenesFolder = "Assets/Scenes";
        const string PrefabsFolder = "Assets/Prefabs";
        const string MaterialsFolder = "Assets/Materials";
        public const string BootstrapScenePath = ScenesFolder + "/Bootstrap.unity";
        const string GameScenePath = ScenesFolder + "/Game.unity";
        const string PlayerPrefabPath = PrefabsFolder + "/Player.prefab";
        const string CratePrefabPath = PrefabsFolder + "/Crate.prefab";
        const string EnemyPrefabPath = PrefabsFolder + "/Enemy.prefab";
        const string HealthCratePrefabPath = PrefabsFolder + "/HealthCrate.prefab";
        const string ExplosiveCratePrefabPath = PrefabsFolder + "/ExplosiveCrate.prefab";
        const string MetalCratePrefabPath = PrefabsFolder + "/MetalCrate.prefab";
        const string AllyRobotPrefabPath = PrefabsFolder + "/AllyRobot.prefab";
        const string SwordEnemyPrefabPath = PrefabsFolder + "/SwordEnemy.prefab";
        const string RangedEnemyPrefabPath = PrefabsFolder + "/RangedEnemy.prefab";
        const string MortarEnemyPrefabPath = PrefabsFolder + "/MortarEnemy.prefab";
        const string ShieldEnemyPrefabPath = PrefabsFolder + "/ShieldEnemy.prefab";
        const string MiniRobotPrefabPath = PrefabsFolder + "/MiniRobot.prefab";
        const string GameSessionPrefabPath = PrefabsFolder + "/GameSession.prefab";
        const string LevelPrefabsFolder = PrefabsFolder + "/Level";
        const string MechanicsFolder = PrefabsFolder + "/Mechanics";
        const string LevelEssentialsPrefabPath = LevelPrefabsFolder + "/LevelEssentials.prefab";
        const string LevelExitPrefabPath = LevelPrefabsFolder + "/LevelExit.prefab";
        const string LedgePrefabPath = LevelPrefabsFolder + "/Ledge.prefab";
        const string WaterVolumePrefabPath = LevelPrefabsFolder + "/WaterVolume.prefab";
        const string Act1Folder = ScenesFolder + "/Act1";
        public const string Act1StartScenePath = Act1Folder + "/A1_Start.unity";
        const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";

        static readonly Vector3[] HealthCratePositions =
        {
            new(2f, 0.5f, -6f), new(-12f, 0.5f, -4f), new(20f, 0.5f, -20f),
            new(27f, 7.6f, -27.5f), // reward on top of the climbing wall
        };

        [MenuItem("Tools/Coop Game/Rebuild Test Scenes And Player Prefab")]
        static void RebuildFromMenu()
        {
            if (EditorUtility.DisplayDialog("Rebuild coop setup",
                    "This overwrites Bootstrap.unity, Game.unity and the base prefabs (Player, crates, enemies) from code; " +
                    "hand-made changes to them are lost. Act scenes and the Level/Mechanics prefabs are not touched, " +
                    "but the rebuilt Game scene won't use them. Continue?", "Rebuild", "Cancel"))
                BuildAll();
        }

        [MenuItem("Tools/Coop Game/Rebuild Game Scene And Prefabs (keep Bootstrap)")]
        static void RebuildGameplayFromMenu()
        {
            if (EditorUtility.DisplayDialog("Rebuild gameplay",
                    "This overwrites Game.unity and the base prefabs (Player, crates, enemies) from code; hand-made " +
                    "changes to them are lost. Act scenes and the Level/Mechanics prefabs are not touched, but the rebuilt " +
                    "Game scene won't use them. Continue?", "Rebuild", "Cancel"))
                RebuildGameplay();
        }

        [MenuItem("Tools/Coop Game/Add Health Crates To Game Scene")]
        static void AddHealthCratesFromMenu() => AddHealthCratesToGameScene();

        /// <summary>
        /// Non-destructive update of the existing Game scene: creates the HealthCrate prefab, places instances
        /// (only if the scene has none yet) and points the PickupSystem at the health material.
        /// Everything else in the scene is left as it is.
        /// </summary>
        public static void AddHealthCratesToGameScene()
        {
            EnsureFolder(PrefabsFolder);
            EnsureFolder(MaterialsFolder);
            GameObject healthCratePrefab = BuildHealthCratePrefab();
            RegisterNetworkPrefab(healthCratePrefab);

            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

            var pickups = Object.FindFirstObjectByType<PickupSystem>();
            if (pickups != null)
            {
                pickups.gameObject.name = "PickupSystem";
                ConfigurePickupSystem(pickups);
            }

            bool alreadyPlaced = scene.GetRootGameObjects().Any(go => PrefabUtility.GetCorrespondingObjectFromSource(go) == healthCratePrefab);
            if (!alreadyPlaced)
                foreach (var position in HealthCratePositions)
                    PlaceInstance(healthCratePrefab, position, Quaternion.identity);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            RefreshInScenePlacedNetworkObjects(GameScenePath);
            Finish("Health crates added to the Game scene.");
        }

        public static void BuildAll()
        {
            GameObject playerPrefab = BuildGameplay();
            BuildBootstrapScene(playerPrefab);
            Finish("Bootstrap scene, Game scene and prefabs generated.");
        }

        /// <summary>Prefabs and the Game scene only; Bootstrap keeps referencing the same Player prefab asset.</summary>
        public static void RebuildGameplay()
        {
            BuildGameplay();
            Finish("Game scene and prefabs regenerated (Bootstrap untouched).");
        }

        static GameObject BuildGameplay()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(MaterialsFolder);

            GameObject playerPrefab = BuildPlayerPrefab();
            GameObject cratePrefab = BuildCratePrefab();
            GameObject enemyPrefab = BuildEnemyPrefab();
            GameObject healthCratePrefab = BuildHealthCratePrefab();
            GameObject explosiveCratePrefab = BuildExplosiveCratePrefab();
            GameObject metalCratePrefab = BuildMetalCratePrefab();
            GameObject swordEnemyPrefab = BuildSwordEnemyPrefab();
            GameObject rangedEnemyPrefab = BuildRangedEnemyPrefab();
            EnsureFolder(LevelPrefabsFolder);
            RegisterNetworkPrefab(BuildGameSessionPrefab(null));
            RegisterNetworkPrefab(metalCratePrefab);
            RegisterNetworkPrefab(swordEnemyPrefab);
            RegisterNetworkPrefab(rangedEnemyPrefab);
            RegisterNetworkPrefab(playerPrefab);
            RegisterNetworkPrefab(cratePrefab);
            RegisterNetworkPrefab(enemyPrefab);
            RegisterNetworkPrefab(healthCratePrefab);
            RegisterNetworkPrefab(explosiveCratePrefab);

            // Scenes must be in the build list before in-scene NetworkObjects compute their ids.
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapScenePath, true),
                new EditorBuildSettingsScene(GameScenePath, true),
            };

            BuildGameScene(cratePrefab, enemyPrefab, healthCratePrefab, explosiveCratePrefab, metalCratePrefab, swordEnemyPrefab, rangedEnemyPrefab);
            RefreshInScenePlacedNetworkObjects(GameScenePath);
            return playerPrefab;
        }

        static void Finish(string message)
        {
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(BootstrapScenePath);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScenePath);
            Debug.Log("[CoopProjectSetup] " + message);
        }

        // ---------- Player ----------

        static GameObject BuildPlayerPrefab()
        {
            var root = new GameObject("Player") { tag = "Player" };

            var controller = root.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.45f;
            controller.center = new Vector3(0f, 1f, 0f);
            controller.stepOffset = 0.35f;
            controller.slopeLimit = 50f;
            controller.skinWidth = 0.05f;

            root.AddComponent<NetworkObject>();

            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            netTransform.SyncRotAngleX = false;
            netTransform.SyncRotAngleZ = false;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;
            netTransform.Interpolate = true;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            var body = CreatePrimitive(PrimitiveType.Capsule, "Body", visual, new Vector3(0f, 1f, 0f), new Vector3(0.9f, 1f, 0.9f),
                Material("Player_Body", Color.white));
            // Visor shows which way the player faces.
            var visor = CreatePrimitive(PrimitiveType.Cube, "Visor", visual, new Vector3(0f, 1.5f, 0.38f), new Vector3(0.6f, 0.2f, 0.2f),
                Material("Player_Visor", new Color(0.1f, 0.1f, 0.12f)));
            var backpack = CreatePrimitive(PrimitiveType.Cube, "Backpack", visual, new Vector3(0f, 1.15f, -0.45f), new Vector3(0.55f, 0.6f, 0.25f),
                Material("Player_Backpack", new Color(0.55f, 0.55f, 0.6f)));

            // Placeholder wrench held at the right shoulder; PlayerMelee swings the pivot.
            var weaponPivot = new GameObject("WeaponPivot").transform;
            weaponPivot.SetParent(visual, false);
            weaponPivot.localPosition = new Vector3(0.5f, 1.2f, 0f);
            weaponPivot.localRotation = Quaternion.Euler(70f, 20f, 0f);
            var metal = Material("Wrench", new Color(0.75f, 0.77f, 0.8f));
            CreatePrimitive(PrimitiveType.Cube, "Wrench_Handle", weaponPivot, new Vector3(0f, 0f, 0.45f), new Vector3(0.09f, 0.09f, 0.9f), metal);
            CreatePrimitive(PrimitiveType.Cube, "Wrench_Head", weaponPivot, new Vector3(0f, 0f, 0.95f), new Vector3(0.35f, 0.1f, 0.18f), metal);

            var movement = root.AddComponent<PlayerMovement>();
            var so = new SerializedObject(movement);
            var renderers = so.FindProperty("tintedRenderers");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = body.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            var melee = root.AddComponent<PlayerMelee>();
            var meleeSo = new SerializedObject(melee);
            meleeSo.FindProperty("weaponPivot").objectReferenceValue = weaponPivot;
            meleeSo.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<PlayerHealth>();
            root.AddComponent<PlayerRevive>();
            AddPlayerPose(root, visual);
            root.AddComponent<PlayerTargeting>();
            root.AddComponent<WrenchThrow>();
            root.AddComponent<WrenchGrab>();
            root.AddComponent<PlayerDevTools>();
            AddWeaponsToPlayer(root);

            return SaveNetworkPrefab(root, PlayerPrefabPath);
        }

        static void AddPlayerPose(GameObject player, Transform visual)
        {
            var pose = player.GetComponent<PlayerPose>();
            if (pose == null) pose = player.AddComponent<PlayerPose>();
            var so = new SerializedObject(pose);
            so.FindProperty("visualRoot").objectReferenceValue = visual;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Tools/Coop Game/Add Swimming (pool + player pose)")]
        static void AddSwimmingFromMenu() => AddSwimming();

        /// <summary>
        /// Non-destructive: gives the existing Player prefab its PlayerPose component and adds the swimming pool
        /// to the existing Game scene (only if it isn't there yet). Nothing else is touched.
        /// </summary>
        public static void AddSwimming()
        {
            var contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            AddPlayerPose(contents, contents.transform.Find("Visual"));
            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            if (scene.GetRootGameObjects().All(go => go.name != "Pool"))
            {
                BuildSwimmingPool(null, Material("Wall", new Color(0.55f, 0.58f, 0.68f)));
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            Finish("Swimming added: PlayerPose on the Player prefab, pool in the Game scene.");
        }

        static GameObject BuildCratePrefab()
        {
            var color = new Color(0.7f, 0.45f, 0.2f);
            var root = CreatePrimitive(PrimitiveType.Cube, "Crate", null, Vector3.zero, Vector3.one, Material("Crate", color));
            root.AddComponent<NetworkObject>();
            ConfigureDamageable(root.AddComponent<Damageable>(), 1, root.GetComponent<Renderer>(), color, 8, 0.3f, boltDrop: 5);
            AddTargetable(root, aimHeight: 0f, radius: 0.6f);
            return SaveNetworkPrefab(root, CratePrefabPath);
        }

        static GameObject BuildHealthCratePrefab()
        {
            var color = new Color(0.15f, 0.7f, 0.55f);
            var root = CreatePrimitive(PrimitiveType.Cube, "HealthCrate", null, Vector3.zero, Vector3.one, Material("HealthCrate", color));

            // White "+" on the top and every side so it reads as health from any angle (visual only).
            var white = Material("HealthCrate_Cross", Color.white);
            Quaternion[] faces =
            {
                Quaternion.identity, Quaternion.Euler(90f, 0f, 0f), Quaternion.Euler(-90f, 0f, 0f),
                Quaternion.Euler(0f, 0f, 90f), Quaternion.Euler(0f, 0f, -90f),
            };
            foreach (var face in faces)
            {
                foreach (var bar in new[] { new Vector3(0.6f, 0.04f, 0.16f), new Vector3(0.16f, 0.04f, 0.6f) })
                {
                    var piece = CreatePrimitive(PrimitiveType.Cube, "Cross", root.transform, face * new Vector3(0f, 0.51f, 0f), bar, white);
                    piece.transform.localRotation = face;
                    Object.DestroyImmediate(piece.GetComponent<Collider>());
                }
            }

            root.AddComponent<NetworkObject>();
            var damageable = root.AddComponent<Damageable>();
            ConfigureDamageable(damageable, 1, root.GetComponent<Renderer>(), color, 8, 0.3f, boltDrop: 0);
            AddTargetable(root, aimHeight: 0f, radius: 0.6f);
            var so = new SerializedObject(damageable);
            so.FindProperty("healthDrop").intValue = 4;
            so.ApplyModifiedPropertiesWithoutUndo();
            return SaveNetworkPrefab(root, HealthCratePrefabPath);
        }

        /// <summary>
        /// Red crate with a yellow band and a fuse lamp on top: breaks into a blast when hit, lights its fuse when a
        /// player walks up (<see cref="ExplosiveCrate"/>). The body is a child so it can swell without moving the collider.
        /// </summary>
        static GameObject BuildExplosiveCratePrefab()
        {
            var color = new Color(0.75f, 0.12f, 0.08f);
            var root = new GameObject("ExplosiveCrate");
            root.AddComponent<BoxCollider>();
            var body = VisualCube("Body", root.transform, Vector3.zero, Vector3.one, Material("ExplosiveCrate", color));
            VisualCube("Band", body.transform, Vector3.zero, new Vector3(1.03f, 0.24f, 1.03f), Material("ExplosiveCrate_Band", new Color(1f, 0.8f, 0.1f)));
            VisualCube("FuseBase", body.transform, new Vector3(0f, 0.54f, 0f), new Vector3(0.32f, 0.08f, 0.32f), Material("ExplosiveCrate_Fuse", new Color(0.15f, 0.15f, 0.17f)));
            var lamp = VisualCube("Lamp", body.transform, new Vector3(0f, 0.63f, 0f), new Vector3(0.18f, 0.12f, 0.18f), Material("ExplosiveCrate_Lamp", new Color(0.3f, 0.05f, 0.05f)));

            root.AddComponent<NetworkObject>();
            ConfigureDamageable(root.AddComponent<Damageable>(), 1, body.GetComponent<Renderer>(), color, 10, 0.3f, boltDrop: 0);
            AddTargetable(root, aimHeight: 0f, radius: 0.6f);

            var explosive = root.AddComponent<ExplosiveCrate>();
            var so = new SerializedObject(explosive);
            so.FindProperty("lamp").objectReferenceValue = lamp.GetComponent<Renderer>();
            so.FindProperty("pulseVisual").objectReferenceValue = body.transform;
            so.FindProperty("fireMaterial").objectReferenceValue = EmissiveMaterial("Explosion_Fire", new Color(1f, 0.42f, 0.05f), 0.8f);
            so.FindProperty("coreMaterial").objectReferenceValue = EmissiveMaterial("Explosion_Core", new Color(1f, 0.9f, 0.45f), 1.3f);
            so.ApplyModifiedPropertiesWithoutUndo();
            return SaveNetworkPrefab(root, ExplosiveCratePrefabPath);
        }

        static Material EmissiveMaterial(string name, Color color, float intensity)
        {
            var material = Material(name, color);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", color * intensity);
            EditorUtility.SetDirty(material);
            return material;
        }

        [MenuItem("Tools/Coop Game/Add Explosive Crates (prefab + test area)")]
        static void AddExplosiveCratesFromMenu() => AddExplosiveCrates();

        /// <summary>
        /// Non-destructive: creates the ExplosiveCrate prefab, registers it as a network prefab and adds the test
        /// area to the existing Game scene (only if the scene has no explosive crates yet).
        /// </summary>
        public static void AddExplosiveCrates()
        {
            EnsureFolder(PrefabsFolder);
            EnsureFolder(MaterialsFolder);
            GameObject explosivePrefab = BuildExplosiveCratePrefab();
            RegisterNetworkPrefab(explosivePrefab);

            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            bool alreadyPlaced = scene.GetRootGameObjects().Any(go => PrefabUtility.GetCorrespondingObjectFromSource(go) == explosivePrefab);
            if (!alreadyPlaced)
            {
                PlaceExplosiveTestArea(explosivePrefab, AssetDatabase.LoadAssetAtPath<GameObject>(CratePrefabPath),
                    AssetDatabase.LoadAssetAtPath<GameObject>(HealthCratePrefabPath), AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath));
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                RefreshInScenePlacedNetworkObjects(GameScenePath);
            }
            Finish("Explosive crates added (prefab + test area in the Game scene).");
        }

        /// <summary>
        /// Explosive crate test spots in the open south-east field, far enough apart that one blast (or the knockback
        /// from it) doesn't set off the next group:
        /// A. One explosive next to two crates and a health crate (the blast breaks them and they drop their loot).
        /// B. A row of four explosives 2.2 m apart along x = 13, ending in a crate: set off one end and they chain.
        /// C. Two explosives by an enemy's post: blow them up while it stands there.
        /// </summary>
        static void PlaceExplosiveTestArea(GameObject explosivePrefab, GameObject cratePrefab, GameObject healthCratePrefab, GameObject enemyPrefab)
        {
            if (explosivePrefab == null) return;

            PlaceInstance(explosivePrefab, new Vector3(18f, 0.5f, -8.5f), Quaternion.identity);
            if (cratePrefab != null)
            {
                PlaceInstance(cratePrefab, new Vector3(16.2f, 0.5f, -8.5f), Quaternion.identity);
                PlaceInstance(cratePrefab, new Vector3(19.8f, 0.5f, -8.8f), Quaternion.Euler(0f, 20f, 0f));
            }
            if (healthCratePrefab != null) PlaceInstance(healthCratePrefab, new Vector3(18f, 0.5f, -6.5f), Quaternion.identity);

            for (int i = 0; i < 4; i++)
                PlaceInstance(explosivePrefab, new Vector3(13f, 0.5f, -14f - 2.2f * i), Quaternion.identity);
            if (cratePrefab != null) PlaceInstance(cratePrefab, new Vector3(13f, 0.5f, -22.8f), Quaternion.identity);

            if (enemyPrefab != null) PlaceInstance(enemyPrefab, new Vector3(26.5f, 0f, -10f), Quaternion.Euler(0f, 180f, 0f));
            PlaceInstance(explosivePrefab, new Vector3(25.3f, 0.5f, -11.6f), Quaternion.identity);
            PlaceInstance(explosivePrefab, new Vector3(27.7f, 0.5f, -11.6f), Quaternion.identity);
        }

        /// <summary>
        /// Grey reinforced crate with dark bars along its edges: only an explosive crate's blast breaks it (wrench,
        /// throws, shots, slams and the whip just throw sparks). Not targetable, so aiming doesn't lock onto it.
        /// </summary>
        static GameObject BuildMetalCratePrefab()
        {
            var color = new Color(0.55f, 0.58f, 0.62f);
            var metal = Material("MetalCrate", color);
            metal.SetFloat("_Metallic", 0.75f);
            metal.SetFloat("_Smoothness", 0.55f);
            var frame = Material("MetalCrate_Frame", new Color(0.22f, 0.23f, 0.26f));
            frame.SetFloat("_Metallic", 0.8f);
            frame.SetFloat("_Smoothness", 0.4f);

            var root = CreatePrimitive(PrimitiveType.Cube, "MetalCrate", null, Vector3.zero, Vector3.one, metal);
            const float edge = 0.47f;
            for (int axis = 0; axis < 3; axis++)
            for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
            {
                Vector3 position = axis == 0 ? new Vector3(0f, a * edge, b * edge)
                    : axis == 1 ? new Vector3(a * edge, 0f, b * edge)
                    : new Vector3(a * edge, b * edge, 0f);
                Vector3 size = Vector3.one * 0.1f;
                size[axis] = 1.04f;
                VisualCube("Bar", root.transform, position, size, frame);
            }

            root.AddComponent<NetworkObject>();
            var damageable = root.AddComponent<Damageable>();
            ConfigureDamageable(damageable, 1, root.GetComponent<Renderer>(), color, 10, 0.3f, boltDrop: 10);
            var so = new SerializedObject(damageable);
            so.FindProperty("vulnerableTo").intValue = (int)DamageTypes.Explosive;
            so.ApplyModifiedPropertiesWithoutUndo();
            return SaveNetworkPrefab(root, MetalCratePrefabPath);
        }

        [MenuItem("Tools/Coop Game/Add Metal Crates (prefab + test area)")]
        static void AddMetalCratesFromMenu() => AddMetalCrates();

        /// <summary>
        /// Non-destructive: creates the MetalCrate prefab, registers it as a network prefab and adds its test spots to
        /// the existing Game scene (only if the scene has no metal crates yet).
        /// </summary>
        public static void AddMetalCrates()
        {
            EnsureFolder(PrefabsFolder);
            EnsureFolder(MaterialsFolder);
            GameObject metalPrefab = BuildMetalCratePrefab();
            RegisterNetworkPrefab(metalPrefab);

            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            bool alreadyPlaced = scene.GetRootGameObjects().Any(go => PrefabUtility.GetCorrespondingObjectFromSource(go) == metalPrefab);
            if (!alreadyPlaced)
            {
                PlaceMetalCrateTestArea(metalPrefab, AssetDatabase.LoadAssetAtPath<GameObject>(ExplosiveCratePrefabPath));
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                RefreshInScenePlacedNetworkObjects(GameScenePath);
            }
            Finish("Metal crates added (prefab + test spots in the Game scene).");
        }

        /// <summary>
        /// Metal crate test spots next to the explosive crate area:
        /// D. A lone metal crate: hit it with everything, nothing breaks it.
        /// E. An explosive crate boxed in by three metal crates (open to the north): set it off to break them.
        /// F. A metal crate at the end of explosive row B, broken by the chain.
        /// </summary>
        static void PlaceMetalCrateTestArea(GameObject metalPrefab, GameObject explosivePrefab)
        {
            if (metalPrefab == null) return;
            PlaceInstance(metalPrefab, new Vector3(21f, 0.5f, -14.5f), Quaternion.identity);

            if (explosivePrefab != null) PlaceInstance(explosivePrefab, new Vector3(25f, 0.5f, -18f), Quaternion.identity);
            PlaceInstance(metalPrefab, new Vector3(23.4f, 0.5f, -18f), Quaternion.identity);
            PlaceInstance(metalPrefab, new Vector3(26.6f, 0.5f, -18f), Quaternion.identity);
            PlaceInstance(metalPrefab, new Vector3(25f, 0.5f, -19.6f), Quaternion.identity);

            PlaceInstance(metalPrefab, new Vector3(14.7f, 0.5f, -21.6f), Quaternion.identity);
        }

        static GameObject BuildEnemyPrefab()
        {
            var root = new GameObject("Enemy");
            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.6f;
            controller.radius = 0.5f;
            controller.center = new Vector3(0f, 0.8f, 0f);

            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            netTransform.SyncRotAngleX = false;
            netTransform.SyncRotAngleZ = false;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;

            // Visual root sits at the feet so the leap telegraph squashes towards the ground.
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            var color = new Color(0.85f, 0.2f, 0.25f);
            var body = CreatePrimitive(PrimitiveType.Capsule, "Body", visual, new Vector3(0f, 0.8f, 0f), new Vector3(1f, 0.8f, 1f),
                Material("Enemy_Body", color));
            var eye = Material("Enemy_Eyes", new Color(1f, 0.95f, 0.3f));
            CreatePrimitive(PrimitiveType.Cube, "Eye_L", visual, new Vector3(-0.18f, 1.15f, 0.42f), new Vector3(0.15f, 0.15f, 0.1f), eye);
            CreatePrimitive(PrimitiveType.Cube, "Eye_R", visual, new Vector3(0.18f, 1.15f, 0.42f), new Vector3(0.15f, 0.15f, 0.1f), eye);

            ConfigureDamageable(root.AddComponent<Damageable>(), 3, body.GetComponent<Renderer>(), color, 10, 0.35f, boltDrop: 10);
            root.AddComponent<EnemyController>();
            SetEnemyCharmMaterial(root);
            SetEnemyIceMaterial(root);
            AddTargetable(root, aimHeight: 0.8f, radius: 0.7f);

            var leap = root.AddComponent<LeapAttack>();
            var leapSo = new SerializedObject(leap);
            leapSo.FindProperty("minRange").floatValue = 2f;
            leapSo.FindProperty("maxRange").floatValue = 7f;
            leapSo.FindProperty("cooldown").floatValue = 2.5f;
            leapSo.FindProperty("visual").objectReferenceValue = visual;
            leapSo.ApplyModifiedPropertiesWithoutUndo();

            return SaveNetworkPrefab(root, EnemyPrefabPath);
        }

        [MenuItem("Tools/Coop Game/Add Sword Enemies (prefab + 2 in the level)")]
        static void AddSwordEnemiesFromMenu() => AddSwordEnemies();

        /// <summary>
        /// Non-destructive: creates the SwordEnemy prefab, registers it as a network prefab and puts two in the
        /// existing Game scene (only if the scene has none yet).
        /// </summary>
        public static void AddSwordEnemies()
        {
            EnsureFolder(PrefabsFolder);
            EnsureFolder(MaterialsFolder);
            GameObject swordPrefab = BuildSwordEnemyPrefab();
            RegisterNetworkPrefab(swordPrefab);

            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            bool alreadyPlaced = scene.GetRootGameObjects().Any(go => PrefabUtility.GetCorrespondingObjectFromSource(go) == swordPrefab);
            if (!alreadyPlaced)
            {
                PlaceSwordEnemies(swordPrefab);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                RefreshInScenePlacedNetworkObjects(GameScenePath);
            }
            Finish("Sword enemies added (prefab + 2 in the Game scene).");
        }

        /// <summary>Two sword enemies in the open stretch south of the spawn, facing it.</summary>
        static void PlaceSwordEnemies(GameObject swordPrefab)
        {
            if (swordPrefab == null) return;
            PlaceInstance(swordPrefab, new Vector3(-1f, 0f, -26f), Quaternion.identity);
            PlaceInstance(swordPrefab, new Vector3(5f, 0f, -28f), Quaternion.identity);
        }

        /// <summary>
        /// Armoured sword enemy, 20% bigger than the player (2.4 m): helmet visor with glowing eyes, shoulder plates
        /// and a sword in the right hand (<see cref="SwordAttack"/>). 10 HP, slower than the basic enemy and harder
        /// to knock about.
        /// </summary>
        static GameObject BuildSwordEnemyPrefab()
        {
            var root = new GameObject("SwordEnemy");
            var controller = root.AddComponent<CharacterController>();
            controller.height = 2.4f;
            controller.radius = 0.54f;
            controller.center = new Vector3(0f, 1.2f, 0f);

            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            netTransform.SyncRotAngleX = false;
            netTransform.SyncRotAngleZ = false;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            var color = new Color(0.42f, 0.22f, 0.5f);
            var armor = Material("SwordEnemy_Armor", new Color(0.55f, 0.57f, 0.63f));
            var dark = Material("SwordEnemy_Visor", new Color(0.08f, 0.08f, 0.1f));
            var eyes = EmissiveMaterial("SwordEnemy_Eyes", new Color(1f, 0.45f, 0.1f), 1.5f);
            var body = CreatePrimitive(PrimitiveType.Capsule, "Body", visual, new Vector3(0f, 1.2f, 0f), new Vector3(1.08f, 1.2f, 1.08f),
                Material("SwordEnemy_Body", color));
            CreatePrimitive(PrimitiveType.Cube, "Visor", visual, new Vector3(0f, 1.9f, 0.47f), new Vector3(0.8f, 0.22f, 0.25f), dark);
            CreatePrimitive(PrimitiveType.Cube, "Eye_L", visual, new Vector3(-0.17f, 1.9f, 0.6f), new Vector3(0.16f, 0.08f, 0.05f), eyes);
            CreatePrimitive(PrimitiveType.Cube, "Eye_R", visual, new Vector3(0.17f, 1.9f, 0.6f), new Vector3(0.16f, 0.08f, 0.05f), eyes);
            CreatePrimitive(PrimitiveType.Cube, "Crest", visual, new Vector3(0f, 2.4f, 0f), new Vector3(0.1f, 0.18f, 0.6f), armor);
            CreatePrimitive(PrimitiveType.Cube, "Shoulder_L", visual, new Vector3(-0.58f, 1.62f, 0f), new Vector3(0.38f, 0.2f, 0.55f), armor);
            CreatePrimitive(PrimitiveType.Cube, "Shoulder_R", visual, new Vector3(0.58f, 1.62f, 0f), new Vector3(0.38f, 0.2f, 0.55f), armor);
            CreatePrimitive(PrimitiveType.Cube, "Belt", visual, new Vector3(0f, 0.95f, 0f), new Vector3(1.12f, 0.14f, 1.12f), dark);

            // Sword in the right hand; the blade points along the pivot's up.
            var swordPivot = new GameObject("SwordPivot").transform;
            swordPivot.SetParent(visual, false);
            swordPivot.localPosition = new Vector3(0.7f, 1.3f, 0.25f);
            swordPivot.localRotation = Quaternion.Euler(35f, 15f, 0f);
            var steel = Material("Sword_Blade", new Color(0.82f, 0.85f, 0.9f));
            var hilt = Material("Sword_Hilt", new Color(0.3f, 0.2f, 0.12f));
            CreatePrimitive(PrimitiveType.Cube, "Sword_Hilt", swordPivot, new Vector3(0f, -0.05f, 0f), new Vector3(0.09f, 0.3f, 0.09f), hilt);
            CreatePrimitive(PrimitiveType.Cube, "Sword_Guard", swordPivot, new Vector3(0f, 0.13f, 0f), new Vector3(0.42f, 0.07f, 0.12f), armor);
            var blade = CreatePrimitive(PrimitiveType.Cube, "Sword_Blade", swordPivot, new Vector3(0f, 0.87f, 0f), new Vector3(0.11f, 1.4f, 0.04f), steel);
            var tip = new GameObject("BladeTip").transform;
            tip.SetParent(swordPivot, false);
            tip.localPosition = new Vector3(0f, 1.55f, 0f);

            ConfigureDamageable(root.AddComponent<Damageable>(), 10, body.GetComponent<Renderer>(), color, 14, 0.4f, boltDrop: 30);

            var enemy = root.AddComponent<EnemyController>();
            var enemySo = new SerializedObject(enemy);
            enemySo.FindProperty("moveSpeed").floatValue = 2.6f;
            enemySo.FindProperty("aggroRange").floatValue = 12f;
            enemySo.FindProperty("stopDistance").floatValue = 1.8f;
            enemySo.FindProperty("knockbackSpeed").floatValue = 5f;
            enemySo.FindProperty("knockbackUpSpeed").floatValue = 2.5f;
            enemySo.FindProperty("stunTime").floatValue = 0.3f;
            enemySo.FindProperty("haloHeight").floatValue = 2.85f;
            enemySo.ApplyModifiedPropertiesWithoutUndo();
            SetEnemyCharmMaterial(root);
            SetEnemyIceMaterial(root);
            AddTargetable(root, aimHeight: 1.3f, radius: 0.85f);

            var sword = root.AddComponent<SwordAttack>();
            var swordSo = new SerializedObject(sword);
            swordSo.FindProperty("minRange").floatValue = 0f;
            swordSo.FindProperty("maxRange").floatValue = 2.6f;
            swordSo.FindProperty("cooldown").floatValue = 1.4f;
            swordSo.FindProperty("swordPivot").objectReferenceValue = swordPivot;
            swordSo.FindProperty("bladeTip").objectReferenceValue = tip;
            swordSo.FindProperty("bladeRenderer").objectReferenceValue = blade.GetComponent<Renderer>();
            swordSo.FindProperty("trailMaterial").objectReferenceValue = EmissiveMaterial("Sword_Trail", new Color(1f, 0.85f, 0.6f), 1.2f);
            swordSo.ApplyModifiedPropertiesWithoutUndo();

            return SaveNetworkPrefab(root, SwordEnemyPrefabPath);
        }

        [MenuItem("Tools/Coop Game/Add Ranged Enemies (prefab + 2 in the level)")]
        static void AddRangedEnemiesFromMenu() => AddRangedEnemies();

        /// <summary>
        /// Non-destructive: creates the RangedEnemy prefab, registers it as a network prefab and puts two in the
        /// existing Game scene (only if the scene has none yet).
        /// </summary>
        public static void AddRangedEnemies()
        {
            EnsureFolder(PrefabsFolder);
            EnsureFolder(MaterialsFolder);
            GameObject rangedPrefab = BuildRangedEnemyPrefab();
            RegisterNetworkPrefab(rangedPrefab);

            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            bool alreadyPlaced = scene.GetRootGameObjects().Any(go => PrefabUtility.GetCorrespondingObjectFromSource(go) == rangedPrefab);
            if (!alreadyPlaced)
            {
                PlaceRangedEnemies(rangedPrefab);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                RefreshInScenePlacedNetworkObjects(GameScenePath);
            }
            Finish("Ranged enemies added (prefab + 2 in the Game scene).");
        }

        /// <summary>Two ranged enemies behind the sword enemies, south of the spawn, facing it (room to back away).</summary>
        static void PlaceRangedEnemies(GameObject rangedPrefab)
        {
            if (rangedPrefab == null) return;
            PlaceInstance(rangedPrefab, new Vector3(-6f, 0f, -32f), Quaternion.identity);
            PlaceInstance(rangedPrefab, new Vector3(8f, 0f, -32f), Quaternion.identity);
        }

        /// <summary>
        /// Ranged enemy, 20% bigger than the player (2.4 m): a green cyclops with a cannon on its right arm and a
        /// power pack on its back. 10 HP. Keeps its distance — stops about 11 m from the player and backs away
        /// (still facing them) when they come within 7 m — and fires a bolt at them every 2 s (<see cref="EnemyShooter"/>).
        /// </summary>
        static GameObject BuildRangedEnemyPrefab()
        {
            var root = new GameObject("RangedEnemy");
            var controller = root.AddComponent<CharacterController>();
            controller.height = 2.4f;
            controller.radius = 0.54f;
            controller.center = new Vector3(0f, 1.2f, 0f);

            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            netTransform.SyncRotAngleX = false;
            netTransform.SyncRotAngleZ = false;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            var color = new Color(0.25f, 0.6f, 0.35f);
            var metal = Material("RangedEnemy_Metal", new Color(0.3f, 0.32f, 0.36f));
            var dark = Material("RangedEnemy_Dark", new Color(0.08f, 0.08f, 0.1f));
            var glow = EmissiveMaterial("RangedEnemy_Glow", new Color(0.6f, 1f, 0.3f), 1.6f);
            var body = CreatePrimitive(PrimitiveType.Capsule, "Body", visual, new Vector3(0f, 1.2f, 0f), new Vector3(1.08f, 1.2f, 1.08f),
                Material("RangedEnemy_Body", color));
            CreatePrimitive(PrimitiveType.Cube, "EyeSocket", visual, new Vector3(0f, 1.85f, 0.47f), new Vector3(0.5f, 0.34f, 0.2f), dark);
            CreatePrimitive(PrimitiveType.Cube, "Eye", visual, new Vector3(0f, 1.85f, 0.58f), new Vector3(0.3f, 0.2f, 0.05f), glow);
            CreatePrimitive(PrimitiveType.Cube, "Antenna", visual, new Vector3(-0.2f, 2.5f, -0.1f), new Vector3(0.05f, 0.4f, 0.05f), metal);
            CreatePrimitive(PrimitiveType.Cube, "Antenna_Tip", visual, new Vector3(-0.2f, 2.72f, -0.1f), Vector3.one * 0.1f, glow);
            CreatePrimitive(PrimitiveType.Cube, "PowerPack", visual, new Vector3(0f, 1.45f, -0.6f), new Vector3(0.65f, 0.75f, 0.3f), metal);
            CreatePrimitive(PrimitiveType.Cube, "PowerPack_Light", visual, new Vector3(0f, 1.6f, -0.76f), new Vector3(0.4f, 0.08f, 0.03f), glow);

            // Cannon on the right arm, pointing forward; bolts leave from the muzzle.
            var cannon = new GameObject("Cannon").transform;
            cannon.SetParent(visual, false);
            cannon.localPosition = new Vector3(0.72f, 1.35f, 0.1f);
            CreatePrimitive(PrimitiveType.Cube, "Cannon_Body", cannon, new Vector3(0f, 0f, 0.15f), new Vector3(0.3f, 0.3f, 0.75f), metal);
            CreatePrimitive(PrimitiveType.Cube, "Cannon_Ring", cannon, new Vector3(0f, 0f, 0.5f), new Vector3(0.36f, 0.36f, 0.08f), glow);
            CreatePrimitive(PrimitiveType.Cube, "Cannon_Mouth", cannon, new Vector3(0f, 0f, 0.58f), new Vector3(0.18f, 0.18f, 0.06f), dark);
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(cannon, false);
            muzzle.localPosition = new Vector3(0f, 0f, 0.72f);
            var chargeGlow = CreatePrimitive(PrimitiveType.Sphere, "ChargeGlow", muzzle, Vector3.zero, Vector3.zero, glow);
            chargeGlow.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            ConfigureDamageable(root.AddComponent<Damageable>(), 10, body.GetComponent<Renderer>(), color, 14, 0.4f, boltDrop: 30);

            var enemy = root.AddComponent<EnemyController>();
            var enemySo = new SerializedObject(enemy);
            enemySo.FindProperty("moveSpeed").floatValue = 2.4f;
            enemySo.FindProperty("aggroRange").floatValue = 16f;
            enemySo.FindProperty("stopDistance").floatValue = 11f;
            enemySo.FindProperty("retreatDistance").floatValue = 7f;
            enemySo.FindProperty("retreatSpeed").floatValue = 3f;
            enemySo.FindProperty("knockbackSpeed").floatValue = 6f;
            enemySo.FindProperty("knockbackUpSpeed").floatValue = 3f;
            enemySo.FindProperty("haloHeight").floatValue = 2.85f;
            enemySo.ApplyModifiedPropertiesWithoutUndo();
            SetEnemyCharmMaterial(root);
            SetEnemyIceMaterial(root);
            AddTargetable(root, aimHeight: 1.3f, radius: 0.85f);

            var shooter = root.AddComponent<EnemyShooter>();
            var shooterSo = new SerializedObject(shooter);
            shooterSo.FindProperty("muzzle").objectReferenceValue = muzzle;
            shooterSo.FindProperty("chargeGlow").objectReferenceValue = chargeGlow.transform;
            shooterSo.FindProperty("boltMaterial").objectReferenceValue = glow;
            shooterSo.ApplyModifiedPropertiesWithoutUndo();

            return SaveNetworkPrefab(root, RangedEnemyPrefabPath);
        }

        [MenuItem("Tools/Coop Game/Build Mortar Enemy Prefabs (MortarEnemy + MiniRobot)")]
        static void BuildMortarEnemyPrefabsFromMenu()
        {
            BuildMortarEnemyPrefabs();
            Finish("MortarEnemy and MiniRobot prefabs built and registered (nothing placed in any scene).");
        }

        /// <summary>
        /// Prefabs only, no scene is touched: the MiniRobot minion and the MortarEnemy that fires it, both registered
        /// as network prefabs (minions are spawned at runtime).
        /// </summary>
        public static void BuildMortarEnemyPrefabs()
        {
            EnsureFolder(PrefabsFolder);
            EnsureFolder(MaterialsFolder);
            var minion = BuildMiniRobotPrefab();
            RegisterNetworkPrefab(minion);
            RegisterNetworkPrefab(BuildMortarEnemyPrefab(minion));
        }

        /// <summary>
        /// Minion the mortar fires: a knee-high orange robot (0.9 m), 1 HP, quick on its feet; it runs at the nearest
        /// player and hops at them for 1 damage (<see cref="LeapAttack"/>, short range). Drops nothing, or a mortar
        /// would be a bolt farm.
        /// </summary>
        static GameObject BuildMiniRobotPrefab()
        {
            var root = new GameObject("MiniRobot");
            var controller = root.AddComponent<CharacterController>();
            controller.height = 0.9f;
            controller.radius = 0.32f;
            controller.center = new Vector3(0f, 0.45f, 0f);
            controller.stepOffset = 0.3f;

            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            netTransform.SyncRotAngleX = false;
            netTransform.SyncRotAngleZ = false;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            var color = new Color(1f, 0.55f, 0.1f);
            var bodyMaterial = Material("MiniRobot_Body", color);
            var metal = KeepMaterial("RangedEnemy_Metal", new Color(0.3f, 0.32f, 0.36f));
            var dark = KeepMaterial("RangedEnemy_Dark", new Color(0.08f, 0.08f, 0.1f));
            var eye = EmissiveMaterial("MiniRobot_Eye", new Color(1f, 0.2f, 0.15f), 2f);
            var body = CreatePrimitive(PrimitiveType.Cube, "Body", visual, new Vector3(0f, 0.5f, 0f), new Vector3(0.55f, 0.45f, 0.5f), bodyMaterial);
            CreatePrimitive(PrimitiveType.Cube, "Face", visual, new Vector3(0f, 0.55f, 0.26f), new Vector3(0.4f, 0.22f, 0.04f), dark);
            CreatePrimitive(PrimitiveType.Cube, "Eye", visual, new Vector3(0f, 0.55f, 0.29f), new Vector3(0.22f, 0.08f, 0.03f), eye);
            CreatePrimitive(PrimitiveType.Cube, "Leg_L", visual, new Vector3(-0.17f, 0.14f, 0f), new Vector3(0.14f, 0.28f, 0.18f), metal);
            CreatePrimitive(PrimitiveType.Cube, "Leg_R", visual, new Vector3(0.17f, 0.14f, 0f), new Vector3(0.14f, 0.28f, 0.18f), metal);
            CreatePrimitive(PrimitiveType.Cube, "Claw_L", visual, new Vector3(-0.34f, 0.45f, 0.18f), new Vector3(0.1f, 0.1f, 0.3f), metal);
            CreatePrimitive(PrimitiveType.Cube, "Claw_R", visual, new Vector3(0.34f, 0.45f, 0.18f), new Vector3(0.1f, 0.1f, 0.3f), metal);
            CreatePrimitive(PrimitiveType.Cube, "Antenna", visual, new Vector3(0.12f, 0.85f, -0.1f), new Vector3(0.04f, 0.25f, 0.04f), metal);
            CreatePrimitive(PrimitiveType.Cube, "Antenna_Tip", visual, new Vector3(0.12f, 0.99f, -0.1f), Vector3.one * 0.07f, eye);

            ConfigureDamageable(root.AddComponent<Damageable>(), 1, body.GetComponent<Renderer>(), color, 6, 0.2f, boltDrop: 0);

            var enemy = root.AddComponent<EnemyController>();
            var enemySo = new SerializedObject(enemy);
            enemySo.FindProperty("moveSpeed").floatValue = 4.5f;
            enemySo.FindProperty("turnSpeed").floatValue = 540f;
            enemySo.FindProperty("aggroRange").floatValue = 40f;
            enemySo.FindProperty("stopDistance").floatValue = 1f;
            enemySo.FindProperty("knockbackSpeed").floatValue = 10f;
            enemySo.FindProperty("haloHeight").floatValue = 1.2f;
            enemySo.ApplyModifiedPropertiesWithoutUndo();
            SetEnemyCharmMaterial(root);
            SetEnemyIceMaterial(root);
            AddTargetable(root, aimHeight: 0.45f, radius: 0.5f);

            var leap = root.AddComponent<LeapAttack>();
            var leapSo = new SerializedObject(leap);
            leapSo.FindProperty("minRange").floatValue = 0f;
            leapSo.FindProperty("maxRange").floatValue = 2.4f;
            leapSo.FindProperty("cooldown").floatValue = 1.2f;
            leapSo.FindProperty("damage").intValue = 1;
            leapSo.FindProperty("hitRadius").floatValue = 0.5f;
            leapSo.FindProperty("hitCapsuleTop").floatValue = 0.8f;
            leapSo.FindProperty("windupTime").floatValue = 0.35f;
            leapSo.FindProperty("leapHorizontalSpeed").floatValue = 7f;
            leapSo.FindProperty("leapHeight").floatValue = 0.9f;
            leapSo.FindProperty("returnHeight").floatValue = 0.7f;
            leapSo.FindProperty("returnAfterHit").boolValue = false;
            leapSo.FindProperty("visual").objectReferenceValue = visual;
            leapSo.ApplyModifiedPropertiesWithoutUndo();

            return SaveNetworkPrefab(root, MiniRobotPrefabPath);
        }

        /// <summary>
        /// Mortar enemy (2.2 m): a squat armoured walker with a big barrel pointing up, 10 HP. From up to 40 m away it
        /// lobs a <see cref="MiniRobotPrefabPath"/> minion at the players every 2 s, standing still while it does
        /// (<see cref="MortarAttack"/>); it walks closer only beyond 30 m. Within 10 m it's helpless: it stops firing and
        /// backs away, still facing them.
        /// </summary>
        static GameObject BuildMortarEnemyPrefab(GameObject minionPrefab)
        {
            var root = new GameObject("MortarEnemy");
            var controller = root.AddComponent<CharacterController>();
            controller.height = 2.2f;
            controller.radius = 0.8f;
            controller.center = new Vector3(0f, 1.1f, 0f);

            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            netTransform.SyncRotAngleX = false;
            netTransform.SyncRotAngleZ = false;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            var color = new Color(0.45f, 0.42f, 0.25f);
            var bodyMaterial = Material("MortarEnemy_Body", color);
            var metal = KeepMaterial("RangedEnemy_Metal", new Color(0.3f, 0.32f, 0.36f));
            var dark = KeepMaterial("RangedEnemy_Dark", new Color(0.08f, 0.08f, 0.1f));
            var glow = EmissiveMaterial("MortarEnemy_Glow", new Color(1f, 0.5f, 0.1f), 1.8f);
            var podBody = KeepMaterial("MiniRobot_Body", new Color(1f, 0.55f, 0.1f));

            // Treads and hull
            CreatePrimitive(PrimitiveType.Cube, "Tread_L", visual, new Vector3(-0.7f, 0.3f, 0f), new Vector3(0.4f, 0.6f, 1.7f), dark);
            CreatePrimitive(PrimitiveType.Cube, "Tread_R", visual, new Vector3(0.7f, 0.3f, 0f), new Vector3(0.4f, 0.6f, 1.7f), dark);
            var body = CreatePrimitive(PrimitiveType.Cube, "Hull", visual, new Vector3(0f, 0.95f, 0f), new Vector3(1.4f, 0.8f, 1.6f), bodyMaterial);
            CreatePrimitive(PrimitiveType.Sphere, "Dome", visual, new Vector3(0f, 1.45f, -0.15f), new Vector3(1.1f, 0.8f, 1.1f), bodyMaterial);
            CreatePrimitive(PrimitiveType.Cube, "Visor", visual, new Vector3(0f, 1.25f, 0.8f), new Vector3(0.7f, 0.18f, 0.06f), glow);
            // Spare robot pods racked on its back.
            for (int i = 0; i < 3; i++)
            {
                CreatePrimitive(PrimitiveType.Cube, "Pod", visual, new Vector3(-0.42f + i * 0.42f, 1.55f, -0.85f), new Vector3(0.36f, 0.36f, 0.36f), podBody);
                CreatePrimitive(PrimitiveType.Cube, "Pod_Band", visual, new Vector3(-0.42f + i * 0.42f, 1.55f, -0.85f), new Vector3(0.4f, 0.1f, 0.4f), metal);
            }

            // Barrel: pivot on top of the hull, pointing forward and 55° up; pods leave from the muzzle.
            var barrel = new GameObject("Barrel").transform;
            barrel.SetParent(visual, false);
            barrel.localPosition = new Vector3(0f, 1.55f, 0.25f);
            barrel.localRotation = Quaternion.Euler(-55f, 0f, 0f);
            var tube = CreatePrimitive(PrimitiveType.Cylinder, "Tube", barrel, new Vector3(0f, 0f, 0.55f), new Vector3(0.55f, 0.65f, 0.55f), metal);
            tube.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var ring = CreatePrimitive(PrimitiveType.Cylinder, "Ring", barrel, new Vector3(0f, 0f, 1.12f), new Vector3(0.68f, 0.06f, 0.68f), glow);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var mouth = CreatePrimitive(PrimitiveType.Cylinder, "Mouth", barrel, new Vector3(0f, 0f, 1.19f), new Vector3(0.4f, 0.02f, 0.4f), dark);
            mouth.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(barrel, false);
            muzzle.localPosition = new Vector3(0f, 0f, 1.35f);
            var chargeGlow = CreatePrimitive(PrimitiveType.Sphere, "ChargeGlow", muzzle, Vector3.zero, Vector3.zero, glow);
            chargeGlow.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            ConfigureDamageable(root.AddComponent<Damageable>(), 10, body.GetComponent<Renderer>(), color, 14, 0.4f, boltDrop: 30);

            var enemy = root.AddComponent<EnemyController>();
            var enemySo = new SerializedObject(enemy);
            enemySo.FindProperty("moveSpeed").floatValue = 2f;
            enemySo.FindProperty("turnSpeed").floatValue = 200f;
            enemySo.FindProperty("aggroRange").floatValue = 45f;
            // It lobs over walls: no need to walk round them to see its target.
            enemySo.FindProperty("needsLineOfSight").boolValue = false;
            enemySo.FindProperty("stopDistance").floatValue = 30f;
            enemySo.FindProperty("retreatDistance").floatValue = 10f;
            enemySo.FindProperty("retreatSpeed").floatValue = 3.2f;
            enemySo.FindProperty("knockbackSpeed").floatValue = 4f;
            enemySo.FindProperty("knockbackUpSpeed").floatValue = 2f;
            enemySo.FindProperty("haloHeight").floatValue = 2.6f;
            enemySo.ApplyModifiedPropertiesWithoutUndo();
            SetEnemyCharmMaterial(root);
            SetEnemyIceMaterial(root);
            AddTargetable(root, aimHeight: 1.1f, radius: 1f);

            var mortar = root.AddComponent<MortarAttack>();
            var mortarSo = new SerializedObject(mortar);
            mortarSo.FindProperty("minRange").floatValue = 10f;
            mortarSo.FindProperty("maxRange").floatValue = 40f;
            // 0.7 s charge + 0.35 s recoil + 0.95 s cooldown = one robot every 2 s.
            mortarSo.FindProperty("cooldown").floatValue = 0.95f;
            mortarSo.FindProperty("minionPrefab").objectReferenceValue = minionPrefab;
            mortarSo.FindProperty("barrel").objectReferenceValue = barrel;
            mortarSo.FindProperty("muzzle").objectReferenceValue = muzzle;
            mortarSo.FindProperty("chargeGlow").objectReferenceValue = chargeGlow.transform;
            mortarSo.FindProperty("podMaterial").objectReferenceValue = podBody;
            mortarSo.FindProperty("podBandMaterial").objectReferenceValue = metal;
            mortarSo.ApplyModifiedPropertiesWithoutUndo();

            return SaveNetworkPrefab(root, MortarEnemyPrefabPath);
        }

        [MenuItem("Tools/Coop Game/Build Shield Enemy Prefab")]
        static void BuildShieldEnemyPrefabFromMenu()
        {
            BuildShieldEnemyPrefabs();
            Finish("ShieldEnemy prefab built and registered (nothing placed in any scene).");
        }

        /// <summary>Prefab only, no scene is touched: the ShieldEnemy, registered as a network prefab.</summary>
        public static void BuildShieldEnemyPrefabs()
        {
            EnsureFolder(PrefabsFolder);
            EnsureFolder(MaterialsFolder);
            RegisterNetworkPrefab(BuildShieldEnemyPrefab());
        }

        /// <summary>
        /// Bulky bronze guard (2.3 m) behind a big steel shield (<see cref="EnemyShield"/>, 30 shield points on top of
        /// 15 HP): while the shield lasts it bashes players up close with it (4 damage and a hard shove); once the
        /// shield is broken it punches instead (2 damage). Slow, and hard to knock about.
        /// </summary>
        static GameObject BuildShieldEnemyPrefab()
        {
            var root = new GameObject("ShieldEnemy");
            var controller = root.AddComponent<CharacterController>();
            controller.height = 2.3f;
            controller.radius = 0.6f;
            controller.center = new Vector3(0f, 1.15f, 0f);

            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            netTransform.SyncRotAngleX = false;
            netTransform.SyncRotAngleZ = false;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            var color = new Color(0.55f, 0.36f, 0.2f);
            var bodyMaterial = Material("ShieldEnemy_Body", color);
            var armor = Material("ShieldEnemy_Armor", new Color(0.33f, 0.36f, 0.41f));
            var dark = Material("ShieldEnemy_Dark", new Color(0.08f, 0.08f, 0.1f));
            var eyes = EmissiveMaterial("ShieldEnemy_Eyes", new Color(1f, 0.8f, 0.2f), 1.6f);
            var body = CreatePrimitive(PrimitiveType.Capsule, "Body", visual, new Vector3(0f, 1.15f, 0f), new Vector3(1.25f, 1.15f, 1.25f), bodyMaterial);
            CreatePrimitive(PrimitiveType.Cube, "Helmet", visual, new Vector3(0f, 2.02f, 0.05f), new Vector3(0.85f, 0.5f, 0.85f), armor);
            CreatePrimitive(PrimitiveType.Cube, "VisorSlit", visual, new Vector3(0f, 2f, 0.48f), new Vector3(0.6f, 0.08f, 0.05f), eyes);
            CreatePrimitive(PrimitiveType.Cube, "Shoulder_L", visual, new Vector3(-0.68f, 1.62f, 0f), new Vector3(0.45f, 0.25f, 0.6f), armor);
            CreatePrimitive(PrimitiveType.Cube, "Shoulder_R", visual, new Vector3(0.68f, 1.62f, 0f), new Vector3(0.45f, 0.25f, 0.6f), armor);
            CreatePrimitive(PrimitiveType.Cube, "Belt", visual, new Vector3(0f, 0.95f, 0f), new Vector3(1.3f, 0.14f, 1.3f), dark);

            // The shield in the left hand, covering the front.
            var shieldPivot = new GameObject("ShieldPivot").transform;
            shieldPivot.SetParent(visual, false);
            shieldPivot.localPosition = new Vector3(-0.35f, 1.2f, 0.72f);
            var plate = CreatePrimitive(PrimitiveType.Cube, "Shield_Plate", shieldPivot, Vector3.zero, new Vector3(1.15f, 1.55f, 0.14f),
                Material("ShieldEnemy_Shield", new Color(0.38f, 0.5f, 0.65f)));
            var rim = CreatePrimitive(PrimitiveType.Cube, "Shield_Rim", shieldPivot, new Vector3(0f, 0f, -0.03f), new Vector3(1.27f, 1.67f, 0.1f), armor);
            var boss = CreatePrimitive(PrimitiveType.Cube, "Shield_Boss", shieldPivot, new Vector3(0f, 0.05f, 0.1f), new Vector3(0.35f, 0.35f, 0.1f),
                Material("ShieldEnemy_Boss", new Color(0.75f, 0.55f, 0.25f)));

            // Right fist, for when the shield is gone.
            var fistPivot = new GameObject("FistPivot").transform;
            fistPivot.SetParent(visual, false);
            fistPivot.localPosition = new Vector3(0.72f, 1.2f, 0.25f);
            CreatePrimitive(PrimitiveType.Cube, "Forearm", fistPivot, new Vector3(0f, 0f, 0.2f), new Vector3(0.24f, 0.24f, 0.5f), bodyMaterial);
            var fist = CreatePrimitive(PrimitiveType.Cube, "Fist", fistPivot, new Vector3(0f, 0f, 0.5f), new Vector3(0.34f, 0.34f, 0.34f), armor);

            ConfigureDamageable(root.AddComponent<Damageable>(), 15, body.GetComponent<Renderer>(), color, 14, 0.4f, boltDrop: 40);

            var enemy = root.AddComponent<EnemyController>();
            var enemySo = new SerializedObject(enemy);
            enemySo.FindProperty("moveSpeed").floatValue = 2.4f;
            enemySo.FindProperty("aggroRange").floatValue = 12f;
            enemySo.FindProperty("stopDistance").floatValue = 1.6f;
            enemySo.FindProperty("knockbackSpeed").floatValue = 4f;
            enemySo.FindProperty("knockbackUpSpeed").floatValue = 2f;
            enemySo.FindProperty("stunTime").floatValue = 0.35f;
            enemySo.FindProperty("haloHeight").floatValue = 2.75f;
            enemySo.ApplyModifiedPropertiesWithoutUndo();
            SetEnemyCharmMaterial(root);
            SetEnemyIceMaterial(root);
            AddTargetable(root, aimHeight: 1.3f, radius: 0.9f);

            var shield = root.AddComponent<EnemyShield>();
            var shieldSo = new SerializedObject(shield);
            shieldSo.FindProperty("maxShield").intValue = 30;
            shieldSo.FindProperty("shieldVisual").objectReferenceValue = shieldPivot;
            var shieldParts = shieldSo.FindProperty("shieldRenderers");
            shieldParts.arraySize = 2;
            shieldParts.GetArrayElementAtIndex(0).objectReferenceValue = plate.GetComponent<Renderer>();
            shieldParts.GetArrayElementAtIndex(1).objectReferenceValue = rim.GetComponent<Renderer>();
            shieldSo.ApplyModifiedPropertiesWithoutUndo();

            // The bash comes first: while the shield lasts the punch can't start anyway, and the other way round.
            ConfigureMeleeStrike(root.AddComponent<MeleeStrikeAttack>(), MeleeStrikeAttack.Requires.Shield, damage: 4, reach: 2.3f, arc: 55f,
                shove: 9f, windup: 0.6f, strike: 0.18f, recover: 0.65f, lunge: 1f, maxRange: 2.2f, cooldown: 1.6f,
                shieldPivot, boss.GetComponent<Renderer>(),
                new Vector3(-0.35f, 1.2f, 0.72f), Vector3.zero,
                new Vector3(-0.45f, 1.35f, 0.35f), new Vector3(-12f, 25f, 0f),
                new Vector3(-0.1f, 1.15f, 1.35f), new Vector3(0f, -5f, 0f));
            ConfigureMeleeStrike(root.AddComponent<MeleeStrikeAttack>(), MeleeStrikeAttack.Requires.NoShield, damage: 2, reach: 1.9f, arc: 50f,
                shove: 0f, windup: 0.35f, strike: 0.12f, recover: 0.4f, lunge: 0.4f, maxRange: 1.8f, cooldown: 0.8f,
                fistPivot, fist.GetComponent<Renderer>(),
                new Vector3(0.72f, 1.2f, 0.25f), Vector3.zero,
                new Vector3(0.75f, 1.3f, -0.25f), new Vector3(0f, 15f, 0f),
                new Vector3(0.45f, 1.35f, 0.95f), new Vector3(0f, -10f, 0f));

            return SaveNetworkPrefab(root, ShieldEnemyPrefabPath);
        }

        static void ConfigureMeleeStrike(MeleeStrikeAttack attack, MeleeStrikeAttack.Requires requires, int damage, float reach, float arc,
            float shove, float windup, float strike, float recover, float lunge, float maxRange, float cooldown,
            Transform pivot, Renderer glow, Vector3 idlePosition, Vector3 idleAngles, Vector3 windupPosition, Vector3 windupAngles,
            Vector3 strikePosition, Vector3 strikeAngles)
        {
            var so = new SerializedObject(attack);
            so.FindProperty("requires").enumValueIndex = (int)requires;
            so.FindProperty("damage").intValue = damage;
            so.FindProperty("reach").floatValue = reach;
            so.FindProperty("arcHalfAngle").floatValue = arc;
            so.FindProperty("shove").floatValue = shove;
            so.FindProperty("windupTime").floatValue = windup;
            so.FindProperty("strikeTime").floatValue = strike;
            so.FindProperty("recoverTime").floatValue = recover;
            so.FindProperty("lunge").floatValue = lunge;
            so.FindProperty("minRange").floatValue = 0f;
            so.FindProperty("maxRange").floatValue = maxRange;
            so.FindProperty("cooldown").floatValue = cooldown;
            so.FindProperty("pivot").objectReferenceValue = pivot;
            so.FindProperty("glowRenderer").objectReferenceValue = glow;
            so.FindProperty("idlePosition").vector3Value = idlePosition;
            so.FindProperty("idleAngles").vector3Value = idleAngles;
            so.FindProperty("windupPosition").vector3Value = windupPosition;
            so.FindProperty("windupAngles").vector3Value = windupAngles;
            so.FindProperty("strikePosition").vector3Value = strikePosition;
            so.FindProperty("strikeAngles").vector3Value = strikeAngles;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureDamageable(Damageable damageable, int health, Renderer flashRenderer, Color debrisColor, int debrisCount, float debrisSize,
            int boltDrop)
        {
            var so = new SerializedObject(damageable);
            so.FindProperty("maxHealth").intValue = health;
            so.FindProperty("boltDrop").intValue = boltDrop;
            var renderers = so.FindProperty("flashRenderers");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = flashRenderer;
            so.FindProperty("debrisColor").colorValue = debrisColor;
            so.FindProperty("debrisCount").intValue = debrisCount;
            so.FindProperty("debrisSize").floatValue = debrisSize;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The NavMeshAgent an enemy plans its paths with, sized from its CharacterController. Saved switched off: the
        /// enemy turns it on itself (server only) once it stands on the NavMesh.
        /// </summary>
        public static void ConfigureEnemyNavMeshAgent(GameObject enemy)
        {
            if (enemy.GetComponent<EnemyController>() == null) return;
            var agent = enemy.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent == null) agent = enemy.AddComponent<UnityEngine.AI.NavMeshAgent>();
            var motor = enemy.GetComponent<CharacterController>();
            agent.radius = motor != null ? motor.radius : 0.5f;
            agent.height = motor != null ? motor.height : 2f;
            agent.baseOffset = 0f;
            agent.speed = 3f;
            agent.angularSpeed = 360f;
            agent.acceleration = 30f;
            agent.stoppingDistance = 0f;
            agent.autoBraking = false;
            agent.obstacleAvoidanceType = UnityEngine.AI.ObstacleAvoidanceType.NoObstacleAvoidance;
            agent.enabled = false;
        }

        static GameObject SaveNetworkPrefab(GameObject root, string path)
        {
            ConfigureEnemyNavMeshAgent(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            // Re-save from inside the asset so NetworkObject generates its prefab GlobalObjectIdHash.
            var contents = PrefabUtility.LoadPrefabContents(path);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);

            // The hash worked out inside the prefab contents isn't the asset's own; the editor works that one out
            // when it loads the asset. Save it too, or other editors reading the file (Multiplayer Play Mode players)
            // end up with a different network config and can't join.
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var networkObject = asset.GetComponent<NetworkObject>();
            if (networkObject != null)
            {
                typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(networkObject, null);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            return asset;
        }

        static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, NetworkPrefabsListPath);
            }

            if (!list.PrefabList.Any(p => p.Prefab == prefab))
                list.Add(new NetworkPrefab { Prefab = prefab });

            EditorUtility.SetDirty(list);
        }

        // ---------- Bootstrap scene ----------

        static void BuildBootstrapScene(GameObject playerPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("NetworkManager");
            var networkManager = go.AddComponent<NetworkManager>();
            var transport = go.AddComponent<UnityTransport>();
            SetSessionPrefab(go.AddComponent<NetworkBootstrap>());

            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
            networkManager.NetworkConfig.EnableSceneManagement = true;
            networkManager.NetworkConfig.ConnectionApproval = false;

            var prefabsList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
            var lists = networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists;
            if (!lists.Contains(prefabsList)) lists.Add(prefabsList);

            // Something to render while the menu is up.
            var cameraGo = new GameObject("Menu Camera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.14f, 0.2f);

            EditorSceneManager.SaveScene(scene, BootstrapScenePath);
        }

        // ---------- Game scene ----------

        static void BuildGameScene(GameObject cratePrefab, GameObject enemyPrefab, GameObject healthCratePrefab, GameObject explosiveCratePrefab,
            GameObject metalCratePrefab, GameObject swordEnemyPrefab, GameObject rangedEnemyPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var light = Object.FindFirstObjectByType<Light>();
            if (light != null) light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            BuildCamera();
            BuildLevel();
            BuildSpawnPoints();
            PlaceBreakables(cratePrefab, enemyPrefab);
            BuildTetherArea(cratePrefab);
            BuildCoopArea(cratePrefab, healthCratePrefab);
            BuildJumpPadArea(cratePrefab, healthCratePrefab);
            PlaceExplosiveTestArea(explosiveCratePrefab, cratePrefab, healthCratePrefab, enemyPrefab);
            PlaceMetalCrateTestArea(metalCratePrefab, explosiveCratePrefab);
            PlaceSwordEnemies(swordEnemyPrefab);
            PlaceRangedEnemies(rangedEnemyPrefab);
            foreach (var position in HealthCratePositions)
                PlaceInstance(healthCratePrefab, position, Quaternion.identity);
            BuildHud();
            EnsureEventSystem();

            EditorSceneManager.SaveScene(scene, GameScenePath);
        }

        static void PlaceBreakables(GameObject cratePrefab, GameObject enemyPrefab)
        {
            // Kept at the scene root: in-scene NetworkObjects are simplest without non-network parents.
            Vector3[] cratePositions =
            {
                new(-3f, 0.5f, 5f), new(-1.5f, 0.5f, 5.5f), new(0f, 0.5f, 5f), new(1.5f, 0.5f, 5.5f), new(3f, 0.5f, 5f),
                new(-6f, 0.5f, -3f), new(-6f, 0.5f, -4.5f), new(6f, 0.5f, -3f), new(6f, 0.5f, -4.5f),
                new(10f, 1.5f, 0f), new(18f, 1.5f, 1.5f), new(18f, 1.5f, -1.5f),
            };
            foreach (var position in cratePositions)
                PlaceInstance(cratePrefab, position, Quaternion.Euler(0f, Random.Range(-15f, 15f), 0f));

            PlaceInstance(enemyPrefab, new Vector3(-8f, 0f, -8f), Quaternion.Euler(0f, 45f, 0f));
            PlaceInstance(enemyPrefab, new Vector3(8f, 0f, -8f), Quaternion.Euler(0f, -45f, 0f));
            PlaceInstance(enemyPrefab, new Vector3(-10f, 0f, 20f), Quaternion.Euler(0f, 160f, 0f));
        }

        static void ConfigurePickupSystem(PickupSystem pickups)
        {
            var so = new SerializedObject(pickups);
            so.FindProperty("boltMaterial").objectReferenceValue = Material("Bolt", new Color(1f, 0.8f, 0.25f));
            so.FindProperty("healthMaterial").objectReferenceValue = Material("HealthOrb", new Color(0.3f, 1f, 0.55f));
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildHud()
        {
            var canvasGo = new GameObject("HUD");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var boltLabel = HudLabel(canvasGo.transform, "BoltCounter", "Części: 0", new Vector2(1f, 1f), new Vector2(-40f, -30f),
                TextAlignmentOptions.TopRight, new Color(1f, 0.85f, 0.3f));
            var counter = canvasGo.AddComponent<BoltCounterUI>();
            var so = new SerializedObject(counter);
            so.FindProperty("label").objectReferenceValue = boltLabel;
            so.ApplyModifiedPropertiesWithoutUndo();

            var hpLabel = HudLabel(canvasGo.transform, "PlayerHealth", "HP: 10/10", new Vector2(1f, 0f), new Vector2(-40f, 30f),
                TextAlignmentOptions.BottomRight, Color.white);
            var health = canvasGo.AddComponent<PlayerHealthUI>();
            var healthSo = new SerializedObject(health);
            healthSo.FindProperty("label").objectReferenceValue = hpLabel;
            healthSo.ApplyModifiedPropertiesWithoutUndo();

            var reviveLabel = HudLabel(canvasGo.transform, "ReviveStatus", "", new Vector2(0.5f, 0f), new Vector2(0f, 160f),
                TextAlignmentOptions.Bottom, Color.white);
            ((RectTransform)reviveLabel.transform).sizeDelta = new Vector2(1200f, 80f);
            reviveLabel.fontSize = 44f;
            var revive = canvasGo.AddComponent<ReviveUI>();
            var reviveSo = new SerializedObject(revive);
            reviveSo.FindProperty("label").objectReferenceValue = reviveLabel;
            reviveSo.ApplyModifiedPropertiesWithoutUndo();

            BuildTargetingHud(canvasGo);
            BuildLoadoutHud(canvasGo);
            BuildWeaponSelectPanel(canvasGo);
        }

        // ---------- Weapons ----------

        [MenuItem("Tools/Coop Game/Add Weapons (loadout, pistol, TAB panel)")]
        static void AddWeaponsFromMenu() => AddWeapons();

        /// <summary>
        /// Non-destructive: gives the existing Player prefab the loadout + pistol (with placeholder hand/back models)
        /// and adds the loadout HUD, the TAB weapon panel and an EventSystem to the existing Game scene.
        /// </summary>
        public static void AddWeapons()
        {
            EditPrefab(PlayerPrefabPath, AddWeaponsToPlayer);

            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            var hud = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "HUD");
            if (hud != null)
            {
                if (hud.GetComponent<LoadoutHUD>() == null) BuildLoadoutHud(hud);
                if (hud.GetComponent<WeaponSelectPanel>() == null) BuildWeaponSelectPanel(hud);
            }
            EnsureEventSystem();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Finish("Weapons added: loadout + pistol on the Player prefab, loadout HUD and TAB panel in the Game scene.");
        }

        /// <summary>Placeholder weapon models (hand pistol, wrench + pistol on the back) and the weapon components. Idempotent.</summary>
        static void AddWeaponsToPlayer(GameObject player)
        {
            var visual = player.transform.Find("Visual");
            var wrenchMaterial = Material("Wrench", new Color(0.75f, 0.77f, 0.8f));
            var pistolMaterial = Material("Pistol", new Color(0.18f, 0.18f, 0.2f));

            var backMount = visual.Find("BackMount");
            if (backMount == null)
            {
                backMount = new GameObject("BackMount").transform;
                backMount.SetParent(visual, false);
                backMount.localPosition = new Vector3(0f, 1.25f, -0.62f);

                // Wrench strapped diagonally across the back.
                var wrenchBack = new GameObject("WrenchBack").transform;
                wrenchBack.SetParent(backMount, false);
                wrenchBack.localPosition = new Vector3(0.15f, -0.35f, 0f);
                wrenchBack.localRotation = Quaternion.LookRotation(new Vector3(-0.45f, 0.9f, 0f), Vector3.back);
                CreatePrimitive(PrimitiveType.Cube, "Wrench_Handle", wrenchBack, new Vector3(0f, 0f, 0.45f), new Vector3(0.09f, 0.09f, 0.9f), wrenchMaterial);
                CreatePrimitive(PrimitiveType.Cube, "Wrench_Head", wrenchBack, new Vector3(0f, 0f, 0.95f), new Vector3(0.35f, 0.1f, 0.18f), wrenchMaterial);

                // Pistol hanging barrel-down on the lower back.
                var pistolBack = new GameObject("PistolBack").transform;
                pistolBack.SetParent(backMount, false);
                pistolBack.localPosition = new Vector3(-0.2f, -0.4f, 0.02f);
                pistolBack.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.back);
                BuildPistolMesh(pistolBack, pistolMaterial);
            }

            var pistolHand = visual.Find("PistolPivot");
            if (pistolHand == null)
            {
                pistolHand = new GameObject("PistolPivot").transform;
                pistolHand.SetParent(visual, false);
                pistolHand.localPosition = new Vector3(0.45f, 1.15f, 0.3f);
                BuildPistolMesh(pistolHand, pistolMaterial);
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(pistolHand, false);
                muzzle.localPosition = new Vector3(0f, 0.02f, 0.26f);
            }

            var weapons = player.GetComponent<PlayerWeapons>();
            if (weapons == null) weapons = player.AddComponent<PlayerWeapons>();
            var weaponsSo = new SerializedObject(weapons);
            weaponsSo.FindProperty("wrenchBackModel").objectReferenceValue = backMount.Find("WrenchBack").gameObject;
            weaponsSo.ApplyModifiedPropertiesWithoutUndo();

            var pistol = player.GetComponent<PistolWeapon>();
            if (pistol == null) pistol = player.AddComponent<PistolWeapon>();
            var pistolSo = new SerializedObject(pistol);
            pistolSo.FindProperty("handModel").objectReferenceValue = pistolHand.gameObject;
            pistolSo.FindProperty("backModel").objectReferenceValue = backMount.Find("PistolBack").gameObject;
            pistolSo.FindProperty("muzzle").objectReferenceValue = pistolHand.Find("Muzzle");
            pistolSo.FindProperty("tracerMaterial").objectReferenceValue = Material("Tracer", new Color(1f, 0.9f, 0.45f));
            pistolSo.FindProperty("bulletMaterial").objectReferenceValue = PistolBulletMaterial();
            pistolSo.ApplyModifiedPropertiesWithoutUndo();

            AddShockWhipToPlayer(player, visual, backMount);
            AddGrenadeToPlayer(player);
            AddShotgunToPlayer(player);
            AddRocketLauncherToPlayer(player);
            AddRobotWeaponToPlayer(player);
            AddDiscLauncherToPlayer(player);
            AddFreezerToPlayer(player);

            // Nothing secondary is equipped at spawn.
            pistolHand.gameObject.SetActive(false);
            backMount.Find("PistolBack").gameObject.SetActive(false);
            backMount.Find("WrenchBack").gameObject.SetActive(false);
        }

        [MenuItem("Tools/Coop Game/Replace Shock Hammer With Whip")]
        public static void AddShockWhip()
        {
            // Prefab only: swaps the old hammer models for the whip's (the weapon component keeps its slot); scenes don't change.
            EditPrefab(PlayerPrefabPath, player =>
            {
                var visual = player.transform.Find("Visual");
                AddShockWhipToPlayer(player, visual, visual.Find("BackMount"));
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[CoopProjectSetup] Electric whip set up on the Player prefab (weapon slot 2).");
        }

        /// <summary>
        /// Electric whip: the handle in the right hand (the lash itself is built at runtime by the weapon) and a coiled
        /// copy on the back. Idempotent; removes the old shock hammer models.
        /// </summary>
        static void AddShockWhipToPlayer(GameObject player, Transform visual, Transform backMount)
        {
            foreach (var old in new[] { visual.Find("HammerPivot"), backMount.Find("HammerBack") })
                if (old != null) Object.DestroyImmediate(old.gameObject);

            var grip = KeepMaterial("Whip_Grip", new Color(0.22f, 0.17f, 0.15f));
            var lash = KeepEmissiveMaterial("Whip_Lash", new Color(0.2f, 0.75f, 1f), 1.1f);

            var hand = visual.Find("WhipPivot");
            if (hand == null)
            {
                hand = new GameObject("WhipPivot").transform;
                hand.SetParent(visual, false);
                hand.localPosition = new Vector3(0.5f, 1.15f, 0.15f);
                hand.localRotation = Quaternion.Euler(30f, 10f, 0f);
                BuildWhipHandle(hand, grip, lash);
                var lashRoot = new GameObject("LashRoot").transform;
                lashRoot.SetParent(hand, false);
                lashRoot.localPosition = new Vector3(0f, 0f, 0.36f);
            }

            var back = backMount.Find("WhipBack");
            if (back == null)
            {
                back = new GameObject("WhipBack").transform;
                back.SetParent(backMount, false);
                back.localPosition = new Vector3(0f, -0.3f, -0.04f);

                // The lash coiled flat against the back, the handle tucked through the coil.
                const int coilLinks = 12;
                for (int i = 0; i < coilLinks; i++)
                {
                    float angle = i * Mathf.PI * 2f / coilLinks;
                    var link = CreatePrimitive(PrimitiveType.Cube, "Whip_Coil", back, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.2f, new Vector3(0.11f, 0.04f, 0.04f), lash);
                    link.transform.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
                }
                var handleBack = new GameObject("Whip_Handle").transform;
                handleBack.SetParent(back, false);
                handleBack.localPosition = new Vector3(-0.12f, -0.16f, -0.03f);
                handleBack.localRotation = Quaternion.LookRotation(new Vector3(0.6f, 0.8f, 0f), Vector3.back);
                BuildWhipHandle(handleBack, grip, lash);
            }

            var whip = player.GetComponent<ShockWhipWeapon>();
            if (whip == null) whip = player.AddComponent<ShockWhipWeapon>();
            var so = new SerializedObject(whip);
            so.FindProperty("handModel").objectReferenceValue = hand.gameObject;
            so.FindProperty("backModel").objectReferenceValue = back.gameObject;
            so.FindProperty("handle").objectReferenceValue = hand;
            so.FindProperty("lashRoot").objectReferenceValue = hand.Find("LashRoot");
            so.FindProperty("lashMaterial").objectReferenceValue = lash;
            so.ApplyModifiedPropertiesWithoutUndo();

            hand.gameObject.SetActive(false);
            back.gameObject.SetActive(false);
        }

        [MenuItem("Tools/Coop Game/Add Grenade To Player")]
        public static void AddGrenade()
        {
            // Prefab only (adds just the grenade); scenes don't change.
            EditPrefab(PlayerPrefabPath, AddGrenadeToPlayer);
            AssetDatabase.SaveAssets();
            Debug.Log("[CoopProjectSetup] Grenade added to the Player prefab (weapon slot 3).");
        }

        /// <summary>Impact grenade: one in the right hand (also where it's thrown from) and one clipped to the belt. Idempotent.</summary>
        static void AddGrenadeToPlayer(GameObject player)
        {
            var visual = player.transform.Find("Visual");
            var backMount = visual.Find("BackMount");
            var body = KeepMaterial("Grenade", new Color(0.25f, 0.45f, 0.2f));
            var band = KeepMaterial("Grenade_Band", new Color(1f, 0.8f, 0.15f));

            var hand = visual.Find("GrenadePivot");
            if (hand == null)
            {
                hand = new GameObject("GrenadePivot").transform;
                hand.SetParent(visual, false);
                hand.localPosition = new Vector3(0.45f, 1.2f, 0.25f);
                BuildGrenadeMesh(hand, body, band);
            }

            var back = backMount.Find("GrenadeBack");
            if (back == null)
            {
                back = new GameObject("GrenadeBack").transform;
                back.SetParent(backMount, false);
                back.localPosition = new Vector3(0.3f, -0.55f, 0.05f);
                BuildGrenadeMesh(back, body, band);
            }

            var grenade = player.GetComponent<GrenadeWeapon>();
            if (grenade == null) grenade = player.AddComponent<GrenadeWeapon>();
            var so = new SerializedObject(grenade);
            so.FindProperty("handModel").objectReferenceValue = hand.gameObject;
            so.FindProperty("backModel").objectReferenceValue = back.gameObject;
            so.FindProperty("throwPoint").objectReferenceValue = hand;
            so.FindProperty("grenadeMaterial").objectReferenceValue = body;
            so.FindProperty("bandMaterial").objectReferenceValue = band;
            so.FindProperty("arcMaterial").objectReferenceValue = KeepEmissiveMaterial("GrenadeArc", new Color(1f, 0.7f, 0.15f), 1f);
            so.FindProperty("fireMaterial").objectReferenceValue = KeepEmissiveMaterial("Explosion_Fire", new Color(1f, 0.42f, 0.05f), 0.8f);
            so.FindProperty("coreMaterial").objectReferenceValue = KeepEmissiveMaterial("Explosion_Core", new Color(1f, 0.9f, 0.45f), 1.3f);
            so.ApplyModifiedPropertiesWithoutUndo();

            hand.gameObject.SetActive(false);
            back.gameObject.SetActive(false);
        }

        static void BuildGrenadeMesh(Transform parent, Material body, Material band)
        {
            CreatePrimitive(PrimitiveType.Cube, "Grenade_Body", parent, Vector3.zero, Vector3.one * 0.2f, body);
            CreatePrimitive(PrimitiveType.Cube, "Grenade_Band", parent, Vector3.zero, new Vector3(0.23f, 0.06f, 0.23f), band);
            CreatePrimitive(PrimitiveType.Cube, "Grenade_Cap", parent, new Vector3(0f, 0.13f, 0f), new Vector3(0.08f, 0.06f, 0.08f), band);
        }

        [MenuItem("Tools/Coop Game/Add Shotgun To Player")]
        public static void AddShotgun()
        {
            // Prefab only (adds just the shotgun); scenes don't change.
            EditPrefab(PlayerPrefabPath, AddShotgunToPlayer);
            AssetDatabase.SaveAssets();
            Debug.Log("[CoopProjectSetup] Shotgun added to the Player prefab (weapon slot 4).");
        }

        /// <summary>Double-barrelled shotgun: one in the right hand (muzzle at the barrel ends) and one slung across the back. Idempotent.</summary>
        static void AddShotgunToPlayer(GameObject player)
        {
            var visual = player.transform.Find("Visual");
            var backMount = visual.Find("BackMount");
            var metal = KeepMaterial("Shotgun_Metal", new Color(0.2f, 0.2f, 0.23f));
            var wood = KeepMaterial("Shotgun_Wood", new Color(0.45f, 0.28f, 0.15f));

            var hand = visual.Find("ShotgunPivot");
            if (hand == null)
            {
                hand = new GameObject("ShotgunPivot").transform;
                hand.SetParent(visual, false);
                hand.localPosition = new Vector3(0.45f, 1.15f, 0.25f);
                BuildShotgunMesh(hand, metal, wood);
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(hand, false);
                muzzle.localPosition = new Vector3(0f, 0.02f, 0.82f);
            }

            var back = backMount.Find("ShotgunBack");
            if (back == null)
            {
                back = new GameObject("ShotgunBack").transform;
                back.SetParent(backMount, false);
                back.localPosition = new Vector3(-0.1f, -0.35f, 0f);
                back.localRotation = Quaternion.LookRotation(new Vector3(0.45f, 0.9f, 0f), Vector3.back);
                BuildShotgunMesh(back, metal, wood);
            }

            var shotgun = player.GetComponent<ShotgunWeapon>();
            if (shotgun == null) shotgun = player.AddComponent<ShotgunWeapon>();
            var so = new SerializedObject(shotgun);
            so.FindProperty("handModel").objectReferenceValue = hand.gameObject;
            so.FindProperty("backModel").objectReferenceValue = back.gameObject;
            so.FindProperty("muzzle").objectReferenceValue = hand.Find("Muzzle");
            so.FindProperty("tracerMaterial").objectReferenceValue = KeepMaterial("Tracer", new Color(1f, 0.9f, 0.45f));
            so.ApplyModifiedPropertiesWithoutUndo();

            hand.gameObject.SetActive(false);
            back.gameObject.SetActive(false);
        }

        static void BuildShotgunMesh(Transform parent, Material metal, Material wood)
        {
            CreatePrimitive(PrimitiveType.Cube, "Shotgun_Barrel_L", parent, new Vector3(-0.035f, 0.02f, 0.42f), new Vector3(0.06f, 0.06f, 0.78f), metal);
            CreatePrimitive(PrimitiveType.Cube, "Shotgun_Barrel_R", parent, new Vector3(0.035f, 0.02f, 0.42f), new Vector3(0.06f, 0.06f, 0.78f), metal);
            CreatePrimitive(PrimitiveType.Cube, "Shotgun_Foregrip", parent, new Vector3(0f, -0.03f, 0.36f), new Vector3(0.12f, 0.07f, 0.24f), wood);
            CreatePrimitive(PrimitiveType.Cube, "Shotgun_Body", parent, new Vector3(0f, 0f, 0.02f), new Vector3(0.11f, 0.1f, 0.14f), metal);
            CreatePrimitive(PrimitiveType.Cube, "Shotgun_Stock", parent, new Vector3(0f, -0.06f, -0.2f), new Vector3(0.08f, 0.14f, 0.34f), wood);
        }

        [MenuItem("Tools/Coop Game/Add Rocket Launcher To Player")]
        public static void AddRocketLauncher()
        {
            // Prefab only (adds just the launcher); scenes don't change.
            EditPrefab(PlayerPrefabPath, AddRocketLauncherToPlayer);
            AssetDatabase.SaveAssets();
            Debug.Log("[CoopProjectSetup] Rocket launcher added to the Player prefab (weapon slot 5).");
        }

        /// <summary>Shoulder-mounted rocket tube (muzzle at the front ring) and a copy slung on the back. Idempotent.</summary>
        static void AddRocketLauncherToPlayer(GameObject player)
        {
            var visual = player.transform.Find("Visual");
            var backMount = visual.Find("BackMount");
            var tube = KeepMaterial("Rocket_Tube", new Color(0.35f, 0.42f, 0.25f));
            var trim = KeepMaterial("Rocket_Trim", new Color(0.8f, 0.15f, 0.1f));

            var hand = visual.Find("RocketPivot");
            if (hand == null)
            {
                hand = new GameObject("RocketPivot").transform;
                hand.SetParent(visual, false);
                hand.localPosition = new Vector3(0.38f, 1.6f, 0f);
                BuildRocketLauncherMesh(hand, tube, trim);
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(hand, false);
                muzzle.localPosition = new Vector3(0f, 0f, 0.68f);
            }

            var back = backMount.Find("RocketBack");
            if (back == null)
            {
                back = new GameObject("RocketBack").transform;
                back.SetParent(backMount, false);
                back.localPosition = new Vector3(0f, -0.15f, -0.05f);
                back.localRotation = Quaternion.LookRotation(new Vector3(0.25f, 1f, 0f), Vector3.back);
                BuildRocketLauncherMesh(back, tube, trim);
            }

            var launcher = player.GetComponent<RocketLauncherWeapon>();
            if (launcher == null) launcher = player.AddComponent<RocketLauncherWeapon>();
            var so = new SerializedObject(launcher);
            so.FindProperty("handModel").objectReferenceValue = hand.gameObject;
            so.FindProperty("backModel").objectReferenceValue = back.gameObject;
            so.FindProperty("muzzle").objectReferenceValue = hand.Find("Muzzle");
            so.FindProperty("bodyMaterial").objectReferenceValue = KeepMaterial("Rocket_Body", new Color(0.85f, 0.85f, 0.88f));
            so.FindProperty("noseMaterial").objectReferenceValue = trim;
            so.FindProperty("flameMaterial").objectReferenceValue = KeepEmissiveMaterial("Explosion_Core", new Color(1f, 0.9f, 0.45f), 1.3f);
            so.FindProperty("smokeMaterial").objectReferenceValue = KeepMaterial("Rocket_Smoke", new Color(0.72f, 0.72f, 0.74f));
            so.FindProperty("fireMaterial").objectReferenceValue = KeepEmissiveMaterial("Explosion_Fire", new Color(1f, 0.42f, 0.05f), 0.8f);
            so.FindProperty("coreMaterial").objectReferenceValue = KeepEmissiveMaterial("Explosion_Core", new Color(1f, 0.9f, 0.45f), 1.3f);
            so.ApplyModifiedPropertiesWithoutUndo();

            hand.gameObject.SetActive(false);
            back.gameObject.SetActive(false);
        }

        static void BuildRocketLauncherMesh(Transform parent, Material tube, Material trim)
        {
            CreatePrimitive(PrimitiveType.Cube, "Rocket_Tube", parent, new Vector3(0f, 0f, 0.1f), new Vector3(0.22f, 0.22f, 1.05f), tube);
            CreatePrimitive(PrimitiveType.Cube, "Rocket_FrontRing", parent, new Vector3(0f, 0f, 0.6f), new Vector3(0.28f, 0.28f, 0.08f), trim);
            CreatePrimitive(PrimitiveType.Cube, "Rocket_RearRing", parent, new Vector3(0f, 0f, -0.4f), new Vector3(0.27f, 0.27f, 0.06f), trim);
            CreatePrimitive(PrimitiveType.Cube, "Rocket_Grip", parent, new Vector3(0f, -0.17f, 0.12f), new Vector3(0.06f, 0.14f, 0.08f), tube);
            CreatePrimitive(PrimitiveType.Cube, "Rocket_Sight", parent, new Vector3(-0.13f, 0.08f, 0.2f), new Vector3(0.05f, 0.08f, 0.12f), trim);
        }

        // ---------- Disc launcher ----------

        [MenuItem("Tools/Coop Game/Add Disc Launcher To Player")]
        public static void AddDiscLauncher()
        {
            // Prefab only (adds just the disc launcher); scenes don't change.
            EditPrefab(PlayerPrefabPath, AddDiscLauncherToPlayer);
            AssetDatabase.SaveAssets();
            Debug.Log("[CoopProjectSetup] Disc launcher added to the Player prefab (weapon slot 7).");
        }

        /// <summary>Disc launcher: a short barrel with a glowing disc loaded on top, in the hand and on the back. Idempotent.</summary>
        static void AddDiscLauncherToPlayer(GameObject player)
        {
            var visual = player.transform.Find("Visual");
            var backMount = visual.Find("BackMount");
            var body = KeepMaterial("DiscLauncher_Body", new Color(0.3f, 0.32f, 0.38f));
            var disc = KeepEmissiveMaterial("Disc", new Color(0.3f, 0.9f, 1f), 1.2f);
            var hub = KeepMaterial("Disc_Hub", new Color(0.15f, 0.15f, 0.18f));

            var hand = visual.Find("DiscPivot");
            if (hand == null)
            {
                hand = new GameObject("DiscPivot").transform;
                hand.SetParent(visual, false);
                hand.localPosition = new Vector3(0.45f, 1.15f, 0.3f);
                BuildDiscLauncherMesh(hand, body, disc);
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(hand, false);
                muzzle.localPosition = new Vector3(0f, 0.04f, 0.32f);
            }

            var back = backMount.Find("DiscBack");
            if (back == null)
            {
                back = new GameObject("DiscBack").transform;
                back.SetParent(backMount, false);
                back.localPosition = new Vector3(0.2f, -0.25f, 0.05f);
                back.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.back);
                BuildDiscLauncherMesh(back, body, disc);
            }

            var launcher = player.GetComponent<DiscLauncherWeapon>();
            if (launcher == null) launcher = player.AddComponent<DiscLauncherWeapon>();
            var so = new SerializedObject(launcher);
            so.FindProperty("handModel").objectReferenceValue = hand.gameObject;
            so.FindProperty("backModel").objectReferenceValue = back.gameObject;
            so.FindProperty("muzzle").objectReferenceValue = hand.Find("Muzzle");
            so.FindProperty("discMaterial").objectReferenceValue = disc;
            so.FindProperty("hubMaterial").objectReferenceValue = hub;
            so.ApplyModifiedPropertiesWithoutUndo();

            hand.gameObject.SetActive(false);
            back.gameObject.SetActive(false);
        }

        static void BuildDiscLauncherMesh(Transform parent, Material body, Material disc)
        {
            CreatePrimitive(PrimitiveType.Cube, "Disc_Barrel", parent, new Vector3(0f, 0f, 0.08f), new Vector3(0.14f, 0.12f, 0.44f), body);
            CreatePrimitive(PrimitiveType.Cube, "Disc_Grip", parent, new Vector3(0f, -0.1f, -0.04f), new Vector3(0.07f, 0.14f, 0.08f), body);
            CreatePrimitive(PrimitiveType.Cube, "Disc_Loaded", parent, new Vector3(0f, 0.09f, 0.08f), new Vector3(0.3f, 0.04f, 0.3f), disc);
        }

        // ---------- Freezer ----------

        [MenuItem("Tools/Coop Game/Add Freezer (weapon + enemy ice)")]
        public static void AddFreezer()
        {
            // Prefabs only; scenes don't change (in-scene enemies pick up the ice material from their prefab).
            EditPrefab(PlayerPrefabPath, AddFreezerToPlayer);
            EditPrefab(EnemyPrefabPath, SetEnemyIceMaterial);
            AssetDatabase.SaveAssets();
            Debug.Log("[CoopProjectSetup] Freezer added to the Player prefab (weapon slot 8); enemies got their ice material.");
        }

        static Material IceMaterial() => KeepEmissiveMaterial("Ice", new Color(0.55f, 0.88f, 1f), 0.6f);

        /// <summary>Freezer: a pale launcher with a frosty ball loaded on top, in the hand and on the back. Idempotent.</summary>
        static void AddFreezerToPlayer(GameObject player)
        {
            var visual = player.transform.Find("Visual");
            var backMount = visual.Find("BackMount");
            var body = KeepMaterial("Freezer_Body", new Color(0.82f, 0.86f, 0.9f));
            var ice = IceMaterial();
            var preview = KeepEmissiveMaterial("Freezer_Preview", new Color(0.6f, 0.95f, 1f), 1f);

            var hand = visual.Find("FreezerPivot");
            if (hand == null)
            {
                hand = new GameObject("FreezerPivot").transform;
                hand.SetParent(visual, false);
                hand.localPosition = new Vector3(0.45f, 1.15f, 0.3f);
                BuildFreezerMesh(hand, body, ice);
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(hand, false);
                muzzle.localPosition = new Vector3(0f, 0.02f, 0.4f);
            }

            var back = backMount.Find("FreezerBack");
            if (back == null)
            {
                back = new GameObject("FreezerBack").transform;
                back.SetParent(backMount, false);
                back.localPosition = new Vector3(-0.2f, -0.2f, 0.05f);
                back.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.back);
                BuildFreezerMesh(back, body, ice);
            }

            var freezer = player.GetComponent<FreezeBallWeapon>();
            if (freezer == null) freezer = player.AddComponent<FreezeBallWeapon>();
            var so = new SerializedObject(freezer);
            so.FindProperty("handModel").objectReferenceValue = hand.gameObject;
            so.FindProperty("backModel").objectReferenceValue = back.gameObject;
            so.FindProperty("muzzle").objectReferenceValue = hand.Find("Muzzle");
            so.FindProperty("ballMaterial").objectReferenceValue = ice;
            so.FindProperty("previewMaterial").objectReferenceValue = preview;
            so.ApplyModifiedPropertiesWithoutUndo();

            hand.gameObject.SetActive(false);
            back.gameObject.SetActive(false);
        }

        static void BuildFreezerMesh(Transform parent, Material body, Material ice)
        {
            CreatePrimitive(PrimitiveType.Cube, "Freezer_Barrel", parent, new Vector3(0f, 0f, 0.12f), new Vector3(0.16f, 0.15f, 0.5f), body);
            CreatePrimitive(PrimitiveType.Cube, "Freezer_Grip", parent, new Vector3(0f, -0.11f, -0.04f), new Vector3(0.07f, 0.15f, 0.08f), body);
            CreatePrimitive(PrimitiveType.Cube, "Freezer_Nozzle", parent, new Vector3(0f, 0.02f, 0.38f), new Vector3(0.2f, 0.2f, 0.06f), ice);
            CreatePrimitive(PrimitiveType.Sphere, "Freezer_Tank", parent, new Vector3(0f, 0.15f, 0.04f), Vector3.one * 0.2f, ice);
        }

        static void SetEnemyIceMaterial(GameObject enemy)
        {
            var so = new SerializedObject(enemy.GetComponent<EnemyController>());
            so.FindProperty("iceMaterial").objectReferenceValue = IceMaterial();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- Charm robot ----------

        [MenuItem("Tools/Coop Game/Add Charm Robot (prefab, weapon, enemy halo)")]
        public static void AddCharmRobot()
        {
            // Prefabs only; scenes don't change (in-scene enemies pick up the halo material from their prefab).
            EnsureFolder(PrefabsFolder);
            EnsureFolder(MaterialsFolder);
            BuildAllyRobotPrefab();
            EditPrefab(EnemyPrefabPath, SetEnemyCharmMaterial);
            EditPrefab(PlayerPrefabPath, AddRobotWeaponToPlayer);
            AssetDatabase.SaveAssets();
            Debug.Log("[CoopProjectSetup] Charm robot added: AllyRobot prefab (network prefab), robot weapon on the Player (slot 6), halo on enemies.");
        }

        [MenuItem("Tools/Coop Game/Update Charm Robot Weapon On Player")]
        public static void UpdateRobotWeapon()
        {
            // Player prefab only: refreshes the robot weapon's references (throw point, materials, robot prefab).
            EditPrefab(PlayerPrefabPath, AddRobotWeaponToPlayer);
            AssetDatabase.SaveAssets();
            Debug.Log("[CoopProjectSetup] Robot weapon on the Player prefab updated.");
        }

        static Material RobotMagicMaterial() => KeepEmissiveMaterial("AllyRobot_Magic", new Color(0.9f, 0.3f, 1f), 1.5f);

        static void SetEnemyCharmMaterial(GameObject enemy)
        {
            var controller = enemy.GetComponent<EnemyController>();
            var so = new SerializedObject(controller);
            so.FindProperty("charmMaterial").objectReferenceValue = RobotMagicMaterial();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The charm robot (~1.4 m): legs, body, a grenade launcher on its right side, a head that turns to its
        /// target with a glowing visor, an antenna the spell leaves from, and a health bar above. Only enemies can
        /// hurt it (6 HP). Registered as a network prefab (the server spawns it).
        /// </summary>
        static GameObject BuildAllyRobotPrefab()
        {
            var bodyColor = new Color(0.82f, 0.86f, 0.9f);
            var body = KeepMaterial("AllyRobot_Body", bodyColor);
            var dark = KeepMaterial("AllyRobot_Dark", new Color(0.22f, 0.23f, 0.27f));
            var magic = RobotMagicMaterial();

            var root = new GameObject("AllyRobot");
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.65f, 0f);
            collider.size = new Vector3(0.9f, 1.3f, 0.8f);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            VisualCube("Leg_L", visual, new Vector3(-0.22f, 0.18f, 0f), new Vector3(0.22f, 0.36f, 0.3f), dark);
            VisualCube("Leg_R", visual, new Vector3(0.22f, 0.18f, 0f), new Vector3(0.22f, 0.36f, 0.3f), dark);
            var torso = VisualCube("Body", visual, new Vector3(0f, 0.66f, 0f), new Vector3(0.85f, 0.6f, 0.65f), body);
            VisualCube("ChestLight", visual, new Vector3(0f, 0.7f, 0.34f), new Vector3(0.26f, 0.12f, 0.04f), magic);
            VisualCube("Launcher_Arm", visual, new Vector3(0.52f, 0.78f, 0.08f), new Vector3(0.18f, 0.18f, 0.5f), dark);
            var launcher = new GameObject("Launcher").transform;
            launcher.SetParent(visual, false);
            launcher.localPosition = new Vector3(0.52f, 0.85f, 0.42f);

            var head = new GameObject("Head").transform;
            head.SetParent(visual, false);
            head.localPosition = new Vector3(0f, 0.96f, 0f);
            VisualCube("Head_Box", head, new Vector3(0f, 0.19f, 0f), new Vector3(0.5f, 0.38f, 0.45f), body);
            VisualCube("Visor", head, new Vector3(0f, 0.22f, 0.23f), new Vector3(0.4f, 0.11f, 0.04f), magic);
            VisualCube("Antenna", head, new Vector3(0f, 0.55f, 0f), new Vector3(0.04f, 0.34f, 0.04f), dark);
            var orb = VisualCube("AntennaOrb", head, new Vector3(0f, 0.76f, 0f), Vector3.one * 0.13f, magic);

            var bar = new GameObject("HealthBar").transform;
            bar.SetParent(root.transform, false);
            bar.localPosition = new Vector3(0f, 2.05f, 0f);
            VisualCube("Back", bar, Vector3.zero, new Vector3(1.04f, 0.14f, 0.02f), KeepMaterial("HealthBar_Back", new Color(0.1f, 0.1f, 0.12f)));
            var fillPivot = new GameObject("FillPivot").transform;
            fillPivot.SetParent(bar, false);
            fillPivot.localPosition = new Vector3(0f, 0f, -0.015f);
            // Glowing, so it reads against the sky and from the unlit side.
            VisualCube("Fill", fillPivot, Vector3.zero, new Vector3(1f, 0.1f, 0.02f), EmissiveMaterial("HealthBar_Fill", new Color(0.3f, 0.95f, 0.4f), 1.2f));

            root.AddComponent<NetworkObject>();
            var damageable = root.AddComponent<Damageable>();
            ConfigureDamageable(damageable, 6, torso.GetComponent<Renderer>(), bodyColor, 10, 0.25f, boltDrop: 0);
            var damageableSo = new SerializedObject(damageable);
            damageableSo.FindProperty("vulnerableTo").intValue = (int)DamageTypes.Enemy;
            damageableSo.ApplyModifiedPropertiesWithoutUndo();

            var robot = root.AddComponent<AllyRobot>();
            var so = new SerializedObject(robot);
            so.FindProperty("head").objectReferenceValue = head;
            so.FindProperty("launcher").objectReferenceValue = launcher;
            so.FindProperty("spellOrigin").objectReferenceValue = orb.transform;
            so.FindProperty("healthBar").objectReferenceValue = bar;
            so.FindProperty("healthFill").objectReferenceValue = fillPivot;
            so.FindProperty("spellMaterial").objectReferenceValue = magic;
            so.FindProperty("grenadeMaterial").objectReferenceValue = KeepMaterial("Grenade", new Color(0.25f, 0.45f, 0.2f));
            so.FindProperty("grenadeBandMaterial").objectReferenceValue = KeepMaterial("Grenade_Band", new Color(1f, 0.8f, 0.15f));
            so.FindProperty("fireMaterial").objectReferenceValue = KeepEmissiveMaterial("Explosion_Fire", new Color(1f, 0.42f, 0.05f), 0.8f);
            so.FindProperty("coreMaterial").objectReferenceValue = KeepEmissiveMaterial("Explosion_Core", new Color(1f, 0.9f, 0.45f), 1.3f);
            so.FindProperty("smokeMaterial").objectReferenceValue = KeepMaterial("Rocket_Smoke", new Color(0.72f, 0.72f, 0.74f));
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = SaveNetworkPrefab(root, AllyRobotPrefabPath);
            RegisterNetworkPrefab(prefab);
            return prefab;
        }

        /// <summary>The robot weapon: a folded-up mini robot in the hand and one clipped to the back. Idempotent.</summary>
        static void AddRobotWeaponToPlayer(GameObject player)
        {
            var visual = player.transform.Find("Visual");
            var backMount = visual.Find("BackMount");
            var body = KeepMaterial("AllyRobot_Body", new Color(0.82f, 0.86f, 0.9f));
            var magic = RobotMagicMaterial();
            var robotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AllyRobotPrefabPath);
            if (robotPrefab == null) robotPrefab = BuildAllyRobotPrefab();

            var hand = visual.Find("RobotPivot");
            if (hand == null)
            {
                hand = new GameObject("RobotPivot").transform;
                hand.SetParent(visual, false);
                hand.localPosition = new Vector3(0.45f, 1.15f, 0.3f);
                BuildMiniRobotMesh(hand, body, magic);
            }

            var back = backMount.Find("RobotBack");
            if (back == null)
            {
                back = new GameObject("RobotBack").transform;
                back.SetParent(backMount, false);
                back.localPosition = new Vector3(0f, -0.15f, 0.08f);
                back.localRotation = Quaternion.Euler(0f, 180f, 0f);
                BuildMiniRobotMesh(back, body, magic);
            }

            var weapon = player.GetComponent<RobotWeapon>();
            if (weapon == null) weapon = player.AddComponent<RobotWeapon>();
            var so = new SerializedObject(weapon);
            so.FindProperty("handModel").objectReferenceValue = hand.gameObject;
            so.FindProperty("backModel").objectReferenceValue = back.gameObject;
            so.FindProperty("robotPrefab").objectReferenceValue = robotPrefab;
            so.FindProperty("throwPoint").objectReferenceValue = hand;
            so.FindProperty("previewMaterial").objectReferenceValue = magic;
            so.FindProperty("capsuleMaterial").objectReferenceValue = body;
            so.FindProperty("capsuleBandMaterial").objectReferenceValue = magic;
            so.ApplyModifiedPropertiesWithoutUndo();

            hand.gameObject.SetActive(false);
            back.gameObject.SetActive(false);
        }

        static void BuildMiniRobotMesh(Transform parent, Material body, Material magic)
        {
            CreatePrimitive(PrimitiveType.Cube, "MiniRobot_Body", parent, Vector3.zero, new Vector3(0.24f, 0.2f, 0.2f), body);
            CreatePrimitive(PrimitiveType.Cube, "MiniRobot_Head", parent, new Vector3(0f, 0.16f, 0f), new Vector3(0.16f, 0.12f, 0.14f), body);
            CreatePrimitive(PrimitiveType.Cube, "MiniRobot_Visor", parent, new Vector3(0f, 0.17f, 0.075f), new Vector3(0.12f, 0.04f, 0.02f), magic);
        }

        /// <summary>Like <see cref="KeepMaterial"/>, but a newly created one gets an emissive glow.</summary>
        static Material KeepEmissiveMaterial(string name, Color color, float intensity)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/{name}.mat");
            return existing != null ? existing : EmissiveMaterial(name, color, intensity);
        }

        static void BuildWhipHandle(Transform parent, Material grip, Material glow)
        {
            CreatePrimitive(PrimitiveType.Cube, "Whip_Grip", parent, new Vector3(0f, 0f, 0.17f), new Vector3(0.07f, 0.07f, 0.34f), grip);
            CreatePrimitive(PrimitiveType.Cube, "Whip_Pommel", parent, new Vector3(0f, 0f, -0.01f), new Vector3(0.1f, 0.1f, 0.05f), glow);
            CreatePrimitive(PrimitiveType.Cube, "Whip_Coil_A", parent, new Vector3(0f, 0f, 0.24f), new Vector3(0.09f, 0.09f, 0.03f), glow);
            CreatePrimitive(PrimitiveType.Cube, "Whip_Coil_B", parent, new Vector3(0f, 0f, 0.31f), new Vector3(0.09f, 0.09f, 0.03f), glow);
        }

        static Material PistolBulletMaterial() => KeepEmissiveMaterial("PistolBullet", new Color(1f, 0.7f, 0.2f), 1.4f);

        static void BuildPistolMesh(Transform parent, Material material)
        {
            CreatePrimitive(PrimitiveType.Cube, "Pistol_Grip", parent, new Vector3(0f, -0.07f, -0.02f), new Vector3(0.07f, 0.16f, 0.08f), material);
            CreatePrimitive(PrimitiveType.Cube, "Pistol_Barrel", parent, new Vector3(0f, 0.02f, 0.1f), new Vector3(0.07f, 0.08f, 0.28f), material);
        }

        static void BuildLoadoutHud(GameObject canvasGo)
        {
            EnsureGraphicRaycaster(canvasGo);
            var main = HudBox(canvasGo.transform, "Loadout_Main", new Vector2(30f, 30f), new Vector2(360f, 70f), out var mainLabel);
            var secondary = HudBox(canvasGo.transform, "Loadout_Secondary", new Vector2(410f, 30f), new Vector2(420f, 70f), out var secondaryLabel);

            var loadout = canvasGo.AddComponent<LoadoutHUD>();
            var so = new SerializedObject(loadout);
            so.FindProperty("mainBox").objectReferenceValue = main;
            so.FindProperty("mainLabel").objectReferenceValue = mainLabel;
            so.FindProperty("secondaryBox").objectReferenceValue = secondary;
            so.FindProperty("secondaryLabel").objectReferenceValue = secondaryLabel;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Image HudBox(Transform canvas, string name, Vector2 bottomLeftOffset, Vector2 size, out TextMeshProUGUI label)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = bottomLeftOffset;
            rect.sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.color = new Color(0.1f, 0.1f, 0.12f, 0.6f);
            image.raycastTarget = false;

            label = StretchedLabel(go.transform, "Label", 30f, TextAlignmentOptions.Left);
            label.margin = new Vector4(18f, 0f, 10f, 0f);
            return image;
        }

        static TextMeshProUGUI StretchedLabel(Transform parent, string name, float fontSize, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        static void BuildWeaponSelectPanel(GameObject canvasGo)
        {
            EnsureGraphicRaycaster(canvasGo);

            var panel = new GameObject("WeaponSelect", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1000f, 480f);
            panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

            var title = StretchedLabel(panel.transform, "Title", 40f, TextAlignmentOptions.Top);
            title.text = "Wybór broni  (TAB)";
            title.margin = new Vector4(0f, 20f, 0f, 0f);

            const int columns = 4, slotCount = 8;
            var buttons = new Button[slotCount];
            var labels = new TextMeshProUGUI[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                var slot = new GameObject($"Slot_{i + 1}", typeof(RectTransform));
                slot.transform.SetParent(panel.transform, false);
                var rect = (RectTransform)slot.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(220f, 160f);
                rect.anchoredPosition = new Vector2(-360f + (i % columns) * 240f, 40f - (i / columns) * 180f);

                var image = slot.AddComponent<Image>();
                buttons[i] = slot.AddComponent<Button>();
                buttons[i].targetGraphic = image;
                labels[i] = StretchedLabel(slot.transform, "Label", 34f, TextAlignmentOptions.Center);
            }

            var selector = canvasGo.AddComponent<WeaponSelectPanel>();
            var so = new SerializedObject(selector);
            so.FindProperty("panel").objectReferenceValue = panel;
            var buttonsProp = so.FindProperty("slotButtons");
            var labelsProp = so.FindProperty("slotLabels");
            buttonsProp.arraySize = slotCount;
            labelsProp.arraySize = slotCount;
            for (int i = 0; i < slotCount; i++)
            {
                buttonsProp.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
                labelsProp.GetArrayElementAtIndex(i).objectReferenceValue = labels[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            panel.SetActive(false);
        }

        static void EnsureGraphicRaycaster(GameObject canvasGo)
        {
            if (canvasGo.GetComponent<GraphicRaycaster>() == null) canvasGo.AddComponent<GraphicRaycaster>();
        }

        static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        static void BuildTargetingHud(GameObject canvasGo)
        {
            var center = new Vector2(0.5f, 0.5f);
            var crosshair = HudLabel(canvasGo.transform, "Crosshair", "+", center, Vector2.zero, TextAlignmentOptions.Center, Color.white);
            ((RectTransform)crosshair.transform).sizeDelta = new Vector2(80f, 80f);
            crosshair.fontSize = 64f;
            crosshair.enabled = false;

            var marker = HudLabel(canvasGo.transform, "LockMarker", "[  ]", center, Vector2.zero, TextAlignmentOptions.Center,
                new Color(1f, 0.35f, 0.25f));
            ((RectTransform)marker.transform).sizeDelta = new Vector2(160f, 100f);
            marker.fontSize = 72f;
            marker.enabled = false;

            var hud = canvasGo.AddComponent<TargetingHUD>();
            var so = new SerializedObject(hud);
            so.FindProperty("crosshair").objectReferenceValue = crosshair;
            so.FindProperty("lockMarker").objectReferenceValue = marker;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AddTargetable(GameObject go, float aimHeight, float radius)
        {
            var targetable = go.GetComponent<Targetable>();
            if (targetable == null) targetable = go.AddComponent<Targetable>();
            var so = new SerializedObject(targetable);
            so.FindProperty("aimHeight").floatValue = aimHeight;
            so.FindProperty("radius").floatValue = radius;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- Wrench beam (grab) ----------

        [MenuItem("Tools/Coop Game/Add Wrench Beam (grab) + test area")]
        static void AddWrenchBeamFromMenu() => AddWrenchBeam();

        /// <summary>
        /// Non-destructive: gives the existing Player prefab the wrench beam and adds the grab test area to the
        /// existing Game scene (only if it isn't there yet).
        /// </summary>
        public static void AddWrenchBeam()
        {
            EditPrefab(PlayerPrefabPath, AddComponentIfMissing<WrenchGrab>);

            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            if (scene.GetRootGameObjects().All(go => go.name != "TetherArea"))
            {
                BuildTetherArea(AssetDatabase.LoadAssetAtPath<GameObject>(CratePrefabPath));
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                RefreshInScenePlacedNetworkObjects(GameScenePath);
            }
            Finish("Wrench beam added: WrenchGrab on the Player prefab, grab test area in the Game scene.");
        }

        /// <summary>
        /// Grab test area in the north-east corner, laid out as a small co-op route:
        /// ramp → Cliff A → sliding bridge over a 9 m chasm (springs back when released: one player holds it,
        /// the other crosses) → Cliff B → drawbridge to lower onto the Goal → a block to carry under the high
        /// Reward deck and climb up. A grey block next to it looks similar but can't be grabbed.
        /// </summary>
        static void BuildTetherArea(GameObject cratePrefab)
        {
            var area = new GameObject("TetherArea").transform;
            var rock = Material("Cliff", new Color(0.5f, 0.47f, 0.42f));
            var platform = Material("Platform", new Color(0.85f, 0.75f, 0.35f));
            var plain = Material("PlainBlock", new Color(0.6f, 0.6f, 0.6f));
            var tether = Material("Tether", new Color(0.55f, 0.3f, 0.8f));
            var socket = Material("TetherSocket", new Color(1f, 0.35f, 1f));
            var beam = Material("TetherBeam", new Color(1f, 0.55f, 1f));

            const float rampRise = 3f, rampRun = 8f;
            Box("Tether_Ramp", area, new Vector3(34f, rampRise / 2f, 0f), new Vector3(4f, 0.4f, Mathf.Sqrt(rampRise * rampRise + rampRun * rampRun)),
                platform, Quaternion.Euler(-Mathf.Atan2(rampRise, rampRun) * Mathf.Rad2Deg, 0f, 0f));
            Box("Cliff_A", area, new Vector3(32f, 1.5f, 9f), new Vector3(8f, 3f, 10f), rock);
            Box("Cliff_B", area, new Vector3(32f, 1.5f, 26f), new Vector3(8f, 3f, 6f), rock);
            Box("Goal", area, new Vector3(32f, 1.5f, 36.25f), new Vector3(8f, 3f, 3.5f), rock);
            Box("RewardDeck", area, new Vector3(29.5f, 5f, 36.25f), new Vector3(3f, 4f, 3.5f), rock);
            Box("PlainBlock_NotGrabbable", area, new Vector3(35.2f, 3.75f, 36.2f), new Vector3(1.5f, 1.5f, 1.5f), plain);

            // Grabbable objects sit at the scene root: in-scene NetworkObjects are simplest without non-network parents.
            GraspableObject("Tether_Bridge", new Vector3(32f, 3.15f, 9.25f), Quaternion.identity,
                Graspable.Motion.Slide, Graspable.ReleaseBehaviour.ReturnToStart, speed: 3f,
                meshPosition: Vector3.zero, meshScale: new Vector3(4f, 0.3f, 9.5f), socketPosition: new Vector3(0f, 0.35f, 4.5f),
                tether, socket, beam, so => so.FindProperty("slideOffset").vector3Value = new Vector3(0f, 0f, 9f));

            // Pivot on the hinge at Cliff B's north edge. Modelled lying flat along +Z, placed rotated -90° so it starts
            // standing upright; the beam swings it through hinge angles 0° (upright) to 90° (flat across the gap).
            GraspableObject("Tether_Drawbridge", new Vector3(32f, 3f, 29f), Quaternion.Euler(-90f, 0f, 0f),
                Graspable.Motion.Hinge, Graspable.ReleaseBehaviour.Stay, speed: 2.5f,
                meshPosition: new Vector3(0f, 0.15f, 2.9f), meshScale: new Vector3(4f, 0.3f, 5.8f), socketPosition: new Vector3(0f, 0.35f, 5.6f),
                tether, socket, beam, so =>
                {
                    so.FindProperty("hingeAxis").vector3Value = Vector3.right;
                    so.FindProperty("minAngle").floatValue = 0f;
                    so.FindProperty("maxAngle").floatValue = 90f;
                });

            GraspableObject("Tether_Block", new Vector3(33.5f, 3.75f, 36.2f), Quaternion.identity,
                Graspable.Motion.Carry, Graspable.ReleaseBehaviour.Stay, speed: 3f,
                meshPosition: Vector3.zero, meshScale: new Vector3(1.5f, 1.5f, 1.5f), socketPosition: new Vector3(0f, 0.8f, 0f),
                tether, socket, beam, _ => { });

            if (cratePrefab != null)
            {
                PlaceInstance(cratePrefab, new Vector3(29f, 7.5f, 35.5f), Quaternion.identity);
                PlaceInstance(cratePrefab, new Vector3(30.2f, 7.5f, 36.8f), Quaternion.Euler(0f, 20f, 0f));
                PlaceInstance(cratePrefab, new Vector3(28.8f, 7.5f, 37.2f), Quaternion.Euler(0f, -10f, 0f));
            }
        }

        // ---------- Co-op mechanics ----------

        [MenuItem("Tools/Coop Game/Add Co-op Test Keys (F9 freeze grab, F10 long button window)")]
        public static void AddDevTools()
        {
            // Prefab only: leaves the open scenes alone.
            EditPrefab(PlayerPrefabPath, AddComponentIfMissing<PlayerDevTools>);
            AssetDatabase.SaveAssets();
            Debug.Log("[CoopProjectSetup] PlayerDevTools added to the Player prefab: F9 freezes a beam grab, F10 lengthens the button window.");
        }

        [MenuItem("Tools/Coop Game/Add Co-op Mechanics test area")]
        static void AddCoopMechanicsFromMenu() => AddCoopMechanics();

        /// <summary>Non-destructive: adds the co-op test area to the existing Game scene (only if it isn't there yet).</summary>
        public static void AddCoopMechanics()
        {
            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            if (scene.GetRootGameObjects().All(go => go.name != "CoopArea"))
            {
                BuildCoopArea(AssetDatabase.LoadAssetAtPath<GameObject>(CratePrefabPath), AssetDatabase.LoadAssetAtPath<GameObject>(HealthCratePrefabPath));
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                RefreshInScenePlacedNetworkObjects(GameScenePath);
            }
            Finish("Co-op mechanics test area added to the Game scene.");
        }

        /// <summary>
        /// Co-op test strip along the south edge (z ≈ -34), west to east:
        /// 1. Bridge over a 6 m gap that slides out only when two beam buttons are pressed at the same moment.
        /// 2. Gate into a yard that rises only while two spring-loaded levers are pulled all the way together.
        /// 3. A heavy block with two handles (one player can't budge it) to push under a 4 m high deck.
        /// 4. A barrier clamped by a lock: two players pull its halves apart to reach the alcove behind it.
        /// </summary>
        static void BuildCoopArea(GameObject cratePrefab, GameObject healthCratePrefab)
        {
            var area = new GameObject("CoopArea").transform;
            var wall = Material("Wall", new Color(0.55f, 0.58f, 0.68f));
            var rock = Material("Cliff", new Color(0.5f, 0.47f, 0.42f));
            var platform = Material("Platform", new Color(0.85f, 0.75f, 0.35f));
            var socket = Material("TetherSocket", new Color(1f, 0.35f, 1f));
            var beam = Material("TetherBeam", new Color(1f, 0.55f, 1f));
            var lamp = Material("Lamp", new Color(0.25f, 0.25f, 0.3f));
            var buttonMat = Material("Button", new Color(1f, 0.55f, 0.15f));
            var leverMat = Material("Lever", new Color(0.8f, 0.8f, 0.85f));
            var mechanism = Material("Mechanism", new Color(0.35f, 0.6f, 0.8f));
            var heavyMat = Material("Heavy", new Color(0.3f, 0.3f, 0.35f));
            var barrierMat = Material("Barrier", new Color(0.45f, 0.35f, 0.3f));
            var lockMat = Material("Lock", new Color(1f, 0.5f, 0.1f));

            // 1. Button bridge ------------------------------------------------------------
            Box("Coop_RampA", area, new Vector3(-33f, 1.5f, -26f), new Vector3(4f, 0.4f, Mathf.Sqrt(8f * 8f + 3f * 3f)), platform,
                Quaternion.Euler(Mathf.Atan2(3f, 8f) * Mathf.Rad2Deg, 0f, 0f));
            Box("Coop_PlatA", area, new Vector3(-33f, 1.5f, -34f), new Vector3(6f, 3f, 8f), rock);
            Box("Coop_PlatB", area, new Vector3(-21f, 1.5f, -34f), new Vector3(6f, 3f, 8f), rock);
            Box("Coop_ButtonPost_1", area, new Vector3(-35.6f, 3.7f, -37.2f), new Vector3(0.6f, 1.4f, 0.6f), wall);
            Box("Coop_ButtonPost_2", area, new Vector3(-35.6f, 3.7f, -30.8f), new Vector3(0.6f, 1.4f, 0.6f), wall);
            var button1 = BeamButton("Coop_Button_1", new Vector3(-35.27f, 4f, -37.2f), Vector3.right, buttonMat, lamp, beam);
            var button2 = BeamButton("Coop_Button_2", new Vector3(-35.27f, 4f, -30.8f), Vector3.right, buttonMat, lamp, beam);

            // Bridge lies retracted on Plat A and slides 6 m east across the gap.
            var bridge = MechanismObject("Coop_Bridge", new Vector3(-33f, 3.15f, -34f), new Vector3(6.4f, 0.3f, 3f), mechanism,
                openOffset: new Vector3(6f, 0f, 0f), openTime: 2.5f);
            LinkMechanism(bridge, button1, button2);

            // 2. Lever gate ---------------------------------------------------------------
            Box("Coop_Yard_Front_L", area, new Vector3(-11.5f, 1.75f, -34f), new Vector3(5f, 3.5f, 0.6f), wall);
            Box("Coop_Yard_Front_R", area, new Vector3(-3.5f, 1.75f, -34f), new Vector3(5f, 3.5f, 0.6f), wall);
            Box("Coop_Yard_Side_W", area, new Vector3(-14f, 1.75f, -36.75f), new Vector3(0.6f, 3.5f, 5.5f), wall);
            Box("Coop_Yard_Side_E", area, new Vector3(-1f, 1.75f, -36.75f), new Vector3(0.6f, 3.5f, 5.5f), wall);
            var gate = MechanismObject("Coop_Gate", new Vector3(-7.5f, 1.75f, -34f), new Vector3(3f, 3.5f, 0.5f), mechanism,
                openOffset: new Vector3(0f, 3.6f, 0f), openTime: 2f);
            var lever1 = BeamLever("Coop_Lever_1", area, new Vector3(-13f, 0f, -30f), leverMat, socket, lamp, beam, wall);
            var lever2 = BeamLever("Coop_Lever_2", area, new Vector3(-2f, 0f, -30f), leverMat, socket, lamp, beam, wall);
            LinkMechanism(gate, lever1, lever2);

            // 3. Heavy block --------------------------------------------------------------
            Box("Coop_HeavyDeck", area, new Vector3(10f, 2f, -36f), new Vector3(4f, 4f, 4f), rock);
            HeavyBlock("Coop_HeavyBlock", new Vector3(4f, 1f, -32f), heavyMat, socket, beam);

            // 4. Tearable barrier ---------------------------------------------------------
            Box("Coop_Alcove_Front_L", area, new Vector3(14.5f, 1.5f, -34f), new Vector3(3f, 3f, 0.6f), wall);
            Box("Coop_Alcove_Front_R", area, new Vector3(21.5f, 1.5f, -34f), new Vector3(3f, 3f, 0.6f), wall);
            Box("Coop_Alcove_Side_W", area, new Vector3(13f, 1.5f, -36.75f), new Vector3(0.6f, 3f, 5.5f), wall);
            Box("Coop_Alcove_Side_E", area, new Vector3(23f, 1.5f, -36.75f), new Vector3(0.6f, 3f, 5.5f), wall);
            TearBarrier("Coop_Barrier", new Vector3(18f, 0f, -34f), barrierMat, lockMat, socket, beam);

            // Rewards behind each obstacle.
            if (cratePrefab != null)
            {
                PlaceInstance(cratePrefab, new Vector3(-21f, 3.5f, -35f), Quaternion.identity);
                PlaceInstance(cratePrefab, new Vector3(9f, 4.5f, -36.5f), Quaternion.identity);
                PlaceInstance(cratePrefab, new Vector3(11f, 4.5f, -35.5f), Quaternion.Euler(0f, 25f, 0f));
                PlaceInstance(cratePrefab, new Vector3(17f, 0.5f, -37.5f), Quaternion.identity);
                PlaceInstance(cratePrefab, new Vector3(19f, 0.5f, -37.5f), Quaternion.Euler(0f, -15f, 0f));
            }
            if (healthCratePrefab != null)
            {
                PlaceInstance(healthCratePrefab, new Vector3(-10f, 0.5f, -37.5f), Quaternion.identity);
                PlaceInstance(healthCratePrefab, new Vector3(-5f, 0.5f, -37.5f), Quaternion.identity);
            }
        }

        // ---------- Jump pads ----------

        [MenuItem("Tools/Coop Game/Add Jump Pads test area")]
        static void AddJumpPadsFromMenu() => AddJumpPads();

        /// <summary>Non-destructive: adds the jump pad test area to the existing Game scene (only if it isn't there yet).</summary>
        public static void AddJumpPads()
        {
            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            if (scene.GetRootGameObjects().All(go => go.name != "JumpPadArea"))
            {
                BuildJumpPadArea(AssetDatabase.LoadAssetAtPath<GameObject>(CratePrefabPath), AssetDatabase.LoadAssetAtPath<GameObject>(HealthCratePrefabPath));
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                RefreshInScenePlacedNetworkObjects(GameScenePath);
            }
            Finish("Jump pad test area added to the Game scene.");
        }

        /// <summary>
        /// Jump pad strip along the north edge (z ≈ 31), west to east, each pad under a 7 m perch that only the pad
        /// gets you onto (rewards on top):
        /// 1. Automatic pad: step on and it fires, then recharges by itself.
        /// 2. Crank pad: wind it up with the wrench beam first (one crank); one launch per winding.
        /// 3. Co-op crank pad: two cranks that have to be turned at the same time; one launch per winding.
        /// </summary>
        static void BuildJumpPadArea(GameObject cratePrefab, GameObject healthCratePrefab)
        {
            var area = new GameObject("JumpPadArea").transform;
            var materials = new JumpPadMaterials
            {
                Base = KeepMaterial("PadBase", new Color(0.22f, 0.22f, 0.26f)),
                Plate = KeepMaterial("PadPlate", new Color(0.35f, 0.12f, 0.1f)),
                Post = KeepMaterial("Wall", new Color(0.55f, 0.58f, 0.68f)),
                Crank = KeepMaterial("Lever", new Color(0.8f, 0.8f, 0.85f)),
                Socket = KeepMaterial("TetherSocket", new Color(1f, 0.35f, 1f)),
                Beam = KeepMaterial("TetherBeam", new Color(1f, 0.55f, 1f)),
                GaugeBack = KeepMaterial("PadGaugeBack", new Color(0.12f, 0.12f, 0.14f)),
                GaugeFill = KeepMaterial("PadGaugeFill", new Color(1f, 0.75f, 0.15f)),
            };
            var rock = KeepMaterial("Cliff", new Color(0.5f, 0.47f, 0.42f));

            const float padZ = 31f;
            float[] stationX = { -16f, -6f, 10f }; // clear of the pool (west) and the ramp top (x -3..3, z ≤ 28)
            for (int i = 0; i < stationX.Length; i++)
                Box($"JumpPad_Perch_{i + 1}", area, new Vector3(stationX[i], 3.5f, 37.25f), new Vector3(6f, 7f, 4.5f), rock);

            JumpPadObject("JumpPad_Auto", new Vector3(stationX[0], 0f, padZ), new Vector3[0], materials);
            JumpPadObject("JumpPad_Crank", new Vector3(stationX[1], 0f, padZ), new[] { new Vector3(2.4f, 0f, 0f) }, materials);
            JumpPadObject("JumpPad_Coop", new Vector3(stationX[2], 0f, padZ), new[] { new Vector3(-2.4f, 0f, 0f), new Vector3(2.4f, 0f, 0f) }, materials);

            // Rewards on the perches.
            if (cratePrefab != null)
                foreach (float x in stationX)
                {
                    PlaceInstance(cratePrefab, new Vector3(x - 1.3f, 7.5f, 37.8f), Quaternion.identity);
                    PlaceInstance(cratePrefab, new Vector3(x + 1.3f, 7.5f, 37.8f), Quaternion.Euler(0f, 20f, 0f));
                }
            if (healthCratePrefab != null)
                PlaceInstance(healthCratePrefab, new Vector3(stationX[2], 7.5f, 38.2f), Quaternion.identity);
        }

        struct JumpPadMaterials
        {
            public Material Base, Plate, Post, Crank, Socket, Beam, GaugeBack, GaugeFill;
        }

        /// <summary>
        /// 3×3 m launch pad. Each entry in <paramref name="crankPositions"/> (local) adds a post with a beam crank on its
        /// south face (towards players coming from the arena) and a charge gauge under it; none = automatic pad.
        /// </summary>
        static JumpPad JumpPadObject(string name, Vector3 position, Vector3[] crankPositions, JumpPadMaterials m)
        {
            var root = NetworkedRoot(name, position, Quaternion.identity, syncTransform: false);
            CreatePrimitive(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(3f, 0.3f, 3f), m.Base);
            var plate = VisualCube("Plate", root.transform, new Vector3(0f, 0.28f, 0f), new Vector3(2.5f, 0.06f, 2.5f), m.Plate);

            var cranks = new Graspable[crankPositions.Length];
            var arms = new Transform[crankPositions.Length];
            var gauges = new Transform[crankPositions.Length];
            for (int i = 0; i < crankPositions.Length; i++)
            {
                Vector3 p = crankPositions[i];
                CreatePrimitive(PrimitiveType.Cube, $"CrankPost_{i + 1}", root.transform, p + new Vector3(0f, 0.75f, 0f), new Vector3(0.4f, 1.5f, 0.4f), m.Post);

                // Local +Z points out of the post's south face.
                var crank = new GameObject($"Crank_{i + 1}").transform;
                crank.SetParent(root.transform, false);
                crank.localPosition = p + new Vector3(0f, 1.2f, -0.2f);
                crank.localRotation = Quaternion.LookRotation(Vector3.back);
                var hub = VisualCube("Hub", crank, new Vector3(0f, 0f, 0.07f), new Vector3(0.28f, 0.28f, 0.14f), m.Socket);
                var arm = new GameObject("Arm").transform;
                arm.SetParent(crank, false);
                arm.localPosition = new Vector3(0f, 0f, 0.16f);
                VisualCube("Bar", arm, new Vector3(0f, 0.2f, 0f), new Vector3(0.08f, 0.45f, 0.06f), m.Crank);
                VisualCube("Knob", arm, new Vector3(0f, 0.4f, 0.08f), new Vector3(0.12f, 0.12f, 0.14f), m.Crank);
                cranks[i] = SetupGraspable(crank.gameObject, hub.transform, Graspable.Motion.Fixed, Graspable.ReleaseBehaviour.Stay, 0f, m.Beam);
                arms[i] = arm;

                // Charge gauge under the crank, filled from the bottom.
                VisualCube("GaugeBack", crank, new Vector3(0f, -0.65f, 0.01f), new Vector3(0.16f, 0.82f, 0.02f), m.GaugeBack);
                var gauge = new GameObject("Gauge").transform;
                gauge.SetParent(crank, false);
                gauge.localPosition = new Vector3(0f, -1.05f, 0.03f);
                VisualCube("Fill", gauge, new Vector3(0f, 0.4f, 0f), new Vector3(0.11f, 0.8f, 0.02f), m.GaugeFill);
                gauges[i] = gauge;
            }

            var pad = root.AddComponent<JumpPad>();
            var so = new SerializedObject(pad);
            so.FindProperty("plate").objectReferenceValue = plate.transform;
            SetObjectArray(so.FindProperty("lamps"), new Object[] { plate.GetComponent<Renderer>() });
            SetObjectArray(so.FindProperty("cranks"), cranks);
            SetObjectArray(so.FindProperty("crankArms"), arms);
            SetObjectArray(so.FindProperty("gauges"), gauges);
            if (crankPositions.Length > 1) so.FindProperty("windTime").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return pad;
        }

        static void SetObjectArray(SerializedProperty array, Object[] values)
        {
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /// <summary>Loads a material without touching its colour (so tweaks made in the editor survive), creating it if missing.</summary>
        static Material KeepMaterial(string name, Color color)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/{name}.mat");
            return existing != null ? existing : Material(name, color);
        }

        /// <summary>Scene-root networked object (in-scene NetworkObject), optionally replicating its transform from the server.</summary>
        static GameObject NetworkedRoot(string name, Vector3 position, Quaternion rotation, bool syncTransform)
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(position, rotation);
            root.AddComponent<NetworkObject>();
            if (syncTransform)
            {
                var netTransform = root.AddComponent<NetworkTransform>();
                netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
                netTransform.SyncScaleX = false;
                netTransform.SyncScaleY = false;
                netTransform.SyncScaleZ = false;
            }
            return root;
        }

        /// <summary>Configures a Graspable + Targetable on <paramref name="go"/> with <paramref name="socket"/> as the beam/aim point.</summary>
        static Graspable SetupGraspable(GameObject go, Transform socket, Graspable.Motion motion, Graspable.ReleaseBehaviour release,
            float speed, Material beam, System.Action<SerializedObject> configure = null)
        {
            var graspable = go.AddComponent<Graspable>();
            var so = new SerializedObject(graspable);
            so.FindProperty("motion").enumValueIndex = (int)motion;
            so.FindProperty("onRelease").enumValueIndex = (int)release;
            so.FindProperty("socket").objectReferenceValue = socket;
            so.FindProperty("moveSpeed").floatValue = speed;
            so.FindProperty("returnSpeed").floatValue = speed;
            so.FindProperty("beamMaterial").objectReferenceValue = beam;
            configure?.Invoke(so);
            so.ApplyModifiedPropertiesWithoutUndo();

            var targetable = go.AddComponent<Targetable>();
            var targetSo = new SerializedObject(targetable);
            targetSo.FindProperty("aimOverride").objectReferenceValue = socket;
            targetSo.FindProperty("radius").floatValue = 0.6f;
            targetSo.ApplyModifiedPropertiesWithoutUndo();
            return graspable;
        }

        /// <summary>Visual-only cube (no collider).</summary>
        static GameObject VisualCube(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var cube = CreatePrimitive(PrimitiveType.Cube, name, parent, localPosition, localScale, material);
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            return cube;
        }

        /// <summary>Wall button facing <paramref name="facing"/>: latch the beam on to press it (counts only for a moment).</summary>
        static BeamSwitch BeamButton(string name, Vector3 position, Vector3 facing, Material capMaterial, Material lampMaterial, Material beam)
        {
            var root = NetworkedRoot(name, position, Quaternion.LookRotation(facing), syncTransform: false);
            var cap = VisualCube("Cap", root.transform, new Vector3(0f, 0f, 0.06f), new Vector3(0.4f, 0.4f, 0.12f), capMaterial);
            var lampCube = VisualCube("Lamp", root.transform, new Vector3(0f, 0.38f, 0.02f), new Vector3(0.18f, 0.18f, 0.1f), lampMaterial);

            SetupGraspable(root, cap.transform, Graspable.Motion.Fixed, Graspable.ReleaseBehaviour.Stay, 0f, beam);
            var beamSwitch = root.AddComponent<BeamSwitch>();
            var so = new SerializedObject(beamSwitch);
            so.FindProperty("kind").enumValueIndex = (int)BeamSwitch.Kind.Button;
            so.FindProperty("lamp").objectReferenceValue = lampCube.GetComponent<Renderer>();
            so.FindProperty("buttonCap").objectReferenceValue = cap.transform;
            so.FindProperty("pressedOffset").vector3Value = new Vector3(0f, 0f, -0.1f);
            so.ApplyModifiedPropertiesWithoutUndo();
            return beamSwitch;
        }

        /// <summary>
        /// Floor lever facing +Z (players stand north of it): starts tipped away, pulling it towards you (S) swings it
        /// to the other end; it springs back when let go.
        /// </summary>
        static BeamSwitch BeamLever(string name, Transform area, Vector3 position, Material leverMaterial, Material socketMaterial,
            Material lampMaterial, Material beam, Material baseMaterial)
        {
            Box(name + "_Base", area, position + new Vector3(0f, 0.2f, 0f), new Vector3(0.7f, 0.4f, 0.7f), baseMaterial);
            var lampCube = Box(name + "_Lamp", area, position + new Vector3(0f, 0.45f, 0.42f), new Vector3(0.25f, 0.1f, 0.12f), lampMaterial);
            Object.DestroyImmediate(lampCube.GetComponent<Collider>());
            lampCube.isStatic = false;

            var root = NetworkedRoot(name, position + new Vector3(0f, 0.4f, 0f), Quaternion.Euler(-35f, 0f, 0f), syncTransform: true);
            VisualCube("Stick", root.transform, new Vector3(0f, 0.6f, 0f), new Vector3(0.12f, 1.2f, 0.12f), leverMaterial);
            var knob = VisualCube("Knob", root.transform, new Vector3(0f, 1.25f, 0f), Vector3.one * 0.3f, socketMaterial);

            SetupGraspable(root, knob.transform, Graspable.Motion.Hinge, Graspable.ReleaseBehaviour.ReturnToStart, 2f, beam, so =>
            {
                so.FindProperty("hingeAxis").vector3Value = Vector3.right;
                so.FindProperty("minAngle").floatValue = 0f;
                so.FindProperty("maxAngle").floatValue = 70f;
                so.FindProperty("returnSpeed").floatValue = 2.5f;
            });
            var beamSwitch = root.AddComponent<BeamSwitch>();
            var switchSo = new SerializedObject(beamSwitch);
            switchSo.FindProperty("kind").enumValueIndex = (int)BeamSwitch.Kind.Lever;
            switchSo.FindProperty("lamp").objectReferenceValue = lampCube.GetComponent<Renderer>();
            switchSo.ApplyModifiedPropertiesWithoutUndo();
            return beamSwitch;
        }

        /// <summary>A bridge/gate a <see cref="CoopMechanism"/> opens: networked mover with a solid mesh.</summary>
        static MechanismMover MechanismObject(string name, Vector3 position, Vector3 size, Material material, Vector3 openOffset, float openTime)
        {
            var root = NetworkedRoot(name, position, Quaternion.identity, syncTransform: true);
            CreatePrimitive(PrimitiveType.Cube, "Mesh", root.transform, Vector3.zero, size, material);
            var mover = root.AddComponent<MechanismMover>();
            var so = new SerializedObject(mover);
            so.FindProperty("openOffset").vector3Value = openOffset;
            so.FindProperty("openTime").floatValue = openTime;
            so.ApplyModifiedPropertiesWithoutUndo();
            return mover;
        }

        static void LinkMechanism(MechanismMover mover, params BeamSwitch[] switches)
        {
            var mechanism = mover.gameObject.AddComponent<CoopMechanism>();
            var so = new SerializedObject(mechanism);
            var switchesProp = so.FindProperty("switches");
            switchesProp.arraySize = switches.Length;
            for (int i = 0; i < switches.Length; i++) switchesProp.GetArrayElementAtIndex(i).objectReferenceValue = switches[i];
            var moversProp = so.FindProperty("movers");
            moversProp.arraySize = 1;
            moversProp.GetArrayElementAtIndex(0).objectReferenceValue = mover;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Fixed beam handle on a co-op object (child of it, sharing its NetworkObject).</summary>
        static Graspable CoopHandle(string name, Transform parent, Vector3 localPosition, Material socketMaterial, Material beam)
        {
            var handle = new GameObject(name).transform;
            handle.SetParent(parent, false);
            handle.localPosition = localPosition;
            VisualCube("Socket", handle, Vector3.zero, Vector3.one * 0.35f, socketMaterial);
            return SetupGraspable(handle.gameObject, handle, Graspable.Motion.Fixed, Graspable.ReleaseBehaviour.Stay, 0f, beam);
        }

        static void HeavyBlock(string name, Vector3 position, Material material, Material socketMaterial, Material beam)
        {
            var root = NetworkedRoot(name, position, Quaternion.identity, syncTransform: true);
            var mesh = CreatePrimitive(PrimitiveType.Cube, "Mesh", root.transform, Vector3.zero, Vector3.one * 2f, material);
            var west = CoopHandle("Handle_W", root.transform, new Vector3(-1.15f, 0.3f, 0f), socketMaterial, beam);
            var east = CoopHandle("Handle_E", root.transform, new Vector3(1.15f, 0.3f, 0f), socketMaterial, beam);

            var heavy = root.AddComponent<HeavyObject>();
            var so = new SerializedObject(heavy);
            var handles = so.FindProperty("handles");
            handles.arraySize = 2;
            handles.GetArrayElementAtIndex(0).objectReferenceValue = west;
            handles.GetArrayElementAtIndex(1).objectReferenceValue = east;
            so.FindProperty("shakeVisual").objectReferenceValue = mesh.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void TearBarrier(string name, Vector3 position, Material halfMaterial, Material lockMaterial, Material socketMaterial, Material beam)
        {
            var root = NetworkedRoot(name, position, Quaternion.identity, syncTransform: false);
            Transform Half(string halfName, float x)
            {
                var half = new GameObject(halfName).transform;
                half.SetParent(root.transform, false);
                half.localPosition = new Vector3(x, 1.5f, 0f);
                CreatePrimitive(PrimitiveType.Cube, "Mesh", half, Vector3.zero, new Vector3(2f, 3f, 0.5f), halfMaterial);
                return half;
            }
            var left = Half("LeftHalf", -1f);
            var right = Half("RightHalf", 1f);
            var lockCube = VisualCube("Lock", root.transform, new Vector3(0f, 1.5f, 0f), new Vector3(0.5f, 0.8f, 0.7f), lockMaterial);
            var leftHandle = CoopHandle("Handle_L", left, new Vector3(-0.4f, 0f, 0.35f), socketMaterial, beam);
            var rightHandle = CoopHandle("Handle_R", right, new Vector3(0.4f, 0f, 0.35f), socketMaterial, beam);

            var barrier = root.AddComponent<TearableBarrier>();
            var so = new SerializedObject(barrier);
            so.FindProperty("leftHandle").objectReferenceValue = leftHandle;
            so.FindProperty("rightHandle").objectReferenceValue = rightHandle;
            so.FindProperty("leftHalf").objectReferenceValue = left;
            so.FindProperty("rightHalf").objectReferenceValue = right;
            so.FindProperty("lockVisual").objectReferenceValue = lockCube.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject GraspableObject(string name, Vector3 position, Quaternion rotation, Graspable.Motion motion,
            Graspable.ReleaseBehaviour release, float speed, Vector3 meshPosition, Vector3 meshScale, Vector3 socketPosition,
            Material meshMaterial, Material socketMaterial, Material beamMaterial, System.Action<SerializedObject> configure)
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(position, rotation);
            CreatePrimitive(PrimitiveType.Cube, "Mesh", root.transform, meshPosition, meshScale, meshMaterial);

            // The magenta socket marks "the wrench can grab this"; the beam attaches here.
            var socket = CreatePrimitive(PrimitiveType.Cube, "Socket", root.transform, socketPosition, Vector3.one * 0.35f, socketMaterial);
            Object.DestroyImmediate(socket.GetComponent<Collider>());

            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;

            var graspable = root.AddComponent<Graspable>();
            var so = new SerializedObject(graspable);
            so.FindProperty("motion").enumValueIndex = (int)motion;
            so.FindProperty("onRelease").enumValueIndex = (int)release;
            so.FindProperty("socket").objectReferenceValue = socket.transform;
            so.FindProperty("moveSpeed").floatValue = speed;
            so.FindProperty("returnSpeed").floatValue = speed;
            so.FindProperty("beamMaterial").objectReferenceValue = beamMaterial;
            configure(so);
            so.ApplyModifiedPropertiesWithoutUndo();

            var targetable = root.AddComponent<Targetable>();
            var targetSo = new SerializedObject(targetable);
            targetSo.FindProperty("aimOverride").objectReferenceValue = socket.transform;
            targetSo.FindProperty("radius").floatValue = 0.8f;
            targetSo.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        static void AddComponentIfMissing<T>(GameObject go) where T : Component
        {
            if (go.GetComponent<T>() == null) go.AddComponent<T>();
        }

        static void EditPrefab(string path, System.Action<GameObject> edit)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            edit(contents);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        [MenuItem("Tools/Coop Game/Add Targeting + Wrench Throw")]
        static void AddTargetingFromMenu() => AddTargeting();

        /// <summary>
        /// Non-destructive: adds aiming + wrench throw to the existing Player prefab, makes enemies and crates
        /// targetable, and puts the crosshair/lock marker on the existing Game scene HUD.
        /// </summary>
        public static void AddTargeting()
        {
            EditPrefab(PlayerPrefabPath, player =>
            {
                AddComponentIfMissing<PlayerTargeting>(player);
                AddComponentIfMissing<WrenchThrow>(player);
            });
            EditPrefab(EnemyPrefabPath, enemy => AddTargetable(enemy, 0.8f, 0.7f));
            EditPrefab(CratePrefabPath, crate => AddTargetable(crate, 0f, 0.6f));
            EditPrefab(HealthCratePrefabPath, crate => AddTargetable(crate, 0f, 0.6f));

            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            var hud = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "HUD");
            if (hud != null && hud.GetComponent<TargetingHUD>() == null)
            {
                BuildTargetingHud(hud);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            Finish("Targeting added: Player aims and throws, enemies/crates are targetable, HUD has a crosshair.");
        }

        static TextMeshProUGUI HudLabel(Transform canvas, string name, string text, Vector2 corner, Vector2 offset,
            TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = corner;
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(500f, 80f);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 52f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>
        /// In-scene NetworkObjects derive a unique GlobalObjectIdHash from the saved scene. Objects created
        /// before the scene had a file share their prefab's hash, so re-run NetworkObject.OnValidate and save.
        /// </summary>
        static void RefreshInScenePlacedNetworkObjects(string scenePath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var onValidate = typeof(NetworkObject).GetMethod("OnValidate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            foreach (var root in scene.GetRootGameObjects())
            foreach (var networkObject in root.GetComponentsInChildren<NetworkObject>(true))
                onValidate.Invoke(networkObject, null);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void PlaceInstance(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
            instance.transform.SetPositionAndRotation(position, rotation);
        }

        static void BuildCamera()
        {
            var mainCamera = Camera.main;
            mainCamera.transform.SetPositionAndRotation(new Vector3(0f, 4f, -8f), Quaternion.Euler(15f, 0f, 0f));
            mainCamera.nearClipPlane = 0.1f;
            mainCamera.gameObject.AddComponent<CinemachineBrain>();

            var rig = new GameObject("CameraRig");
            rig.AddComponent<ThirdPersonCameraRig>();
            rig.transform.position = new Vector3(0f, 1.5f, 0f);

            var vcamGo = new GameObject("PlayerCamera");
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Target.TrackingTarget = rig.transform;
            var lens = vcam.Lens;
            lens.FieldOfView = 60f;
            lens.NearClipPlane = 0.1f;
            vcam.Lens = lens;

            var follow = vcamGo.AddComponent<CinemachineThirdPersonFollow>();
            follow.ShoulderOffset = new Vector3(0f, 0.3f, 0f);
            follow.VerticalArmLength = 0.4f;
            follow.CameraSide = 1f; // shoulder offset x = metres to the right (the rig blends it in while aiming)
            follow.CameraDistance = 6f;
            follow.Damping = new Vector3(0.1f, 0.3f, 0.2f);
            var obstacles = follow.AvoidObstacles;
            obstacles.Enabled = true;
            obstacles.CollisionFilter = ~(1 << 2); // not "Ignore Raycast" (e.g. zipline cables right over the player)
            obstacles.IgnoreTag = "Player";
            obstacles.CameraRadius = 0.25f;
            obstacles.DampingIntoCollision = 0.05f;
            obstacles.DampingFromCollision = 0.4f;
            follow.AvoidObstacles = obstacles;
        }

        static void BuildLevel()
        {
            var level = new GameObject("Level").transform;
            var ground = Material("Ground", new Color(0.42f, 0.45f, 0.4f));
            var platform = Material("Platform", new Color(0.85f, 0.75f, 0.35f));
            var high = Material("Platform_High", new Color(0.85f, 0.4f, 0.35f));
            var wall = Material("Wall", new Color(0.55f, 0.58f, 0.68f));

            Box("Ground", level, new Vector3(0f, -0.5f, 0f), new Vector3(80f, 1f, 80f), ground);

            // Jump test: steps of rising height (jump ~1.6, double ~2.9, high jump ~3.2).
            Box("Step_1m", level, new Vector3(-8f, 0.5f, 8f), new Vector3(3f, 1f, 3f), platform);
            Box("Step_2m", level, new Vector3(-12f, 1f, 8f), new Vector3(3f, 2f, 3f), platform);
            Box("Step_3m", level, new Vector3(-16f, 1.5f, 8f), new Vector3(3f, 3f, 3f), high);
            Box("Step_4.5m", level, new Vector3(-20f, 2.25f, 8f), new Vector3(3f, 4.5f, 3f), high);

            // Gap test: normal jump clears 4 m, the 6 m gap needs a long jump (crouch while running + jump).
            Box("Ledge_A", level, new Vector3(10f, 0.5f, 0f), new Vector3(4f, 1f, 6f), platform);
            Box("Ledge_B_gap4", level, new Vector3(18f, 0.5f, 0f), new Vector3(4f, 1f, 6f), platform);
            Box("Ledge_C_gap6", level, new Vector3(28f, 0.5f, 0f), new Vector3(4f, 1f, 6f), high);

            // Ramp and stairs.
            Box("Ramp", level, new Vector3(0f, 1f, 18f), new Vector3(4f, 0.4f, 9f), platform, Quaternion.Euler(-14f, 0f, 0f));
            Box("Ramp_Top", level, new Vector3(0f, 1.9f, 25f), new Vector3(6f, 0.4f, 6f), platform);
            for (int i = 0; i < 8; i++)
                Box($"Stair_{i}", level, new Vector3(8f, 0f, 14f + i * 0.6f), new Vector3(3f, 0.3f * (i + 1), 0.6f), platform,
                    keepBottom: true);

            // Pillars and walls to test camera collision.
            for (int i = 0; i < 5; i++)
                Box($"Pillar_{i}", level, new Vector3(-10f + i * 5f, 2.5f, -12f), new Vector3(1.2f, 5f, 1.2f), wall);
            Box("Wall_Long", level, new Vector3(-22f, 2f, -4f), new Vector3(1f, 4f, 20f), wall);
            Box("Tunnel_Roof", level, new Vector3(-16f, 2.6f, -20f), new Vector3(6f, 0.4f, 10f), wall);
            Box("Tunnel_Wall_L", level, new Vector3(-19f, 1.2f, -20f), new Vector3(0.4f, 2.4f, 10f), wall);
            Box("Tunnel_Wall_R", level, new Vector3(-13f, 1.2f, -20f), new Vector3(0.4f, 2.4f, 10f), wall);

            // Floating platforms for double-jump hopping.
            Box("Float_1", level, new Vector3(14f, 2.2f, 14f), new Vector3(2.5f, 0.4f, 2.5f), platform);
            Box("Float_2", level, new Vector3(18f, 3.8f, 18f), new Vector3(2.5f, 0.4f, 2.5f), high);
            Box("Float_3", level, new Vector3(22f, 5.4f, 22f), new Vector3(2.5f, 0.4f, 2.5f), high);

            // Invisible-ish boundary so nobody walks off the world by accident.
            var border = Material("Border", new Color(0.3f, 0.32f, 0.36f));
            Box("Border_N", level, new Vector3(0f, 1f, 40f), new Vector3(80f, 2f, 1f), border);
            Box("Border_S", level, new Vector3(0f, 1f, -40f), new Vector3(80f, 2f, 1f), border);
            Box("Border_E", level, new Vector3(40f, 1f, 0f), new Vector3(1f, 2f, 80f), border);
            Box("Border_W", level, new Vector3(-40f, 1f, 0f), new Vector3(1f, 2f, 80f), border);

            BuildClimbingWall(level, wall);
            BuildSwimmingPool(null, wall);
        }

        /// <summary>
        /// Swimming test: a raised basin (inner 10 x 14 m, water 4 m deep) north-west of the spawn, with a ramp up to
        /// the south rim to jump in from and a rock on the bottom to swim around. Climb out by jumping at the surface.
        /// </summary>
        static void BuildSwimmingPool(Transform parent, Material wallMaterial)
        {
            var pool = new GameObject("Pool").transform;
            pool.SetParent(parent, false);

            const float cx = -30f, cz = 28f;
            const float wallHeight = 4.3f, waterDepth = 4f;
            Box("Pool_Wall_W", pool, new Vector3(cx - 5.5f, wallHeight / 2f, cz), new Vector3(1f, wallHeight, 16f), wallMaterial);
            Box("Pool_Wall_E", pool, new Vector3(cx + 5.5f, wallHeight / 2f, cz), new Vector3(1f, wallHeight, 16f), wallMaterial);
            Box("Pool_Wall_S", pool, new Vector3(cx, wallHeight / 2f, cz - 7.5f), new Vector3(10f, wallHeight, 1f), wallMaterial);
            Box("Pool_Wall_N", pool, new Vector3(cx, wallHeight / 2f, cz + 7.5f), new Vector3(10f, wallHeight, 1f), wallMaterial);
            Box("Pool_Rock", pool, new Vector3(cx + 1f, 1f, cz + 2f), new Vector3(3f, 2f, 3f), Material("Rock", new Color(0.45f, 0.4f, 0.35f)));

            // Ramp from the ground up to the south rim.
            const float run = 11.5f;
            float angle = Mathf.Atan2(wallHeight, run) * Mathf.Rad2Deg;
            float length = Mathf.Sqrt(run * run + wallHeight * wallHeight);
            Box("Pool_Ramp", pool, new Vector3(cx, wallHeight / 2f, cz - 8f - run / 2f), new Vector3(4f, 0.4f, length),
                Material("Platform", new Color(0.85f, 0.75f, 0.35f)), Quaternion.Euler(-angle, 0f, 0f));

            // The water itself: a see-through trigger box whose top is the surface.
            var water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            water.name = "Water";
            water.transform.SetParent(pool, false);
            // Inset 1 cm from the walls and floor so the faces don't z-fight with them.
            water.transform.position = new Vector3(cx, waterDepth / 2f + 0.005f, cz);
            water.transform.localScale = new Vector3(9.98f, waterDepth - 0.01f, 13.98f);
            var waterRenderer = water.GetComponent<Renderer>();
            waterRenderer.sharedMaterial = WaterMaterial();
            waterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            waterRenderer.receiveShadows = false;
            water.GetComponent<BoxCollider>().isTrigger = true;
            water.AddComponent<WaterVolume>();
        }

        static Material WaterMaterial()
        {
            const string path = MaterialsFolder + "/Water.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            // URP Lit, transparent, both faces so the surface is visible from below too.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_Cull", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_Smoothness", 0.9f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.color = new Color(0.15f, 0.45f, 0.85f, 0.45f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Climbing test: a 7.1 m wall facing the spawn with three marked ledges 2.3 m apart.
        /// Low ledge is wide (shimmy right to reach the middle one), the top one runs along the whole top edge.
        /// </summary>
        static void BuildClimbingWall(Transform level, Material wallMaterial)
        {
            var ledgeMaterial = Material("Ledge", new Color(1f, 0.75f, 0.05f));
            Box("ClimbWall", level, new Vector3(25f, 3.55f, -27f), new Vector3(10f, 7.1f, 3f), wallMaterial);

            const float face = -25.5f;
            LedgeAt("Ledge_Low", level, new Vector3(24f, 2.5f, face), Quaternion.identity, 6f, ledgeMaterial);
            LedgeAt("Ledge_Mid", level, new Vector3(27f, 4.8f, face), Quaternion.identity, 3f, ledgeMaterial);
            LedgeAt("Ledge_Top", level, new Vector3(25f, 7.1f, face), Quaternion.identity, 10f, ledgeMaterial);
        }

        static void LedgeAt(string name, Transform parent, Vector3 edgeCenter, Quaternion outwardFacing, float length, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(edgeCenter, outwardFacing);

            var so = new SerializedObject(go.AddComponent<Ledge>());
            so.FindProperty("length").floatValue = length;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Marker strip so grabbable edges stand out; visual only, so it never blocks the hanging player.
            var strip = CreatePrimitive(PrimitiveType.Cube, "Strip", go.transform, new Vector3(0f, -0.08f, 0.06f), new Vector3(length, 0.16f, 0.12f), material);
            Object.DestroyImmediate(strip.GetComponent<Collider>());
        }

        static void BuildSpawnPoints()
        {
            var spawns = new GameObject("PlayerSpawnPoints");
            var a = new GameObject("Spawn_P1").transform;
            a.SetParent(spawns.transform, false);
            a.position = new Vector3(-1.5f, 0.05f, 0f);
            var b = new GameObject("Spawn_P2").transform;
            b.SetParent(spawns.transform, false);
            b.position = new Vector3(1.5f, 0.05f, 0f);

            var component = spawns.AddComponent<PlayerSpawnPoints>();
            var so = new SerializedObject(component);
            var points = so.FindProperty("spawnPoints");
            points.arraySize = 2;
            points.GetArrayElementAtIndex(0).objectReferenceValue = a;
            points.GetArrayElementAtIndex(1).objectReferenceValue = b;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- Act scenes foundation ----------

        /// <summary>Mechanics in the Game scene that become prefabs: prefab name, then the scene roots that go in it.</summary>
        static readonly (string prefab, string[] objects)[] MechanicPrefabs =
        {
            ("JumpPad_Auto", new[] { "JumpPad_Auto" }),
            ("JumpPad_Crank", new[] { "JumpPad_Crank" }),
            ("JumpPad_Coop", new[] { "JumpPad_Coop" }),
            ("Coop_ButtonBridge", new[] { "Coop_Bridge", "Coop_Button_1", "Coop_Button_2" }),
            ("Coop_LeverGate", new[] { "Coop_Gate", "Coop_Lever_1", "Coop_Lever_2" }),
            ("Coop_HeavyBlock", new[] { "Coop_HeavyBlock" }),
            ("Coop_Barrier", new[] { "Coop_Barrier" }),
            ("Beam_SlideBridge", new[] { "Tether_Bridge" }),
            ("Beam_Drawbridge", new[] { "Tether_Drawbridge" }),
            ("Beam_Block", new[] { "Tether_Block" }),
        };

        /// <summary>Scene roots that every level needs; they go into the LevelEssentials prefab.</summary>
        static readonly string[] EssentialObjects = { "Main Camera", "CameraRig", "PlayerCamera", "HUD", "EventSystem" };

        static readonly Vector3 TestSceneExitPosition = new(-6f, 0f, 3f);

        [MenuItem("Tools/Coop Game/Set Up Act Scenes Foundation")]
        static void SetupActFoundationFromMenu() => SetupActFoundation();

        /// <summary>
        /// One-time step from the code-built test scene towards hand-built act scenes. Non-destructive and safe to
        /// run again (whatever already exists is left alone); it never rebuilds a hand-made scene.
        /// - GameSession network prefab (team bolts + level transitions; lives on across level loads), spawned by
        ///   the Bootstrap's NetworkBootstrap; the Game scene's own PickupSystem moves onto it.
        /// - LevelEssentials prefab (camera, camera rig, HUD, EventSystem) cut out of the Game scene, which keeps it.
        /// - The Game scene's co-op mechanics saved as prefabs in Prefabs/Mechanics (the scene keeps using them).
        /// - LevelExit, Ledge and WaterVolume prefabs in Prefabs/Level; an exit from the Game scene into Act 1.
        /// - An empty Act 1 start scene (Scenes/Act1/A1_Start.unity) and the Build Settings scene list.
        /// </summary>
        public static void SetupActFoundation()
        {
            EnsureFolder(PrefabsFolder);
            EnsureFolder(MaterialsFolder);
            EnsureFolder(LevelPrefabsFolder);
            EnsureFolder(MechanicsFolder);
            EnsureFolder(Act1Folder);

            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

            // The team wallet moves from the test level onto the session object, which outlives levels.
            PickupSystem scenePickups = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out PickupSystem found)) scenePickups = found;
            var session = AssetDatabase.LoadAssetAtPath<GameObject>(GameSessionPrefabPath);
            if (session == null) session = BuildGameSessionPrefab(scenePickups);
            RegisterNetworkPrefab(session);
            if (scenePickups != null) Object.DestroyImmediate(scenePickups.gameObject);

            var essentials = ExtractLevelEssentials(scene);
            foreach (var (prefabName, objectNames) in MechanicPrefabs) ExtractMechanic(scene, prefabName, objectNames);

            var exit = AssetDatabase.LoadAssetAtPath<GameObject>(LevelExitPrefabPath);
            if (exit == null) exit = BuildLevelExitPrefab();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(LedgePrefabPath) == null) BuildLedgePrefab();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(WaterVolumePrefabPath) == null) BuildWaterVolumePrefab();

            if (!scene.GetRootGameObjects().Any(go => go.GetComponent<LevelExit>() != null))
                PlaceLevelExit(exit, TestSceneExitPosition, Quaternion.identity, "A1_Start", "", "Akt 1");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            RefreshInScenePlacedNetworkObjects(GameScenePath);

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Act1StartScenePath) == null) BuildAct1StartScene(essentials, exit);
            EnsureBuildScenes(BootstrapScenePath, GameScenePath, Act1StartScenePath);

            var bootstrap = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
            foreach (var root in bootstrap.GetRootGameObjects())
                if (root.TryGetComponent(out NetworkBootstrap networkBootstrap)) SetSessionPrefab(networkBootstrap);
            EditorSceneManager.MarkSceneDirty(bootstrap);
            EditorSceneManager.SaveScene(bootstrap);

            Finish("Act scenes foundation ready: GameSession, LevelEssentials, mechanics prefabs, LevelExit/Ledge/WaterVolume, Act 1 start scene.");
        }

        /// <summary>Network prefab spawned once per game: the team wallet and pickups, and level transitions.</summary>
        static GameObject BuildGameSessionPrefab(PickupSystem copyPickupsFrom)
        {
            var root = new GameObject("GameSession");
            root.AddComponent<NetworkObject>();
            var pickups = root.AddComponent<PickupSystem>();
            if (copyPickupsFrom != null) EditorUtility.CopySerialized(copyPickupsFrom, pickups);
            else ConfigurePickupSystem(pickups);
            root.AddComponent<SceneFlow>();
            root.AddComponent<CutsceneSystem>();
            return SaveNetworkPrefab(root, GameSessionPrefabPath);
        }

        static void SetSessionPrefab(NetworkBootstrap bootstrap)
        {
            var so = new SerializedObject(bootstrap);
            so.FindProperty("sessionPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(GameSessionPrefabPath);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Groups the scene's camera, rig, HUD and EventSystem under one root and saves it as a prefab the scene keeps using.</summary>
        static GameObject ExtractLevelEssentials(UnityEngine.SceneManagement.Scene scene)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(LevelEssentialsPrefabPath);
            if (existing != null) return existing;

            var root = new GameObject("LevelEssentials");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            foreach (var name in EssentialObjects)
            {
                var part = scene.GetRootGameObjects().FirstOrDefault(go => go.name == name);
                if (part != null) part.transform.SetParent(root.transform, true);
                else Debug.LogWarning($"[CoopProjectSetup] LevelEssentials: no '{name}' in the Game scene.");
            }
            root.transform.SetAsFirstSibling();
            return PrefabUtility.SaveAsPrefabAssetAndConnect(root, LevelEssentialsPrefabPath, InteractionMode.AutomatedAction);
        }

        /// <summary>
        /// Saves a mechanic (one scene root, or several grouped under a new root so their links stay inside) as a
        /// prefab with its root at the origin; the scene keeps it as an instance of that prefab.
        /// </summary>
        static void ExtractMechanic(UnityEngine.SceneManagement.Scene scene, string prefabName, string[] objectNames)
        {
            string path = $"{MechanicsFolder}/{prefabName}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

            var parts = objectNames.Select(n => scene.GetRootGameObjects().FirstOrDefault(go => go.name == n)).ToArray();
            if (parts.Any(p => p == null || PrefabUtility.IsPartOfPrefabInstance(p)))
            {
                Debug.LogWarning($"[CoopProjectSetup] {prefabName}: not all of {string.Join(", ", objectNames)} are plain objects in the Game scene; skipped.");
                return;
            }

            GameObject root = parts[0];
            if (parts.Length > 1)
            {
                root = new GameObject(prefabName);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = parts[0].transform.position;
                foreach (var part in parts) part.transform.SetParent(root.transform, true);
            }
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, path, InteractionMode.AutomatedAction);

            // Asset root at the origin (an instance's own position and rotation always override it).
            var contents = PrefabUtility.LoadPrefabContents(path);
            contents.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        /// <summary>A level exit: a glowing frame over a floor pad; the area the players stand in is the LevelExit's.</summary>
        static GameObject BuildLevelExitPrefab()
        {
            var glow = KeepEmissiveMaterial("LevelExit_Glow", new Color(0.3f, 1f, 0.55f), 1.4f);
            var frame = KeepMaterial("LevelExit_Frame", new Color(0.3f, 0.32f, 0.36f));
            var root = new GameObject("LevelExit");
            root.AddComponent<LevelExit>();
            VisualPart(PrimitiveType.Cube, "Pad", root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(3f, 0.06f, 3f), glow);
            VisualPart(PrimitiveType.Cube, "Post_L", root.transform, new Vector3(-1.6f, 1.6f, 0f), new Vector3(0.3f, 3.2f, 0.3f), frame);
            VisualPart(PrimitiveType.Cube, "Post_R", root.transform, new Vector3(1.6f, 1.6f, 0f), new Vector3(0.3f, 3.2f, 0.3f), frame);
            VisualPart(PrimitiveType.Cube, "Lintel", root.transform, new Vector3(0f, 3.3f, 0f), new Vector3(3.5f, 0.3f, 0.35f), frame);
            VisualPart(PrimitiveType.Cube, "Lintel_Light", root.transform, new Vector3(0f, 3.12f, 0f), new Vector3(2.9f, 0.08f, 0.38f), glow);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, LevelExitPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>A grabbable ledge: the Ledge component plus its yellow marker strip (resized with the length).</summary>
        static GameObject BuildLedgePrefab()
        {
            var holder = new GameObject("LedgeHolder");
            LedgeAt("Ledge", holder.transform, Vector3.zero, Quaternion.identity, 4f, KeepMaterial("Ledge", new Color(1f, 0.75f, 0.05f)));
            var ledge = holder.transform.GetChild(0).gameObject;
            ledge.transform.SetParent(null, false);
            var prefab = PrefabUtility.SaveAsPrefabAsset(ledge, LedgePrefabPath);
            Object.DestroyImmediate(ledge);
            Object.DestroyImmediate(holder);
            return prefab;
        }

        /// <summary>A box of water to swim in: scale it to fill a basin; its top is the surface.</summary>
        static GameObject BuildWaterVolumePrefab()
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            water.name = "WaterVolume";
            water.transform.localScale = new Vector3(8f, 2f, 8f);
            var renderer = water.GetComponent<Renderer>();
            renderer.sharedMaterial = WaterMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            water.GetComponent<BoxCollider>().isTrigger = true;
            water.AddComponent<WaterVolume>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(water, WaterVolumePrefabPath);
            Object.DestroyImmediate(water);
            return prefab;
        }

        static void VisualPart(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var part = CreatePrimitive(type, name, parent, position, scale, material);
            Object.DestroyImmediate(part.GetComponent<Collider>());
        }

        static void PlaceLevelExit(GameObject exitPrefab, Vector3 position, Quaternion rotation, string targetScene, string targetEntry, string label)
        {
            var exit = (GameObject)PrefabUtility.InstantiatePrefab(exitPrefab, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            exit.transform.SetPositionAndRotation(position, rotation);
            exit.name = $"LevelExit_{targetScene}";
            var so = new SerializedObject(exit.GetComponent<LevelExit>());
            so.FindProperty("targetScene").stringValue = targetScene;
            so.FindProperty("targetEntry").stringValue = targetEntry;
            so.FindProperty("label").stringValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The empty start of Act 1: light and sky, LevelEssentials, the players' default spawn, a greybox floor to
        /// build on, empty roots to keep the scene tidy, and an example exit to the test scene.
        /// </summary>
        static void BuildAct1StartScene(GameObject essentials, GameObject exitPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var defaultCamera = Camera.main;
            if (defaultCamera != null) Object.DestroyImmediate(defaultCamera.gameObject); // LevelEssentials brings one
            var light = Object.FindFirstObjectByType<Light>();
            if (light != null) light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            PrefabUtility.InstantiatePrefab(essentials, scene);
            BuildSpawnPoints();

            var greybox = new GameObject("Greybox").transform;
            Box("Floor", greybox, new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), KeepMaterial("Ground", new Color(0.42f, 0.45f, 0.4f)));
            new GameObject("Mechanics");
            new GameObject("Enemies");
            new GameObject("Pickups");

            PlaceLevelExit(exitPrefab, new Vector3(0f, 0f, 20f), Quaternion.identity, "Game", "", "Scena testowa");

            EditorSceneManager.SaveScene(scene, Act1StartScenePath);
        }

        /// <summary>Adds the scenes to the Build Settings list (at the end, enabled) if they aren't in it yet.</summary>
        static void EnsureBuildScenes(params string[] paths)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            foreach (var path in paths)
                if (!scenes.Any(s => s.path == path)) scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ---------- Helpers ----------

        static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material,
            Quaternion? rotation = null, bool keepBottom = false)
        {
            if (keepBottom) position.y = size.y * 0.5f;
            var go = CreatePrimitive(PrimitiveType.Cube, name, parent, position, size, material);
            go.transform.rotation = rotation ?? Quaternion.identity;
            go.isStatic = true;
            return go;
        }

        static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = material;

            // Player parts are visual only; the CharacterController is the collider.
            if (parent != null && parent.root.GetComponent<CharacterController>() != null)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static Material Material(string name, Color color)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }

    /// <summary>
    /// Always enter Play Mode through the Bootstrap scene so the NetworkManager exists, whichever scene is open in
    /// the editor. With "Play From Current Scene" on (the default) and a level scene open, Play goes straight into
    /// that level as host: the scene's name is handed to <see cref="NetworkBootstrap"/>, which hosts and loads it.
    /// </summary>
    [InitializeOnLoad]
    static class BootstrapPlayModeStartScene
    {
        const string MenuPath = "Tools/Coop Game/Play From Current Scene";
        const string EnabledKey = "CoopGame.PlayFromCurrentScene";

        static BootstrapPlayModeStartScene()
        {
            EditorApplication.delayCall += () =>
            {
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(CoopProjectSetup.BootstrapScenePath);
                Menu.SetChecked(MenuPath, Enabled);
            };
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static bool Enabled => EditorPrefs.GetBool(EnabledKey, true);

        [MenuItem(MenuPath)]
        static void Toggle()
        {
            EditorPrefs.SetBool(EnabledKey, !Enabled);
            Menu.SetChecked(MenuPath, Enabled);
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;
            SessionState.EraseString(NetworkBootstrap.PlayFromSceneKey);

            var scene = EditorSceneManager.GetActiveScene();
            if (!Enabled || scene.path == CoopProjectSetup.BootstrapScenePath || string.IsNullOrEmpty(scene.path)) return;

            // A Multiplayer Play Mode virtual player joins the main editor's game instead of hosting one of its own
            // (both hosting on the same port, only one of them got in).
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "com.unity.mppm.clone") >= 0)
            {
                SessionState.SetBool(NetworkBootstrap.JoinLocalKey, true);
                return;
            }

            bool inBuild = System.Array.Exists(EditorBuildSettings.scenes, s => s.enabled && s.path == scene.path);
            if (!inBuild)
            {
                Debug.LogWarning($"[Play From Current Scene] '{scene.name}' isn't in the Build Settings, so the network can't load it; starting from the menu instead. Add it under File > Build Profiles > Scene List.");
                return;
            }

            // The level is loaded from disk, so unsaved edits wouldn't show up in the game.
            if (scene.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorApplication.isPlaying = false;
                return;
            }
            SessionState.SetString(NetworkBootstrap.PlayFromSceneKey, scene.name);
        }
    }
}
