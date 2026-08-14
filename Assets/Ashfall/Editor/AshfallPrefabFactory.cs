using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Ashfall.Core;
using Ashfall.Enemies;
using Ashfall.Nav;
using Ashfall.Weapons;
using Ashfall.World;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// Builds every runtime prefab out of the procedural mesh kit: three enemy
    /// silhouettes, three weapon viewmodels, the FX set and the power-up canister.
    ///
    /// Silhouette is doing all the work here. There are no imported character models, so
    /// each enemy has to be recognisable from its outline alone at twenty metres in fog:
    /// the shambler is hunched and wide, the sprinter is low and forward-pitched, and the
    /// brute is a wall with a lit core.
    /// </summary>
    public static class AshfallPrefabFactory
    {
        public static GameObject ShamblerPrefab { get; private set; }
        public static GameObject SprinterPrefab { get; private set; }
        public static GameObject BrutePrefab { get; private set; }

        public static GameObject SidearmViewModel { get; private set; }
        public static GameObject ShotgunViewModel { get; private set; }
        public static GameObject RifleViewModel { get; private set; }

        public static GameObject PowerUpPrefab { get; private set; }

        public static ParticleSystem SparkPrefab { get; private set; }
        public static ParticleSystem DustPrefab { get; private set; }
        public static ParticleSystem BloodPrefab { get; private set; }
        public static ParticleSystem MuzzleFlashPrefab { get; private set; }
        public static LineRenderer TracerPrefab { get; private set; }
        public static Light ImpactLightPrefab { get; private set; }

        public static EnemyDefinition ShamblerDefinition { get; private set; }
        public static EnemyDefinition SprinterDefinition { get; private set; }
        public static EnemyDefinition BruteDefinition { get; private set; }

        public static WeaponDefinition SidearmDefinition { get; private set; }
        public static WeaponDefinition ShotgunDefinition { get; private set; }
        public static WeaponDefinition RifleDefinition { get; private set; }

        public static void BuildAll()
        {
            AshfallAssetUtility.EnsureFolder(AshfallAssetUtility.PrefabFolder);
            AshfallAssetUtility.EnsureFolder(AshfallAssetUtility.DataFolder);

            BuildDefinitions();
            BuildFx();
            BuildEnemies();
            BuildWeapons();
            BuildPowerUp();

            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------
        // ScriptableObject data
        // ------------------------------------------------------------------

        private static void BuildDefinitions()
        {
            ShamblerDefinition = AshfallAssetUtility.CreateOrReplace<EnemyDefinition>($"{AshfallAssetUtility.DataFolder}/Enemy_Shambler.asset");
            EnemyDefinition.ApplyShambler(ShamblerDefinition);
            EditorUtility.SetDirty(ShamblerDefinition);

            SprinterDefinition = AshfallAssetUtility.CreateOrReplace<EnemyDefinition>($"{AshfallAssetUtility.DataFolder}/Enemy_Sprinter.asset");
            EnemyDefinition.ApplySprinter(SprinterDefinition);
            EditorUtility.SetDirty(SprinterDefinition);

            BruteDefinition = AshfallAssetUtility.CreateOrReplace<EnemyDefinition>($"{AshfallAssetUtility.DataFolder}/Enemy_StormBrute.asset");
            EnemyDefinition.ApplyStormBrute(BruteDefinition);
            EditorUtility.SetDirty(BruteDefinition);

            SidearmDefinition = AshfallAssetUtility.CreateOrReplace<WeaponDefinition>($"{AshfallAssetUtility.DataFolder}/Weapon_MeridianSidearm.asset");
            WeaponDefinition.ApplyMeridianSidearm(SidearmDefinition);
            EditorUtility.SetDirty(SidearmDefinition);

            ShotgunDefinition = AshfallAssetUtility.CreateOrReplace<WeaponDefinition>($"{AshfallAssetUtility.DataFolder}/Weapon_Breakwater.asset");
            WeaponDefinition.ApplyBreakwaterShotgun(ShotgunDefinition);
            EditorUtility.SetDirty(ShotgunDefinition);

            RifleDefinition = AshfallAssetUtility.CreateOrReplace<WeaponDefinition>($"{AshfallAssetUtility.DataFolder}/Weapon_Arc9.asset");
            WeaponDefinition.ApplyArc9Rifle(RifleDefinition);
            EditorUtility.SetDirty(RifleDefinition);
        }

        // ------------------------------------------------------------------
        // Shared part helper
        // ------------------------------------------------------------------

        private static GameObject AddPart(
            Transform parent,
            string name,
            Mesh mesh,
            Material material,
            Vector3 localPosition,
            Vector3 localEuler = default,
            bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;

            return go;
        }

        private static void AddHitbox(
            Transform parent,
            string name,
            Vector3 center,
            Vector3 size,
            EnemyHealth owner,
            float multiplier,
            bool critical,
            int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.layer = layer;

            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            // Triggers so they never fight the CharacterController; the weapon raycast
            // opts into triggers explicitly.
            box.isTrigger = true;

            var relay = go.AddComponent<DamageRelay>();
            relay.Configure(owner, multiplier, critical);
        }

        // ------------------------------------------------------------------
        // FX
        // ------------------------------------------------------------------

        private static void BuildFx()
        {
            SparkPrefab = BuildParticlePrefab(
                "FX_ImpactSparks",
                count: 12,
                lifetime: new Vector2(0.14f, 0.32f),
                speed: new Vector2(3.5f, 8.5f),
                size: new Vector2(0.018f, 0.045f),
                color: AshfallPalette.EmergencyAmber,
                gravity: 0.9f,
                coneAngle: 38f,
                stretch: true);

            DustPrefab = BuildParticlePrefab(
                "FX_ImpactDust",
                count: 7,
                lifetime: new Vector2(0.35f, 0.7f),
                speed: new Vector2(0.5f, 1.8f),
                size: new Vector2(0.12f, 0.34f),
                color: AshfallPalette.ConcreteLight,
                gravity: -0.05f,
                coneAngle: 62f,
                stretch: false);

            BloodPrefab = BuildParticlePrefab(
                "FX_StormMist",
                count: 10,
                lifetime: new Vector2(0.22f, 0.48f),
                speed: new Vector2(1.6f, 4.2f),
                size: new Vector2(0.05f, 0.16f),
                color: AshfallPalette.Blood,
                gravity: 0.35f,
                coneAngle: 46f,
                stretch: false);

            MuzzleFlashPrefab = BuildParticlePrefab(
                "FX_MuzzleFlash",
                count: 5,
                lifetime: new Vector2(0.035f, 0.065f),
                speed: new Vector2(1.5f, 5f),
                size: new Vector2(0.10f, 0.28f),
                color: AshfallPalette.EmergencyAmber,
                gravity: 0f,
                coneAngle: 16f,
                stretch: true);

            // --- tracer -------------------------------------------------------
            var tracerGo = new GameObject("FX_Tracer");
            var line = tracerGo.AddComponent<LineRenderer>();
            line.material = AshfallMaterialLibrary.FxAdditive;
            line.positionCount = 2;
            line.startWidth = 0.03f;
            line.endWidth = 0.008f;
            line.numCapVertices = 0;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.useWorldSpace = true;
            AshfallAssetUtility.SetLayerRecursive(tracerGo, AshfallLayers.Fx);
            TracerPrefab = AshfallAssetUtility
                .SavePrefab(tracerGo, $"{AshfallAssetUtility.PrefabFolder}/FX_Tracer.prefab")
                .GetComponent<LineRenderer>();

            // --- impact light --------------------------------------------------
            var lightGo = new GameObject("FX_ImpactLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 2.5f;
            light.intensity = 4f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForceVertex;
            AshfallAssetUtility.SetLayerRecursive(lightGo, AshfallLayers.Fx);
            ImpactLightPrefab = AshfallAssetUtility
                .SavePrefab(lightGo, $"{AshfallAssetUtility.PrefabFolder}/FX_ImpactLight.prefab")
                .GetComponent<Light>();
        }

        private static ParticleSystem BuildParticlePrefab(
            string name,
            int count,
            Vector2 lifetime,
            Vector2 speed,
            Vector2 size,
            Color color,
            float gravity,
            float coneAngle,
            bool stretch)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.4f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.maxParticles = Mathf.Max(count * 2, 16);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = 0.02f;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.35f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.12f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AshfallMaterialLibrary.FxAdditive;
            renderer.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (stretch)
            {
                renderer.velocityScale = 0.055f;
                renderer.lengthScale = 2.2f;
            }

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.alignment = ParticleSystemRenderSpace.View;

            AshfallAssetUtility.SetLayerRecursive(go, AshfallLayers.Fx);
            return AshfallAssetUtility
                .SavePrefab(go, $"{AshfallAssetUtility.PrefabFolder}/{name}.prefab")
                .GetComponent<ParticleSystem>();
        }

        // ------------------------------------------------------------------
        // Enemies
        // ------------------------------------------------------------------

        private static void BuildEnemies()
        {
            ShamblerPrefab = BuildEnemy(ShamblerDefinition, BuildShamblerBody);
            SprinterPrefab = BuildEnemy(SprinterDefinition, BuildSprinterBody);
            BrutePrefab = BuildEnemy(BruteDefinition, BuildBruteBody);
        }

        private static GameObject BuildEnemy(EnemyDefinition definition, System.Action<Transform, EnemyDefinition, List<Renderer>> bodyBuilder)
        {
            var root = new GameObject($"Enemy_{definition.archetype}");
            root.layer = AshfallLayers.Enemy;

            var controller = root.AddComponent<CharacterController>();
            controller.radius = definition.bodyRadius;
            controller.height = definition.bodyHeight;
            controller.center = new Vector3(0f, definition.bodyHeight * 0.5f, 0f);
            controller.slopeLimit = 52f;
            controller.stepOffset = 0.42f;
            controller.skinWidth = 0.05f;
            controller.minMoveDistance = 0f;

            var agent = root.AddComponent<SteeringAgent>();
            var health = root.AddComponent<EnemyHealth>();
            var brain = root.AddComponent<EnemyBrain>();

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = Vector3.one;

            var renderers = new List<Renderer>();
            bodyBuilder(visual.transform, definition, renderers);

            // Hitboxes: a generous body box plus a small, high-value head.
            float headHeight = definition.bodyHeight * 0.88f;
            AddHitbox(
                root.transform,
                "Hitbox_Body",
                new Vector3(0f, definition.bodyHeight * 0.45f, 0f),
                new Vector3(definition.bodyRadius * 2.0f, definition.bodyHeight * 0.72f, definition.bodyRadius * 1.7f),
                health,
                1f,
                false,
                AshfallLayers.EnemyHitbox);

            AddHitbox(
                root.transform,
                "Hitbox_Head",
                new Vector3(0f, headHeight, 0f),
                Vector3.one * (definition.bodyRadius * 1.15f),
                health,
                definition.criticalMultiplier,
                true,
                AshfallLayers.EnemyHitbox);

            var attackOrigin = new GameObject("AttackOrigin");
            attackOrigin.transform.SetParent(root.transform, false);
            attackOrigin.transform.localPosition = new Vector3(0f, definition.bodyHeight * 0.6f, definition.bodyRadius);

            var colliders = new List<Collider>();
            root.GetComponentsInChildren(true, colliders);
            colliders.RemoveAll(c => c is CharacterController);

            health.Configure(renderers.ToArray(), colliders.ToArray(), visual.transform);

            var serialized = new SerializedObject(brain);
            serialized.FindProperty("definition").objectReferenceValue = definition;
            serialized.FindProperty("visualRoot").objectReferenceValue = visual.transform;
            serialized.FindProperty("attackOrigin").objectReferenceValue = attackOrigin.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var agentSerialized = new SerializedObject(agent);
            agentSerialized.FindProperty("moveSpeed").floatValue = definition.baseMoveSpeed;
            agentSerialized.FindProperty("separationRadius").floatValue = definition.bodyRadius * 2.6f;
            agentSerialized.FindProperty("wallProbeDistance").floatValue = definition.bodyRadius * 2.4f + 0.4f;
            agentSerialized.FindProperty("arriveRadius").floatValue = definition.attackRange * 0.75f;
            agentSerialized.ApplyModifiedPropertiesWithoutUndo();

            return AshfallAssetUtility.SavePrefab(root, $"{AshfallAssetUtility.PrefabFolder}/{root.name}.prefab");
        }

        /// <summary>Hunched, heavy-shouldered, arms hanging past the knees.</summary>
        private static void BuildShamblerBody(Transform parent, EnemyDefinition def, List<Renderer> renderers)
        {
            Material flesh = AshfallMaterialLibrary.EnemyFlesh;
            Material corrupt = AshfallMaterialLibrary.EnemyCorrupt;

            void Part(string n, Mesh m, Material mat, Vector3 p, Vector3 e = default)
            {
                renderers.Add(AddPart(parent, n, m, mat, p, e).GetComponent<Renderer>());
            }

            Part("Pelvis", AshfallGeometry.Box(new Vector3(0.46f, 0.30f, 0.30f), 0.7f), flesh, new Vector3(0f, 0.92f, 0f));
            Part("Torso", AshfallGeometry.Box(new Vector3(0.62f, 0.66f, 0.36f), 0.7f), flesh, new Vector3(0f, 1.30f, 0.06f), new Vector3(14f, 0f, 0f));
            Part("Shoulders", AshfallGeometry.Box(new Vector3(0.82f, 0.20f, 0.34f), 0.7f), flesh, new Vector3(0f, 1.55f, 0.10f), new Vector3(10f, 0f, 0f));
            Part("Head", AshfallGeometry.Box(new Vector3(0.30f, 0.34f, 0.30f), 0.5f), flesh, new Vector3(0f, 1.72f, 0.16f), new Vector3(22f, 0f, 0f));
            Part("Jaw", AshfallGeometry.Box(new Vector3(0.22f, 0.10f, 0.20f), 0.4f), corrupt, new Vector3(0f, 1.62f, 0.28f), new Vector3(22f, 0f, 0f));

            Part("ArmL", AshfallGeometry.Box(new Vector3(0.17f, 0.78f, 0.17f), 0.7f), flesh, new Vector3(-0.44f, 1.16f, 0.12f), new Vector3(-16f, 0f, -6f));
            Part("ArmR", AshfallGeometry.Box(new Vector3(0.17f, 0.78f, 0.17f), 0.7f), flesh, new Vector3(0.44f, 1.16f, 0.12f), new Vector3(-16f, 0f, 6f));
            Part("HandL", AshfallGeometry.Box(new Vector3(0.20f, 0.20f, 0.22f), 0.4f), corrupt, new Vector3(-0.50f, 0.78f, 0.26f));
            Part("HandR", AshfallGeometry.Box(new Vector3(0.20f, 0.20f, 0.22f), 0.4f), corrupt, new Vector3(0.50f, 0.78f, 0.26f));

            Part("LegL", AshfallGeometry.Box(new Vector3(0.20f, 0.86f, 0.22f), 0.7f), flesh, new Vector3(-0.16f, 0.42f, 0f));
            Part("LegR", AshfallGeometry.Box(new Vector3(0.20f, 0.86f, 0.22f), 0.7f), flesh, new Vector3(0.16f, 0.42f, 0f));

            // Storm corruption running up the spine: the tell that reads in fog.
            Part("SpineVein", AshfallGeometry.Box(new Vector3(0.09f, 0.60f, 0.06f), 0.4f), corrupt, new Vector3(0f, 1.32f, -0.16f), new Vector3(14f, 0f, 0f));
            Part("ChestCore", AshfallGeometry.Box(new Vector3(0.16f, 0.16f, 0.08f), 0.3f), corrupt, new Vector3(0f, 1.38f, 0.25f), new Vector3(14f, 0f, 0f));
        }

        /// <summary>Low, pitched forward, long shins. Built to read as fast even standing still.</summary>
        private static void BuildSprinterBody(Transform parent, EnemyDefinition def, List<Renderer> renderers)
        {
            Material flesh = AshfallMaterialLibrary.EnemyFlesh;
            Material corrupt = AshfallMaterialLibrary.EnemyCorrupt;

            void Part(string n, Mesh m, Material mat, Vector3 p, Vector3 e = default)
            {
                renderers.Add(AddPart(parent, n, m, mat, p, e).GetComponent<Renderer>());
            }

            Part("Pelvis", AshfallGeometry.Box(new Vector3(0.34f, 0.24f, 0.26f), 0.6f), flesh, new Vector3(0f, 0.86f, -0.06f));
            Part("Torso", AshfallGeometry.Box(new Vector3(0.44f, 0.56f, 0.28f), 0.6f), flesh, new Vector3(0f, 1.16f, 0.10f), new Vector3(34f, 0f, 0f));
            Part("Neck", AshfallGeometry.Box(new Vector3(0.14f, 0.20f, 0.14f), 0.4f), corrupt, new Vector3(0f, 1.38f, 0.28f), new Vector3(34f, 0f, 0f));
            Part("Head", AshfallGeometry.Box(new Vector3(0.24f, 0.22f, 0.32f), 0.4f), flesh, new Vector3(0f, 1.44f, 0.42f), new Vector3(34f, 0f, 0f));
            Part("Eyes", AshfallGeometry.Box(new Vector3(0.20f, 0.06f, 0.05f), 0.2f), corrupt, new Vector3(0f, 1.47f, 0.57f), new Vector3(34f, 0f, 0f));

            Part("ArmL", AshfallGeometry.Box(new Vector3(0.12f, 0.62f, 0.12f), 0.6f), flesh, new Vector3(-0.30f, 1.10f, 0.14f), new Vector3(-42f, 0f, -8f));
            Part("ArmR", AshfallGeometry.Box(new Vector3(0.12f, 0.62f, 0.12f), 0.6f), flesh, new Vector3(0.30f, 1.10f, 0.14f), new Vector3(-42f, 0f, 8f));

            Part("ThighL", AshfallGeometry.Box(new Vector3(0.16f, 0.48f, 0.18f), 0.6f), flesh, new Vector3(-0.13f, 0.62f, -0.02f), new Vector3(-14f, 0f, 0f));
            Part("ThighR", AshfallGeometry.Box(new Vector3(0.16f, 0.48f, 0.18f), 0.6f), flesh, new Vector3(0.13f, 0.62f, -0.02f), new Vector3(-14f, 0f, 0f));
            Part("ShinL", AshfallGeometry.Box(new Vector3(0.12f, 0.46f, 0.14f), 0.6f), flesh, new Vector3(-0.13f, 0.24f, 0.06f), new Vector3(16f, 0f, 0f));
            Part("ShinR", AshfallGeometry.Box(new Vector3(0.12f, 0.46f, 0.14f), 0.6f), flesh, new Vector3(0.13f, 0.24f, 0.06f), new Vector3(16f, 0f, 0f));

            Part("SpineVein", AshfallGeometry.Box(new Vector3(0.07f, 0.52f, 0.05f), 0.3f), corrupt, new Vector3(0f, 1.18f, -0.06f), new Vector3(34f, 0f, 0f));
            Part("RibGlowL", AshfallGeometry.Box(new Vector3(0.05f, 0.28f, 0.05f), 0.2f), corrupt, new Vector3(-0.19f, 1.16f, 0.14f), new Vector3(34f, 0f, 0f));
            Part("RibGlowR", AshfallGeometry.Box(new Vector3(0.05f, 0.28f, 0.05f), 0.2f), corrupt, new Vector3(0.19f, 1.16f, 0.14f), new Vector3(34f, 0f, 0f));
        }

        /// <summary>A wall of plated metal with a storm reactor in its chest.</summary>
        private static void BuildBruteBody(Transform parent, EnemyDefinition def, List<Renderer> renderers)
        {
            Material armour = AshfallMaterialLibrary.BruteArmour;
            Material corrupt = AshfallMaterialLibrary.EnemyCorrupt;
            Material rust = AshfallMaterialLibrary.RustedMetal;

            void Part(string n, Mesh m, Material mat, Vector3 p, Vector3 e = default)
            {
                renderers.Add(AddPart(parent, n, m, mat, p, e).GetComponent<Renderer>());
            }

            Part("Pelvis", AshfallGeometry.Box(new Vector3(1.02f, 0.52f, 0.68f), 1.1f), armour, new Vector3(0f, 1.18f, 0f));
            Part("Torso", AshfallGeometry.Box(new Vector3(1.28f, 1.02f, 0.82f), 1.1f), armour, new Vector3(0f, 1.86f, 0.04f), new Vector3(8f, 0f, 0f));
            Part("ChestPlate", AshfallGeometry.Box(new Vector3(1.06f, 0.62f, 0.18f), 0.8f), rust, new Vector3(0f, 1.98f, 0.44f), new Vector3(8f, 0f, 0f));
            Part("Reactor", AshfallGeometry.Cylinder(0.24f, 0.20f, 12, 0.5f), corrupt, new Vector3(0f, 1.98f, 0.54f), new Vector3(90f, 0f, 0f));

            Part("ShoulderL", AshfallGeometry.Box(new Vector3(0.52f, 0.56f, 0.62f), 0.9f), armour, new Vector3(-0.84f, 2.24f, 0.02f), new Vector3(0f, 0f, 14f));
            Part("ShoulderR", AshfallGeometry.Box(new Vector3(0.52f, 0.56f, 0.62f), 0.9f), armour, new Vector3(0.84f, 2.24f, 0.02f), new Vector3(0f, 0f, -14f));
            Part("VentL", AshfallGeometry.Box(new Vector3(0.10f, 0.36f, 0.44f), 0.4f), corrupt, new Vector3(-1.02f, 2.30f, 0.02f));
            Part("VentR", AshfallGeometry.Box(new Vector3(0.10f, 0.36f, 0.44f), 0.4f), corrupt, new Vector3(1.02f, 2.30f, 0.02f));

            Part("Head", AshfallGeometry.Box(new Vector3(0.44f, 0.38f, 0.46f), 0.6f), armour, new Vector3(0f, 2.52f, 0.12f));
            Part("Visor", AshfallGeometry.Box(new Vector3(0.34f, 0.09f, 0.06f), 0.3f), corrupt, new Vector3(0f, 2.54f, 0.36f));

            Part("ArmL", AshfallGeometry.Box(new Vector3(0.34f, 1.06f, 0.34f), 0.9f), armour, new Vector3(-0.92f, 1.60f, 0.06f), new Vector3(-8f, 0f, -5f));
            Part("ArmR", AshfallGeometry.Box(new Vector3(0.34f, 1.06f, 0.34f), 0.9f), armour, new Vector3(0.92f, 1.60f, 0.06f), new Vector3(-8f, 0f, 5f));
            Part("FistL", AshfallGeometry.Box(new Vector3(0.50f, 0.44f, 0.52f), 0.6f), rust, new Vector3(-0.98f, 1.04f, 0.20f));
            Part("FistR", AshfallGeometry.Box(new Vector3(0.50f, 0.44f, 0.52f), 0.6f), rust, new Vector3(0.98f, 1.04f, 0.20f));

            Part("LegL", AshfallGeometry.Box(new Vector3(0.42f, 1.02f, 0.46f), 0.9f), armour, new Vector3(-0.32f, 0.52f, 0f));
            Part("LegR", AshfallGeometry.Box(new Vector3(0.42f, 1.02f, 0.46f), 0.9f), armour, new Vector3(0.32f, 0.52f, 0f));
            Part("FootL", AshfallGeometry.Box(new Vector3(0.50f, 0.16f, 0.68f), 0.6f), rust, new Vector3(-0.32f, 0.08f, 0.10f));
            Part("FootR", AshfallGeometry.Box(new Vector3(0.50f, 0.16f, 0.68f), 0.6f), rust, new Vector3(0.32f, 0.08f, 0.10f));

            Part("SpineRod", AshfallGeometry.Box(new Vector3(0.14f, 0.90f, 0.10f), 0.5f), corrupt, new Vector3(0f, 1.92f, -0.44f), new Vector3(8f, 0f, 0f));
        }

        // ------------------------------------------------------------------
        // Weapon viewmodels
        // ------------------------------------------------------------------

        private static void BuildWeapons()
        {
            SidearmViewModel = BuildSidearm();
            ShotgunViewModel = BuildShotgun();
            RifleViewModel = BuildRifle();
        }

        private static GameObject BeginViewModel(string name, out Transform root)
        {
            var go = new GameObject(name);
            root = go.transform;
            go.AddComponent<WeaponViewModel>();
            return go;
        }

        private static GameObject FinishViewModel(
            GameObject go,
            Transform muzzle,
            Transform slide,
            Transform magazine,
            Color accent)
        {
            var flash = Object.Instantiate(MuzzleFlashPrefab, muzzle);
            flash.name = "MuzzleFlash";
            flash.transform.localPosition = Vector3.zero;
            flash.transform.localRotation = Quaternion.identity;

            var lightGo = new GameObject("MuzzleLight");
            lightGo.transform.SetParent(muzzle, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 7f;
            light.intensity = 0f;
            light.color = accent;
            light.shadows = LightShadows.None;
            light.enabled = false;

            var view = go.GetComponent<WeaponViewModel>();
            view.Configure(muzzle, slide, magazine, flash, light);

            // Viewmodels sit in the FX layer so they never take part in world queries.
            AshfallAssetUtility.SetLayerRecursive(go, AshfallLayers.Fx);

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            return AshfallAssetUtility.SavePrefab(go, $"{AshfallAssetUtility.PrefabFolder}/{go.name}.prefab");
        }

        private static GameObject BuildSidearm()
        {
            GameObject go = BeginViewModel("VM_MeridianSidearm", out Transform root);
            Material body = AshfallMaterialLibrary.GunBody;
            Material accentMat = MakeAccent("M_Accent_Sidearm", AshfallPalette.EmergencyAmber);

            AddPart(root, "Frame", AshfallGeometry.Box(new Vector3(0.052f, 0.075f, 0.20f), 0.12f), body, new Vector3(0f, 0f, 0.03f));
            GameObject slide = AddPart(root, "Slide", AshfallGeometry.Box(new Vector3(0.056f, 0.052f, 0.215f), 0.12f), body, new Vector3(0f, 0.055f, 0.045f));
            AddPart(root, "Grip", AshfallGeometry.Box(new Vector3(0.048f, 0.135f, 0.062f), 0.1f), body, new Vector3(0f, -0.088f, -0.035f), new Vector3(-13f, 0f, 0f));
            GameObject magazine = AddPart(root, "Magazine", AshfallGeometry.Box(new Vector3(0.036f, 0.115f, 0.042f), 0.1f), AshfallMaterialLibrary.SteelDark, new Vector3(0f, -0.092f, -0.034f), new Vector3(-13f, 0f, 0f));
            AddPart(root, "TriggerGuard", AshfallGeometry.Box(new Vector3(0.030f, 0.045f, 0.010f), 0.06f), body, new Vector3(0f, -0.035f, 0.012f));
            AddPart(root, "Rail", AshfallGeometry.Box(new Vector3(0.030f, 0.010f, 0.13f), 0.08f), AshfallMaterialLibrary.SteelDark, new Vector3(0f, -0.028f, 0.088f));
            AddPart(root, "SightRear", AshfallGeometry.Box(new Vector3(0.040f, 0.014f, 0.014f), 0.05f), accentMat, new Vector3(0f, 0.088f, -0.040f));
            AddPart(root, "SightFront", AshfallGeometry.Box(new Vector3(0.010f, 0.016f, 0.012f), 0.05f), accentMat, new Vector3(0f, 0.090f, 0.140f));
            AddPart(root, "AccentStripe", AshfallGeometry.Box(new Vector3(0.058f, 0.008f, 0.09f), 0.06f), accentMat, new Vector3(0f, 0.030f, 0.060f));

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root, false);
            muzzle.localPosition = new Vector3(0f, 0.055f, 0.165f);

            return FinishViewModel(go, muzzle, slide.transform, magazine.transform, AshfallPalette.EmergencyAmber);
        }

        private static GameObject BuildShotgun()
        {
            GameObject go = BeginViewModel("VM_Breakwater", out Transform root);
            Material body = AshfallMaterialLibrary.GunBody;
            Material rust = AshfallMaterialLibrary.RustedMetal;
            Material accentMat = MakeAccent("M_Accent_Shotgun", AshfallPalette.RustDeep * 2.2f);

            AddPart(root, "Receiver", AshfallGeometry.Box(new Vector3(0.075f, 0.098f, 0.30f), 0.16f), body, new Vector3(0f, 0f, 0.02f));
            AddPart(root, "Barrel", AshfallGeometry.Cylinder(0.030f, 0.52f, 12, 0.2f), rust, new Vector3(0f, 0.030f, 0.40f), new Vector3(90f, 0f, 0f));
            AddPart(root, "MagTube", AshfallGeometry.Cylinder(0.024f, 0.46f, 10, 0.2f), body, new Vector3(0f, -0.032f, 0.36f), new Vector3(90f, 0f, 0f));
            GameObject slide = AddPart(root, "Pump", AshfallGeometry.Box(new Vector3(0.070f, 0.062f, 0.13f), 0.1f), AshfallMaterialLibrary.Timber, new Vector3(0f, -0.030f, 0.30f));
            AddPart(root, "Stock", AshfallGeometry.Box(new Vector3(0.055f, 0.090f, 0.24f), 0.14f), AshfallMaterialLibrary.Timber, new Vector3(0f, -0.052f, -0.24f), new Vector3(6f, 0f, 0f));
            AddPart(root, "Grip", AshfallGeometry.Box(new Vector3(0.050f, 0.120f, 0.060f), 0.1f), body, new Vector3(0f, -0.098f, -0.075f), new Vector3(-16f, 0f, 0f));
            GameObject magazine = AddPart(root, "ShellCarrier", AshfallGeometry.Box(new Vector3(0.090f, 0.030f, 0.14f), 0.1f), rust, new Vector3(0f, -0.062f, -0.045f));
            AddPart(root, "HeatShield", AshfallGeometry.Box(new Vector3(0.058f, 0.014f, 0.34f), 0.14f), rust, new Vector3(0f, 0.062f, 0.34f));
            AddPart(root, "Bead", AshfallGeometry.Box(new Vector3(0.012f, 0.016f, 0.012f), 0.04f), accentMat, new Vector3(0f, 0.070f, 0.62f));
            AddPart(root, "AccentBand", AshfallGeometry.Box(new Vector3(0.078f, 0.012f, 0.05f), 0.06f), accentMat, new Vector3(0f, 0.048f, 0.06f));

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root, false);
            muzzle.localPosition = new Vector3(0f, 0.030f, 0.66f);

            return FinishViewModel(go, muzzle, slide.transform, magazine.transform, AshfallPalette.EmergencyAmber);
        }

        private static GameObject BuildRifle()
        {
            GameObject go = BeginViewModel("VM_Arc9", out Transform root);
            Material body = AshfallMaterialLibrary.GunBody;
            Material steel = AshfallMaterialLibrary.SteelDark;
            Material accentMat = MakeAccent("M_Accent_Rifle", AshfallPalette.StormTeal);

            AddPart(root, "Receiver", AshfallGeometry.Box(new Vector3(0.062f, 0.088f, 0.34f), 0.16f), body, new Vector3(0f, 0f, 0.05f));
            AddPart(root, "Handguard", AshfallGeometry.Box(new Vector3(0.056f, 0.062f, 0.26f), 0.14f), steel, new Vector3(0f, -0.006f, 0.34f));
            AddPart(root, "Barrel", AshfallGeometry.Cylinder(0.017f, 0.30f, 10, 0.2f), steel, new Vector3(0f, 0.010f, 0.58f), new Vector3(90f, 0f, 0f));
            AddPart(root, "RailTop", AshfallGeometry.Box(new Vector3(0.032f, 0.012f, 0.50f), 0.2f), steel, new Vector3(0f, 0.052f, 0.22f));
            GameObject slide = AddPart(root, "ChargingHandle", AshfallGeometry.Box(new Vector3(0.070f, 0.024f, 0.075f), 0.08f), body, new Vector3(0f, 0.042f, -0.075f));
            AddPart(root, "Stock", AshfallGeometry.Box(new Vector3(0.048f, 0.080f, 0.22f), 0.14f), body, new Vector3(0f, -0.014f, -0.26f));
            AddPart(root, "Cheek", AshfallGeometry.Box(new Vector3(0.040f, 0.026f, 0.16f), 0.1f), steel, new Vector3(0f, 0.038f, -0.24f));
            AddPart(root, "Grip", AshfallGeometry.Box(new Vector3(0.046f, 0.115f, 0.058f), 0.1f), body, new Vector3(0f, -0.092f, -0.070f), new Vector3(-18f, 0f, 0f));
            GameObject magazine = AddPart(root, "Magazine", AshfallGeometry.Box(new Vector3(0.040f, 0.155f, 0.075f), 0.12f), steel, new Vector3(0f, -0.110f, 0.055f), new Vector3(7f, 0f, 0f));

            // The coil pack is the Arc-9's identity: three glowing rings along the barrel.
            for (int i = 0; i < 3; i++)
            {
                AddPart(root, $"Coil{i}", AshfallGeometry.Cylinder(0.030f, 0.022f, 12, 0.1f), accentMat,
                    new Vector3(0f, 0.010f, 0.48f + i * 0.075f), new Vector3(90f, 0f, 0f));
            }

            AddPart(root, "Optic", AshfallGeometry.Box(new Vector3(0.036f, 0.046f, 0.10f), 0.08f), body, new Vector3(0f, 0.085f, 0.055f));
            AddPart(root, "OpticGlass", AshfallGeometry.Box(new Vector3(0.028f, 0.030f, 0.006f), 0.04f), accentMat, new Vector3(0f, 0.085f, 0.108f));
            AddPart(root, "PowerCell", AshfallGeometry.Box(new Vector3(0.030f, 0.030f, 0.11f), 0.06f), accentMat, new Vector3(-0.048f, 0.006f, 0.02f));

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root, false);
            muzzle.localPosition = new Vector3(0f, 0.010f, 0.74f);

            return FinishViewModel(go, muzzle, slide.transform, magazine.transform, AshfallPalette.StormTeal);
        }

        private static Material MakeAccent(string name, Color color)
        {
            string path = $"{AshfallAssetUtility.MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.SetColor("_EmissionColor", color * 1.0f);
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = name };
            m.SetColor("_BaseColor", color * 0.3f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.6f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * 1.0f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ------------------------------------------------------------------
        // Power-up canister
        // ------------------------------------------------------------------

        private static void BuildPowerUp()
        {
            var root = new GameObject("PowerUp_Canister");
            root.layer = AshfallLayers.Fx;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            Material emissive = AshfallMaterialLibrary.EmissiveTeal;
            Material shell = AshfallMaterialLibrary.SteelDark;

            var renderers = new List<Renderer>();

            // An octahedral core inside an open cage: reads clearly from any angle and
            // the cage gives the spin something to catch light on.
            renderers.Add(AddPart(visual.transform, "Core",
                AshfallGeometry.Box(new Vector3(0.30f, 0.30f, 0.30f), 0.3f), emissive,
                Vector3.zero, new Vector3(45f, 0f, 45f), false).GetComponent<Renderer>());

            renderers.Add(AddPart(visual.transform, "CageX",
                AshfallGeometry.Box(new Vector3(0.52f, 0.035f, 0.035f), 0.3f), shell,
                Vector3.zero, Vector3.zero, false).GetComponent<Renderer>());
            renderers.Add(AddPart(visual.transform, "CageY",
                AshfallGeometry.Box(new Vector3(0.035f, 0.52f, 0.035f), 0.3f), shell,
                Vector3.zero, Vector3.zero, false).GetComponent<Renderer>());
            renderers.Add(AddPart(visual.transform, "CageZ",
                AshfallGeometry.Box(new Vector3(0.035f, 0.035f, 0.52f), 0.3f), shell,
                Vector3.zero, Vector3.zero, false).GetComponent<Renderer>());

            for (int i = 0; i < 4; i++)
            {
                renderers.Add(AddPart(visual.transform, $"Fin{i}",
                    AshfallGeometry.Box(new Vector3(0.02f, 0.18f, 0.18f), 0.2f), emissive,
                    Vector3.zero, new Vector3(0f, i * 45f, 0f), false).GetComponent<Renderer>());
            }

            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(root.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 7.5f;
            light.intensity = 4.5f;
            light.shadows = LightShadows.None;

            var pickup = root.AddComponent<PowerUpPickup>();
            pickup.Configure(visual.transform, renderers.ToArray(), light);

            PowerUpPrefab = AshfallAssetUtility.SavePrefab(root, $"{AshfallAssetUtility.PrefabFolder}/PowerUp_Canister.prefab");
        }
    }
}
