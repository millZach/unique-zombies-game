using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Ashfall.Core;
using Ashfall.Enemies;

namespace Ashfall.World
{
    /// <summary>
    /// Turns a round number into a physically different station.
    ///
    /// Every phase change moves five things at once -- routes, lights, props, weather
    /// and storm exposure -- because a map that only swaps a light colour does not read
    /// as evolving. Transitions are lerped over a couple of seconds so the player sees
    /// the station change rather than finding it changed.
    /// </summary>
    public class MapPhaseController : MonoBehaviour
    {
        [Serializable]
        public class PhaseAtmosphere
        {
            public Color fogColor = AshfallPalette.FogCalm;
            public float fogDensity = 0.022f;
            public Color ambientColor = new Color(0.10f, 0.12f, 0.15f);
            public float ambientIntensity = 0.55f;
            public Color sunColor = AshfallPalette.MoonKey;
            public float sunIntensity = 0.55f;
            public Vector3 sunEuler = new Vector3(38f, 156f, 0f);
            public float rainRate;
            public float windStrength;
            public float stormFlashesPerMinute;
        }

        [Header("Registered elements")]
        [SerializeField] private List<PhaseElement> phaseElements = new();
        [SerializeField] private List<PhaseLight> phaseLights = new();
        [SerializeField] private List<RouteDoor> doors = new();
        [SerializeField] private List<WeaponStation> weaponStations = new();
        [SerializeField] private List<StormExposureVolume> stormVolumes = new();

        [Header("Doors that fail open on their own")]
        [Tooltip("Index matches MapPhase. Doors listed for a phase are forced open when it begins.")]
        [SerializeField] private List<PhaseDoorGroup> autoOpenDoors = new();

        [Header("Atmosphere")]
        [SerializeField] private PhaseAtmosphere[] atmospherePerPhase = new PhaseAtmosphere[MapPhases.Count];
        [SerializeField] private Light sunLight;
        [SerializeField] private ParticleSystem rainSystem;
        [SerializeField] private ParticleSystem emberSystem;
        [SerializeField] private float atmosphereBlendSeconds = 2.6f;

        [Header("Storm flashes")]
        [SerializeField] private Light stormFlashLight;
        [SerializeField] private float stormFlashIntensity = 3.2f;

        [Header("Links")]
        [SerializeField] private EnemyDirector enemyDirector;

        [Serializable]
        public class PhaseDoorGroup
        {
            public MapPhase phase;
            public List<RouteDoor> doors = new();
        }

        public event Action<MapPhase> PhaseChanged;

        public MapPhase CurrentPhase { get; private set; } = MapPhase.Standby;

        private PhaseAtmosphere _atmosphereFrom;
        private PhaseAtmosphere _atmosphereTo;
        private float _atmosphereTimer = -1f;
        private float _nextStormFlash;
        private float _stormFlashUntil;
        private bool _initialised;

        private void Awake()
        {
            EnsureAtmosphereDefaults();
        }

        private void Start()
        {
            if (!_initialised)
            {
                ApplyPhase(MapPhase.Standby, instant: true);
            }
        }

        public void Configure(
            List<PhaseElement> elements,
            List<PhaseLight> lights,
            List<RouteDoor> routeDoors,
            List<WeaponStation> stations,
            List<StormExposureVolume> volumes,
            Light sun,
            ParticleSystem rain,
            ParticleSystem embers,
            Light stormFlash,
            EnemyDirector director)
        {
            phaseElements = elements;
            phaseLights = lights;
            doors = routeDoors;
            weaponStations = stations;
            stormVolumes = volumes;
            sunLight = sun;
            rainSystem = rain;
            emberSystem = embers;
            stormFlashLight = stormFlash;
            enemyDirector = director;
        }

        public void SetAtmosphere(int phaseIndex, PhaseAtmosphere atmosphere)
        {
            EnsureAtmosphereDefaults();
            if (phaseIndex >= 0 && phaseIndex < atmospherePerPhase.Length)
            {
                atmospherePerPhase[phaseIndex] = atmosphere;
            }
        }

