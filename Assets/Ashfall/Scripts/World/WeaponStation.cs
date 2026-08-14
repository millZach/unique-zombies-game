using System;
using UnityEngine;
using Ashfall.Core;
using Ashfall.Player;
using Ashfall.Weapons;

namespace Ashfall.World
{
    /// <summary>
    /// A wall-mounted salvage rack that sells one weapon. Once the player owns the
    /// weapon the same station sells an ammo refill at a lower price, so the rack stays
    /// useful for the whole run rather than becoming scenery after round 3.
    /// </summary>
    public class WeaponStation : Interactable
    {
        [Header("Stock")]
        [SerializeField] private WeaponDefinition weapon;
        [SerializeField] private int purchaseCost = 1250;
        [SerializeField] private int refillCost = 450;

        [Header("Availability")]
        [Tooltip("Station stays dark until the station reaches this phase.")]
        [SerializeField] private MapPhase requiredPhase = MapPhase.Standby;

        [Header("Presentation")]
        [SerializeField] private Renderer[] signRenderers;
        [SerializeField] private Light stationLight;
        [SerializeField] private Transform displayModel;
        [SerializeField] private float displaySpinDegreesPerSecond = 26f;
        [SerializeField] private float displayBobAmplitude = 0.06f;
        [SerializeField] private float displayBobSpeed = 1.6f;

        public event Action<WeaponStation, bool> Purchased;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock _block;
        private PlayerLoadout _loadout;
        private Vector3 _displayRest;
        private bool _unlocked = true;

        public WeaponDefinition Weapon => weapon;
        public MapPhase RequiredPhase => requiredPhase;

        public override bool IsAvailable => isActiveAndEnabled && _unlocked && weapon != null;

        public override int Cost => PlayerOwnsWeapon() ? refillCost : purchaseCost;

        protected override void Awake()
        {
            base.Awake();
            _block = new MaterialPropertyBlock();
            if (displayModel != null)
            {
                _displayRest = displayModel.localPosition;
            }
        }

        private void Start()
        {
            _loadout = FindFirstObjectByType<PlayerLoadout>();
            ApplyVisualState();
        }

        public void Configure(WeaponDefinition definition, int cost, int refill, MapPhase phase)
        {
            weapon = definition;
            purchaseCost = cost;
            refillCost = refill;
            requiredPhase = phase;
            title = definition != null ? $"Buy {definition.displayName}" : "Salvage Rack";
        }

        public void SetPhase(MapPhase phase)
        {
            _unlocked = phase >= requiredPhase;
            ApplyVisualState();
        }

        private bool PlayerOwnsWeapon()
        {
            if (weapon == null)
            {
                return false;
            }

            _loadout ??= FindFirstObjectByType<PlayerLoadout>();
            if (_loadout == null)
            {
                return false;
            }

            int index = _loadout.IndexOfDefinition(weapon);
            return index >= 0 && _loadout.IsUnlocked(index);
        }

        public override string BuildPrompt(SalvageWallet wallet)
        {
            if (weapon == null)
            {
                return string.Empty;
            }

            if (!_unlocked)
            {
                return $"{weapon.displayName} rack - offline until {MapPhases.DisplayName(requiredPhase)}";
            }

            bool owned = PlayerOwnsWeapon();
            int cost = owned ? refillCost : purchaseCost;
            string verb = owned ? "Refill" : "Buy";
            bool affordable = wallet == null || wallet.CanAfford(cost);

            return affordable
                ? $"{verb} {weapon.displayName}   {cost} salvage"
                : $"{verb} {weapon.displayName}   {cost} salvage  (need {cost - (wallet != null ? wallet.Balance : 0)} more)";
        }

        public override bool Interact(SalvageWallet wallet, GameObject instigator)
        {
            if (weapon == null || !_unlocked)
            {
                return false;
            }

            _loadout ??= instigator != null ? instigator.GetComponentInParent<PlayerLoadout>() : null;
            _loadout ??= FindFirstObjectByType<PlayerLoadout>();
            if (_loadout == null)
            {
                return false;
            }

            bool owned = PlayerOwnsWeapon();
            int cost = owned ? refillCost : purchaseCost;

            if (wallet != null && !wallet.TrySpend(cost))
            {
                return false;
            }

            if (!_loadout.AcquireWeapon(weapon, out bool wasRefill))
            {
                return false;
            }

            Purchased?.Invoke(this, wasRefill);
            return true;
        }

        protected override void Update()
        {
            base.Update();

            if (displayModel == null || !_unlocked)
            {
                return;
            }

            displayModel.Rotate(Vector3.up, displaySpinDegreesPerSecond * Time.deltaTime, Space.Self);
            displayModel.localPosition = _displayRest +
                Vector3.up * (Mathf.Sin(Time.time * displayBobSpeed) * displayBobAmplitude);
        }

        private void ApplyVisualState()
        {
            Color color = _unlocked
                ? (weapon != null ? weapon.accentColor : AshfallPalette.StormTeal)
                : AshfallPalette.ConcreteLight;

            if (signRenderers != null)
            {
                _block ??= new MaterialPropertyBlock();
                for (int i = 0; i < signRenderers.Length; i++)
                {
                    Renderer r = signRenderers[i];
                    if (r == null)
                    {
                        continue;
                    }

                    r.GetPropertyBlock(_block);
                    _block.SetColor(BaseColorId, color * 0.35f);
                    _block.SetColor(EmissionId, color * (_unlocked ? 2.4f : 0.05f));
                    r.SetPropertyBlock(_block);
                }
            }

            if (stationLight != null)
            {
                stationLight.enabled = _unlocked;
                stationLight.color = color;
            }

            if (displayModel != null)
            {
                displayModel.gameObject.SetActive(_unlocked);
            }
        }
    }
}
