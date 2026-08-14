using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Ashfall.Core;
using Ashfall.Enemies;
using Ashfall.Fx;
using Ashfall.InputLayer;
using Ashfall.Nav;
using Ashfall.Player;
using Ashfall.World;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// Builds the entire playable project from code: render pipeline, textures,
    /// materials, meshes, prefabs, the station, the HUD and the Main scene.
    ///
    /// This is the single command that has to work. Everything the player sees is
    /// derived from source in this repository, so a fresh clone plus one menu item (or
    /// one batch-mode invocation) produces the same game every time -- no hand-wiring,
    /// no missing references, no binary scene that nobody can regenerate.
    /// </summary>
    public static class AshfallProjectBuilder
    {
        public const string ScenePath = "Assets/Ashfall/Scenes/Main.unity";
        private const string MeshAssetPath = "Assets/Ashfall/Art/Generated/Meshes/StationMeshes.asset";
        private const string UrpAssetPath = "Assets/Ashfall/Settings/AshfallURP.asset";
        private const string UrpRendererPath = "Assets/Ashfall/Settings/AshfallURP_Renderer.asset";

        [MenuItem("Ashfall/Build Playable Scene %#b", priority = 0)]
        public static void BuildPlayableSceneMenu()
        {
            BuildAll();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>Batch entry point: -executeMethod Ashfall.EditorTools.AshfallProjectBuilder.BuildFromCommandLine</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                BuildAll();
                Debug.Log("[Ashfall] BUILD_SCENE_OK");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Ashfall] BUILD_SCENE_FAILED: {e}");
                EditorApplication.Exit(2);
                return;
            }

            EditorApplication.Exit(0);
        }

        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        /// <summary>
        /// Batch entry point run before the scene build.
        ///
        /// TextMeshPro's essential resources ship inside the uGUI package as a
        /// .unitypackage. <see cref="AssetDatabase.ImportPackage"/> is asynchronous, so
        /// this session must stay alive until the completion callback fires -- exiting
        /// straight after the call silently imports nothing. Run without -quit.
        /// </summary>
        public static void ImportTextMeshProEssentials()
        {
            const string relative = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";

            if (File.Exists(TmpSettingsPath))
            {
                Debug.Log("[Ashfall] TMP_ALREADY_PRESENT");
                EditorApplication.Exit(0);
                return;
            }

            string full = Path.GetFullPath(relative);
            if (!File.Exists(full))
            {
                Debug.LogError($"[Ashfall] TMP_PACKAGE_MISSING at {full}");
                EditorApplication.Exit(3);
                return;
            }

            AssetDatabase.importPackageCompleted += OnTmpImportCompleted;
            AssetDatabase.importPackageFailed += OnTmpImportFailed;
            AssetDatabase.importPackageCancelled += OnTmpImportCancelled;
            EditorApplication.update += TmpImportWatchdog;

            Debug.Log($"[Ashfall] TMP_IMPORT_STARTED from {full}");
            AssetDatabase.ImportPackage(full, false);
        }

        private static int _tmpWatchdogFrames;

        private static void TmpImportWatchdog()
        {
            _tmpWatchdogFrames++;

            if (File.Exists(TmpSettingsPath))
            {
                FinishTmpImport(0, "TMP_IMPORT_OK");
                return;
            }

            // Roughly a minute of editor frames. Long enough for a cold import, short
            // enough that a stuck batch job does not hang the pipeline.
            if (_tmpWatchdogFrames > 4000)
            {
                FinishTmpImport(4, "TMP_IMPORT_TIMEOUT");
            }
        }

        private static void OnTmpImportCompleted(string packageName)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[Ashfall] TMP package import completed: {packageName}");
        }

        private static void OnTmpImportFailed(string packageName, string error)
        {
            FinishTmpImport(5, $"TMP_IMPORT_FAILED {packageName}: {error}");
        }

        private static void OnTmpImportCancelled(string packageName)
        {
            FinishTmpImport(6, $"TMP_IMPORT_CANCELLED {packageName}");
        }

        private static void FinishTmpImport(int code, string message)
        {
            EditorApplication.update -= TmpImportWatchdog;
            AssetDatabase.importPackageCompleted -= OnTmpImportCompleted;
            AssetDatabase.importPackageFailed -= OnTmpImportFailed;
            AssetDatabase.importPackageCancelled -= OnTmpImportCancelled;

            if (code == 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[Ashfall] {message}");
            }
            else
            {
                Debug.LogError($"[Ashfall] {message}");
            }

            EditorApplication.Exit(code);
        }

        // ------------------------------------------------------------------
        // Main build
        // ------------------------------------------------------------------

        public static void BuildAll()
        {
            EnsureFolders();
            ConfigureRenderPipeline();

            AshfallGeometry.ResetCache();
            AshfallGeometry.BeginPersist(MeshAssetPath);

            AshfallTextureFactory.GenerateAll();
            AshfallMaterialLibrary.BuildAll();
            AshfallAudioLibrary.ConfigureImportSettings();
            AshfallPrefabFactory.BuildAll();

            BuildScene();

            AshfallGeometry.EndPersist();
            Debug.Log($"[Ashfall] Generated {AshfallGeometry.CreatedMeshes.Count} meshes into {MeshAssetPath}.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureFolders()
        {
            AshfallAssetUtility.EnsureFolder("Assets/Ashfall/Art/Generated/Textures");
            AshfallAssetUtility.EnsureFolder("Assets/Ashfall/Art/Generated/Materials");
            AshfallAssetUtility.EnsureFolder("Assets/Ashfall/Art/Generated/Meshes");
            AshfallAssetUtility.EnsureFolder("Assets/Ashfall/Art/Generated/UI");
            AshfallAssetUtility.EnsureFolder(AshfallAudioLibrary.AudioFolder);
            AshfallAssetUtility.EnsureFolder(AshfallAssetUtility.PrefabFolder);
            AshfallAssetUtility.EnsureFolder(AshfallAssetUtility.DataFolder);
            AshfallAssetUtility.EnsureFolder(AshfallAssetUtility.SettingsFolder);
            AshfallAssetUtility.EnsureFolder(AshfallAssetUtility.SceneFolder);
        }

        // ------------------------------------------------------------------
        // Render pipeline
        // ------------------------------------------------------------------

        private static void ConfigureRenderPipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);

            if (pipeline == null)
            {
                ScriptableRendererData rendererData = CreateRendererData(UrpRendererPath);
                if (rendererData == null)
                {
                    Debug.LogError("[Ashfall] Could not create a URP renderer asset; leaving the pipeline unset.");
                    return;
                }

                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, UrpAssetPath);
            }

            // Tuned for a dark, foggy interior with a lot of small point lights: shadows
            // stay close, additional lights are per-pixel, and HDR is on so the emissive
            // storm elements can actually bloom past white.
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            pipeline.renderScale = 1f;
            pipeline.shadowDistance = 85f;
            pipeline.shadowCascadeCount = 4;
            pipeline.maxAdditionalLightsCount = 8;
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.shadowDepthBias = 1.1f;
            pipeline.shadowNormalBias = 1.0f;

            // A handful of URP settings only expose a getter, so they are written through
            // the serialized backing fields. Named rather than index-based, so a URP
            // upgrade that renames one produces a clear warning instead of silent drift.
            var pipelineSerialized = new SerializedObject(pipeline);
            SetSerialized(pipelineSerialized, "m_SoftShadowsSupported", p => p.boolValue = true);
            SetSerialized(pipelineSerialized, "m_MainLightShadowsSupported", p => p.boolValue = true);
            SetSerialized(pipelineSerialized, "m_MainLightShadowmapResolution", p => p.intValue = 2048);
            SetSerialized(pipelineSerialized, "m_MainLightRenderingMode", p => p.intValue = (int)LightRenderingMode.PerPixel);
            SetSerialized(pipelineSerialized, "m_AdditionalLightsRenderingMode", p => p.intValue = (int)LightRenderingMode.PerPixel);
            SetSerialized(pipelineSerialized, "m_AdditionalLightsPerObjectLimit", p => p.intValue = 8);
            SetSerialized(pipelineSerialized, "m_AdditionalLightShadowsSupported", p => p.boolValue = false);
            pipelineSerialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }

            AssetDatabase.SaveAssets();
        }

        private static void SetSerialized(SerializedObject serialized, string path, System.Action<SerializedProperty> apply)
        {
            SerializedProperty property = serialized.FindProperty(path);
            if (property == null)
            {
                Debug.LogWarning($"[Ashfall] URP asset has no serialized field '{path}'; skipping that setting.");
                return;
            }

            apply(property);
        }

        /// <summary>
        /// Creates the renderer data through URP's own internal factory so it is wired
        /// exactly as the "Create / Rendering / URP Asset" menu item would do it. Falls
        /// back to a bare instance if the internal API ever moves.
        /// </summary>
        private static ScriptableRendererData CreateRendererData(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (existing != null)
            {
                return existing;
            }

            MethodInfo factory = typeof(UniversalRenderPipelineAsset).GetMethod(
                "CreateRendererAsset",
                BindingFlags.Static | BindingFlags.NonPublic);

            if (factory != null)
            {
                try
                {
                    var created = factory.Invoke(null, new object[] { path, RendererType.UniversalRenderer, true, "Renderer" })
                        as ScriptableRendererData;
                    if (created != null)
                    {
                        return created;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Ashfall] URP internal renderer factory failed ({e.Message}); using a plain instance.");
                }
            }

            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(data, path);
            return data;
        }

        // ------------------------------------------------------------------
        // Scene
        // ------------------------------------------------------------------

        private static void BuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sceneRoot = new GameObject("Ashfall").transform;

            // --- systems ---------------------------------------------------------
            var systemsGo = new GameObject("Systems");
            systemsGo.transform.SetParent(sceneRoot, false);

            var wallet = systemsGo.AddComponent<SalvageWallet>();
            var enemyDirector = systemsGo.AddComponent<EnemyDirector>();
            var powerUps = systemsGo.AddComponent<PowerUpManager>();
            var phaseController = systemsGo.AddComponent<MapPhaseController>();
            var gameDirector = systemsGo.AddComponent<GameDirector>();
            var fx = systemsGo.AddComponent<FxDirector>();
            var audio = systemsGo.AddComponent<Ashfall.Audio.AudioDirector>();

            // Input lives on its own object. Both input singletons call
            // DontDestroyOnLoad on themselves, and hosting them on the shared Systems
            // object would drag every manager in the game across scene loads with them.
            var inputGo = new GameObject("Input");
            inputGo.transform.SetParent(sceneRoot, false);
            inputGo.AddComponent<AshfallInput>();
            inputGo.AddComponent<GamepadRumble>();

            var navGo = new GameObject("Nav Graph");
            navGo.transform.SetParent(sceneRoot, false);
            var navGraph = navGo.AddComponent<NavGraph>();

            // --- map ---------------------------------------------------------------
            AshfallMapBuilder.Result map = AshfallMapBuilder.Build(sceneRoot);

            // --- player --------------------------------------------------------------
            PlayerRig player = BuildPlayer(sceneRoot, map.PlayerSpawn, map.PlayerSpawnYaw, wallet);

            // --- nav bake -------------------------------------------------------------
            // Bake with every door physically out of the way. A shut door blocks the
            // capsule sweep, so links through its opening would never be created at all
            // and the gate would have nothing to gate -- the routes would be permanently
            // impassable rather than merely locked.
            List<Collider> doorColliders = DisableDoorBlockers(map.Doors);
            AshfallNavBaker.BakeReport report;
            try
            {
                report = AshfallNavBaker.Bake(navGraph, map.NavBounds, map.GateVolumes);
            }
            finally
            {
                for (int i = 0; i < doorColliders.Count; i++)
                {
                    doorColliders[i].enabled = true;
                }

                Physics.SyncTransforms();
            }

            Debug.Log($"[Ashfall] Nav graph baked: {report.NodeCount} nodes, {report.LinkCount} links, " +
                      $"{report.GateCount} gates ({report.GatedLinkCount} gated links), " +
                      $"{report.IsolatedNodeCount} isolated nodes.");

            // --- wiring ---------------------------------------------------------------
            audio.Configure(AshfallAudioLibrary.BuildCueTable(), AshfallAudioLibrary.StormAmbience());

            fx.Configure(
                AshfallPrefabFactory.SparkPrefab,
                AshfallPrefabFactory.DustPrefab,
                AshfallPrefabFactory.BloodPrefab,
                AshfallPrefabFactory.MuzzleFlashPrefab,
                AshfallPrefabFactory.TracerPrefab,
                AshfallPrefabFactory.ImpactLightPrefab);

            enemyDirector.Configure(
                new List<EnemyDirector.ArchetypePrefab>
                {
                    new()
                    {
                        archetype = EnemyArchetype.Shambler,
                        definition = AshfallPrefabFactory.ShamblerDefinition,
                        prefab = AshfallPrefabFactory.ShamblerPrefab
                    },
                    new()
                    {
                        archetype = EnemyArchetype.Sprinter,
                        definition = AshfallPrefabFactory.SprinterDefinition,
                        prefab = AshfallPrefabFactory.SprinterPrefab
                    },
                    new()
                    {
                        archetype = EnemyArchetype.StormBrute,
                        definition = AshfallPrefabFactory.BruteDefinition,
                        prefab = AshfallPrefabFactory.BrutePrefab
                    }
                },
                map.SpawnPoints);

            powerUps.Configure(
                AshfallPrefabFactory.PowerUpPrefab,
                player.Loadout,
                player.Health,
                wallet);

            phaseController.Configure(
                map.PhaseElements,
                map.PhaseLights,
                map.Doors,
                map.Stations,
                map.StormVolumes,
                map.Sun,
                map.Rain,
                map.Embers,
                map.StormFlash,
                enemyDirector);

            foreach (KeyValuePair<MapPhase, List<RouteDoor>> pair in map.AutoOpenDoors)
            {
                phaseController.SetAutoOpenDoors(pair.Key, pair.Value);
            }

            for (int i = 0; i < MapPhases.Count; i++)
            {
                phaseController.SetAtmosphere(i, MapPhaseController.DefaultAtmosphere((MapPhase)i));
            }

            gameDirector.Configure(player, enemyDirector, phaseController, powerUps, wallet);

            // --- HUD -------------------------------------------------------------------
            AshfallHudBuilder.Build(sceneRoot, gameDirector);

            // --- scene render settings -----------------------------------------------
            RenderSettings.skybox = AshfallMaterialLibrary.Skybox;
            RenderSettings.sun = map.Sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = AshfallPalette.FogCalm;
            RenderSettings.fogDensity = 0.020f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.085f, 0.098f, 0.125f);
            RenderSettings.ambientIntensity = 0.62f;
            RenderSettings.reflectionIntensity = 0.35f;

            // Realtime GI baking is off; the whole slice is dynamically lit so the phase
            // transitions can actually change the lighting.
            Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            RegisterSceneInBuildSettings();
        }

        private static List<Collider> DisableDoorBlockers(List<RouteDoor> doors)
        {
            var disabled = new List<Collider>();

            for (int i = 0; i < doors.Count; i++)
            {
                if (doors[i] == null)
                {
                    continue;
                }

                foreach (Collider collider in doors[i].GetComponentsInChildren<Collider>(true))
                {
                    if (collider.isTrigger || !collider.enabled)
                    {
                        continue;
                    }

                    collider.enabled = false;
                    disabled.Add(collider);
                }
            }

            Physics.SyncTransforms();
            return disabled;
        }

        private static PlayerRig BuildPlayer(Transform parent, Vector3 spawn, float yaw, SalvageWallet wallet)
        {
            var playerGo = new GameObject("Player");
            playerGo.transform.SetParent(parent, false);
            playerGo.transform.SetPositionAndRotation(spawn, Quaternion.Euler(0f, yaw, 0f));
            playerGo.layer = AshfallLayers.Player;
            playerGo.tag = AshfallTags.Player;

            var controller = playerGo.AddComponent<CharacterController>();
            controller.radius = 0.35f;
            controller.height = 1.85f;
            controller.center = new Vector3(0f, 0.925f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.45f;
            controller.skinWidth = 0.04f;
            controller.minMoveDistance = 0f;

            var pivotGo = new GameObject("Camera Pivot");
            pivotGo.transform.SetParent(playerGo.transform, false);
            pivotGo.transform.localPosition = new Vector3(0f, 1.66f, 0f);
            var cameraRig = pivotGo.AddComponent<PlayerCameraRig>();

            var shakeGo = new GameObject("Shake Socket");
            shakeGo.transform.SetParent(pivotGo.transform, false);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.transform.SetParent(shakeGo.transform, false);
            cameraGo.tag = "MainCamera";

            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = 80f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 320f;
            camera.allowHDR = true;
            camera.allowMSAA = true;

            var cameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            cameraData.renderShadows = true;

            cameraGo.AddComponent<AudioListener>();

            var weaponSocketGo = new GameObject("Weapon Socket");
            weaponSocketGo.transform.SetParent(cameraGo.transform, false);
            weaponSocketGo.transform.localPosition = new Vector3(0.21f, -0.20f, 0.46f);

            cameraRig.Configure(pivotGo.transform, shakeGo.transform, weaponSocketGo.transform, camera);

            var motor = playerGo.AddComponent<PlayerMotor>();
            var health = playerGo.AddComponent<PlayerHealth>();
            var loadout = playerGo.AddComponent<PlayerLoadout>();
            var interactor = playerGo.AddComponent<PlayerInteractor>();
            var rig = playerGo.AddComponent<PlayerRig>();

            motor.SetCameraRig(cameraRig);
            loadout.Configure(cameraRig, weaponSocketGo.transform);
            interactor.Configure(cameraRig, wallet);
            rig.Configure(motor, cameraRig, loadout, health, interactor);

            // Weapon slots. Only the sidearm starts unlocked; the other two are bought.
            var loadoutSerialized = new SerializedObject(loadout);
            SerializedProperty slots = loadoutSerialized.FindProperty("slots");
            slots.arraySize = 3;

            (Weapons.WeaponDefinition def, GameObject view)[] entries =
            {
                (AshfallPrefabFactory.SidearmDefinition, AshfallPrefabFactory.SidearmViewModel),
                (AshfallPrefabFactory.ShotgunDefinition, AshfallPrefabFactory.ShotgunViewModel),
                (AshfallPrefabFactory.RifleDefinition, AshfallPrefabFactory.RifleViewModel)
            };

            for (int i = 0; i < entries.Length; i++)
            {
                SerializedProperty element = slots.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("definition").objectReferenceValue = entries[i].def;
                element.FindPropertyRelative("viewModelPrefab").objectReferenceValue = entries[i].view;
            }

            loadoutSerialized.FindProperty("startingSlotCount").intValue = 1;
            loadoutSerialized.FindProperty("hitMask").intValue = AshfallLayers.WeaponHitMask;
            loadoutSerialized.ApplyModifiedPropertiesWithoutUndo();

            var interactorSerialized = new SerializedObject(interactor);
            interactorSerialized.FindProperty("interactMask").intValue = AshfallLayers.InteractMask;
            interactorSerialized.ApplyModifiedPropertiesWithoutUndo();

            var rigSerialized = new SerializedObject(rig);
            rigSerialized.FindProperty("spawnPoint").objectReferenceValue = null;
            rigSerialized.ApplyModifiedPropertiesWithoutUndo();

            return rig;
        }

        private static void RegisterSceneInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool found = false;

            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == ScenePath)
                {
                    scenes[i] = new EditorBuildSettingsScene(ScenePath, true);
                    found = true;
                }
            }

            if (!found)
            {
                scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

    }
}