        public void SetAutoOpenDoors(MapPhase phase, List<RouteDoor> group)
        {
            for (int i = 0; i < autoOpenDoors.Count; i++)
            {
                if (autoOpenDoors[i].phase == phase)
                {
                    autoOpenDoors[i].doors = group;
                    return;
                }
            }

            autoOpenDoors.Add(new PhaseDoorGroup { phase = phase, doors = group });
        }

        private void EnsureAtmosphereDefaults()
        {
            if (atmospherePerPhase != null && atmospherePerPhase.Length == MapPhases.Count)
            {
                for (int i = 0; i < atmospherePerPhase.Length; i++)
                {
                    atmospherePerPhase[i] ??= DefaultAtmosphere((MapPhase)i);
                }

                return;
            }

            atmospherePerPhase = new PhaseAtmosphere[MapPhases.Count];
            for (int i = 0; i < MapPhases.Count; i++)
            {
                atmospherePerPhase[i] = DefaultAtmosphere((MapPhase)i);
            }
        }

        /// <summary>
        /// The authored atmosphere arc: a dim amber night that loses its power and is
        /// replaced by a teal electrical storm.
        /// </summary>
        public static PhaseAtmosphere DefaultAtmosphere(MapPhase phase)
        {
            switch (phase)
            {
                case MapPhase.Standby:
                    return new PhaseAtmosphere
                    {
                        fogColor = AshfallPalette.FogCalm,
                        fogDensity = 0.020f,
                        ambientColor = new Color(0.085f, 0.098f, 0.125f),
                        ambientIntensity = 0.62f,
                        sunColor = AshfallPalette.MoonKey,
                        sunIntensity = 0.55f,
                        sunEuler = new Vector3(34f, 152f, 0f),
                        rainRate = 140f,
                        windStrength = 0.35f,
                        stormFlashesPerMinute = 3f
                    };

                case MapPhase.Breach:
                    return new PhaseAtmosphere
                    {
                        fogColor = Color.Lerp(AshfallPalette.FogCalm, AshfallPalette.FogStorm, 0.35f),
                        fogDensity = 0.026f,
                        ambientColor = new Color(0.078f, 0.106f, 0.133f),
                        ambientIntensity = 0.56f,
                        sunColor = Color.Lerp(AshfallPalette.MoonKey, AshfallPalette.StormTeal, 0.28f),
                        sunIntensity = 0.46f,
                        sunEuler = new Vector3(28f, 168f, 0f),
                        rainRate = 320f,
                        windStrength = 0.6f,
                        stormFlashesPerMinute = 7f
                    };

                case MapPhase.Surge:
                    return new PhaseAtmosphere
                    {
                        fogColor = Color.Lerp(AshfallPalette.FogCalm, AshfallPalette.FogStorm, 0.65f),
                        fogDensity = 0.034f,
                        ambientColor = new Color(0.063f, 0.106f, 0.129f),
                        ambientIntensity = 0.50f,
                        sunColor = Color.Lerp(AshfallPalette.MoonKey, AshfallPalette.StormTeal, 0.5f),
                        sunIntensity = 0.36f,
                        sunEuler = new Vector3(21f, 184f, 0f),
                        rainRate = 620f,
                        windStrength = 0.95f,
                        stormFlashesPerMinute = 14f
                    };

                case MapPhase.Blackout:
                    return new PhaseAtmosphere
                    {
                        fogColor = AshfallPalette.FogStorm,
                        fogDensity = 0.044f,
                        ambientColor = new Color(0.043f, 0.098f, 0.118f),
                        ambientIntensity = 0.40f,
                        sunColor = Color.Lerp(AshfallPalette.MoonKey, AshfallPalette.StormTeal, 0.72f),
                        sunIntensity = 0.22f,
                        sunEuler = new Vector3(15f, 200f, 0f),
                        rainRate = 900f,
                        windStrength = 1.3f,
                        stormFlashesPerMinute = 24f
                    };

                case MapPhase.Meridian:
                default:
                    return new PhaseAtmosphere
                    {
                        fogColor = new Color(0.043f, 0.118f, 0.137f),
                        fogDensity = 0.055f,
                        ambientColor = new Color(0.035f, 0.110f, 0.133f),
                        ambientIntensity = 0.34f,
                        sunColor = AshfallPalette.StormTeal,
                        sunIntensity = 0.16f,
                        sunEuler = new Vector3(9f, 214f, 0f),
                        rainRate = 1400f,
                        windStrength = 1.85f,
                        stormFlashesPerMinute = 42f
                    };
            }
        }

