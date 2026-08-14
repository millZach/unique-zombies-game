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
            return AddPart(parent, name, mesh, material, localPosition, Quaternion.Euler(localEuler), castShadows);
        }

        private static GameObject AddPart(
            Transform parent,
            string name,
            Mesh mesh,
            Material material,
            Vector3 localPosition,
            Quaternion localRotation,
            bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;

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

        /// <summary>
        /// Places a tapered limb between two joint positions.
        ///
        /// Authoring bodies by joint rather than by "box at position, rotated
        /// by these Euler angles" is the whole reason the new silhouettes hold
        /// together: a knee is a point both bones agree on, so nothing floats
        /// or interpenetrates when a proportion is nudged. Mirrored limbs of
        /// equal length share one cached mesh.
        /// </summary>
        private static GameObject AddBone(
            Transform parent,
            string goName,
            string shapeKey,
            Material material,
            Vector3 from,
            Vector3 to,
            float rootRadius,
            float midRadius,
            float tipRadius,
            float bend = 0f,
            int segments = 8,
            List<Renderer> renderers = null)
        {
            float length = Vector3.Distance(from, to);
            Vector3 direction = length > 1e-5f ? (to - from) / length : Vector3.up;

            Mesh mesh = AshfallGeometry.Limb(shapeKey, length, rootRadius, midRadius, tipRadius, bend, segments);
            GameObject go = AddPart(
                parent, goName, mesh, material,
                (from + to) * 0.5f,
                Quaternion.FromToRotation(Vector3.up, direction));

            renderers?.Add(go.GetComponent<Renderer>());
            return go;
        }

        /// <summary>Convenience: a lofted body section described by (height, forward, halfWidth, halfDepth).</summary>
        private static Mesh BodySection(string key, params Vector4[] rings)
        {
            var loft = new List<AshfallGeometry.LoftRing>(rings.Length);
            for (int i = 0; i < rings.Length; i++)
            {
                loft.Add(new AshfallGeometry.LoftRing(
                    new Vector3(0f, rings[i].x, rings[i].y), rings[i].z, rings[i].w));
            }

            return AshfallGeometry.Loft(key, loft, segments: 12, capStart: true, capEnd: true, tileSize: 0.5f);
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

            // The procedural body lives under its own root so an approved
            // Meshcaster model can take its place without the death squash,
            // the gait bob or the hit flash needing to know which one is on.
            var procedural = new GameObject("Procedural");
            procedural.transform.SetParent(visual.transform, false);

            var proceduralRenderers = new List<Renderer>();
            bodyBuilder(procedural.transform, definition, proceduralRenderers);

            var renderers = new List<Renderer>();
            GameObject imported = AshfallMeshcasterImport.TryAttach(
                visual.transform, AshfallMeshcasterImport.KeyForArchetype(definition.archetype), renderers);

            if (imported != null)
            {
                procedural.SetActive(false);
            }
            else
            {
                renderers.AddRange(proceduralRenderers);
            }

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

        /// <summary>
        /// Hunched, heavy-shouldered, arms hanging past the knees.
        ///
        /// The hunch is in the spine itself: the torso is one lofted surface
        /// whose rings climb and move forward together, so the back curves
        /// instead of stepping. That single change is most of the difference
        /// between "stacked crates" and "a body that is bent over".
        /// </summary>
        private static void BuildShamblerBody(Transform parent, EnemyDefinition def, List<Renderer> renderers)
        {
            Material flesh = AshfallMaterialLibrary.EnemyFlesh;
            Material corrupt = AshfallMaterialLibrary.EnemyCorrupt;

            void Part(string n, Mesh m, Material mat, Vector3 p, Vector3 e = default)
            {
                renderers.Add(AddPart(parent, n, m, mat, p, e).GetComponent<Renderer>());
            }

            // --- torso: one continuous, forward-curving surface ---------------
            Part("Torso", BodySection("ShamblerTorso",
                    new Vector4(0.84f, -0.02f, 0.175f, 0.125f),   // hips
                    new Vector4(0.99f, 0.00f, 0.185f, 0.130f),    // waist
                    new Vector4(1.14f, 0.035f, 0.230f, 0.150f),   // lower ribs
                    new Vector4(1.29f, 0.085f, 0.268f, 0.163f),   // chest
                    new Vector4(1.42f, 0.140f, 0.285f, 0.158f),   // upper chest
                    new Vector4(1.52f, 0.190f, 0.230f, 0.135f)),  // shoulder girdle
                flesh, Vector3.zero);

            Part("DeltoidL", AshfallGeometry.Ellipsoid("ShamblerDeltoid", new Vector3(0.115f, 0.105f, 0.115f), 10, 7),
                flesh, new Vector3(-0.265f, 1.455f, 0.150f));
            Part("DeltoidR", AshfallGeometry.Ellipsoid("ShamblerDeltoid", new Vector3(0.115f, 0.105f, 0.115f), 10, 7),
                flesh, new Vector3(0.265f, 1.455f, 0.150f));

            // --- head, hung low and forward -----------------------------------
            AddBone(parent, "Neck", "ShamblerNeck", flesh,
                new Vector3(0f, 1.520f, 0.185f), new Vector3(0f, 1.605f, 0.265f),
                0.080f, 0.072f, 0.070f, renderers: renderers);

            Part("Head", AshfallGeometry.Ellipsoid("ShamblerHead", new Vector3(0.113f, 0.132f, 0.142f), 12, 8),
                flesh, new Vector3(0f, 1.665f, 0.300f), new Vector3(26f, 0f, 0f));
            Part("Jaw", AshfallGeometry.Ellipsoid("ShamblerJaw", new Vector3(0.078f, 0.048f, 0.088f), 10, 6),
                flesh, new Vector3(0f, 1.600f, 0.360f), new Vector3(18f, 0f, 0f));
            Part("Eyes", AshfallGeometry.Ellipsoid("ShamblerEyes", new Vector3(0.086f, 0.017f, 0.017f), 8, 5),
                corrupt, new Vector3(0f, 1.695f, 0.410f));

            // --- arms: long, heavy, hanging past the knee ---------------------
            for (int side = -1; side <= 1; side += 2)
            {
                string s = side < 0 ? "L" : "R";
                var shoulder = new Vector3(side * 0.275f, 1.450f, 0.145f);
                var elbow = new Vector3(side * 0.365f, 1.055f, 0.215f);
                var wrist = new Vector3(side * 0.400f, 0.700f, 0.275f);

                AddBone(parent, $"UpperArm{s}", "ShamblerUpperArm", flesh,
                    shoulder, elbow, 0.092f, 0.080f, 0.062f, bend: 0.022f, renderers: renderers);
                AddBone(parent, $"Forearm{s}", "ShamblerForearm", flesh,
                    elbow, wrist, 0.062f, 0.058f, 0.046f, bend: -0.018f, renderers: renderers);

                // Flesh, not corruption: an emissive hand at 0.06 m² is the
                // brightest thing on the model at every distance, and the
                // silhouette stops being a body and becomes two floating lamps.
                Part($"Hand{s}", AshfallGeometry.Ellipsoid("ShamblerHand", new Vector3(0.062f, 0.075f, 0.048f), 8, 6),
                    flesh, wrist + new Vector3(0f, -0.055f, 0.010f), new Vector3(12f, 0f, 0f));
            }

            // --- legs ----------------------------------------------------------
            for (int side = -1; side <= 1; side += 2)
            {
                string s = side < 0 ? "L" : "R";
                var hip = new Vector3(side * 0.135f, 0.855f, -0.010f);
                var knee = new Vector3(side * 0.150f, 0.455f, 0.030f);
                var ankle = new Vector3(side * 0.145f, 0.085f, -0.015f);

                AddBone(parent, $"Thigh{s}", "ShamblerThigh", flesh,
                    hip, knee, 0.118f, 0.104f, 0.078f, bend: 0.020f, renderers: renderers);
                AddBone(parent, $"Shin{s}", "ShamblerShin", flesh,
                    knee, ankle, 0.078f, 0.068f, 0.050f, bend: -0.022f, renderers: renderers);

                Part($"Foot{s}", AshfallGeometry.Ellipsoid("ShamblerFoot", new Vector3(0.070f, 0.048f, 0.135f), 8, 5),
                    flesh, new Vector3(side * 0.145f, 0.050f, 0.040f));
            }

            // --- storm corruption: the tell that reads in fog -------------------
            Part("SpineVein", BodySection("ShamblerSpine",
                    new Vector4(0.95f, -0.108f, 0.019f, 0.014f),
                    new Vector4(1.14f, -0.093f, 0.024f, 0.017f),
                    new Vector4(1.33f, -0.052f, 0.021f, 0.015f),
                    new Vector4(1.48f, 0.008f, 0.014f, 0.011f)),
                corrupt, Vector3.zero);

            Part("ChestCore", AshfallGeometry.Ellipsoid("ShamblerCore", new Vector3(0.058f, 0.058f, 0.038f), 10, 6),
                corrupt, new Vector3(0f, 1.320f, 0.242f));
        }

        /// <summary>
        /// Low, pitched forward, long shins. Built to read as fast even standing still.
        ///
        /// Everything is pushed onto the front foot: the spine leans nearly
        /// forty degrees, the head is further forward than the hips, and the
        /// legs are folded rather than straight. A sprinter that is standing
        /// still should still look like it is about to leave.
        /// </summary>
        private static void BuildSprinterBody(Transform parent, EnemyDefinition def, List<Renderer> renderers)
        {
            Material flesh = AshfallMaterialLibrary.EnemyFlesh;
            Material corrupt = AshfallMaterialLibrary.EnemyCorrupt;

            void Part(string n, Mesh m, Material mat, Vector3 p, Vector3 e = default)
            {
                renderers.Add(AddPart(parent, n, m, mat, p, e).GetComponent<Renderer>());
            }

            Part("Torso", BodySection("SprinterTorso",
                    new Vector4(0.80f, -0.120f, 0.140f, 0.108f),  // hips, pushed back
                    new Vector4(0.92f, -0.060f, 0.148f, 0.112f),
                    new Vector4(1.05f, 0.020f, 0.190f, 0.128f),   // ribcage
                    new Vector4(1.17f, 0.120f, 0.198f, 0.124f),
                    new Vector4(1.27f, 0.225f, 0.155f, 0.104f)),  // shoulders, well forward
                flesh, Vector3.zero);

            Part("DeltoidL", AshfallGeometry.Ellipsoid("SprinterDeltoid", new Vector3(0.078f, 0.070f, 0.078f), 8, 6),
                flesh, new Vector3(-0.180f, 1.245f, 0.215f));
            Part("DeltoidR", AshfallGeometry.Ellipsoid("SprinterDeltoid", new Vector3(0.078f, 0.070f, 0.078f), 8, 6),
                flesh, new Vector3(0.180f, 1.245f, 0.215f));

            AddBone(parent, "Neck", "SprinterNeck", corrupt,
                new Vector3(0f, 1.275f, 0.240f), new Vector3(0f, 1.335f, 0.340f),
                0.062f, 0.056f, 0.052f, renderers: renderers);

            // A long, narrow skull pushed out ahead of the shoulders.
            Part("Head", AshfallGeometry.Ellipsoid("SprinterHead", new Vector3(0.088f, 0.092f, 0.150f), 12, 8),
                flesh, new Vector3(0f, 1.360f, 0.430f), new Vector3(14f, 0f, 0f));
            Part("Eyes", AshfallGeometry.Ellipsoid("SprinterEyes", new Vector3(0.080f, 0.014f, 0.016f), 8, 5),
                corrupt, new Vector3(0f, 1.385f, 0.550f));

            for (int side = -1; side <= 1; side += 2)
            {
                string s = side < 0 ? "L" : "R";
                var shoulder = new Vector3(side * 0.175f, 1.240f, 0.205f);
                var elbow = new Vector3(side * 0.225f, 0.995f, 0.050f);
                var wrist = new Vector3(side * 0.245f, 0.790f, 0.180f);

                AddBone(parent, $"UpperArm{s}", "SprinterUpperArm", flesh,
                    shoulder, elbow, 0.062f, 0.054f, 0.042f, bend: -0.020f, renderers: renderers);
                AddBone(parent, $"Forearm{s}", "SprinterForearm", flesh,
                    elbow, wrist, 0.044f, 0.040f, 0.030f, bend: 0.016f, renderers: renderers);

                Part($"Hand{s}", AshfallGeometry.Ellipsoid("SprinterHand", new Vector3(0.038f, 0.058f, 0.030f), 8, 5),
                    flesh, wrist + new Vector3(0f, -0.042f, 0.028f), new Vector3(-30f, 0f, 0f));
                Part($"Claw{s}", AshfallGeometry.Ellipsoid("SprinterClaw", new Vector3(0.026f, 0.030f, 0.016f), 6, 4),
                    corrupt, wrist + new Vector3(0f, -0.088f, 0.062f), new Vector3(-30f, 0f, 0f));
            }

            // Folded, digitigrade legs: knee forward, ankle high and back.
            for (int side = -1; side <= 1; side += 2)
            {
                string s = side < 0 ? "L" : "R";
                var hip = new Vector3(side * 0.105f, 0.820f, -0.095f);
                var knee = new Vector3(side * 0.118f, 0.520f, 0.075f);
                var ankle = new Vector3(side * 0.115f, 0.180f, -0.055f);
                var toe = new Vector3(side * 0.112f, 0.030f, 0.060f);

                AddBone(parent, $"Thigh{s}", "SprinterThigh", flesh,
                    hip, knee, 0.085f, 0.074f, 0.052f, bend: 0.018f, renderers: renderers);
                AddBone(parent, $"Shin{s}", "SprinterShin", flesh,
                    knee, ankle, 0.052f, 0.046f, 0.034f, bend: -0.014f, renderers: renderers);
                AddBone(parent, $"Foot{s}", "SprinterFoot", flesh,
                    ankle, toe, 0.034f, 0.032f, 0.024f, renderers: renderers);
            }

            Part("SpineVein", BodySection("SprinterSpine",
                    new Vector4(0.88f, -0.175f, 0.028f, 0.020f),
                    new Vector4(1.02f, -0.110f, 0.034f, 0.024f),
                    new Vector4(1.16f, -0.010f, 0.030f, 0.022f),
                    new Vector4(1.25f, 0.105f, 0.022f, 0.017f)),
                corrupt, Vector3.zero);

            // Ribs lit from inside: the sprinter's signature at range.
            for (int side = -1; side <= 1; side += 2)
            {
                string s = side < 0 ? "L" : "R";
                for (int rib = 0; rib < 3; rib++)
                {
                    float t = rib / 2f;
                    Part($"RibGlow{s}{rib}",
                        AshfallGeometry.Ellipsoid("SprinterRib", new Vector3(0.018f, 0.016f, 0.058f), 6, 4),
                        corrupt,
                        new Vector3(side * (0.150f - t * 0.020f), 1.030f + t * 0.075f, 0.045f + t * 0.070f),
                        new Vector3(0f, 0f, side * 18f));
                }
            }
        }

        /// <summary>
        /// A wall of plated metal with a storm reactor in its chest.
        ///
        /// The brute deliberately stays angular where the other two went
        /// organic -- it is salvage-tech armour, not a body -- but every plate
        /// is chamfered rather than a raw box, so the edges catch a highlight
        /// and the mass reads as machined instead of untextured.
        /// </summary>
        private static void BuildBruteBody(Transform parent, EnemyDefinition def, List<Renderer> renderers)
        {
            Material armour = AshfallMaterialLibrary.BruteArmour;
            Material corrupt = AshfallMaterialLibrary.EnemyCorrupt;
            Material rust = AshfallMaterialLibrary.RustedMetal;

            void Part(string n, Mesh m, Material mat, Vector3 p, Vector3 e = default)
            {
                renderers.Add(AddPart(parent, n, m, mat, p, e).GetComponent<Renderer>());
            }

            // --- torso: a tapering armoured drum, not a cube --------------------
            Part("Torso", BodySection("BruteTorso",
                    new Vector4(1.00f, 0.00f, 0.480f, 0.330f),
                    new Vector4(1.42f, 0.02f, 0.545f, 0.360f),
                    new Vector4(1.84f, 0.05f, 0.640f, 0.405f),
                    new Vector4(2.18f, 0.06f, 0.610f, 0.380f),
                    new Vector4(2.36f, 0.05f, 0.430f, 0.300f)),
                armour, Vector3.zero);

            Part("ChestPlate", AshfallGeometry.Chamfer(new Vector3(0.96f, 0.66f, 0.16f), 0.055f, 0.5f),
                rust, new Vector3(0f, 1.96f, 0.400f), new Vector3(9f, 0f, 0f));
            Part("HipPlate", AshfallGeometry.Chamfer(new Vector3(1.00f, 0.34f, 0.62f), 0.070f, 0.5f),
                armour, new Vector3(0f, 1.10f, 0.010f));
            Part("Reactor", AshfallGeometry.Cylinder(0.165f, 0.175f, 16, 0.5f),
                corrupt, new Vector3(0f, 1.96f, 0.495f), new Vector3(90f, 0f, 0f));
            Part("ReactorRing", AshfallGeometry.Cylinder(0.245f, 0.090f, 16, 0.5f),
                rust, new Vector3(0f, 1.96f, 0.465f), new Vector3(90f, 0f, 0f));

            Part("Head", AshfallGeometry.Chamfer(new Vector3(0.42f, 0.38f, 0.46f), 0.075f, 0.5f),
                armour, new Vector3(0f, 2.53f, 0.100f));
            Part("Visor", AshfallGeometry.Chamfer(new Vector3(0.32f, 0.075f, 0.05f), 0.018f, 0.3f),
                corrupt, new Vector3(0f, 2.545f, 0.335f));

            for (int side = -1; side <= 1; side += 2)
            {
                string s = side < 0 ? "L" : "R";

                Part($"Shoulder{s}", AshfallGeometry.Chamfer(new Vector3(0.50f, 0.54f, 0.60f), 0.105f, 0.6f),
                    armour, new Vector3(side * 0.825f, 2.22f, 0.020f), new Vector3(0f, 0f, side * -14f));
                Part($"Vent{s}", AshfallGeometry.Chamfer(new Vector3(0.09f, 0.34f, 0.42f), 0.028f, 0.4f),
                    corrupt, new Vector3(side * 1.020f, 2.29f, 0.020f));

                var shoulder = new Vector3(side * 0.800f, 2.030f, 0.030f);
                var elbow = new Vector3(side * 0.915f, 1.400f, 0.075f);
                var wrist = new Vector3(side * 0.950f, 0.900f, 0.140f);

                AddBone(parent, $"UpperArm{s}", "BruteUpperArm", armour,
                    shoulder, elbow, 0.220f, 0.195f, 0.150f, bend: 0.035f, segments: 10, renderers: renderers);
                AddBone(parent, $"Forearm{s}", "BruteForearm", armour,
                    elbow, wrist, 0.175f, 0.170f, 0.145f, bend: -0.025f, segments: 10, renderers: renderers);

                Part($"Fist{s}", AshfallGeometry.Chamfer(new Vector3(0.44f, 0.40f, 0.46f), 0.085f, 0.5f),
                    rust, new Vector3(side * 0.955f, 0.760f, 0.165f), new Vector3(10f, 0f, 0f));

                var hip = new Vector3(side * 0.290f, 1.000f, 0.000f);
                var knee = new Vector3(side * 0.315f, 0.545f, 0.055f);
                var ankle = new Vector3(side * 0.310f, 0.135f, -0.010f);

                AddBone(parent, $"Thigh{s}", "BruteThigh", armour,
                    hip, knee, 0.245f, 0.220f, 0.175f, bend: 0.030f, segments: 10, renderers: renderers);
                AddBone(parent, $"Shin{s}", "BruteShin", armour,
                    knee, ankle, 0.185f, 0.170f, 0.140f, bend: -0.020f, segments: 10, renderers: renderers);

                Part($"Foot{s}", AshfallGeometry.Chamfer(new Vector3(0.46f, 0.16f, 0.64f), 0.045f, 0.5f),
                    rust, new Vector3(side * 0.310f, 0.080f, 0.095f));
            }

            Part("SpineRod", BodySection("BruteSpine",
                    new Vector4(1.30f, -0.400f, 0.062f, 0.040f),
                    new Vector4(1.70f, -0.430f, 0.075f, 0.048f),
                    new Vector4(2.10f, -0.415f, 0.068f, 0.044f),
                    new Vector4(2.34f, -0.360f, 0.048f, 0.032f)),
                corrupt, Vector3.zero);
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
            Color accent,
            string meshcasterKey = null)
        {
            // An approved Meshcaster body replaces the visible parts but not the
            // rig: `slide` and `magazine` stay wired to their now-hidden
            // procedural transforms so the reload animation still has something
            // to drive, and the root tilt and equip slide -- which is what the
            // player actually reads -- apply to the imported mesh for free.
            if (!string.IsNullOrEmpty(meshcasterKey)
                && AshfallMeshcasterImport.HasApprovedModel(meshcasterKey))
            {
                var procedural = new GameObject("Procedural");
                procedural.transform.SetParent(go.transform, false);

                var toMove = new List<Transform>();
                foreach (Transform child in go.transform)
                {
                    if (child != procedural.transform && child != muzzle)
                    {
                        toMove.Add(child);
                    }
                }

                for (int i = 0; i < toMove.Count; i++)
                {
                    toMove[i].SetParent(procedural.transform, true);
                }

                AshfallMeshcasterImport.TryAttach(go.transform, meshcasterKey, null);

                // Hidden, not destroyed: destroying them would null the slide
                // and magazine references the viewmodel serialises.
                foreach (Renderer r in procedural.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = false;
                }
            }

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

        /// <summary>A rifled barrel with a slight muzzle swell, in one lofted piece.</summary>
        private static Mesh Barrel(string key, float length, float breechRadius, float muzzleRadius, int segments = 12)
        {
            var rings = new List<AshfallGeometry.LoftRing>
            {
                new(new Vector3(0f, -length * 0.5f, 0f), breechRadius * 1.16f),
                new(new Vector3(0f, -length * 0.5f + length * 0.10f, 0f), breechRadius),
                new(new Vector3(0f, length * 0.5f - length * 0.14f, 0f), Mathf.Lerp(breechRadius, muzzleRadius, 0.85f)),
                new(new Vector3(0f, length * 0.5f - length * 0.04f, 0f), muzzleRadius * 1.18f),
                new(new Vector3(0f, length * 0.5f, 0f), muzzleRadius * 1.12f)
            };

            return AshfallGeometry.Loft(key, rings, segments, capStart: true, capEnd: true, tileSize: 0.25f);
        }

        private static GameObject BuildSidearm()
        {
            GameObject go = BeginViewModel("VM_MeridianSidearm", out Transform root);
            Material body = AshfallMaterialLibrary.GunBody;
            Material steel = AshfallMaterialLibrary.SteelDark;
            Material accentMat = MakeAccent("M_Accent_Sidearm", AshfallPalette.EmergencyAmber);

            AddPart(root, "Frame", AshfallGeometry.Chamfer(new Vector3(0.050f, 0.072f, 0.200f), 0.008f), body, new Vector3(0f, 0f, 0.030f));
            GameObject slide = AddPart(root, "Slide", AshfallGeometry.Chamfer(new Vector3(0.054f, 0.050f, 0.215f), 0.009f), body, new Vector3(0f, 0.055f, 0.045f));
            AddPart(root, "SlideSerrations", AshfallGeometry.Chamfer(new Vector3(0.056f, 0.030f, 0.048f), 0.004f), steel, new Vector3(0f, 0.055f, -0.042f));
            AddPart(root, "Barrel", Barrel("SidearmBarrel", 0.070f, 0.013f, 0.011f, 10), steel, new Vector3(0f, 0.055f, 0.150f), new Vector3(90f, 0f, 0f));

            // The grip is a lofted, swelling column with a backstrap curve --
            // the one part of a pistol the eye reads as ergonomic or not.
            AddPart(root, "Grip", AshfallGeometry.Loft("SidearmGrip", new List<AshfallGeometry.LoftRing>
            {
                new(new Vector3(0f, -0.150f, -0.062f), 0.026f, 0.020f),
                new(new Vector3(0f, -0.115f, -0.056f), 0.027f, 0.024f),
                new(new Vector3(0f, -0.070f, -0.044f), 0.026f, 0.027f),
                new(new Vector3(0f, -0.030f, -0.030f), 0.025f, 0.026f),
                new(new Vector3(0f, -0.004f, -0.020f), 0.025f, 0.030f)
            }, 10, true, true, 0.25f), body, Vector3.zero);

            GameObject magazine = AddPart(root, "Magazine", AshfallGeometry.Chamfer(new Vector3(0.034f, 0.112f, 0.040f), 0.005f), steel, new Vector3(0f, -0.092f, -0.038f), new Vector3(-11f, 0f, 0f));
            AddPart(root, "TriggerGuard", AshfallGeometry.Loft("SidearmGuard", new List<AshfallGeometry.LoftRing>
            {
                new(new Vector3(0f, -0.014f, -0.014f), 0.006f),
                new(new Vector3(0f, -0.054f, 0.004f), 0.006f),
                new(new Vector3(0f, -0.050f, 0.038f), 0.006f),
                new(new Vector3(0f, -0.020f, 0.046f), 0.006f)
            }, 8, true, true, 0.25f), body, Vector3.zero);

            AddPart(root, "Trigger", AshfallGeometry.Chamfer(new Vector3(0.010f, 0.032f, 0.008f), 0.003f), steel, new Vector3(0f, -0.030f, 0.008f), new Vector3(-8f, 0f, 0f));
            AddPart(root, "Rail", AshfallGeometry.Chamfer(new Vector3(0.028f, 0.010f, 0.120f), 0.003f), steel, new Vector3(0f, -0.028f, 0.088f));
            AddPart(root, "SightRear", AshfallGeometry.Chamfer(new Vector3(0.038f, 0.013f, 0.013f), 0.003f), accentMat, new Vector3(0f, 0.086f, -0.040f));
            AddPart(root, "SightFront", AshfallGeometry.Chamfer(new Vector3(0.009f, 0.015f, 0.011f), 0.003f), accentMat, new Vector3(0f, 0.088f, 0.140f));
            AddPart(root, "AccentStripe", AshfallGeometry.Chamfer(new Vector3(0.056f, 0.007f, 0.088f), 0.002f), accentMat, new Vector3(0f, 0.030f, 0.060f));

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root, false);
            muzzle.localPosition = new Vector3(0f, 0.055f, 0.188f);

            return FinishViewModel(go, muzzle, slide.transform, magazine.transform, AshfallPalette.EmergencyAmber, "Weapon_MeridianSidearm");
        }

        private static GameObject BuildShotgun()
        {
            GameObject go = BeginViewModel("VM_Breakwater", out Transform root);
            Material body = AshfallMaterialLibrary.GunBody;
            Material rust = AshfallMaterialLibrary.RustedMetal;
            Material timber = AshfallMaterialLibrary.Timber;
            Material accentMat = MakeAccent("M_Accent_Shotgun", AshfallPalette.RustDeep * 2.2f);

            AddPart(root, "Receiver", AshfallGeometry.Chamfer(new Vector3(0.072f, 0.095f, 0.300f), 0.011f), body, new Vector3(0f, 0f, 0.020f));
            AddPart(root, "EjectionPort", AshfallGeometry.Chamfer(new Vector3(0.078f, 0.042f, 0.110f), 0.005f), rust, new Vector3(0f, 0.014f, 0.040f));
            AddPart(root, "Barrel", Barrel("ShotgunBarrel", 0.520f, 0.030f, 0.027f, 14), rust, new Vector3(0f, 0.030f, 0.400f), new Vector3(90f, 0f, 0f));
            AddPart(root, "MagTube", Barrel("ShotgunTube", 0.460f, 0.024f, 0.022f, 12), body, new Vector3(0f, -0.032f, 0.360f), new Vector3(90f, 0f, 0f));

            // A ribbed wooden pump: five bands, so the fore-end reads as
            // something a hand grips rather than a smooth block.
            GameObject slide = AddPart(root, "Pump", AshfallGeometry.Loft("ShotgunPump", new List<AshfallGeometry.LoftRing>
            {
                new(new Vector3(0f, -0.075f, 0f), 0.031f, 0.034f),
                new(new Vector3(0f, -0.055f, 0f), 0.037f, 0.040f),
                new(new Vector3(0f, 0.000f, 0f), 0.038f, 0.041f),
                new(new Vector3(0f, 0.055f, 0f), 0.037f, 0.040f),
                new(new Vector3(0f, 0.075f, 0f), 0.031f, 0.034f)
            }, 12, true, true, 0.25f), timber, new Vector3(0f, -0.028f, 0.300f), new Vector3(90f, 0f, 0f));

            AddPart(root, "Stock", AshfallGeometry.Loft("ShotgunStock", new List<AshfallGeometry.LoftRing>
            {
                new(new Vector3(0f, -0.026f, -0.010f), 0.026f, 0.036f),
                new(new Vector3(0f, -0.040f, -0.090f), 0.028f, 0.042f),
                new(new Vector3(0f, -0.058f, -0.200f), 0.030f, 0.048f),
                new(new Vector3(0f, -0.068f, -0.310f), 0.032f, 0.055f),
                new(new Vector3(0f, -0.070f, -0.348f), 0.030f, 0.052f)
            }, 12, true, true, 0.25f), timber, Vector3.zero);

            AddPart(root, "Grip", AshfallGeometry.Loft("ShotgunGrip", new List<AshfallGeometry.LoftRing>
            {
                new(new Vector3(0f, -0.160f, -0.100f), 0.026f, 0.022f),
                new(new Vector3(0f, -0.120f, -0.094f), 0.028f, 0.026f),
                new(new Vector3(0f, -0.070f, -0.078f), 0.027f, 0.029f),
                new(new Vector3(0f, -0.028f, -0.058f), 0.026f, 0.030f)
            }, 10, true, true, 0.25f), body, Vector3.zero);

            GameObject magazine = AddPart(root, "ShellCarrier", AshfallGeometry.Chamfer(new Vector3(0.086f, 0.028f, 0.135f), 0.007f), rust, new Vector3(0f, -0.062f, -0.045f));
            AddPart(root, "HeatShield", AshfallGeometry.Chamfer(new Vector3(0.056f, 0.013f, 0.330f), 0.005f), rust, new Vector3(0f, 0.062f, 0.340f));
            AddPart(root, "Bead", AshfallGeometry.Ellipsoid("ShotgunBead", new Vector3(0.006f, 0.008f, 0.006f), 6, 4), accentMat, new Vector3(0f, 0.070f, 0.620f));
            AddPart(root, "AccentBand", AshfallGeometry.Chamfer(new Vector3(0.076f, 0.011f, 0.048f), 0.003f), accentMat, new Vector3(0f, 0.048f, 0.060f));

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root, false);
            muzzle.localPosition = new Vector3(0f, 0.030f, 0.665f);

            return FinishViewModel(go, muzzle, slide.transform, magazine.transform, AshfallPalette.EmergencyAmber, "Weapon_BreakwaterShotgun");
        }

        private static GameObject BuildRifle()
        {
            GameObject go = BeginViewModel("VM_Arc9", out Transform root);
            Material body = AshfallMaterialLibrary.GunBody;
            Material steel = AshfallMaterialLibrary.SteelDark;
            Material accentMat = MakeAccent("M_Accent_Rifle", AshfallPalette.StormTeal);

            AddPart(root, "Receiver", AshfallGeometry.Chamfer(new Vector3(0.060f, 0.086f, 0.340f), 0.010f), body, new Vector3(0f, 0f, 0.050f));
            AddPart(root, "Handguard", AshfallGeometry.Loft("RifleHandguard", new List<AshfallGeometry.LoftRing>
            {
                new(new Vector3(0f, -0.135f, 0f), 0.026f, 0.030f),
                new(new Vector3(0f, -0.105f, 0f), 0.030f, 0.034f),
                new(new Vector3(0f, 0.060f, 0f), 0.029f, 0.032f),
                new(new Vector3(0f, 0.120f, 0f), 0.024f, 0.026f),
                new(new Vector3(0f, 0.135f, 0f), 0.021f, 0.023f)
            }, 12, true, true, 0.25f), steel, new Vector3(0f, -0.004f, 0.340f), new Vector3(90f, 0f, 0f));

            AddPart(root, "Barrel", Barrel("RifleBarrel", 0.320f, 0.016f, 0.013f, 12), steel, new Vector3(0f, 0.010f, 0.590f), new Vector3(90f, 0f, 0f));
            AddPart(root, "RailTop", AshfallGeometry.Chamfer(new Vector3(0.030f, 0.011f, 0.490f), 0.003f), steel, new Vector3(0f, 0.052f, 0.220f));
            GameObject slide = AddPart(root, "ChargingHandle", AshfallGeometry.Chamfer(new Vector3(0.068f, 0.022f, 0.072f), 0.006f), body, new Vector3(0f, 0.042f, -0.075f));

            AddPart(root, "Stock", AshfallGeometry.Loft("RifleStock", new List<AshfallGeometry.LoftRing>
            {
                new(new Vector3(0f, -0.010f, -0.148f), 0.023f, 0.038f),
                new(new Vector3(0f, -0.014f, -0.230f), 0.024f, 0.042f),
                new(new Vector3(0f, -0.018f, -0.320f), 0.026f, 0.048f),
                new(new Vector3(0f, -0.020f, -0.372f), 0.024f, 0.045f)
            }, 12, true, true, 0.25f), body, Vector3.zero);

            AddPart(root, "Cheek", AshfallGeometry.Chamfer(new Vector3(0.038f, 0.024f, 0.155f), 0.006f), steel, new Vector3(0f, 0.038f, -0.240f));

            AddPart(root, "Grip", AshfallGeometry.Loft("RifleGrip", new List<AshfallGeometry.LoftRing>
            {
                new(new Vector3(0f, -0.155f, -0.100f), 0.024f, 0.021f),
                new(new Vector3(0f, -0.118f, -0.092f), 0.026f, 0.025f),
                new(new Vector3(0f, -0.070f, -0.076f), 0.025f, 0.028f),
                new(new Vector3(0f, -0.026f, -0.056f), 0.024f, 0.029f)
            }, 10, true, true, 0.25f), body, Vector3.zero);

            GameObject magazine = AddPart(root, "Magazine", AshfallGeometry.Loft("RifleMagazine", new List<AshfallGeometry.LoftRing>
            {
                new(new Vector3(0f, -0.088f, 0f), 0.019f, 0.034f),
                new(new Vector3(0f, -0.060f, 0.004f), 0.021f, 0.038f),
                new(new Vector3(0f, 0.020f, 0.010f), 0.021f, 0.038f),
                new(new Vector3(0f, 0.078f, 0.012f), 0.019f, 0.033f)
            }, 10, true, true, 0.25f), steel, new Vector3(0f, -0.110f, 0.055f), new Vector3(7f, 0f, 0f));

            // The coil pack is the Arc-9's identity: three glowing rings along the barrel.
            for (int i = 0; i < 3; i++)
            {
                AddPart(root, $"Coil{i}", AshfallGeometry.Cylinder(0.029f, 0.020f, 16, 0.1f), accentMat,
                    new Vector3(0f, 0.010f, 0.480f + i * 0.075f), new Vector3(90f, 0f, 0f));
            }

            AddPart(root, "Optic", AshfallGeometry.Chamfer(new Vector3(0.034f, 0.044f, 0.098f), 0.008f), body, new Vector3(0f, 0.085f, 0.055f));
            AddPart(root, "OpticGlass", AshfallGeometry.Ellipsoid("RifleOptic", new Vector3(0.014f, 0.015f, 0.004f), 10, 6), accentMat, new Vector3(0f, 0.085f, 0.108f));
            AddPart(root, "PowerCell", AshfallGeometry.Loft("RiflePowerCell", new List<AshfallGeometry.LoftRing>
            {
                new(new Vector3(0f, -0.055f, 0f), 0.011f),
                new(new Vector3(0f, -0.040f, 0f), 0.015f),
                new(new Vector3(0f, 0.040f, 0f), 0.015f),
                new(new Vector3(0f, 0.055f, 0f), 0.011f)
            }, 10, true, true, 0.25f), accentMat, new Vector3(-0.048f, 0.006f, 0.020f), new Vector3(90f, 0f, 0f));

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root, false);
            muzzle.localPosition = new Vector3(0f, 0.010f, 0.755f);

            return FinishViewModel(go, muzzle, slide.transform, magazine.transform, AshfallPalette.StormTeal, "Weapon_Arc9Rifle");
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

            // A glowing core inside an open cage: reads clearly from any angle and
            // the cage gives the spin something to catch light on. The core is a
            // sphere rather than a rotated cube so it holds the same silhouette
            // through the whole spin -- a cube flickers between wide and narrow.
            renderers.Add(AddPart(visual.transform, "Core",
                AshfallGeometry.Ellipsoid("PowerUpCore", new Vector3(0.165f, 0.165f, 0.165f), 14, 9), emissive,
                Vector3.zero, Vector3.zero, false).GetComponent<Renderer>());

            renderers.Add(AddPart(visual.transform, "CageX",
                AshfallGeometry.Chamfer(new Vector3(0.52f, 0.032f, 0.032f), 0.010f, 0.3f), shell,
                Vector3.zero, Vector3.zero, false).GetComponent<Renderer>());
            renderers.Add(AddPart(visual.transform, "CageY",
                AshfallGeometry.Chamfer(new Vector3(0.032f, 0.52f, 0.032f), 0.010f, 0.3f), shell,
                Vector3.zero, Vector3.zero, false).GetComponent<Renderer>());
            renderers.Add(AddPart(visual.transform, "CageZ",
                AshfallGeometry.Chamfer(new Vector3(0.032f, 0.032f, 0.52f), 0.010f, 0.3f), shell,
                Vector3.zero, Vector3.zero, false).GetComponent<Renderer>());

            // Four fins on a ring, angled so the canister catches the storm light
            // from any approach.
            for (int i = 0; i < 4; i++)
            {
                renderers.Add(AddPart(visual.transform, $"Fin{i}",
                    AshfallGeometry.Chamfer(new Vector3(0.016f, 0.170f, 0.170f), 0.006f, 0.2f), emissive,
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
