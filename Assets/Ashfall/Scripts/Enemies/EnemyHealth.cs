using System;
using UnityEngine;
using Ashfall.Core;

namespace Ashfall.Enemies
{
    /// <summary>
    /// Enemy vitals plus the death presentation.
    ///
    /// Death is a short dissolve-and-collapse rather than a ragdoll: it keeps the
    /// silhouette readable while it happens, costs nothing in physics, and returns the
    /// object to the pool on a fixed timer so a heavy round cannot leak corpses.
    /// </summary>
    public class EnemyHealth : MonoBehaviour, IDamageable
    {
        [Header("References")]
        [SerializeField] private Renderer[] bodyRenderers;
        [SerializeField] private Collider[] bodyColliders;
        [SerializeField] private Transform visualRoot;

        [Header("Death")]
        [SerializeField] private float deathCollapseSeconds = 0.85f;
        [SerializeField] private float deathSinkDistance = 1.1f;

        [Header("Hit flash")]
        [SerializeField] private float hitFlashSeconds = 0.09f;
        [SerializeField] private Color hitFlashColor = Color.white;

        /// <summary>(damage dealt, was critical)</summary>
        public event Action<float, bool> DamageTaken;

        /// <summary>(this, killerGameObject)</summary>
        public event Action<EnemyHealth, GameObject> Died;

        /// <summary>Raised once the death animation has finished and the body can be recycled.</summary>
        public event Action<EnemyHealth> DeathFinished;

        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private MaterialPropertyBlock _block;
        private float _maxHealth = 100f;
        private float _health;
        private float _flashUntil;
        private float _deathTimer = -1f;
        private Vector3 _visualRest;
        private Color _restEmission;
        private Color _restBaseColor;

        // Per-renderer authored colours, captured from each part's own material.
        //
        // Enemies are assembled from two materials on purpose -- dull flesh for the
        // body, emissive corruption for the veins and core -- and that contrast is the
        // whole silhouette. Overriding every renderer with one shared colour turned all
        // three archetypes into identical glowing teal blobs, so the per-part colours
        // are remembered here and only ever modulated, never replaced.
        private Color[] _partBaseColors;
        private Color[] _partEmissionColors;
        private bool _paletteCaptured;

        public float Health => _health;
        public float MaxHealth => _maxHealth;
        public float HealthFraction => _maxHealth <= 0f ? 0f : Mathf.Clamp01(_health / _maxHealth);
        public bool IsAlive => _health > 0f;
        public bool IsDying => _deathTimer >= 0f;

        /// <summary>Damage from the last hit, for stagger decisions in the brain.</summary>
        public float LastHitAmount { get; private set; }

        /// <summary>
        /// Whether death squashes and sinks <see cref="visualRoot"/>.
        ///
        /// A rigged body has a Death clip that collapses it properly; squashing
        /// the root as well would flatten the animation into a puddle. <see
        /// cref="ZombieAnimator"/> turns this off only when it has a Death state
        /// to play. The dissolve tint, the collider disable, the timer and
        /// <see cref="DeathFinished"/> are unaffected either way, so pooling and
        /// the round's alive count behave identically.
        /// </summary>
        public bool ProceduralDeathCollapse { get; set; } = true;

        /// <summary>
        /// Retimes the death presentation, in seconds.
        ///
        /// The pool recycles a corpse when this elapses. A rigged death clip is
        /// longer than the default squash, and recycling a body halfway through
        /// its collapse is the most visible bug an art pass can introduce.
        /// </summary>
        public void SetDeathCollapseSeconds(float seconds)
        {
            // NaN survives Mathf.Clamp, and a NaN timer never reaches 1, so the
            // body would sit in the pool as a permanent corpse.
            deathCollapseSeconds = float.IsNaN(seconds) ? 0.85f : Mathf.Clamp(seconds, 0.05f, 4f);
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (bodyRenderers == null || bodyRenderers.Length == 0)
            {
                bodyRenderers = GetComponentsInChildren<Renderer>();
            }

            if (bodyColliders == null || bodyColliders.Length == 0)
            {
                bodyColliders = GetComponentsInChildren<Collider>();
            }

            visualRoot ??= transform;
            _visualRest = visualRoot.localPosition;
            CapturePalette();
        }

        private void CapturePalette()
        {
            if (_paletteCaptured || bodyRenderers == null)
            {
                return;
            }

            _partBaseColors = new Color[bodyRenderers.Length];
            _partEmissionColors = new Color[bodyRenderers.Length];

            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                Renderer r = bodyRenderers[i];
                Material material = r != null ? r.sharedMaterial : null;

                _partBaseColors[i] = material != null && material.HasProperty(BaseColorId)
                    ? material.GetColor(BaseColorId)
                    : Color.grey;

                _partEmissionColors[i] = material != null && material.HasProperty(EmissionId)
                    ? material.GetColor(EmissionId)
                    : Color.black;
            }

            _paletteCaptured = true;
        }

