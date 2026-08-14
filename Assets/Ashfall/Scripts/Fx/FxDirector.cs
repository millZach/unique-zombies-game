using System.Collections.Generic;
using UnityEngine;
using Ashfall.Core;

namespace Ashfall.Fx
{
    /// <summary>
    /// Every transient visual in the game routes through here so the slice has one
    /// pooling policy, one budget, and one place to keep the look consistent.
    ///
    /// The prefabs are built procedurally by the editor scene builder and assigned to
    /// the serialized fields below. If a reference is ever missing the director
    /// degrades quietly rather than throwing every time a bullet lands.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class FxDirector : MonoBehaviour
    {
        [Header("Prefabs (assigned by the scene builder)")]
        [SerializeField] private ParticleSystem impactSparkPrefab;
        [SerializeField] private ParticleSystem impactDustPrefab;
        [SerializeField] private ParticleSystem bloodMistPrefab;
        [SerializeField] private ParticleSystem muzzleFlashPrefab;
        [SerializeField] private LineRenderer tracerPrefab;
        [SerializeField] private Light impactLightPrefab;

        [Header("Budget")]
        [SerializeField] private int poolWarmCount = 8;
        [SerializeField] private int maxLiveTracers = 48;
        [SerializeField] private float tracerLifetime = 0.055f;
        [SerializeField] private float impactLightLifetime = 0.09f;

        public static FxDirector Instance { get; private set; }

        private readonly Dictionary<ParticleSystem, Queue<ParticleSystem>> _particlePools = new();
        private readonly List<PendingReturn<ParticleSystem>> _liveParticles = new(64);
        private readonly Queue<LineRenderer> _tracerPool = new();
        private readonly List<PendingReturn<LineRenderer>> _liveTracers = new(64);
        private readonly Queue<Light> _lightPool = new();
        private readonly List<PendingReturn<Light>> _liveLights = new(32);

        private Transform _root;

        private struct PendingReturn<T>
        {
            public T Item;
            public float ReturnAt;
            public ParticleSystem Key;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            _root = new GameObject("Fx Pool").transform;
            _root.SetParent(transform, false);

            Warm(impactSparkPrefab);
            Warm(impactDustPrefab);
            Warm(bloodMistPrefab);
            WarmTracers();
            WarmLights();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Configure(
            ParticleSystem sparks,
            ParticleSystem dust,
            ParticleSystem blood,
            ParticleSystem muzzle,
            LineRenderer tracer,
            Light impactLight)
        {
            impactSparkPrefab = sparks;
            impactDustPrefab = dust;
            bloodMistPrefab = blood;
            muzzleFlashPrefab = muzzle;
            tracerPrefab = tracer;
            impactLightPrefab = impactLight;
        }

        public ParticleSystem MuzzleFlashPrefab => muzzleFlashPrefab;

        private void Update()
        {
            float now = Time.time;

            for (int i = _liveParticles.Count - 1; i >= 0; i--)
            {
                if (now < _liveParticles[i].ReturnAt)
                {
                    continue;
                }

                var entry = _liveParticles[i];
                _liveParticles.RemoveAt(i);
                if (entry.Item != null)
                {
                    entry.Item.gameObject.SetActive(false);
                    entry.Item.transform.SetParent(_root, false);
                    GetPool(entry.Key).Enqueue(entry.Item);
                }
            }

            for (int i = _liveTracers.Count - 1; i >= 0; i--)
            {
                var entry = _liveTracers[i];
                if (entry.Item == null)
                {
                    _liveTracers.RemoveAt(i);
                    continue;
                }

                float remaining = entry.ReturnAt - now;
                if (remaining <= 0f)
                {
                    _liveTracers.RemoveAt(i);
                    entry.Item.gameObject.SetActive(false);
                    _tracerPool.Enqueue(entry.Item);
                    continue;
                }

                // Fade the streak out over its short life rather than popping it off.
                float t = Mathf.Clamp01(remaining / Mathf.Max(0.001f, tracerLifetime));
                float width = Mathf.Lerp(0.005f, 0.032f, t * t);
                entry.Item.startWidth = width;
                entry.Item.endWidth = width * 0.35f;
            }

            for (int i = _liveLights.Count - 1; i >= 0; i--)
            {
                var entry = _liveLights[i];
                if (entry.Item == null)
                {
                    _liveLights.RemoveAt(i);
                    continue;
                }

                float remaining = entry.ReturnAt - now;
                if (remaining <= 0f)
                {
                    _liveLights.RemoveAt(i);
                    entry.Item.gameObject.SetActive(false);
                    _lightPool.Enqueue(entry.Item);
                    continue;
                }

                float t = Mathf.Clamp01(remaining / Mathf.Max(0.001f, impactLightLifetime));
                entry.Item.intensity = Mathf.Lerp(0f, entry.Item.range * 1.6f, t * t);
            }
        }

        // ------------------------------------------------------------------
        // Public FX API
        // ------------------------------------------------------------------

        /// <summary>Bullet striking hard geometry: sparks, dust puff and a brief light.</summary>
        public void SpawnWorldImpact(Vector3 point, Vector3 normal, Color tint)
        {
            Quaternion rotation = normal.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(normal)
                : Quaternion.identity;

            PlayParticles(impactSparkPrefab, point + normal * 0.02f, rotation, tint);
            PlayParticles(impactDustPrefab, point + normal * 0.02f, rotation, AshfallPalette.ConcreteLight);
            FlashLight(point + normal * 0.25f, tint, 2.2f, impactLightLifetime);
        }

        /// <summary>Bullet striking a corrupted body: storm-lit mist, no sparks.</summary>
        public void SpawnFleshImpact(Vector3 point, Vector3 normal, bool critical)
        {
            Quaternion rotation = normal.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(normal)
                : Quaternion.identity;

            Color tint = critical
                ? Color.Lerp(AshfallPalette.Blood, AshfallPalette.StormTeal, 0.45f)
                : AshfallPalette.Blood;

            PlayParticles(bloodMistPrefab, point + normal * 0.05f, rotation, tint);
            if (critical)
            {
                FlashLight(point + normal * 0.2f, AshfallPalette.StormTeal, 1.6f, impactLightLifetime * 1.4f);
            }
        }

        /// <summary>A short bright streak from muzzle to impact.</summary>
        public void SpawnTracer(Vector3 from, Vector3 to, Color color)
        {
            if (tracerPrefab == null || _liveTracers.Count >= maxLiveTracers)
            {
                return;
            }

            LineRenderer tracer = _tracerPool.Count > 0 ? _tracerPool.Dequeue() : Instantiate(tracerPrefab, _root);
            if (tracer == null)
            {
                return;
            }

            tracer.gameObject.SetActive(true);
            tracer.positionCount = 2;
            tracer.SetPosition(0, from);
            tracer.SetPosition(1, to);
            tracer.startColor = color;
            tracer.endColor = new Color(color.r, color.g, color.b, 0f);

            _liveTracers.Add(new PendingReturn<LineRenderer>
            {
                Item = tracer,
                ReturnAt = Time.time + tracerLifetime
            });
        }

        public void FlashLight(Vector3 position, Color color, float range, float lifetime)
        {
            if (impactLightPrefab == null)
            {
                return;
            }

            Light light = _lightPool.Count > 0 ? _lightPool.Dequeue() : Instantiate(impactLightPrefab, _root);
            if (light == null)
            {
                return;
            }

            light.transform.position = position;
            light.color = color;
            light.range = range;
            light.intensity = range * 1.6f;
            light.gameObject.SetActive(true);

            _liveLights.Add(new PendingReturn<Light>
            {
                Item = light,
                ReturnAt = Time.time + lifetime
            });
        }

        public void PlayParticles(ParticleSystem prefab, Vector3 position, Quaternion rotation, Color tint)
        {
            if (prefab == null)
            {
                return;
            }

            Queue<ParticleSystem> pool = GetPool(prefab);
            ParticleSystem instance = pool.Count > 0 ? pool.Dequeue() : Instantiate(prefab, _root);
            if (instance == null)
            {
                return;
            }

            instance.transform.SetPositionAndRotation(position, rotation);
            ParticleSystem.MainModule main = instance.main;
            main.startColor = tint;

            instance.gameObject.SetActive(true);
            instance.Clear(true);
            instance.Play(true);

            float life = main.duration + main.startLifetime.constantMax + 0.1f;
            _liveParticles.Add(new PendingReturn<ParticleSystem>
            {
                Item = instance,
                ReturnAt = Time.time + life,
                Key = prefab
            });
        }

        private Queue<ParticleSystem> GetPool(ParticleSystem prefab)
        {
            if (!_particlePools.TryGetValue(prefab, out Queue<ParticleSystem> pool))
            {
                pool = new Queue<ParticleSystem>(16);
                _particlePools[prefab] = pool;
            }

            return pool;
        }

        private void Warm(ParticleSystem prefab)
        {
            if (prefab == null)
            {
                return;
            }

            Queue<ParticleSystem> pool = GetPool(prefab);
            for (int i = 0; i < poolWarmCount; i++)
            {
                ParticleSystem instance = Instantiate(prefab, _root);
                instance.gameObject.SetActive(false);
                pool.Enqueue(instance);
            }
        }

        private void WarmTracers()
        {
            if (tracerPrefab == null)
            {
                return;
            }

            for (int i = 0; i < poolWarmCount * 2; i++)
            {
                LineRenderer instance = Instantiate(tracerPrefab, _root);
                instance.gameObject.SetActive(false);
                _tracerPool.Enqueue(instance);
            }
        }

        private void WarmLights()
        {
            if (impactLightPrefab == null)
            {
                return;
            }

            for (int i = 0; i < poolWarmCount; i++)
            {
                Light instance = Instantiate(impactLightPrefab, _root);
                instance.gameObject.SetActive(false);
                _lightPool.Enqueue(instance);
            }
        }
    }
}
