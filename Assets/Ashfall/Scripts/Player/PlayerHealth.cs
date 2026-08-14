using System;
using UnityEngine;
using Ashfall.Core;

namespace Ashfall.Player
{
    /// <summary>
    /// Player vitals: damage, delayed regeneration, and the Last Stand save.
    ///
    /// Regeneration is deliberate rather than generous -- it rewards breaking contact
    /// and repositioning, which is the behaviour the round loop is built around.
    /// </summary>
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        [Header("Vitals")]
        [SerializeField] private float maxHealth = 150f;
        [SerializeField] private float regenDelaySeconds = 4.5f;
        [SerializeField] private float regenPerSecond = 26f;
        [SerializeField] private float regenRampSeconds = 1.2f;

        [Header("Feedback")]
        [SerializeField] private float damageTrauma = 0.55f;
        [SerializeField] private float criticalHealthFraction = 0.3f;

        [Header("Last Stand")]
        [Tooltip("Health the player is left on when Last Stand absorbs a lethal hit.")]
        [SerializeField] private float lastStandSurviveHealth = 35f;
        [SerializeField] private float lastStandInvulnerabilitySeconds = 1.25f;

        public event Action<float, float> HealthChanged;
        public event Action<DamageInfo> Damaged;
        public event Action Died;
        public event Action LastStandSaved;

        private float _health;
        private float _regenTimer;
        private float _regenRamp;
        private float _invulnerableUntil;
        private PlayerCameraRig _cameraRig;

        public float Health => _health;
        public float MaxHealth => maxHealth;
        public float HealthFraction => maxHealth <= 0f ? 0f : Mathf.Clamp01(_health / maxHealth);
        public bool IsAlive => _health > 0f;
        public bool IsCritical => IsAlive && HealthFraction <= criticalHealthFraction;

        /// <summary>Set by the power-up manager while Last Stand is running.</summary>
        public bool LastStandActive { get; set; }

        /// <summary>Seconds since the last time the player took a hit.</summary>
        public float TimeSinceDamage { get; private set; } = 999f;

        private void Awake()
        {
            _health = maxHealth;
            _cameraRig = GetComponentInChildren<PlayerCameraRig>();
        }

        private void Start()
        {
            HealthChanged?.Invoke(_health, maxHealth);
        }

        public void ResetVitals()
        {
            _health = maxHealth;
            _regenTimer = 0f;
            _regenRamp = 0f;
            _invulnerableUntil = 0f;
            LastStandActive = false;
            TimeSinceDamage = 999f;
            HealthChanged?.Invoke(_health, maxHealth);
        }

        private void Update()
        {
            if (!IsAlive)
            {
                return;
            }

            float dt = Time.deltaTime;
            TimeSinceDamage += dt;
            _regenTimer += dt;

            if (_regenTimer < regenDelaySeconds || _health >= maxHealth)
            {
                _regenRamp = 0f;
                return;
            }

            // Ease regeneration in so the exact moment it starts is not a hard step.
            _regenRamp = Mathf.Clamp01(_regenRamp + dt / Mathf.Max(0.01f, regenRampSeconds));
            float before = _health;
            _health = Mathf.Min(maxHealth, _health + regenPerSecond * _regenRamp * dt);

            if (!Mathf.Approximately(before, _health))
            {
                HealthChanged?.Invoke(_health, maxHealth);
            }
        }

        public float ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || Time.time < _invulnerableUntil)
            {
                return 0f;
            }

            float amount = Mathf.Max(0f, info.Amount);
            if (amount <= 0f)
            {
                return 0f;
            }

            bool wouldBeLethal = amount >= _health;

            if (wouldBeLethal && LastStandActive)
            {
                // Last Stand: absorb the killing blow, leave the player on their feet
                // with a moment of immunity to escape the pack.
                float absorbed = _health;
                _health = lastStandSurviveHealth;
                _invulnerableUntil = Time.time + lastStandInvulnerabilitySeconds;
                _regenTimer = 0f;
                TimeSinceDamage = 0f;

                _cameraRig?.AddTrauma(1f);
                LastStandSaved?.Invoke();
                HealthChanged?.Invoke(_health, maxHealth);
                Damaged?.Invoke(info);
                return absorbed;
            }

            _health = Mathf.Max(0f, _health - amount);
            _regenTimer = 0f;
            _regenRamp = 0f;
            TimeSinceDamage = 0f;

            _cameraRig?.AddTrauma(Mathf.Clamp01(amount / 40f) * damageTrauma);

            Damaged?.Invoke(info);
            HealthChanged?.Invoke(_health, maxHealth);

            if (_health <= 0f)
            {
                Died?.Invoke();
            }

            return amount;
        }

        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f)
            {
                return;
            }

            _health = Mathf.Min(maxHealth, _health + amount);
            HealthChanged?.Invoke(_health, maxHealth);
        }

        public void GrantTemporaryInvulnerability(float seconds)
        {
            _invulnerableUntil = Mathf.Max(_invulnerableUntil, Time.time + seconds);
        }

        public bool IsInvulnerable => Time.time < _invulnerableUntil;
    }
}