        public void Configure(Renderer[] renderers, Collider[] colliders, Transform visual)
        {
            bodyRenderers = renderers;
            bodyColliders = colliders;
            visualRoot = visual;
        }

        /// <summary>Resets the enemy for reuse out of the pool.</summary>
        public void Initialise(float maxHealth, Color baseColor, Color emissionColor)
        {
            _maxHealth = Mathf.Max(1f, maxHealth);
            _health = _maxHealth;
            _deathTimer = -1f;
            _flashUntil = 0f;
            _restBaseColor = baseColor;
            _restEmission = emissionColor;

            if (visualRoot != null)
            {
                visualRoot.localPosition = _visualRest;
                visualRoot.localScale = Vector3.one;
            }

            CapturePalette();
            SetCollidersEnabled(true);
            ApplyTint(Color.white, 0f, 1f);
        }

        public float ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive)
            {
                return 0f;
            }

            float amount = Mathf.Max(0f, info.Amount);
            if (info.CriticalHit)
            {
                // The relay already scaled by the hitbox multiplier; the archetype's own
                // crit bonus is applied here so it stays a property of the enemy.
                amount *= 1f;
            }

            _health -= amount;
            LastHitAmount = amount;
            _flashUntil = Time.time + hitFlashSeconds;

            DamageTaken?.Invoke(amount, info.CriticalHit);

            if (_health <= 0f)
            {
                _health = 0f;
                BeginDeath(info.Instigator);
            }

            return amount;
        }

        private void BeginDeath(GameObject killer)
        {
            if (_deathTimer >= 0f)
            {
                return;
            }

            _deathTimer = 0f;
            SetCollidersEnabled(false);
            Died?.Invoke(this, killer);
        }

        /// <summary>Kills instantly without a death animation. Used by round cleanup.</summary>
        public void KillSilently()
        {
            _health = 0f;
            _deathTimer = -1f;
            SetCollidersEnabled(false);
        }

        private void SetCollidersEnabled(bool enabledState)
        {
            if (bodyColliders == null)
            {
                return;
            }

            for (int i = 0; i < bodyColliders.Length; i++)
            {
                if (bodyColliders[i] != null)
                {
                    bodyColliders[i].enabled = enabledState;
                }
            }
        }

        private void Update()
        {
            if (_deathTimer >= 0f)
            {
                TickDeath(Time.deltaTime);
                return;
            }

            bool flashing = Time.time < _flashUntil;
            ApplyTint(hitFlashColor, flashing ? 0.85f : 0f, 1f);
        }

        private void TickDeath(float deltaTime)
        {
            _deathTimer += deltaTime;
            float t = Mathf.Clamp01(_deathTimer / Mathf.Max(0.05f, deathCollapseSeconds));

            if (visualRoot != null && ProceduralDeathCollapse)
            {
                // Squash down and sink: reads clearly at distance and never leaves a
                // body standing where the player expects a clear lane.
                float squash = Mathf.Lerp(1f, 0.15f, t * t);
                float widen = Mathf.Lerp(1f, 1.25f, Mathf.Sin(t * Mathf.PI) * 0.6f);
                visualRoot.localScale = new Vector3(widen, squash, widen);
                visualRoot.localPosition = _visualRest + Vector3.down * (deathSinkDistance * t * t);
            }

            // Flare with storm energy on the way out, then fade to black. Each part keeps
            // its own colour through the fade, so the corruption is the last thing to go.
            float flare = Mathf.Sin(Mathf.Clamp01(t * 2.2f) * Mathf.PI);
            ApplyTint(_restEmission, flare * 0.55f, 1f - t);

            if (t >= 1f)
            {
                _deathTimer = -1f;
                DeathFinished?.Invoke(this);
            }
        }

        /// <summary>
        /// Modulates every part around its own authored colour.
        /// </summary>
        /// <param name="overlay">Colour blended toward, for hit flash and death flare.</param>
        /// <param name="overlayAmount">0 = the part's own colour, 1 = fully the overlay.</param>
        /// <param name="brightness">Scales the result; drives the fade to black on death.</param>
        private void ApplyTint(Color overlay, float overlayAmount, float brightness)
        {
            if (bodyRenderers == null || _block == null || _partBaseColors == null)
            {
                return;
            }

            for (int i = 0; i < bodyRenderers.Length && i < _partBaseColors.Length; i++)
            {
                Renderer r = bodyRenderers[i];
                if (r == null)
                {
                    continue;
                }

                Color baseColor = Color.Lerp(_partBaseColors[i], overlay, overlayAmount) * brightness;

                // Emissive parts flare; unlit parts stay unlit rather than being handed
                // a glow they were never meant to have.
                Color partEmission = _partEmissionColors[i];
                Color emission = partEmission.maxColorComponent > 0.001f
                    ? Color.Lerp(partEmission, overlay * 3.5f, overlayAmount) * brightness
                    : overlay * (overlayAmount * 2.5f * brightness);

                r.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, baseColor);
                _block.SetColor(EmissionId, emission);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
