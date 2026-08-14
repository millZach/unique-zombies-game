using System.Collections.Generic;
using UnityEngine;
using Ashfall.Core;
using Ashfall.Enemies;
using Ashfall.Nav;
using Ashfall.Weapons;
using Ashfall.World;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// Builds the Black Meridian station.
    ///
    /// Layout is a loop, not a corridor. The courtyard is the hub; the lab wing runs
    /// north, the generator room east, and a storm-exposed rooftop lane connects the two
    /// back over the top. Every leg of that loop is behind a purchasable route, so the
    /// player's map grows in exactly the order they can afford it -- and the phase
    /// controller forces each route open a few rounds later whether they bought it or not.
    ///
    ///        [ LAB WING ]  z 17..45
    ///             |  shutter (buy, or fails open at round 3)
    ///   roof lane ==================================\
    ///             |                                 |  y 5.5, storm-exposed
    ///        [ COURTYARD ] --- blast door --- [ GENERATOR ]  x 18..44
    ///         spawn, round 1-2                 (buy, or powers open at round 6)
    /// </summary>
    public static class AshfallMapBuilder
    {
        // --- overall dimensions ------------------------------------------------
        private const float WallHeight = 7f;
        private const float WallThickness = 0.6f;
        private const float RoofY = 5.5f;
        private const float CatwalkY = 3.5f;

        // Courtyard
        private const float CourtMinX = -17f, CourtMaxX = 17f;
        private const float CourtMinZ = -16f, CourtMaxZ = 16f;

        // Lab wing
        private const float LabMinX = -13f, LabMaxX = 13f;
        private const float LabMinZ = 17f, LabMaxZ = 45f;
        private const float LabCeilingY = 4.6f;

        // Generator room
        private const float GenMinX = 18f, GenMaxX = 44f;
        private const float GenMinZ = -12f, GenMaxZ = 14f;
        private const float GenCeilingY = 8f;

        // Roof lane. Its west end deliberately stops short of the lab wing's ceiling
        // hole, so walking off that edge drops the player back into the lab -- a
        // one-way escape that closes the loop without giving the AI a shortcut.
        private const float RoofMinX = -4f, RoofMaxX = 42f;
        private const float RoofMinZ = 17.5f, RoofMaxZ = 25.5f;

        // The hole cut in the lab ceiling under the roof lane's west end.
        private const float DropHoleMinX = -11f, DropHoleMaxX = -3.5f;
        private const float DropHoleMinZ = 18f, DropHoleMaxZ = 25f;

        public class Result
        {
            public Transform Root;
            public Vector3 PlayerSpawn;
            public float PlayerSpawnYaw;
            public readonly List<PhaseElement> PhaseElements = new();
            public readonly List<PhaseLight> PhaseLights = new();
            public readonly List<RouteDoor> Doors = new();
            public readonly List<WeaponStation> Stations = new();
            public readonly List<Barricade> Barricades = new();
            public readonly List<EnemySpawnPoint> SpawnPoints = new();
            public readonly List<StormExposureVolume> StormVolumes = new();
            public readonly List<AshfallNavBaker.GateVolume> GateVolumes = new();
            public readonly Dictionary<MapPhase, List<RouteDoor>> AutoOpenDoors = new();
            public Light Sun;
            public Light StormFlash;
            public ParticleSystem Rain;
            public ParticleSystem Embers;
            public Bounds NavBounds;
        }

        private static Result _r;
        private static Transform _geometry;
        private static Transform _props;
        private static Transform _lights;
        private static Transform _markers;

        public static Result Build(Transform parent)
        {
            _r = new Result();

            var root = new GameObject("Station").transform;
            root.SetParent(parent, false);
            _r.Root = root;

            _geometry = AshfallAssetUtility.NewChild(root, "Geometry").transform;
            _props = AshfallAssetUtility.NewChild(root, "Props").transform;
            _lights = AshfallAssetUtility.NewChild(root, "Lighting").transform;
            _markers = AshfallAssetUtility.NewChild(root, "Markers").transform;

            BuildCourtyard();
            BuildLabWing();
            BuildGeneratorRoom();
            BuildRoofLane();
            BuildAtmosphere();
            BuildSpawnPoints();

            _r.PlayerSpawn = new Vector3(0f, 0.2f, -11f);
            _r.PlayerSpawnYaw = 0f;
            _r.NavBounds = new Bounds(
                new Vector3(14f, 3f, 14f),
                new Vector3(74f, 14f, 76f));

            return _r;
        }

        // ------------------------------------------------------------------
        // Primitive helpers
        // ------------------------------------------------------------------

        private static GameObject Block(
            Transform parent, string name, Vector3 center, Vector3 size, Material material,
            float tile = 2f, int layer = -1, bool collider = true, Vector3 euler = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.Euler(euler);

            go.AddComponent<MeshFilter>().sharedMesh = AshfallGeometry.Box(size, tile);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            if (collider)
            {
                var box = go.AddComponent<BoxCollider>();
                box.size = size;
            }

            go.layer = layer >= 0 ? layer : AshfallLayers.World;
            return go;
        }

        private static GameObject Column(
            Transform parent, string name, Vector3 center, float radius, float height,
            Material material, int segments = 16, float tile = 2f, bool collider = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;

            go.AddComponent<MeshFilter>().sharedMesh = AshfallGeometry.Cylinder(radius, height, segments, tile);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            if (collider)
            {
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.radius = radius;
                capsule.height = height;
                capsule.direction = 1;
            }

            go.layer = AshfallLayers.World;
            return go;
        }

        /// <summary>
        /// A flight of stairs: stepped geometry for the eye, one smooth ramp collider
        /// for everything else.
        ///
        /// Per-step colliders look correct and behave badly. The nav baker's capsule fit
        /// test fails between treads, so a stepped flight bakes as a wall and every zone
        /// above it becomes unreachable -- which is exactly what happened here the first
        /// time. A single sloped box is what both the CharacterController and the baker
        /// actually want.
        /// </summary>
        private static GameObject Stairs(
            Transform parent, string name, Vector3 bottomCenter, Vector3 direction,
            int steps, float stepHeight, float stepDepth, float width, Material material)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = bottomCenter;
            root.transform.localRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

            for (int i = 0; i < steps; i++)
            {
                float y = (i + 0.5f) * stepHeight;
                float z = (i + 0.5f) * stepDepth;
                Block(root.transform, $"Step{i:00}",
                    new Vector3(0f, y - stepHeight * 0.5f, z),
                    new Vector3(width, stepHeight, stepDepth),
                    material, 1.2f, collider: false);
            }

            float run = steps * stepDepth;
            float rise = steps * stepHeight;

            // Skirt under the flight so the open underside is never visible.
            Block(root.transform, "Skirt",
                new Vector3(0f, rise * 0.25f - 0.05f, run * 0.5f),
                new Vector3(width * 0.98f, rise * 0.5f, run),
                material, 2f, collider: false);

            const float rampThickness = 0.5f;
            float slopeLength = Mathf.Sqrt(run * run + rise * rise);
            float pitch = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

            var rampGo = new GameObject("RampCollider");
            rampGo.transform.SetParent(root.transform, false);
            Quaternion rampRotation = Quaternion.Euler(-pitch, 0f, 0f);
            rampGo.transform.localRotation = rampRotation;
            rampGo.transform.localPosition =
                new Vector3(0f, rise * 0.5f, run * 0.5f) - (rampRotation * Vector3.up) * (rampThickness * 0.5f);

            var rampBox = rampGo.AddComponent<BoxCollider>();
            rampBox.size = new Vector3(width, rampThickness, slopeLength);
            rampGo.layer = AshfallLayers.World;

            return root;
        }

        private static GameObject Railing(
            Transform parent, string name, Vector3 center, Vector3 size, Material material)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = center;

            bool alongX = size.x >= size.z;
            float length = alongX ? size.x : size.z;
            int posts = Mathf.Max(2, Mathf.RoundToInt(length / 2.4f) + 1);

            for (int i = 0; i < posts; i++)
            {
                float t = posts <= 1 ? 0f : i / (float)(posts - 1);
                float offset = Mathf.Lerp(-length * 0.5f, length * 0.5f, t);
                Vector3 p = alongX ? new Vector3(offset, 0.55f, 0f) : new Vector3(0f, 0.55f, offset);
                Block(root.transform, $"Post{i}", p, new Vector3(0.08f, 1.1f, 0.08f), material, 0.5f, collider: false);
            }

            Vector3 railSize = alongX ? new Vector3(length, 0.07f, 0.07f) : new Vector3(0.07f, 0.07f, length);
            Block(root.transform, "TopRail", new Vector3(0f, 1.08f, 0f), railSize, material, 1f, collider: false);
            Block(root.transform, "MidRail", new Vector3(0f, 0.60f, 0f), railSize, material, 1f, collider: false);

            // One invisible collider does the actual blocking; the visual rails do not.
            var blocker = new GameObject("Blocker");
            blocker.transform.SetParent(root.transform, false);
            var box = blocker.AddComponent<BoxCollider>();
            box.size = alongX ? new Vector3(length, 1.15f, 0.16f) : new Vector3(0.16f, 1.15f, length);
            box.center = new Vector3(0f, 0.58f, 0f);
            blocker.layer = AshfallLayers.World;

            return root;
        }

        private static PhaseLight Lamp(
            Transform parent, string name, Vector3 position, float range,
            Color early, float earlyIntensity, Color late, float lateIntensity,
            bool flicker = false, LightType type = LightType.Point, Vector3 euler = default)
        {
            var housing = Block(parent, name, position, new Vector3(0.42f, 0.16f, 0.42f),
                AshfallMaterialLibrary.SteelDark, 0.4f, collider: false, euler: euler);

            var bulbGo = new GameObject("Bulb");
            bulbGo.transform.SetParent(housing.transform, false);
            bulbGo.transform.localPosition = new Vector3(0f, -0.10f, 0f);
            bulbGo.AddComponent<MeshFilter>().sharedMesh = AshfallGeometry.Box(new Vector3(0.34f, 0.05f, 0.34f), 0.3f);
            var bulbRenderer = bulbGo.AddComponent<MeshRenderer>();
            bulbRenderer.sharedMaterial = AshfallMaterialLibrary.EmissiveAmber;
            bulbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(housing.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, -0.18f, 0f);
            lightGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var light = lightGo.AddComponent<Light>();
            light.type = type;
            light.range = range;
            light.spotAngle = 110f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;

            var phaseLight = lightGo.AddComponent<PhaseLight>();
            var settings = new PhaseLight.PhaseSetting[MapPhases.Count];
            for (int i = 0; i < MapPhases.Count; i++)
            {
                float t = i / (float)(MapPhases.Count - 1);
                settings[i] = new PhaseLight.PhaseSetting
                {
                    color = Color.Lerp(early, late, t),
                    intensity = Mathf.Lerp(earlyIntensity, lateIntensity, t),
                    range = range
                };
            }

            phaseLight.Configure(settings, new[] { bulbRenderer }, flicker);
            _r.PhaseLights.Add(phaseLight);
            return phaseLight;
        }

        private static void HazardStrip(Transform parent, string name, Vector3 center, Vector3 size)
        {
            GameObject go = Block(parent, name, center, size, AshfallMaterialLibrary.HazardPaint, 1.4f, collider: false);
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------
        // Courtyard
        // ------------------------------------------------------------------

        private static void BuildCourtyard()
        {
            var zone = AshfallAssetUtility.NewChild(_geometry, "Courtyard").transform;

            float width = CourtMaxX - CourtMinX;
            float depth = CourtMaxZ - CourtMinZ;
            var centre = new Vector3((CourtMinX + CourtMaxX) * 0.5f, 0f, (CourtMinZ + CourtMaxZ) * 0.5f);

            Block(zone, "Floor", centre + Vector3.down * 0.25f,
                new Vector3(width, 0.5f, depth), AshfallMaterialLibrary.WetFloor, 4f);

            // Threshold slabs. The courtyard, lab and generator floors each stop at their
            // own wall line, which leaves a metre of empty air in every doorway -- enough
            // to drop the player through and to sever the nav graph. These bridge it.
            Block(zone, "ThresholdLab", new Vector3(0f, -0.25f, CourtMaxZ + 0.5f),
                new Vector3(8.6f, 0.5f, 2.4f), AshfallMaterialLibrary.TreadPlate, 2f);
            HazardStrip(zone, "ThresholdLabStripe", new Vector3(0f, 0.015f, CourtMaxZ + 0.5f),
                new Vector3(8.6f, 0.03f, 2.4f));

            Block(zone, "ThresholdGenerator", new Vector3(CourtMaxX + 0.5f, -0.25f, 0f),
                new Vector3(2.4f, 0.5f, 8.6f), AshfallMaterialLibrary.TreadPlate, 2f);
            HazardStrip(zone, "ThresholdGeneratorStripe", new Vector3(CourtMaxX + 0.5f, 0.015f, 0f),
                new Vector3(2.4f, 0.03f, 8.6f));

            // South wall, with two boarded breaches.
            WallWithGaps(zone, "WallSouth", new Vector3(centre.x, WallHeight * 0.5f, CourtMinZ),
                new Vector3(width, WallHeight, WallThickness), true,
                new[] { (-9f, 3.2f), (7f, 3.2f) });

            // West wall, one breach; the Breakwater rack hangs beside it.
            WallWithGaps(zone, "WallWest", new Vector3(CourtMinX, WallHeight * 0.5f, centre.z),
                new Vector3(WallThickness, WallHeight, depth), false,
                new[] { (6f, 3.2f) });

            // North wall: opening for the lab shutter.
            WallWithGaps(zone, "WallNorth", new Vector3(centre.x, WallHeight * 0.5f, CourtMaxZ),
                new Vector3(width, WallHeight, WallThickness), true,
                new[] { (0f, 8f) });

            // East wall: opening for the generator blast door.
            WallWithGaps(zone, "WallEast", new Vector3(CourtMaxX, WallHeight * 0.5f, centre.z),
                new Vector3(WallThickness, WallHeight, depth), false,
                new[] { (0f, 8f) });

            // A partial canopy: keeps the courtyard from feeling like an open field and
            // gives the storm somewhere to pour off.
            Block(zone, "CanopyWest", new Vector3(-11f, 6.6f, 2f), new Vector3(12f, 0.4f, 22f),
                AshfallMaterialLibrary.TreadPlate, 3f);
            Block(zone, "CanopyBeamA", new Vector3(-5.2f, 3.4f, -8f), new Vector3(0.35f, 6.6f, 0.35f),
                AshfallMaterialLibrary.SteelPanel, 1.5f);
            Block(zone, "CanopyBeamB", new Vector3(-5.2f, 3.4f, 11f), new Vector3(0.35f, 6.6f, 0.35f),
                AshfallMaterialLibrary.SteelPanel, 1.5f);

            // Central collapsed antenna mast: the landmark players orient by.
            var mast = AshfallAssetUtility.NewChild(_props, "CollapsedMast").transform;
            Block(mast, "Base", new Vector3(2f, 0.5f, 1f), new Vector3(4.4f, 1f, 4.4f),
                AshfallMaterialLibrary.ConcreteWall, 2f);
            HazardStrip(mast, "BaseStripe", new Vector3(2f, 1.02f, 1f), new Vector3(4.6f, 0.02f, 4.6f));
            Column(mast, "MastLower", new Vector3(2f, 3.2f, 1f), 0.42f, 4.4f, AshfallMaterialLibrary.RustedMetal);
            var mastUpper = Column(mast, "MastUpper", new Vector3(4.4f, 5.0f, 3.4f), 0.34f, 6.4f, AshfallMaterialLibrary.RustedMetal);
            mastUpper.transform.localRotation = Quaternion.Euler(38f, 30f, 0f);
            Block(mast, "DishFrame", new Vector3(6.6f, 6.6f, 5.6f), new Vector3(3.2f, 0.25f, 3.2f),
                AshfallMaterialLibrary.SteelPanel, 2f, euler: new Vector3(52f, 30f, 0f));

            // Scattered crates and drums: cover, and something to break the ground plane.
            ScatterProps(zone);

            // --- lighting ------------------------------------------------------
            Lamp(_lights, "CourtLamp_A", new Vector3(-10f, 6.3f, -6f), 16f,
                AshfallPalette.EmergencyAmber, 4.2f, AshfallPalette.StormTeal, 1.2f);
            Lamp(_lights, "CourtLamp_B", new Vector3(-10f, 6.3f, 9f), 16f,
                AshfallPalette.EmergencyAmber, 4.2f, AshfallPalette.StormTeal, 1.2f, flicker: true);
            Lamp(_lights, "CourtLamp_C", new Vector3(12f, 6.3f, -11f), 14f,
                AshfallPalette.EmergencyAmber, 3.4f, AshfallPalette.StormTealDeep, 0.8f);
            Lamp(_lights, "CourtLamp_D", new Vector3(13f, 6.3f, 10f), 14f,
                AshfallPalette.EmergencyAmberDeep, 2.6f, AshfallPalette.StormTeal, 2.8f);

            // Storm lamps: dark at Standby, dominant by Meridian. The inverse arc of the
            // amber lamps above, which is what sells the power failing.
            Lamp(_lights, "StormLamp_A", new Vector3(0f, 6.4f, 14f), 18f,
                AshfallPalette.StormTeal, 0f, AshfallPalette.StormTeal, 5.5f);
            Lamp(_lights, "StormLamp_B", new Vector3(15f, 6.4f, 0f), 18f,
                AshfallPalette.StormTeal, 0f, AshfallPalette.StormTeal, 5.5f);

            // --- the Breakwater rack --------------------------------------------
            _r.Stations.Add(BuildWeaponStation(
                "Station_Breakwater",
                new Vector3(CourtMinX + 0.65f, 1.55f, -2f),
                Quaternion.Euler(0f, 90f, 0f),
                AshfallPrefabFactory.ShotgunDefinition,
                1250, 450, MapPhase.Standby));

            // --- doors -----------------------------------------------------------
            RouteDoor labShutter = BuildRouteDoor(
                "Door_LabShutter",
                "Force the lab wing shutter",
                new Vector3(0f, 0f, CourtMaxZ),
                Quaternion.identity,
                new Vector3(8f, WallHeight, 0.8f),
                950,
                "LabWing",
                StationZone.LabWing);

            RouteDoor blastDoor = BuildRouteDoor(
                "Door_GeneratorBlast",
                "Cut open the generator blast door",
                new Vector3(CourtMaxX, 0f, 0f),
                Quaternion.Euler(0f, 90f, 0f),
                new Vector3(8f, WallHeight, 0.8f),
                1350,
                "Generator",
                StationZone.GeneratorRoom);

            _r.AutoOpenDoors[MapPhase.Breach] = new List<RouteDoor> { labShutter };
            _r.AutoOpenDoors[MapPhase.Surge] = new List<RouteDoor> { blastDoor };

            // --- phase debris ------------------------------------------------------
            var debris = AshfallAssetUtility.NewChild(_props, "PhaseDebris_Courtyard").transform;
            Block(debris, "FallenPanelA", new Vector3(-3f, 1.1f, -12f), new Vector3(5.5f, 2.2f, 0.6f),
                AshfallMaterialLibrary.RustedMetal, 2f, euler: new Vector3(0f, 24f, 14f));
            Block(debris, "FallenPanelB", new Vector3(9f, 0.9f, 6f), new Vector3(4.5f, 1.8f, 0.5f),
                AshfallMaterialLibrary.RustedMetal, 2f, euler: new Vector3(0f, -38f, -9f));
            Column(debris, "SnappedBeam", new Vector3(-8f, 0.7f, 13f), 0.3f, 7f, AshfallMaterialLibrary.SteelPanel);
            debris.localRotation = Quaternion.identity;
            AddPhaseElement(debris.gameObject, MapPhase.Blackout, MapPhase.Meridian);

            // Hoarding that seals the storm out early, and is gone by Meridian.
            var hoarding = AshfallAssetUtility.NewChild(_geometry, "PhaseHoarding_Canopy").transform;
            Block(hoarding, "CanopyEast", new Vector3(7f, 6.6f, 6f), new Vector3(18f, 0.4f, 18f),
                AshfallMaterialLibrary.TreadPlate, 3f);
            AddPhaseElement(hoarding.gameObject, MapPhase.Standby, MapPhase.Blackout);

            // --- Breach (round 3): the lab shutter fails open -------------------------
            // Boards nailed over the shutter while the station is still on Standby.
            // Visual only -- the RouteDoor's own panel does the blocking -- so removing
            // them cannot desync the nav graph.
            var boardedOver = AshfallAssetUtility.NewChild(_props, "PhaseSeal_LabShutter").transform;
            for (int i = 0; i < 4; i++)
            {
                Block(boardedOver, $"SealPlank{i}",
                    new Vector3(0f, 1.1f + i * 1.15f, CourtMaxZ - 0.55f),
                    new Vector3(9.2f, 0.34f, 0.14f), AshfallMaterialLibrary.Timber, 1f,
                    collider: false, euler: new Vector3(0f, 0f, i % 2 == 0 ? 1.6f : -2.1f));
            }

            Block(boardedOver, "SealNotice", new Vector3(3.1f, 3.3f, CourtMaxZ - 0.62f),
                new Vector3(1.1f, 0.8f, 0.05f), AshfallMaterialLibrary.EmissiveRed, 0.6f, collider: false);
            AddPhaseElement(boardedOver.gameObject, MapPhase.Standby, MapPhase.Standby);

            // The wreckage the failing shutter leaves behind, plus the first storm-blown
            // debris in the courtyard. Appears exactly when the Breach phase begins.
            var breachDebris = AshfallAssetUtility.NewChild(_props, "PhaseDebris_Breach").transform;
            Block(breachDebris, "TornShutterA", new Vector3(-4.8f, 0.8f, 13.6f),
                new Vector3(3.6f, 1.6f, 0.35f), AshfallMaterialLibrary.RustedMetal, 2f,
                euler: new Vector3(0f, 18f, -22f));
            Block(breachDebris, "TornShutterB", new Vector3(5.2f, 0.6f, 12.9f),
                new Vector3(2.8f, 1.2f, 0.3f), AshfallMaterialLibrary.RustedMetal, 2f,
                euler: new Vector3(0f, -34f, 15f));
            Column(breachDebris, "BentRail", new Vector3(1.5f, 0.45f, 14.4f),
                0.16f, 5.2f, AshfallMaterialLibrary.SteelPanel, 8, 1f);
            breachDebris.GetChild(2).localRotation = Quaternion.Euler(0f, 24f, 88f);
            HazardStrip(breachDebris, "SpillMarking", new Vector3(0f, 0.02f, 11.5f),
                new Vector3(9f, 0.04f, 3.2f));
            AddPhaseElement(breachDebris.gameObject, MapPhase.Breach, MapPhase.Meridian);

            // --- Surge (round 6): live conduits light the courtyard --------------------
            var conduits = AshfallAssetUtility.NewChild(_props, "PhaseConduits_Surge").transform;
            for (int i = 0; i < 5; i++)
            {
                Block(conduits, $"Conduit{i}", new Vector3(16.4f, 1.4f + i * 1.05f, -6f + i * 3.2f),
                    new Vector3(0.5f, 0.16f, 5.4f), AshfallMaterialLibrary.EmissiveTeal, 1f, collider: false);
            }

            AddPhaseElement(conduits.gameObject, MapPhase.Surge, MapPhase.Meridian);
        }

        private static void ScatterProps(Transform zone)
        {
            var props = AshfallAssetUtility.NewChild(_props, "CourtyardProps").transform;

            (Vector3 pos, Vector3 size, float yaw, Material mat)[] items =
            {
                (new Vector3(-13f, 0.6f, -12f), new Vector3(1.2f, 1.2f, 1.2f), 18f, AshfallMaterialLibrary.SteelPanel),
                (new Vector3(-12f, 1.8f, -12.2f), new Vector3(1.1f, 1.1f, 1.1f), -12f, AshfallMaterialLibrary.SteelPanel),
                (new Vector3(-14.5f, 0.55f, 4f), new Vector3(1.1f, 1.1f, 2.2f), 0f, AshfallMaterialLibrary.RustedMetal),
                (new Vector3(12f, 0.7f, -13f), new Vector3(2.4f, 1.4f, 1.2f), -25f, AshfallMaterialLibrary.SteelPanel),
                (new Vector3(14f, 0.6f, 13f), new Vector3(1.3f, 1.2f, 1.3f), 40f, AshfallMaterialLibrary.SteelPanel),
                (new Vector3(-2f, 0.5f, 13.5f), new Vector3(2.6f, 1.0f, 1.0f), 8f, AshfallMaterialLibrary.RustedMetal)
            };

            for (int i = 0; i < items.Length; i++)
            {
                Block(props, $"Crate{i}", items[i].pos, items[i].size, items[i].mat, 1.2f,
                    euler: new Vector3(0f, items[i].yaw, 0f));
            }

            // Fuel drums read instantly as "industrial" and give the palette its rust.
            Vector3[] drums =
            {
                new(-15.4f, 0.55f, -4f),
                new(-15.4f, 0.55f, -2.6f),
                new(15.2f, 0.55f, 5.5f),
                new(4.5f, 0.55f, -14f)
            };

            for (int i = 0; i < drums.Length; i++)
            {
                GameObject drum = Column(props, $"Drum{i}", drums[i], 0.42f, 1.1f, AshfallMaterialLibrary.RustedMetal, 12, 1f);
                HazardStrip(drum.transform, "Band", new Vector3(0f, 0.18f, 0f), new Vector3(0.88f, 0.14f, 0.88f));
            }
        }

        /// <summary>
        /// A wall broken by openings. Gaps are given as (centre offset along the wall,
        /// width); breach gaps get a boarded barricade dropped into them.
        /// </summary>
        private static void WallWithGaps(
            Transform parent, string name, Vector3 center, Vector3 size, bool alongX,
            (float offset, float width)[] gaps, bool barricadeGaps = true)
        {
            var root = AshfallAssetUtility.NewChild(parent, name).transform;
            root.localPosition = center;

            float length = alongX ? size.x : size.z;
            var segments = new List<(float start, float end)>();

            var sorted = new List<(float offset, float width)>(gaps);
            sorted.Sort((a, b) => a.offset.CompareTo(b.offset));

            float cursor = -length * 0.5f;
            for (int i = 0; i < sorted.Count; i++)
            {
                float gapStart = sorted[i].offset - sorted[i].width * 0.5f;
                float gapEnd = sorted[i].offset + sorted[i].width * 0.5f;
                if (gapStart > cursor)
                {
                    segments.Add((cursor, gapStart));
                }

                cursor = Mathf.Max(cursor, gapEnd);
            }

            if (cursor < length * 0.5f)
            {
                segments.Add((cursor, length * 0.5f));
            }

            for (int i = 0; i < segments.Count; i++)
            {
                float segLength = segments[i].end - segments[i].start;
                if (segLength <= 0.05f)
                {
                    continue;
                }

                float mid = (segments[i].start + segments[i].end) * 0.5f;
                Vector3 segCenter = alongX ? new Vector3(mid, 0f, 0f) : new Vector3(0f, 0f, mid);
                Vector3 segSize = alongX
                    ? new Vector3(segLength, size.y, size.z)
                    : new Vector3(size.x, size.y, segLength);

                Block(root, $"Segment{i}", segCenter, segSize, AshfallMaterialLibrary.ConcreteWall, 3f);
            }

            if (!barricadeGaps)
            {
                return;
            }

            // Anything narrower than five metres is a breach, not a doorway.
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].width > 5f)
                {
                    continue;
                }

                Vector3 gapCenter = alongX
                    ? new Vector3(sorted[i].offset, 0f, 0f)
                    : new Vector3(0f, 0f, sorted[i].offset);

                _r.Barricades.Add(BuildBarricade(root, $"Breach{i}", gapCenter, sorted[i].width, alongX));
            }
        }

        private static Barricade BuildBarricade(Transform parent, string name, Vector3 localCenter, float width, bool alongX)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localCenter;

            // A lintel above so the breach reads as a broken window, not a doorway.
            Vector3 lintelSize = alongX
                ? new Vector3(width + 0.4f, WallHeight - 2.6f, WallThickness)
                : new Vector3(WallThickness, WallHeight - 2.6f, width + 0.4f);
            Block(root.transform, "Lintel", new Vector3(0f, (WallHeight + 2.6f) * 0.5f - 0.6f, 0f),
                lintelSize, AshfallMaterialLibrary.ConcreteWall, 2f);

            var boards = new Transform[4];
            for (int i = 0; i < boards.Length; i++)
            {
                float y = 0.55f + i * 0.52f;
                float tilt = (i % 2 == 0 ? 1f : -1f) * Random.Range(3f, 9f);
                Vector3 boardSize = alongX
                    ? new Vector3(width + 0.5f, 0.26f, 0.16f)
                    : new Vector3(0.16f, 0.26f, width + 0.5f);

                GameObject board = Block(root.transform, $"Board{i}",
                    new Vector3(0f, y, 0f), boardSize, AshfallMaterialLibrary.Timber, 1f,
                    layer: AshfallLayers.NavBlocker,
                    euler: alongX ? new Vector3(0f, 0f, tilt) : new Vector3(tilt, 0f, 0f));
                boards[i] = board.transform;
            }

            // A separate trigger drives the interact prompt so the player can aim at the
            // opening rather than having to hit a specific plank.
            var promptGo = new GameObject("PromptVolume");
            promptGo.transform.SetParent(root.transform, false);
            promptGo.layer = AshfallLayers.Interactable;
            var trigger = promptGo.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 1.3f, 0f);
            trigger.size = alongX
                ? new Vector3(width + 0.6f, 2.6f, 1.4f)
                : new Vector3(1.4f, 2.6f, width + 0.6f);

            var barricade = root.AddComponent<Barricade>();
            barricade.Configure(boards, 4, 110f, 22);
            return barricade;
        }

        private static RouteDoor BuildRouteDoor(
            string name, string prompt, Vector3 worldPosition, Quaternion rotation,
            Vector3 size, int cost, string gateName, StationZone zone)
        {
            var root = new GameObject(name);
            root.transform.SetParent(_geometry, false);
            root.transform.SetPositionAndRotation(worldPosition, rotation);
            root.layer = AshfallLayers.Door;

            var moving = new GameObject("Shutter");
            moving.transform.SetParent(root.transform, false);
            moving.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);

            GameObject panel = Block(moving.transform, "Panel", Vector3.zero, size,
                AshfallMaterialLibrary.SteelPanel, 2f, layer: AshfallLayers.Door);
            var blocker = panel.GetComponent<BoxCollider>();

            // Chevrons across the bottom edge so a closed route reads at a glance.
            HazardStrip(moving.transform, "Chevron",
                new Vector3(0f, -size.y * 0.5f + 0.55f, size.z * 0.55f),
                new Vector3(size.x * 0.94f, 1.0f, 0.05f));
            HazardStrip(moving.transform, "ChevronBack",
                new Vector3(0f, -size.y * 0.5f + 0.55f, -size.z * 0.55f),
                new Vector3(size.x * 0.94f, 1.0f, 0.05f));

            // Frame and status lamp.
            Block(root.transform, "FrameLeft", new Vector3(-size.x * 0.5f - 0.25f, size.y * 0.5f, 0f),
                new Vector3(0.5f, size.y, size.z + 0.3f), AshfallMaterialLibrary.SteelDark, 2f);
            Block(root.transform, "FrameRight", new Vector3(size.x * 0.5f + 0.25f, size.y * 0.5f, 0f),
                new Vector3(0.5f, size.y, size.z + 0.3f), AshfallMaterialLibrary.SteelDark, 2f);

            var signGo = new GameObject("StatusSign");
            signGo.transform.SetParent(root.transform, false);
            signGo.transform.localPosition = new Vector3(size.x * 0.5f + 0.25f, 2.2f, size.z * 0.7f);
            signGo.AddComponent<MeshFilter>().sharedMesh = AshfallGeometry.Box(new Vector3(0.5f, 0.5f, 0.14f), 0.3f);
            var signRenderer = signGo.AddComponent<MeshRenderer>();
            signRenderer.sharedMaterial = AshfallMaterialLibrary.EmissiveRed;
            signRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var signLightGo = new GameObject("SignLight");
            signLightGo.transform.SetParent(signGo.transform, false);
            var signLight = signLightGo.AddComponent<Light>();
            signLight.type = LightType.Point;
            signLight.range = 5f;
            signLight.intensity = 2.4f;
            signLight.color = AshfallPalette.WarningRed;
            signLight.shadows = LightShadows.None;

            // Interact volume in front of the door.
            var promptGo = new GameObject("PromptVolume");
            promptGo.transform.SetParent(root.transform, false);
            promptGo.layer = AshfallLayers.Interactable;
            var trigger = promptGo.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 1.6f, 0f);
            trigger.size = new Vector3(size.x * 0.9f, 3.2f, 2.4f);

            var door = root.AddComponent<RouteDoor>();
            door.Configure(cost, gateName, zone, moving.transform, new Vector3(0f, size.y - 0.35f, 0f));

            var serialized = new UnityEditor.SerializedObject(door);
            serialized.FindProperty("title").stringValue = prompt;
            serialized.FindProperty("promptRange").floatValue = 4.0f;
            SetObjectArray(serialized, "blockingColliders", new Object[] { blocker });
            SetObjectArray(serialized, "signRenderers", new Object[] { signRenderer });
            SetObjectArray(serialized, "signLights", new Object[] { signLight });
            SetObjectArray(serialized, "highlightRenderers", new Object[] { signRenderer });
            serialized.ApplyModifiedPropertiesWithoutUndo();

            _r.Doors.Add(door);

            // The gate volume tells the nav baker which links this door controls. The
            // door's own rotation is baked into an axis-aligned extent so the baker can
            // do a cheap containment test per link.
            Vector3 rotated = rotation * new Vector3(size.x + 1.5f, 6f, 4.5f);
            _r.GateVolumes.Add(new AshfallNavBaker.GateVolume
            {
                Name = gateName,
                Bounds = new Bounds(
                    worldPosition + Vector3.up * 1.5f,
                    new Vector3(Mathf.Abs(rotated.x), Mathf.Abs(rotated.y), Mathf.Abs(rotated.z)))
            });

            return door;
        }

        private static WeaponStation BuildWeaponStation(
            string name, Vector3 position, Quaternion rotation,
            Weapons.WeaponDefinition definition, int cost, int refill, MapPhase requiredPhase)
        {
            var root = new GameObject(name);
            root.transform.SetParent(_props, false);
            root.transform.SetPositionAndRotation(position, rotation);
            root.layer = AshfallLayers.Interactable;

            Block(root.transform, "Backboard", new Vector3(0f, 0f, 0f), new Vector3(1.9f, 1.4f, 0.16f),
                AshfallMaterialLibrary.SteelPanel, 1.2f, layer: AshfallLayers.Interactable);
            Block(root.transform, "Shelf", new Vector3(0f, -0.72f, 0.28f), new Vector3(1.9f, 0.12f, 0.55f),
                AshfallMaterialLibrary.TreadPlate, 1f, layer: AshfallLayers.Interactable);
            HazardStrip(root.transform, "Stripe", new Vector3(0f, 0.62f, 0.10f), new Vector3(1.9f, 0.20f, 0.03f));

            var signGo = new GameObject("Sign");
            signGo.transform.SetParent(root.transform, false);
            signGo.transform.localPosition = new Vector3(0f, 0.34f, 0.10f);
            signGo.AddComponent<MeshFilter>().sharedMesh = AshfallGeometry.Box(new Vector3(1.5f, 0.42f, 0.05f), 0.5f);
            var signRenderer = signGo.AddComponent<MeshRenderer>();
            signRenderer.sharedMaterial = AshfallMaterialLibrary.EmissiveTeal;
            signRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // A slowly rotating display copy of the weapon: the clearest possible label.
            var display = new GameObject("Display");
            display.transform.SetParent(root.transform, false);
            display.transform.localPosition = new Vector3(0f, -0.28f, 0.42f);
            display.transform.localScale = Vector3.one * 1.35f;

            GameObject viewModelPrefab = definition == AshfallPrefabFactory.ShotgunDefinition
                ? AshfallPrefabFactory.ShotgunViewModel
                : definition == AshfallPrefabFactory.RifleDefinition
                    ? AshfallPrefabFactory.RifleViewModel
                    : AshfallPrefabFactory.SidearmViewModel;

            if (viewModelPrefab != null)
            {
                GameObject copy = Object.Instantiate(viewModelPrefab, display.transform);
                copy.name = "WeaponDisplay";
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

                // Object.Instantiate on a prefab asset yields a plain clone, but guard
                // anyway: components on a connected prefab instance cannot be removed.
                if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(copy))
                {
                    UnityEditor.PrefabUtility.UnpackPrefabInstance(
                        copy,
                        UnityEditor.PrefabUnpackMode.Completely,
                        UnityEditor.InteractionMode.AutomatedAction);
                }

                // The display is scenery, not a working weapon.
                Object.DestroyImmediate(copy.GetComponent<WeaponViewModel>());
                foreach (ParticleSystem ps in copy.GetComponentsInChildren<ParticleSystem>(true))
                {
                    Object.DestroyImmediate(ps.gameObject);
                }

                foreach (Light l in copy.GetComponentsInChildren<Light>(true))
                {
                    Object.DestroyImmediate(l.gameObject);
                }

                AshfallAssetUtility.SetLayerRecursive(copy, AshfallLayers.Interactable);
            }

            var stationLightGo = new GameObject("StationLight");
            stationLightGo.transform.SetParent(root.transform, false);
            stationLightGo.transform.localPosition = new Vector3(0f, 0.1f, 1.0f);
            var stationLight = stationLightGo.AddComponent<Light>();
            stationLight.type = LightType.Point;
            stationLight.range = 6.5f;
            stationLight.intensity = 2.8f;
            stationLight.color = definition != null ? definition.accentColor : AshfallPalette.StormTeal;
            stationLight.shadows = LightShadows.None;

            var promptGo = new GameObject("PromptVolume");
            promptGo.transform.SetParent(root.transform, false);
            promptGo.layer = AshfallLayers.Interactable;
            var trigger = promptGo.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0f, 0.7f);
            trigger.size = new Vector3(2.2f, 2.4f, 1.6f);

            var station = root.AddComponent<WeaponStation>();
            station.Configure(definition, cost, refill, requiredPhase);

            var serialized = new UnityEditor.SerializedObject(station);
            serialized.FindProperty("promptRange").floatValue = 3.4f;
            serialized.FindProperty("stationLight").objectReferenceValue = stationLight;
            serialized.FindProperty("displayModel").objectReferenceValue = display.transform;
            SetObjectArray(serialized, "signRenderers", new Object[] { signRenderer });
            SetObjectArray(serialized, "highlightRenderers", new Object[] { signRenderer });
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return station;
        }

        private static void SetObjectArray(UnityEditor.SerializedObject serialized, string propertyName, Object[] values)
        {
            UnityEditor.SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void AddPhaseElement(GameObject go, MapPhase first, MapPhase last, bool invert = false)
        {
            var element = go.GetComponent<PhaseElement>();
            if (element == null)
            {
                element = go.AddComponent<PhaseElement>();
            }

            element.Configure(first, last, invert);
            _r.PhaseElements.Add(element);
        }

        // ------------------------------------------------------------------
        // Lab wing
        // ------------------------------------------------------------------

        private static void BuildLabWing()
        {
            var zone = AshfallAssetUtility.NewChild(_geometry, "LabWing").transform;

            float width = LabMaxX - LabMinX;
            float depth = LabMaxZ - LabMinZ;
            var centre = new Vector3((LabMinX + LabMaxX) * 0.5f, 0f, (LabMinZ + LabMaxZ) * 0.5f);

            Block(zone, "Floor", centre + Vector3.down * 0.25f,
                new Vector3(width, 0.5f, depth), AshfallMaterialLibrary.ConcreteFloor, 4f);

            // Ceiling in four strips, leaving an open shaft under the roof lane's west
            // end. Built as segments rather than one slab because a box cannot have a
            // hole, and the shaft is what makes the roof-to-lab drop possible.
            float ceilingY = LabCeilingY + 0.2f;
            Block(zone, "CeilingWest",
                new Vector3((LabMinX + DropHoleMinX) * 0.5f, ceilingY, centre.z),
                new Vector3(DropHoleMinX - LabMinX, 0.4f, depth), AshfallMaterialLibrary.ConcreteWall, 4f);
            Block(zone, "CeilingEast",
                new Vector3((DropHoleMaxX + LabMaxX) * 0.5f, ceilingY, centre.z),
                new Vector3(LabMaxX - DropHoleMaxX, 0.4f, depth), AshfallMaterialLibrary.ConcreteWall, 4f);
            Block(zone, "CeilingSouth",
                new Vector3((DropHoleMinX + DropHoleMaxX) * 0.5f, ceilingY, (LabMinZ + DropHoleMinZ) * 0.5f),
                new Vector3(DropHoleMaxX - DropHoleMinX, 0.4f, DropHoleMinZ - LabMinZ),
                AshfallMaterialLibrary.ConcreteWall, 4f);
            Block(zone, "CeilingNorth",
                new Vector3((DropHoleMinX + DropHoleMaxX) * 0.5f, ceilingY, (DropHoleMaxZ + LabMaxZ) * 0.5f),
                new Vector3(DropHoleMaxX - DropHoleMinX, 0.4f, LabMaxZ - DropHoleMaxZ),
                AshfallMaterialLibrary.ConcreteWall, 4f);

            // Perimeter. South wall has the shutter opening back to the courtyard.
            WallWithGaps(zone, "WallSouth", new Vector3(centre.x, LabCeilingY * 0.5f, LabMinZ),
                new Vector3(width, LabCeilingY, WallThickness), true, new[] { (0f, 8f) }, barricadeGaps: false);
            WallWithGaps(zone, "WallNorth", new Vector3(centre.x, LabCeilingY * 0.5f, LabMaxZ),
                new Vector3(width, LabCeilingY, WallThickness), true, new[] { (-6f, 3.2f) });
            WallWithGaps(zone, "WallWest", new Vector3(LabMinX, LabCeilingY * 0.5f, centre.z),
                new Vector3(WallThickness, LabCeilingY, depth), false, new[] { (-3f, 3.2f) });
            Block(zone, "WallEast", new Vector3(LabMaxX, LabCeilingY * 0.5f, centre.z),
                new Vector3(WallThickness, LabCeilingY, depth), AshfallMaterialLibrary.ConcreteWall, 3f);

            // Interior partition making a corridor down the west side and two bays east.
            Block(zone, "PartitionA", new Vector3(-1f, LabCeilingY * 0.5f, 26f),
                new Vector3(0.5f, LabCeilingY, 12f), AshfallMaterialLibrary.SteelPanel, 3f);
            Block(zone, "PartitionB", new Vector3(3.5f, LabCeilingY * 0.5f, 34f),
                new Vector3(9.5f, LabCeilingY, 0.5f), AshfallMaterialLibrary.SteelPanel, 3f);

            // Lab benches and tanks.
            var props = AshfallAssetUtility.NewChild(_props, "LabProps").transform;
            for (int i = 0; i < 4; i++)
            {
                Block(props, $"Bench{i}", new Vector3(6.5f, 0.5f, 20f + i * 3.4f),
                    new Vector3(9f, 1.0f, 1.1f), AshfallMaterialLibrary.SteelPanel, 1.5f);
            }

            for (int i = 0; i < 3; i++)
            {
                GameObject tank = Column(props, $"Tank{i}", new Vector3(-9.5f, 1.4f, 22f + i * 6f),
                    0.9f, 2.8f, AshfallMaterialLibrary.SteelPanel, 16, 1.5f);
                var glow = new GameObject("Glow");
                glow.transform.SetParent(tank.transform, false);
                glow.transform.localPosition = new Vector3(0f, 0.2f, 0f);
                glow.AddComponent<MeshFilter>().sharedMesh = AshfallGeometry.Cylinder(0.93f, 0.9f, 16, 1f);
                var glowRenderer = glow.AddComponent<MeshRenderer>();
                glowRenderer.sharedMaterial = AshfallMaterialLibrary.EmissiveTeal;
                glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            Block(props, "ServerRack", new Vector3(10.5f, 1.1f, 40f), new Vector3(3.2f, 2.2f, 1.2f),
                AshfallMaterialLibrary.SteelDark, 1.5f);
            Block(props, "ServerGlow", new Vector3(10.5f, 1.1f, 39.35f), new Vector3(2.8f, 1.8f, 0.06f),
                AshfallMaterialLibrary.EmissiveTeal, 1f, collider: false);

            // --- lighting ------------------------------------------------------
            for (int i = 0; i < 4; i++)
            {
                Lamp(_lights, $"LabLamp_{i}", new Vector3(0f, LabCeilingY - 0.25f, 20f + i * 7f), 13f,
                    AshfallPalette.EmergencyAmber, 3.6f, AshfallPalette.StormTeal, 2.2f,
                    flicker: i == 2);
            }

            // --- sidearm resupply -------------------------------------------------
            _r.Stations.Add(BuildWeaponStation(
                "Station_MeridianResupply",
                new Vector3(LabMaxX - 0.65f, 1.55f, 24f),
                Quaternion.Euler(0f, -90f, 0f),
                AshfallPrefabFactory.SidearmDefinition,
                0, 300, MapPhase.Standby));

            // The shaft under the roof lane, edged in hazard paint so the drop reads as
            // deliberate from below as well as above.
            var shaft = AshfallAssetUtility.NewChild(_props, "DropShaft").transform;
            float shaftCentreX = (DropHoleMinX + DropHoleMaxX) * 0.5f;
            float shaftCentreZ = (DropHoleMinZ + DropHoleMaxZ) * 0.5f;
            float shaftWidth = DropHoleMaxX - DropHoleMinX;
            float shaftDepth = DropHoleMaxZ - DropHoleMinZ;

            HazardStrip(shaft, "ShaftLipWest",
                new Vector3(DropHoleMinX + 0.1f, LabCeilingY + 0.45f, shaftCentreZ),
                new Vector3(0.2f, 0.5f, shaftDepth));
            HazardStrip(shaft, "ShaftLipNorth",
                new Vector3(shaftCentreX, LabCeilingY + 0.45f, DropHoleMaxZ - 0.1f),
                new Vector3(shaftWidth, 0.5f, 0.2f));
            HazardStrip(shaft, "ShaftLipSouth",
                new Vector3(shaftCentreX, LabCeilingY + 0.45f, DropHoleMinZ + 0.1f),
                new Vector3(shaftWidth, 0.5f, 0.2f));

            // A stack of crates under the shaft: signposts the landing spot and softens
            // the read of a five-metre drop.
            Block(shaft, "LandingCrateA", new Vector3(shaftCentreX - 1.2f, 0.6f, shaftCentreZ + 1f),
                new Vector3(1.6f, 1.2f, 1.6f), AshfallMaterialLibrary.SteelPanel, 1.2f,
                euler: new Vector3(0f, 12f, 0f));
            Block(shaft, "LandingCrateB", new Vector3(shaftCentreX + 1.4f, 0.45f, shaftCentreZ - 1.4f),
                new Vector3(1.4f, 0.9f, 1.4f), AshfallMaterialLibrary.SteelPanel, 1.2f,
                euler: new Vector3(0f, -22f, 0f));
        }

        // ------------------------------------------------------------------
        // Generator room
        // ------------------------------------------------------------------

        private static void BuildGeneratorRoom()
        {
            var zone = AshfallAssetUtility.NewChild(_geometry, "GeneratorRoom").transform;

            float width = GenMaxX - GenMinX;
            float depth = GenMaxZ - GenMinZ;
            var centre = new Vector3((GenMinX + GenMaxX) * 0.5f, 0f, (GenMinZ + GenMaxZ) * 0.5f);

            Block(zone, "Floor", centre + Vector3.down * 0.25f,
                new Vector3(width, 0.5f, depth), AshfallMaterialLibrary.ConcreteFloor, 4f);

            // Roof with a long skylight slot: how the storm gets in at high phases.
            Block(zone, "RoofSouth", new Vector3(centre.x, GenCeilingY, GenMinZ + depth * 0.28f),
                new Vector3(width, 0.4f, depth * 0.56f), AshfallMaterialLibrary.TreadPlate, 4f);
            Block(zone, "RoofNorth", new Vector3(centre.x, GenCeilingY, GenMaxZ - depth * 0.14f),
                new Vector3(width, 0.4f, depth * 0.28f), AshfallMaterialLibrary.TreadPlate, 4f);

            // Gap offsets are measured from each wall's own centre. The west opening has
            // to line up with the courtyard's east opening at world z = 0, and the north
            // opening with the roof bridge at world x = 40.5.
            WallWithGaps(zone, "WallWest", new Vector3(GenMinX, WallHeight * 0.5f, centre.z),
                new Vector3(WallThickness, WallHeight, depth), false,
                new[] { (0f - centre.z, 8f) }, barricadeGaps: false);
            WallWithGaps(zone, "WallEast", new Vector3(GenMaxX, WallHeight * 0.5f, centre.z),
                new Vector3(WallThickness, WallHeight, depth), false, new[] { (-4f, 3.2f) });
            WallWithGaps(zone, "WallSouth", new Vector3(centre.x, WallHeight * 0.5f, GenMinZ),
                new Vector3(width, WallHeight, WallThickness), true, new[] { (-6f, 3.2f) });
            WallWithGaps(zone, "WallNorth", new Vector3(centre.x, WallHeight * 0.5f, GenMaxZ),
                new Vector3(width, WallHeight, WallThickness), true,
                new[] { (40.5f - centre.x, 4.0f) }, barricadeGaps: false);

            // --- the generators themselves ---------------------------------------
            var props = AshfallAssetUtility.NewChild(_props, "GeneratorProps").transform;
            for (int i = 0; i < 3; i++)
            {
                float x = 23f + i * 7.5f;
                Block(props, $"GenBase{i}", new Vector3(x, 0.45f, -5f), new Vector3(5.2f, 0.9f, 5.2f),
                    AshfallMaterialLibrary.ConcreteWall, 2f);
                Column(props, $"GenBody{i}", new Vector3(x, 2.6f, -5f), 1.9f, 3.4f,
                    AshfallMaterialLibrary.SteelPanel, 18, 2f);
                Column(props, $"GenCap{i}", new Vector3(x, 4.5f, -5f), 2.1f, 0.5f,
                    AshfallMaterialLibrary.RustedMetal, 18, 1f);
                HazardStrip(props, $"GenStripe{i}", new Vector3(x, 0.92f, -5f), new Vector3(5.4f, 0.03f, 5.4f));

                // The rings that light up at the Surge phase.
                var ringGo = new GameObject($"GenRing{i}");
                ringGo.transform.SetParent(props, false);
                ringGo.transform.localPosition = new Vector3(x, 3.4f, -5f);
                ringGo.AddComponent<MeshFilter>().sharedMesh = AshfallGeometry.Cylinder(1.95f, 0.28f, 18, 1f);
                var ringRenderer = ringGo.AddComponent<MeshRenderer>();
                ringRenderer.sharedMaterial = AshfallMaterialLibrary.EmissiveTeal;
                ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                AddPhaseElement(ringGo, MapPhase.Surge, MapPhase.Meridian);

                // Overhead conduit tying the machines to the ceiling.
                Column(props, $"GenConduit{i}", new Vector3(x, 6.4f, -5f), 0.28f, 3.4f,
                    AshfallMaterialLibrary.RustedMetal, 10, 1.5f, collider: false);
            }

            Block(props, "PipeRun", new Vector3(31f, 6.2f, 6f), new Vector3(24f, 0.5f, 0.5f),
                AshfallMaterialLibrary.RustedMetal, 3f, collider: false);
            Block(props, "PipeRun2", new Vector3(31f, 5.6f, 6.9f), new Vector3(24f, 0.36f, 0.36f),
                AshfallMaterialLibrary.RustedMetal, 3f, collider: false);

            // --- catwalk ----------------------------------------------------------
            // Deck top sits at CatwalkY + 0.15; every flight is sized to arrive exactly on
            // that surface, so nothing needs a step-up larger than the AI can make.
            const float catwalkTop = CatwalkY + 0.15f;
            var catwalk = AshfallAssetUtility.NewChild(_geometry, "GeneratorCatwalk").transform;
            // Deck depth is 4.4m on purpose. The nav baker samples on a 2m grid, so a
            // 3.2m walkway catches only a single row of nodes -- and the roof ramp
            // overhanging that one row was enough to sever the entire roof lane.
            Block(catwalk, "Deck", new Vector3(31f, CatwalkY, 10f), new Vector3(24f, 0.3f, 4.4f),
                AshfallMaterialLibrary.TreadPlate, 2f);

            // The rail deliberately stops short of the stair head; that gap is the way up.
            Railing(catwalk, "RailSouth", new Vector3(32.5f, catwalkTop, 7.9f),
                new Vector3(21f, 1f, 0.1f), AshfallMaterialLibrary.SteelDark);

            // Run of 7m for a 3.65m rise, i.e. about 27 degrees. Steeper flights are
            // walkable but sit near the nav baker's slope tolerance, and a stair that
            // bakes as a wall is worse than one that takes an extra second to climb.
            const int catwalkSteps = 16;
            Stairs(catwalk, "CatwalkStairs", new Vector3(20.5f, 0f, 0.8f), Vector3.forward,
                catwalkSteps, catwalkTop / catwalkSteps, 7.0f / catwalkSteps, 2.6f,
                AshfallMaterialLibrary.TreadPlate);

            // --- stairs to the roof lane -------------------------------------------
            // Starts at z = 12.6, north of the deck's 12.2 edge, so the ramp never
            // shadows a deck node.
            const int roofSteps = 9;
            Stairs(catwalk, "RoofStairs", new Vector3(40.5f, catwalkTop, 12.6f), Vector3.forward,
                roofSteps, (RoofY - catwalkTop) / roofSteps, 3.4f / roofSteps, 2.6f,
                AshfallMaterialLibrary.TreadPlate);

            // Short landing that overlaps both the ramp top and the roof deck's south
            // edge, so the sample grid always finds floor across the junction.
            Block(catwalk, "RoofBridge", new Vector3(40.5f, RoofY - 0.15f, 16.55f),
                new Vector3(2.8f, 0.3f, 2.1f), AshfallMaterialLibrary.TreadPlate, 2f);

            Railing(catwalk, "BridgeRailEast", new Vector3(41.9f, RoofY, 16.55f),
                new Vector3(0.1f, 1f, 2.1f), AshfallMaterialLibrary.SteelDark);
            Railing(catwalk, "BridgeRailWest", new Vector3(39.1f, RoofY, 16.55f),
                new Vector3(0.1f, 1f, 2.1f), AshfallMaterialLibrary.SteelDark);

            // The third purchasable route. It sits on the flat part of the bridge, where
            // the walkway is exactly as wide as the shutter, so it cannot be walked around.
            RouteDoor hatch = BuildRouteDoor(
                "Door_RoofHatch",
                "Blow the roof access hatch",
                new Vector3(40.5f, RoofY, 16.6f),
                Quaternion.identity,
                new Vector3(2.8f, 2.6f, 0.4f),
                1600,
                "Rooftop",
                StationZone.Rooftop);

            _r.AutoOpenDoors[MapPhase.Meridian] = new List<RouteDoor> { hatch };

            // --- lighting ------------------------------------------------------
            for (int i = 0; i < 3; i++)
            {
                Lamp(_lights, $"GenLamp_{i}", new Vector3(23f + i * 8f, 7.4f, 2f), 15f,
                    AshfallPalette.EmergencyAmberDeep, 1.4f, AshfallPalette.StormTeal, 4.2f);
            }

            Lamp(_lights, "GenLamp_Catwalk", new Vector3(31f, 7.4f, 11f), 14f,
                AshfallPalette.EmergencyAmber, 2.2f, AshfallPalette.StormTeal, 3.4f, flicker: true);

            // --- Arc-9 rack ---------------------------------------------------------
            _r.Stations.Add(BuildWeaponStation(
                "Station_Arc9",
                new Vector3(GenMaxX - 0.7f, 1.55f, 4f),
                Quaternion.Euler(0f, -90f, 0f),
                AshfallPrefabFactory.RifleDefinition,
                1900, 620, MapPhase.Standby));
        }

        // ------------------------------------------------------------------
        // Roof lane
        // ------------------------------------------------------------------

        private static void BuildRoofLane()
        {
            var zone = AshfallAssetUtility.NewChild(_geometry, "RoofLane").transform;

            float width = RoofMaxX - RoofMinX;
            float depth = RoofMaxZ - RoofMinZ;
            var centre = new Vector3((RoofMinX + RoofMaxX) * 0.5f, RoofY, (RoofMinZ + RoofMaxZ) * 0.5f);

            Block(zone, "Deck", centre + Vector3.down * 0.15f,
                new Vector3(width, 0.3f, depth), AshfallMaterialLibrary.TreadPlate, 3f);

            // South rail stops short of x = 38.5: that opening is where the generator
            // bridge lands on the deck.
            Railing(zone, "RailSouth", new Vector3((RoofMinX + 38.5f) * 0.5f, RoofY, RoofMinZ + 0.2f),
                new Vector3(38.5f - RoofMinX, 1f, 0.1f), AshfallMaterialLibrary.SteelDark);
            Railing(zone, "RailNorth", new Vector3(centre.x, RoofY, RoofMaxZ - 0.2f),
                new Vector3(width, 1f, 0.1f), AshfallMaterialLibrary.SteelDark);

            // No railing on the west end: that edge is the escape drop into the lab.
            // A painted lip marks it so the player can find it deliberately rather than
            // discovering it by falling off.
            HazardStrip(zone, "DropLip", new Vector3(RoofMinX + 0.5f, RoofY + 0.02f, centre.z),
                new Vector3(1.0f, 0.04f, depth));
            for (int i = 0; i < 2; i++)
            {
                Block(zone, $"DropPost{i}", new Vector3(RoofMinX + 0.15f, RoofY + 0.55f, centre.z + (i == 0 ? -depth * 0.45f : depth * 0.45f)),
                    new Vector3(0.14f, 1.1f, 0.14f), AshfallMaterialLibrary.SteelDark, 0.5f, collider: false);
            }

            // Vents and aerials so the lane is not a bare plank.
            var props = AshfallAssetUtility.NewChild(_props, "RoofProps").transform;
            for (int i = 0; i < 5; i++)
            {
                float x = RoofMinX + 3f + i * ((RoofMaxX - RoofMinX - 6f) / 4f);
                Block(props, $"Vent{i}", new Vector3(x, RoofY + 0.55f, 23.5f),
                    new Vector3(2.0f, 1.1f, 1.6f), AshfallMaterialLibrary.SteelPanel, 1.2f);
                Column(props, $"Aerial{i}", new Vector3(x + 3f, RoofY + 1.6f, 19f),
                    0.10f, 3.2f, AshfallMaterialLibrary.RustedMetal, 8, 1f, collider: false);
            }

            // Storm conductors: dead early, blazing by Meridian.
            for (int i = 0; i < 4; i++)
            {
                float x = RoofMinX + 4f + i * ((RoofMaxX - RoofMinX - 8f) / 3f);
                var rodGo = new GameObject($"Conductor{i}");
                rodGo.transform.SetParent(props, false);
                rodGo.transform.localPosition = new Vector3(x, RoofY + 2.6f, 21.5f);
                rodGo.AddComponent<MeshFilter>().sharedMesh = AshfallGeometry.Cylinder(0.16f, 5.2f, 10, 1f);
                var rodRenderer = rodGo.AddComponent<MeshRenderer>();
                rodRenderer.sharedMaterial = AshfallMaterialLibrary.EmissiveTeal;
                rodRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                AddPhaseElement(rodGo, MapPhase.Surge, MapPhase.Meridian);

                Lamp(_lights, $"ConductorLight{i}", new Vector3(x, RoofY + 5.4f, 21.5f), 16f,
                    AshfallPalette.StormTeal, 0f, AshfallPalette.StormTeal, 6f);
            }

            // Weather hoarding: shelters the lane until the Meridian phase tears it away.
            var hoarding = AshfallAssetUtility.NewChild(_geometry, "RoofHoarding").transform;
            Block(hoarding, "Cover", new Vector3(centre.x, RoofY + 3.2f, centre.z),
                new Vector3(width * 0.86f, 0.3f, depth * 0.8f), AshfallMaterialLibrary.SteelPanel, 3f);
            for (int i = 0; i < 6; i++)
            {
                Block(hoarding, $"Strut{i}", new Vector3(RoofMinX + 2f + i * ((width - 4f) / 5f), RoofY + 1.6f, RoofMaxZ - 1.5f),
                    new Vector3(0.22f, 3.2f, 0.22f), AshfallMaterialLibrary.SteelPanel, 1f);
            }

            AddPhaseElement(hoarding.gameObject, MapPhase.Standby, MapPhase.Blackout);

            // --- storm exposure ----------------------------------------------------
            _r.StormVolumes.Add(BuildStormVolume(
                "Storm_RoofLane",
                new Vector3(centre.x, RoofY + 2f, centre.z),
                new Vector3(width, 4.5f, depth),
                new[] { 0f, 0f, 3.5f, 7.5f, 13f }));

            // The generator's skylight slot lets the storm reach the catwalk late on.
            _r.StormVolumes.Add(BuildStormVolume(
                "Storm_GeneratorSkylight",
                new Vector3(31f, CatwalkY + 1.6f, 10f),
                new Vector3(24f, 3.2f, 3.4f),
                new[] { 0f, 0f, 0f, 4f, 8f }));
        }

        private static StormExposureVolume BuildStormVolume(string name, Vector3 center, Vector3 size, float[] damagePerPhase)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_markers, false);
            go.transform.position = center;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;

            var rainGo = new GameObject("ExposureFx");
            rainGo.transform.SetParent(go.transform, false);
            rainGo.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            ParticleSystem fx = BuildStormSpray(rainGo, size);

            var lightGo = new GameObject("ExposureLight");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, size.y * 0.4f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = Mathf.Max(size.x, size.z) * 0.6f;
            light.color = AshfallPalette.StormTeal;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.enabled = false;

            var volume = go.AddComponent<StormExposureVolume>();
            volume.Configure(damagePerPhase, fx, light);
            return volume;
        }

        private static ParticleSystem BuildStormSpray(GameObject host, Vector3 size)
        {
            var ps = host.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startColor = new Color(AshfallPalette.StormTeal.r, AshfallPalette.StormTeal.g, AshfallPalette.StormTeal.b, 0.55f);
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.4f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(size.x, 0.1f, size.z);
            shape.rotation = new Vector3(90f, 0f, 0f);

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AshfallMaterialLibrary.FxAdditive;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.09f;
            renderer.lengthScale = 3.4f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return ps;
        }

        // ------------------------------------------------------------------
        // Atmosphere
        // ------------------------------------------------------------------

        private static void BuildAtmosphere()
        {
            var sunGo = new GameObject("Sun (Storm Key)");
            sunGo.transform.SetParent(_lights, false);
            sunGo.transform.rotation = Quaternion.Euler(34f, 152f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = AshfallPalette.MoonKey;
            sun.intensity = 0.55f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.82f;
            _r.Sun = sun;

            var flashGo = new GameObject("Storm Flash");
            flashGo.transform.SetParent(_lights, false);
            flashGo.transform.rotation = Quaternion.Euler(22f, 200f, 0f);
            var flash = flashGo.AddComponent<Light>();
            flash.type = LightType.Directional;
            flash.color = AshfallPalette.StormTeal;
            flash.intensity = 0f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
            _r.StormFlash = flash;

            // Rain covering the whole playable footprint, driven by the phase controller.
            var rainGo = new GameObject("Rain");
            rainGo.transform.SetParent(_lights, false);
            rainGo.transform.position = new Vector3(14f, 22f, 14f);
            var rain = rainGo.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = rain.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(14f, 20f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);
            main.startColor = new Color(0.68f, 0.80f, 0.84f, 0.42f);
            main.maxParticles = 4000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1.1f;

            ParticleSystem.EmissionModule emission = rain.emission;
            emission.rateOverTime = 140f;

            ParticleSystem.ShapeModule shape = rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(84f, 0.1f, 86f);
            shape.rotation = new Vector3(90f, 0f, 0f);

            var rainRenderer = rainGo.GetComponent<ParticleSystemRenderer>();
            rainRenderer.sharedMaterial = AshfallMaterialLibrary.FxAdditive;
            rainRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            rainRenderer.velocityScale = 0.10f;
            rainRenderer.lengthScale = 4.5f;
            rainRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rainRenderer.receiveShadows = false;
            _r.Rain = rain;

            // Wind-blown embers and grit: the layer that makes the air feel occupied.
            var emberGo = new GameObject("Embers");
            emberGo.transform.SetParent(_lights, false);
            emberGo.transform.position = new Vector3(14f, 4f, 14f);
            var embers = emberGo.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule emberMain = embers.main;
            emberMain.loop = true;
            emberMain.playOnAwake = true;
            emberMain.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 7f);
            emberMain.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.4f);
            emberMain.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.07f);
            emberMain.startColor = new Color(AshfallPalette.EmergencyAmber.r, AshfallPalette.EmergencyAmber.g, AshfallPalette.EmergencyAmber.b, 0.5f);
            emberMain.maxParticles = 260;
            emberMain.simulationSpace = ParticleSystemSimulationSpace.World;
            emberMain.gravityModifier = -0.03f;

            ParticleSystem.EmissionModule emberEmission = embers.emission;
            emberEmission.rateOverTime = 6f;

            ParticleSystem.ShapeModule emberShape = embers.shape;
            emberShape.shapeType = ParticleSystemShapeType.Box;
            emberShape.scale = new Vector3(70f, 8f, 72f);

            ParticleSystem.NoiseModule noise = embers.noise;
            noise.enabled = true;
            noise.strength = 0.9f;
            noise.frequency = 0.22f;

            var emberRenderer = emberGo.GetComponent<ParticleSystemRenderer>();
            emberRenderer.sharedMaterial = AshfallMaterialLibrary.FxAdditive;
            emberRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            emberRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            emberRenderer.receiveShadows = false;
            _r.Embers = embers;
        }

        // ------------------------------------------------------------------
        // Spawn points
        // ------------------------------------------------------------------

        private static void BuildSpawnPoints()
        {
            var root = AshfallAssetUtility.NewChild(_markers, "SpawnPoints").transform;

            Add(root, "Spawn_Court_South_A", new Vector3(-9f, 0.1f, CourtMinZ + 1.6f), MapPhase.Standby, StationZone.Courtyard, 1.4f);
            Add(root, "Spawn_Court_South_B", new Vector3(7f, 0.1f, CourtMinZ + 1.6f), MapPhase.Standby, StationZone.Courtyard, 1.4f);
            Add(root, "Spawn_Court_West", new Vector3(CourtMinX + 1.6f, 0.1f, 6f), MapPhase.Standby, StationZone.Courtyard, 1.2f);

            Add(root, "Spawn_Lab_North", new Vector3(-6f, 0.1f, LabMaxZ - 1.8f), MapPhase.Breach, StationZone.LabWing, 1.2f);
            Add(root, "Spawn_Lab_West", new Vector3(LabMinX + 1.8f, 0.1f, 45f - 17f), MapPhase.Breach, StationZone.LabWing, 1f);

            Add(root, "Spawn_Gen_East", new Vector3(GenMaxX - 1.8f, 0.1f, -4f), MapPhase.Surge, StationZone.GeneratorRoom, 1.2f);
            Add(root, "Spawn_Gen_South", new Vector3(GenMinX + 6f, 0.1f, GenMinZ + 1.8f), MapPhase.Surge, StationZone.GeneratorRoom, 1.2f);

            Add(root, "Spawn_Catwalk", new Vector3(38f, CatwalkY + 0.2f, 10f), MapPhase.Blackout, StationZone.Catwalk, 1f);

            Add(root, "Spawn_Roof_West", new Vector3(RoofMinX + 3f, RoofY + 0.2f, 21.5f), MapPhase.Meridian, StationZone.Rooftop, 1.3f);
            Add(root, "Spawn_Roof_East", new Vector3(RoofMaxX - 3f, RoofY + 0.2f, 21.5f), MapPhase.Meridian, StationZone.Rooftop, 1.3f);
        }

        private static void Add(Transform parent, string name, Vector3 position, MapPhase phase, StationZone zone, float weight)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            var point = go.AddComponent<EnemySpawnPoint>();
            point.Configure(phase, zone, weight, false, string.Empty);
            _r.SpawnPoints.Add(point);
        }
    }
}
