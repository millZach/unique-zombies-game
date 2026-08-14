using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Ashfall.Core;
using Ashfall.Enemies;
using Ashfall.Fx;
using Ashfall.Nav;
using Ashfall.Player;
using Ashfall.UI;
using Ashfall.World;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// Checks that the generated project is actually playable before anyone presses
    /// Play, and reports what is missing rather than leaving it to be discovered as a
    /// null-reference exception three rounds in.
    ///
    /// Two classes of check:
    ///  - structural: every critical component exists and its references are assigned;
    ///  - behavioural: the nav graph genuinely gates the map, so buying a door is the
    ///    only way into the lab wing.
    /// </summary>
    public static class AshfallValidation
    {
        private class Report
        {
            public readonly List<string> Errors = new();
            public readonly List<string> Warnings = new();
            public readonly List<string> Info = new();

            public void Error(string message) => Errors.Add(message);
            public void Warn(string message) => Warnings.Add(message);
            public void Note(string message) => Info.Add(message);
        }

        [MenuItem("Ashfall/Validate Project", priority = 20)]
        public static void ValidateMenu()
        {
            Report report = Run();
            Debug.Log(Format(report));

            if (report.Errors.Count > 0)
            {
                EditorUtility.DisplayDialog(
                    "Ashfall validation",
                    $"{report.Errors.Count} problem(s) found. See the Console for the full report.",
                    "OK");
            }
        }

        /// <summary>Batch entry point. Exits non-zero when anything critical is missing.</summary>
        public static void ValidateFromCommandLine()
        {
            Report report = Run();
            string text = Format(report);

            if (report.Errors.Count > 0)
            {
                Debug.LogError(text);
                Debug.LogError("[Ashfall] VALIDATION_FAILED");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log(text);
            Debug.Log("[Ashfall] VALIDATION_OK");
            EditorApplication.Exit(0);
        }

        private static Report Run()
        {
            var report = new Report();

            ValidateRenderPipeline(report);
            ValidateBuildSettings(report);

            Scene scene = EditorSceneManager.OpenScene(AshfallProjectBuilder.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                report.Error($"Could not open the Main scene at {AshfallProjectBuilder.ScenePath}.");
                return report;
            }

            var director = Object.FindFirstObjectByType<GameDirector>();
            var player = Object.FindFirstObjectByType<PlayerRig>();
            var enemies = Object.FindFirstObjectByType<EnemyDirector>();
            var phase = Object.FindFirstObjectByType<MapPhaseController>();
            var powerUps = Object.FindFirstObjectByType<PowerUpManager>();
            var wallet = Object.FindFirstObjectByType<SalvageWallet>();
            var fx = Object.FindFirstObjectByType<FxDirector>();
            var nav = Object.FindFirstObjectByType<NavGraph>();
            var hud = Object.FindFirstObjectByType<HudController>();
            var pause = Object.FindFirstObjectByType<PauseMenu>();

            Require(report, director, "GameDirector");
            Require(report, player, "PlayerRig");
            Require(report, enemies, "EnemyDirector");
            Require(report, phase, "MapPhaseController");
            Require(report, powerUps, "PowerUpManager");
            Require(report, wallet, "SalvageWallet");
            Require(report, fx, "FxDirector");
            Require(report, nav, "NavGraph");
            Require(report, hud, "HudController");
            Require(report, pause, "PauseMenu");

            ValidatePlayer(report, player);
            ValidateEnemies(report, enemies);
            ValidateWorld(report, phase);
            ValidateNav(report, nav, player);
            ValidateSerializedReferences(report);
            ValidateNoMissingScripts(report);
            ValidateHitboxes(report);

            return report;
        }

        private static void Require(Report report, Object value, string name)
        {
            if (value == null)
            {
                report.Error($"Missing {name} in the Main scene.");
            }
        }

        private static void ValidateRenderPipeline(Report report)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.defaultRenderPipeline;
            if (pipeline == null)
            {
                report.Error("No render pipeline asset is assigned in Graphics Settings.");
            }
            else
            {
                report.Note($"Render pipeline: {pipeline.name} ({pipeline.GetType().Name}).");
            }

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                report.Warn("Colour space is not Linear; the lighting will read flat.");
            }
        }

        private static void ValidateBuildSettings(Report report)
        {
            bool found = false;
            for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
            {
                if (EditorBuildSettings.scenes[i].path == AshfallProjectBuilder.ScenePath
                    && EditorBuildSettings.scenes[i].enabled)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                report.Error("The Main scene is not an enabled entry in the build settings.");
            }
        }

        private static void ValidatePlayer(Report report, PlayerRig player)
        {
            if (player == null)
            {
                return;
            }

            if (player.Motor == null) report.Error("PlayerRig has no PlayerMotor.");
            if (player.CameraRig == null) report.Error("PlayerRig has no PlayerCameraRig.");
            if (player.Health == null) report.Error("PlayerRig has no PlayerHealth.");
            if (player.Interactor == null) report.Error("PlayerRig has no PlayerInteractor.");
            if (player.ViewCamera == null) report.Error("PlayerCameraRig has no camera assigned.");

            PlayerLoadout loadout = player.Loadout;
            if (loadout == null)
            {
                report.Error("PlayerRig has no PlayerLoadout.");
                return;
            }

            int usable = 0;
            for (int i = 0; i < loadout.Slots.Count; i++)
            {
                PlayerLoadout.WeaponSlot slot = loadout.Slots[i];
                if (slot.definition == null)
                {
                    report.Error($"Weapon slot {i} has no definition.");
                    continue;
                }

                if (slot.viewModelPrefab == null)
                {
                    report.Error($"Weapon slot {i} ({slot.definition.displayName}) has no viewmodel prefab.");
                }

                usable++;
            }

            if (usable < 3)
            {
                report.Error($"Expected 3 weapons in the loadout, found {usable}.");
            }
            else
            {
                report.Note($"Loadout: {usable} weapons configured.");
            }
        }

        private static void ValidateEnemies(Report report, EnemyDirector enemies)
        {
            if (enemies == null)
            {
                return;
            }

            var archetypes = new[] { EnemyArchetype.Shambler, EnemyArchetype.Sprinter, EnemyArchetype.StormBrute };
            for (int i = 0; i < archetypes.Length; i++)
            {
                if (enemies.DefinitionFor(archetypes[i]) == null)
                {
                    report.Error($"EnemyDirector has no definition registered for {archetypes[i]}.");
                }
            }

            if (enemies.SpawnPoints.Count == 0)
            {
                report.Error("EnemyDirector has no spawn points.");
                return;
            }

            var byPhase = new Dictionary<MapPhase, int>();
            for (int i = 0; i < enemies.SpawnPoints.Count; i++)
            {
                EnemySpawnPoint point = enemies.SpawnPoints[i];
                if (point == null)
                {
                    report.Error($"Spawn point slot {i} is empty.");
                    continue;
                }

                byPhase.TryGetValue(point.RequiredPhase, out int count);
                byPhase[point.RequiredPhase] = count + 1;
            }

            if (!byPhase.TryGetValue(MapPhase.Standby, out int standby) || standby < 2)
            {
                report.Error("Fewer than two spawn points are available in the Standby phase; round 1 could stall.");
            }

            var summary = new StringBuilder("Spawn points by phase: ");
            for (int i = 0; i < MapPhases.Count; i++)
            {
                byPhase.TryGetValue((MapPhase)i, out int count);
                summary.Append($"{MapPhases.DisplayName((MapPhase)i)}={count} ");
            }

            report.Note(summary.ToString().TrimEnd());
        }

        private static void ValidateWorld(Report report, MapPhaseController phase)
        {
            if (phase == null)
            {
                return;
            }

            RouteDoor[] doors = Object.FindObjectsByType<RouteDoor>(FindObjectsSortMode.None);
            WeaponStation[] stations = Object.FindObjectsByType<WeaponStation>(FindObjectsSortMode.None);
            Barricade[] barricades = Object.FindObjectsByType<Barricade>(FindObjectsSortMode.None);
            PhaseElement[] elements = Object.FindObjectsByType<PhaseElement>(FindObjectsSortMode.None);
            PhaseLight[] lights = Object.FindObjectsByType<PhaseLight>(FindObjectsSortMode.None);
            StormExposureVolume[] storms = Object.FindObjectsByType<StormExposureVolume>(FindObjectsSortMode.None);

            if (doors.Length == 0) report.Error("No purchasable RouteDoor in the scene.");
            if (stations.Length == 0) report.Error("No WeaponStation in the scene.");
            if (barricades.Length == 0) report.Warn("No Barricade in the scene; the repair economy is unreachable.");
            if (elements.Length == 0) report.Error("No PhaseElement in the scene; the map will not visibly change.");
            if (lights.Length == 0) report.Error("No PhaseLight in the scene; the lighting will not change with phase.");
            if (storms.Length == 0) report.Warn("No StormExposureVolume; the rooftop carries no risk.");

            // A phase that changes nothing is a bug in the level, not just the code.
            for (int p = 0; p < MapPhases.Count; p++)
            {
                var target = (MapPhase)p;
                int changing = 0;
                for (int i = 0; i < elements.Length; i++)
                {
                    if (elements[i].FirstPhase == target || (int)elements[i].LastPhase == p - 1)
                    {
                        changing++;
                    }
                }

                if (p > 0 && changing == 0)
                {
                    report.Warn($"No PhaseElement appears or disappears at {MapPhases.DisplayName(target)}.");
                }
            }

            report.Note($"World: {doors.Length} routes, {stations.Length} weapon stations, " +
                        $"{barricades.Length} barricades, {elements.Length} phase props, " +
                        $"{lights.Length} phase lights, {storms.Length} storm volumes.");
        }

        private static void ValidateNav(Report report, NavGraph nav, PlayerRig player)
        {
            if (nav == null)
            {
                return;
            }

            if (nav.NodeCount < 100)
            {
                report.Error($"Nav graph has only {nav.NodeCount} nodes; the bake probably failed.");
                return;
            }

            report.Note($"Nav graph: {nav.NodeCount} nodes, {nav.LinkCount} links, {nav.GateCount} gates.");

            if (nav.GateCount == 0)
            {
                report.Error("Nav graph has no gates; routes will not be blocked by closed doors.");
                return;
            }

            // The graph is baked but Awake never runs in edit mode.
            nav.PrimeForQueries();

            Vector3 spawn = player != null ? player.transform.position : Vector3.zero;
            int spawnNode = nav.NearestNode(spawn, 6f);
            if (spawnNode < 0)
            {
                report.Error($"Player spawn at {spawn} is not on the nav graph.");
                return;
            }

            var scratch = new List<int>();
            (string label, Vector3 point)[] destinations =
            {
                ("lab wing", new Vector3(0f, 0.2f, 30f)),
                ("generator room", new Vector3(31f, 0.2f, -4f)),
                ("roof lane", new Vector3(20f, 5.7f, 21.5f))
            };

            // Closed: the interior routes must be unreachable, or buying doors is pointless.
            nav.CloseAllGates();
            for (int i = 0; i < destinations.Length; i++)
            {
                int target = nav.NearestNode(destinations[i].point, 10f);
                if (target < 0)
                {
                    report.Error($"No nav node near the {destinations[i].label} sample point {destinations[i].point}.");
                    continue;
                }

                if (nav.FindPath(spawnNode, target, scratch))
                {
                    report.Error($"The {destinations[i].label} is reachable with every route still closed; " +
                                 "the door gates are not blocking the graph.");
                }
            }

            // Open: everything must connect, or a round can never be cleared.
            for (int g = 0; g < nav.GateCount; g++)
            {
                nav.SetGateOpen(g, true);
            }

            for (int i = 0; i < destinations.Length; i++)
            {
                int target = nav.NearestNode(destinations[i].point, 10f);
                if (target < 0)
                {
                    continue;
                }

                if (!nav.FindPath(spawnNode, target, scratch))
                {
                    report.Error($"The {destinations[i].label} is unreachable even with every route open; " +
                                 "enemies spawned there could never reach the player.");
                }
                else
                {
                    report.Note($"Route to the {destinations[i].label}: {scratch.Count} nodes.");
                }
            }

            // Every spawn point must be able to reach the player once its phase is live.
            EnemySpawnPoint[] spawns = Object.FindObjectsByType<EnemySpawnPoint>(FindObjectsSortMode.None);
            for (int i = 0; i < spawns.Length; i++)
            {
                int node = nav.NearestNode(spawns[i].transform.position, 8f);
                if (node < 0)
                {
                    report.Error($"Spawn point '{spawns[i].name}' is not on the nav graph.");
                    continue;
                }

                if (!nav.FindPath(node, spawnNode, scratch))
                {
                    report.Error($"Spawn point '{spawns[i].name}' cannot reach the player even with every route open.");
                }
            }

            nav.CloseAllGates();
        }

        /// <summary>
        /// Sweeps every Ashfall component in the scene for serialized object references
        /// that were left empty. Catches the wiring mistakes that structural checks miss.
        /// </summary>
        private static void ValidateSerializedReferences(Report report)
        {
            MonoBehaviour[] behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            int missing = 0;

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                System.Type type = behaviour.GetType();
                if (type.Namespace == null || !type.Namespace.StartsWith("Ashfall"))
                {
                    continue;
                }

                var serialized = new SerializedObject(behaviour);
                SerializedProperty property = serialized.GetIterator();
                bool enterChildren = true;

                while (property.NextVisible(enterChildren))
                {
                    enterChildren = false;

                    if (property.propertyType != SerializedPropertyType.ObjectReference)
                    {
                        continue;
                    }

                    if (property.objectReferenceValue != null || property.objectReferenceEntityIdValue == default)
                    {
                        // A cleanly-empty slot is allowed; a reference whose target was
                        // deleted is not.
                        continue;
                    }

                    missing++;
                    report.Error($"{behaviour.name}/{type.Name}.{property.propertyPath} points at a deleted object.");
                }
            }

            if (missing == 0)
            {
                report.Note("No dangling serialized references in Ashfall components.");
            }
        }

        /// <summary>
        /// Hunts for components Unity could not resolve to a script, in the scene and in
        /// every Ashfall prefab.
        ///
        /// This exists because of a bug that shipped silently: DamageRelay was declared
        /// inside Damage.cs, and Unity will only deserialise a MonoBehaviour from a file
        /// named after it. Every enemy hitbox loaded with a missing script and the game
        /// quietly lost its head-shot multipliers, with nothing but an editor warning to
        /// show for it.
        /// </summary>
        private static void ValidateNoMissingScripts(Report report)
        {
            int missing = 0;

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                missing += CountMissingScripts(root, $"scene/{root.name}", report);
            }

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { AshfallAssetUtility.PrefabFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    missing += CountMissingScripts(prefab, path, report);
                }
            }

            if (missing == 0)
            {
                report.Note($"No missing scripts in the scene or in {guids.Length} prefabs.");
            }
        }

        private static int CountMissingScripts(GameObject root, string label, Report report)
        {
            int missing = 0;

            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                Component[] components = transform.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] != null)
                    {
                        continue;
                    }

                    missing++;
                    report.Error(
                        $"{label} -> '{transform.name}' has a missing script. " +
                        "A MonoBehaviour must live in a file named after its class.");
                }
            }

            return missing;
        }

        /// <summary>
        /// Enemy prefabs must carry working hitboxes, including one critical hitbox.
        /// Without it every shot is a body shot and the weapons all feel identical.
        /// </summary>
        private static void ValidateHitboxes(Report report)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { AshfallAssetUtility.PrefabFolder });

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponent<EnemyBrain>() == null)
                {
                    continue;
                }

                DamageRelay[] relays = prefab.GetComponentsInChildren<DamageRelay>(true);
                if (relays.Length == 0)
                {
                    report.Error($"{path} has no DamageRelay hitboxes; it cannot be shot accurately.");
                    continue;
                }

                bool hasCritical = false;
                for (int r = 0; r < relays.Length; r++)
                {
                    if (relays[r].CountsAsCritical)
                    {
                        hasCritical = true;
                    }

                    var collider = relays[r].GetComponent<Collider>();
                    if (collider == null)
                    {
                        report.Error($"{path} -> '{relays[r].name}' has a DamageRelay but no collider.");
                    }
                    else if (collider.gameObject.layer != AshfallLayers.EnemyHitbox)
                    {
                        report.Error(
                            $"{path} -> '{relays[r].name}' is not on the {AshfallLayers.EnemyHitboxName} layer, " +
                            "so weapon raycasts will miss it.");
                    }
                }

                if (!hasCritical)
                {
                    report.Error($"{path} has no critical hitbox; head shots would do body damage.");
                }
            }
        }

        private static string Format(Report report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("===== Ashfall project validation =====");

            for (int i = 0; i < report.Info.Count; i++)
            {
                sb.AppendLine($"  .  {report.Info[i]}");
            }

            for (int i = 0; i < report.Warnings.Count; i++)
            {
                sb.AppendLine($"  ~  WARNING: {report.Warnings[i]}");
            }

            for (int i = 0; i < report.Errors.Count; i++)
            {
                sb.AppendLine($"  X  ERROR:   {report.Errors[i]}");
            }

            sb.AppendLine(report.Errors.Count == 0
                ? $"===== PASS ({report.Warnings.Count} warning(s)) ====="
                : $"===== FAIL: {report.Errors.Count} error(s), {report.Warnings.Count} warning(s) =====");

            return sb.ToString();
        }
    }
}