        /// <summary>Called by the game director whenever a round begins.</summary>
        public void ApplyRound(int round, bool instant = false)
        {
            MapPhase phase = MapPhases.ForRound(round);
            if (phase != CurrentPhase || !_initialised)
            {
                ApplyPhase(phase, instant);
            }
        }

        public void ApplyPhase(MapPhase phase, bool instant)
        {
            EnsureAtmosphereDefaults();

            CurrentPhase = phase;
            _initialised = true;

            for (int i = 0; i < phaseElements.Count; i++)
            {
                phaseElements[i]?.ApplyPhase(phase, instant);
            }

            for (int i = 0; i < phaseLights.Count; i++)
            {
                phaseLights[i]?.ApplyPhase(phase, instant);
            }

            for (int i = 0; i < weaponStations.Count; i++)
            {
                weaponStations[i]?.SetPhase(phase);
            }

            for (int i = 0; i < stormVolumes.Count; i++)
            {
                stormVolumes[i]?.SetPhase(phase);
            }

            for (int i = 0; i < autoOpenDoors.Count; i++)
            {
                PhaseDoorGroup group = autoOpenDoors[i];
                if (group == null || group.phase > phase)
                {
                    continue;
                }

                for (int d = 0; d < group.doors.Count; d++)
                {
                    group.doors[d]?.ForceOpen(instant);
                }
            }

            enemyDirector?.SetPhase(phase);

            PhaseAtmosphere target = atmospherePerPhase[Mathf.Clamp((int)phase, 0, atmospherePerPhase.Length - 1)];
            if (instant)
            {
                _atmosphereFrom = target;
                _atmosphereTo = target;
                _atmosphereTimer = -1f;
                ApplyAtmosphere(target);
            }
            else
            {
                _atmosphereFrom = CaptureCurrentAtmosphere();
                _atmosphereTo = target;
                _atmosphereTimer = 0f;
            }

            // The storm bed joins the rain, the lights and the fog rather than
            // being a separate thing that happens to be playing.
            Audio.AudioDirector.Instance?.SetStormIntensity(StormIntensityFor(target));

            PhaseChanged?.Invoke(phase);
        }

        /// <summary>
        /// Weather severity as a 0..1 dial, taken from wind rather than rain:
        /// rain is a particle count that also tracks how much of the map is
        /// under cover, while wind is the phase's own idea of how bad it is.
        /// The floor keeps a distant rumble audible even on a calm night.
        /// </summary>
        public static float StormIntensityFor(PhaseAtmosphere atmosphere)
        {
            if (atmosphere == null)
            {
                return 0f;
            }

            return 0.12f + 0.88f * Mathf.InverseLerp(0.30f, 1.85f, atmosphere.windStrength);
        }

        private PhaseAtmosphere CaptureCurrentAtmosphere()
        {
            var current = new PhaseAtmosphere
            {
                fogColor = RenderSettings.fogColor,
                fogDensity = RenderSettings.fogDensity,
                ambientColor = RenderSettings.ambientLight,
                ambientIntensity = RenderSettings.ambientIntensity,
                sunColor = sunLight != null ? sunLight.color : AshfallPalette.MoonKey,
                sunIntensity = sunLight != null ? sunLight.intensity : 0.5f,
                sunEuler = sunLight != null ? sunLight.transform.eulerAngles : new Vector3(35f, 150f, 0f)
            };

            if (rainSystem != null)
            {
                current.rainRate = rainSystem.emission.rateOverTime.constant;
            }

            current.stormFlashesPerMinute = _atmosphereTo?.stormFlashesPerMinute ?? 0f;
            current.windStrength = _atmosphereTo?.windStrength ?? 0f;
            return current;
        }

        private void Update()
        {
            if (_atmosphereTimer >= 0f)
            {
                _atmosphereTimer += Time.deltaTime;
                float t = Mathf.Clamp01(_atmosphereTimer / Mathf.Max(0.1f, atmosphereBlendSeconds));
                float eased = t * t * (3f - 2f * t);
                ApplyAtmosphere(Blend(_atmosphereFrom, _atmosphereTo, eased));

                if (t >= 1f)
                {
                    _atmosphereTimer = -1f;
                }
            }

            TickStormFlash();
        }

