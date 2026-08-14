using System;
using UnityEngine;
using Ashfall.Core;

namespace Ashfall.World
{
    public enum PowerUpKind
    {
        /// <summary>Weapon damage multiplier for a short window.</summary>
        Overcharge = 0,

        /// <summary>All salvage income multiplied.</summary>
        SalvageSurge = 1,

        /// <summary>The next lethal hit is survived instead of ending the run.</summary>
        LastStand = 2
    }

    public static class PowerUps
    {
        public static string DisplayName(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.Overcharge: return "OVERCHARGE";
                case PowerUpKind.SalvageSurge: return "SALVAGE SURGE";
                case PowerUpKind.LastStand: return "LAST STAND";
                default: return kind.ToString().ToUpperInvariant();
            }
        }

        public static string Blurb(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.Overcharge: return "Weapon output doubled";
                case PowerUpKind.SalvageSurge: return "Salvage income doubled";
                case PowerUpKind.LastStand: return "Next lethal hit survived";
                default: return string.Empty;
            }
        }

        public static Color Tint(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.Overcharge: return AshfallPalette.OverchargeViolet;
                case PowerUpKind.SalvageSurge: return AshfallPalette.SalvageGreen;
                case PowerUpKind.LastStand: return AshfallPalette.LastStandGold;
                default: return AshfallPalette.StormTeal;
            }
        }
    }

    /// <summary>
    /// A dropped power-up canister. Floats, spins, pulses its light, and expires with a
    /// visible warning flash so the player can judge whether a grab is worth the risk.
    /// </summary>
    public class PowerUpPickup : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private PowerUpKind kind = PowerUpKind.Overcharge;

        [Header("Lifetime")]
        [SerializeField] private float lifetimeSeconds = 22f;
        [SerializeField] private float warningSeconds = 5f;

        [Header("Motion")]
        [SerializeField] private float spinDegreesPerSecond = 95f;
        [SerializeField] private float bobAmplitude = 0.22f;
        [SerializeField] private float bobSpeed = 2.1f;
        [SerializeField] private float hoverHeight = 1.05f;

        [Header("References")]
        [SerializeField] private Transform visual;
        [SerializeField] private Renderer[] tintedRenderers;
        [SerializeField] private Light glowLight;
        [SerializeField] private float collectRadius = 1.7f;

        public event Action<PowerUpPickup, PowerUpKind> Collected;
        public event Action<PowerUpPickup> Expired;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock _block;
        private float _age;
        private Vector3 _origin;
        private Transform _player;
        private bool _consumed;

        public PowerUpKind Kind => kind;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            visual ??= transform;
            if (tintedRenderers == null || tintedRenderers.Length == 0)
            {
                tintedRenderers = GetComponentsInChildren<Renderer>();
            }
        }

        public void Configure(Transform visualRoot, Renderer[] renderers, Light light)
        {
            visual = visualRoot;
            tintedRenderers = renderers;
            glowLight = light;
        }

        /// <summary>Arms the pickup at a world position. Called by the power-up manager.</summary>
        public void Arm(PowerUpKind powerUpKind, Vector3 position, Transform player)
        {
            kind = powerUpKind;
            _origin = position + Vector3.up * hoverHeight;
            _age = 0f;
            _consumed = false;
            _player = player;
            transform.position = _origin;
            gameObject.SetActive(true);
            ApplyTint(1f);
        }

        private void Update()
        {
            if (_consumed)
            {
                return;
            }

            float dt = Time.deltaTime;
            _age += dt;

            if (visual != null)
            {
                visual.Rotate(Vector3.up, spinDegreesPerSecond * dt, Space.World);
                visual.Rotate(Vector3.right, spinDegreesPerSecond * 0.35f * dt, Space.Self);
            }

            transform.position = _origin + Vector3.up * (Mathf.Sin(_age * bobSpeed) * bobAmplitude);

            float remaining = lifetimeSeconds - _age;
            float intensity = 1f;
            if (remaining <= warningSeconds)
            {
                // Blink faster as it runs out: a readable "decide now" signal.
                float rate = Mathf.Lerp(14f, 3f, Mathf.Clamp01(remaining / warningSeconds));
                intensity = Mathf.Abs(Mathf.Sin(_age * rate)) * 0.85f + 0.15f;
            }

            ApplyTint(intensity);

            if (_player != null && (_player.position - transform.position).sqrMagnitude <= collectRadius * collectRadius)
            {
                Collect();
                return;
            }

            if (remaining <= 0f)
            {
                _consumed = true;
                Expired?.Invoke(this);
                gameObject.SetActive(false);
            }
        }

        private void Collect()
        {
            _consumed = true;
            Collected?.Invoke(this, kind);
            gameObject.SetActive(false);
        }

        private void ApplyTint(float intensity)
        {
            Color tint = PowerUps.Tint(kind);

            if (tintedRenderers != null)
            {
                _block ??= new MaterialPropertyBlock();
                for (int i = 0; i < tintedRenderers.Length; i++)
                {
                    Renderer r = tintedRenderers[i];
                    if (r == null)
                    {
                        continue;
                    }

                    r.GetPropertyBlock(_block);
                    _block.SetColor(BaseColorId, tint * 0.35f);
                    _block.SetColor(EmissionId, tint * (3.2f * intensity));
                    r.SetPropertyBlock(_block);
                }
            }

            if (glowLight != null)
            {
                glowLight.color = tint;
                glowLight.intensity = 4.5f * intensity;
            }
        }
    }
}
