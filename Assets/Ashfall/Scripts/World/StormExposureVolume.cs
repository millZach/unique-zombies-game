using System;
using UnityEngine;
using Ashfall.Core;
using Ashfall.Player;

namespace Ashfall.World
{
    /// <summary>
    /// A region of the station open to the weather. Harmless early, lethal late.
    ///
    /// This is what makes the rooftop route a real decision rather than a free
    /// shortcut: by the Meridian phase, standing in the open costs health every second,
    /// so the fastest lane is also the one you cannot linger in.
    /// </summary>
    public class StormExposureVolume : MonoBehaviour
    {
        [Header("Danger by phase")]
        [Tooltip("Damage per second, indexed by MapPhase.")]
        [SerializeField] private float[] damagePerSecondPerPhase = { 0f, 0f, 3.5f, 7f, 12f };

        [Header("Warning")]
        [SerializeField] private float warningLeadSeconds = 1.1f;

        [Header("Presentation")]
        [SerializeField] private ParticleSystem exposureFx;
        [SerializeField] private Light exposureLight;

        /// <summary>(inside, damagePerSecond) -- drives the HUD exposure warning.</summary>
        public event Action<bool, float> ExposureChanged;

        private MapPhase _phase = MapPhase.Standby;
        private PlayerHealth _occupant;
        private float _tickAccumulator;
        private float _dwellTime;
        private bool _reportedInside;

        public float CurrentDamagePerSecond =>
            damagePerSecondPerPhase != null && (int)_phase < damagePerSecondPerPhase.Length
                ? damagePerSecondPerPhase[(int)_phase]
                : 0f;

        public bool IsDangerous => CurrentDamagePerSecond > 0.01f;

        public void Configure(float[] perPhaseDamage, ParticleSystem fx, Light light)
        {
            damagePerSecondPerPhase = perPhaseDamage;
            exposureFx = fx;
            exposureLight = light;
        }

        public void SetPhase(MapPhase phase)
        {
            _phase = phase;

            if (exposureFx != null)
            {
                ParticleSystem.EmissionModule emission = exposureFx.emission;
                emission.rateOverTime = CurrentDamagePerSecond * 9f;

                if (IsDangerous && !exposureFx.isPlaying)
                {
                    exposureFx.Play(true);
                }
                else if (!IsDangerous && exposureFx.isPlaying)
                {
                    exposureFx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            if (exposureLight != null)
            {
                exposureLight.enabled = IsDangerous;
                exposureLight.intensity = CurrentDamagePerSecond * 0.28f;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            var health = other.GetComponentInParent<PlayerHealth>();
            if (health != null)
            {
                _occupant = health;
                _dwellTime = 0f;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            var health = other.GetComponentInParent<PlayerHealth>();
            if (health != null && health == _occupant)
            {
                _occupant = null;
                _dwellTime = 0f;
                _tickAccumulator = 0f;

                if (_reportedInside)
                {
                    _reportedInside = false;
                    ExposureChanged?.Invoke(false, 0f);
                }
            }
        }

        private void Update()
        {
            if (_occupant == null || !_occupant.IsAlive)
            {
                return;
            }

            float dps = CurrentDamagePerSecond;

            if (!_reportedInside)
            {
                _reportedInside = true;
                ExposureChanged?.Invoke(true, dps);
            }

            if (dps <= 0.01f)
            {
                return;
            }

            _dwellTime += Time.deltaTime;

            // A grace window so crossing the roof at a run is survivable and only
            // camping in the open is punished.
            if (_dwellTime < warningLeadSeconds)
            {
                return;
            }

            // Apply in discrete ticks so the damage numbers and audio read as strikes
            // rather than an invisible drain.
            _tickAccumulator += Time.deltaTime;
            const float tickInterval = 0.5f;
            while (_tickAccumulator >= tickInterval)
            {
                _tickAccumulator -= tickInterval;
                _occupant.ApplyDamage(new DamageInfo
                {
                    Amount = dps * tickInterval,
                    Point = _occupant.transform.position,
                    Direction = Vector3.down,
                    Normal = Vector3.up,
                    Kind = DamageKind.Storm,
                    Instigator = gameObject
                });
            }
        }
    }
}