        private static PhaseAtmosphere Blend(PhaseAtmosphere a, PhaseAtmosphere b, float t)
        {
            return new PhaseAtmosphere
            {
                fogColor = Color.Lerp(a.fogColor, b.fogColor, t),
                fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t),
                ambientColor = Color.Lerp(a.ambientColor, b.ambientColor, t),
                ambientIntensity = Mathf.Lerp(a.ambientIntensity, b.ambientIntensity, t),
                sunColor = Color.Lerp(a.sunColor, b.sunColor, t),
                sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t),
                sunEuler = Vector3.Lerp(a.sunEuler, b.sunEuler, t),
                rainRate = Mathf.Lerp(a.rainRate, b.rainRate, t),
                windStrength = Mathf.Lerp(a.windStrength, b.windStrength, t),
                stormFlashesPerMinute = Mathf.Lerp(a.stormFlashesPerMinute, b.stormFlashesPerMinute, t)
            };
        }

        private void ApplyAtmosphere(PhaseAtmosphere a)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = a.fogColor;
            RenderSettings.fogDensity = a.fogDensity;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = a.ambientColor;
            RenderSettings.ambientIntensity = a.ambientIntensity;

            if (sunLight != null)
            {
                sunLight.color = a.sunColor;
                sunLight.intensity = a.sunIntensity;
                sunLight.transform.rotation = Quaternion.Euler(a.sunEuler);
            }

            if (rainSystem != null)
            {
                ParticleSystem.EmissionModule emission = rainSystem.emission;
                emission.rateOverTime = a.rainRate;

                // All three axes must use the same MinMaxCurve mode or Unity rejects the
                // assignment outright ("Particle Velocity curves must all be in the same
                // mode"), so Y is written as a two-constant range even though the rain's
                // fall speed comes from gravity.
                ParticleSystem.VelocityOverLifetimeModule velocity = rainSystem.velocityOverLifetime;
                velocity.enabled = true;
                velocity.x = new ParticleSystem.MinMaxCurve(-a.windStrength * 4.5f, -a.windStrength * 1.5f);
                velocity.y = new ParticleSystem.MinMaxCurve(-a.windStrength * 0.5f, 0f);
                velocity.z = new ParticleSystem.MinMaxCurve(a.windStrength * 0.8f, a.windStrength * 2.6f);
            }

            if (emberSystem != null)
            {
                ParticleSystem.EmissionModule emission = emberSystem.emission;
                emission.rateOverTime = a.windStrength * 14f;
            }
        }

        private void TickStormFlash()
        {
            if (stormFlashLight == null)
            {
                return;
            }

            float perMinute = _atmosphereTo?.stormFlashesPerMinute ?? 0f;
            if (perMinute <= 0.01f)
            {
                stormFlashLight.enabled = false;
                return;
            }

            float now = Time.time;

            if (now < _stormFlashUntil)
            {
                // Double-strike flicker rather than a single flat pulse.
                float remaining = _stormFlashUntil - now;
                float pulse = Mathf.Abs(Mathf.Sin(remaining * 48f));
                stormFlashLight.enabled = true;
                stormFlashLight.intensity = stormFlashIntensity * pulse * Mathf.Clamp01(remaining / 0.18f);
                return;
            }

            stormFlashLight.enabled = false;

            if (now < _nextStormFlash)
            {
                return;
            }

            float meanInterval = 60f / perMinute;
            _nextStormFlash = now + UnityEngine.Random.Range(meanInterval * 0.4f, meanInterval * 1.7f);
            _stormFlashUntil = now + UnityEngine.Random.Range(0.10f, 0.24f);
            stormFlashLight.color = Color.Lerp(AshfallPalette.StormTeal, Color.white, 0.35f);
        }

        /// <summary>Resets the station to phase one. Used by restart.</summary>
        public void ResetToStart()
        {
            for (int i = 0; i < doors.Count; i++)
            {
                doors[i]?.ResetToClosed();
            }

            _initialised = false;
            ApplyPhase(MapPhase.Standby, instant: true);
        }
    }
}
