using UnityEngine;
using Ashfall.Core;

namespace Ashfall.World
{
    /// <summary>
    /// A light fixture whose colour and intensity are authored per map phase.
    ///
    /// This is the main instrument of the station's visual arc: the amber emergency
    /// lamps die back as the mains fail, and the teal storm lamps take over. Each
    /// fixture also drives the emissive colour of its own housing so the geometry and
    /// the lighting never disagree.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class PhaseLight : MonoBehaviour
    {
        [System.Serializable]
        public struct PhaseSetting
        {
            public Color color;
            public float intensity;
            public float range;
        }

        [SerializeField]
        private PhaseSetting[] perPhase = new PhaseSetting[MapPhases.Count];

        [Header("Housing")]
        [SerializeField] private Renderer[] housingRenderers;
        [SerializeField] private float emissionScale = 1.6f;

        [Header("Flicker")]
        [SerializeField] private bool flicker;
        [SerializeField] private float flickerAmount = 0.22f;
        [SerializeField] private float flickerSpeed = 11f;

        [Header("Transition")]
        [SerializeField] private float transitionSeconds = 1.6f;

        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private Light _light;
        private MaterialPropertyBlock _block;
        private PhaseSetting _from;
        private PhaseSetting _to;
        private float _transitionTimer = -1f;
        private float _flickerSeed;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _block = new MaterialPropertyBlock();
            _flickerSeed = Random.value * 100f;

            if (perPhase == null || perPhase.Length < MapPhases.Count)
            {
                var resized = new PhaseSetting[MapPhases.Count];
                if (perPhase != null)
                {
                    for (int i = 0; i < perPhase.Length && i < resized.Length; i++)
                    {
                        resized[i] = perPhase[i];
                    }
                }

                perPhase = resized;
            }
        }

        public void Configure(PhaseSetting[] settings, Renderer[] housing, bool doesFlicker)
        {
            perPhase = settings;
            housingRenderers = housing;
            flicker = doesFlicker;
        }

        /// <summary>Convenience setup used by the scene builder for a simple two-tone lamp.</summary>
        public void ConfigureSimple(Color early, float earlyIntensity, Color late, float lateIntensity, float range)
        {
            var settings = new PhaseSetting[MapPhases.Count];
            for (int i = 0; i < MapPhases.Count; i++)
            {
                float t = MapPhases.Count <= 1 ? 0f : i / (float)(MapPhases.Count - 1);
                settings[i] = new PhaseSetting
                {
                    color = Color.Lerp(early, late, t),
                    intensity = Mathf.Lerp(earlyIntensity, lateIntensity, t),
                    range = range
                };
            }

            perPhase = settings;
        }

        public void ApplyPhase(MapPhase phase, bool instant)
        {
            int index = Mathf.Clamp((int)phase, 0, perPhase.Length - 1);
            PhaseSetting target = perPhase[index];

            if (instant || _light == null)
            {
                _from = target;
                _to = target;
                _transitionTimer = -1f;
                ApplySetting(target, 1f);
                return;
            }

            _from = new PhaseSetting { color = _light.color, intensity = _light.intensity, range = _light.range };
            _to = target;
            _transitionTimer = 0f;
        }

        private void Update()
        {
            if (_light == null)
            {
                return;
            }

            if (_transitionTimer >= 0f)
            {
                _transitionTimer += Time.deltaTime;
                float t = Mathf.Clamp01(_transitionTimer / Mathf.Max(0.05f, transitionSeconds));
                var blended = new PhaseSetting
                {
                    color = Color.Lerp(_from.color, _to.color, t),
                    intensity = Mathf.Lerp(_from.intensity, _to.intensity, t),
                    range = Mathf.Lerp(_from.range, _to.range, t)
                };

                ApplySetting(blended, 1f);

                if (t >= 1f)
                {
                    _transitionTimer = -1f;
                }

                return;
            }

            if (flicker)
            {
                float noise = Mathf.PerlinNoise(_flickerSeed, Time.time * flickerSpeed);
                ApplySetting(_to, 1f - flickerAmount * noise);
            }
        }

        private void ApplySetting(PhaseSetting setting, float scale)
        {
            _light.color = setting.color;
            _light.intensity = setting.intensity * scale;
            _light.range = setting.range;
            _light.enabled = setting.intensity > 0.01f;

            if (housingRenderers == null || housingRenderers.Length == 0)
            {
                return;
            }

            _block ??= new MaterialPropertyBlock();
            Color emission = setting.color * (setting.intensity * scale * emissionScale * 0.1f);

            for (int i = 0; i < housingRenderers.Length; i++)
            {
                Renderer r = housingRenderers[i];
                if (r == null)
                {
                    continue;
                }

                r.GetPropertyBlock(_block);
                _block.SetColor(EmissionId, emission);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
