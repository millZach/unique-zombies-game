using UnityEngine;
using Ashfall.Core;

namespace Ashfall.Weapons
{
    public enum FireMode
    {
        Semi,
        Automatic,
        Burst
    }

    /// <summary>
    /// Data-only description of one weapon. Three of these are generated as assets by
    /// the editor scene builder, so tuning is a matter of editing an inspector rather
    /// than recompiling.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashfall/Weapon Definition", fileName = "Weapon")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Weapon";
        [TextArea] public string flavourLine = "";
        public int salvageCost = 900;

        [Header("Fire")]
        public FireMode fireMode = FireMode.Semi;
        [Tooltip("Rounds per minute.")]
        public float roundsPerMinute = 320f;
        [Tooltip("Projectiles per trigger pull. Above 1 makes it a shot spread.")]
        public int pelletsPerShot = 1;
        public int burstCount = 3;
        public float burstSpacing = 0.06f;
        public int ammoPerShot = 1;

        [Header("Damage")]
        public float damagePerPellet = 34f;
        public float criticalMultiplier = 2.4f;
        [Tooltip("Metres at which damage begins to drop off.")]
        public float falloffStart = 22f;
        [Tooltip("Metres at which damage reaches its floor.")]
        public float falloffEnd = 45f;
        [Range(0.05f, 1f)] public float falloffFloor = 0.45f;
        public float maxRange = 140f;
        public float impactForce = 6f;

        [Header("Accuracy")]
        [Tooltip("Cone half-angle in degrees while hip firing and stationary.")]
        public float baseSpreadDegrees = 0.9f;
        public float maxSpreadDegrees = 5.5f;
        [Tooltip("Spread added per shot, decaying back to base.")]
        public float spreadPerShot = 0.7f;
        public float spreadRecoveryPerSecond = 3.4f;
        [Range(0f, 1f)] public float aimSpreadScale = 0.25f;
        public float movementSpreadScale = 1.9f;

        [Header("Recoil")]
        public float recoilVertical = 1.15f;
        public float recoilHorizontal = 0.45f;
        public float recoilRecoveryPerSecond = 7.5f;
        public float kickBack = 0.045f;

        [Header("Ammo")]
        public int magazineSize = 12;
        public int reserveAmmo = 120;
        public int maxReserveAmmo = 240;
        public float reloadSeconds = 1.4f;
        [Tooltip("Shell-at-a-time reload: the magazine refills incrementally and can be cancelled by firing.")]
        public bool incrementalReload;
        public int shellsPerReloadStep = 1;

        [Header("Handling")]
        [Range(0.2f, 1f)] public float aimMoveSpeedScale = 0.55f;
        public float aimFovScale = 0.78f;
        public float aimTransitionSeconds = 0.14f;
        public float equipSeconds = 0.45f;

        [Header("Presentation")]
        public Color accentColor = new Color(0.239f, 0.878f, 0.855f);
        public Color tracerColor = new Color(1f, 0.78f, 0.42f);
        public float muzzleFlashScale = 1f;
        public float cameraShakeAmount = 0.35f;
        [Range(0f, 1f)] public float lowFrequencyRumble = 0.25f;
        [Range(0f, 1f)] public float highFrequencyRumble = 0.45f;
        public float rumbleSeconds = 0.09f;

        [Header("Audio")]
        public float fireVolume = 0.55f;
        public float firePitch = 1f;

        [Tooltip("Which shot the audio director plays. Data, not a switch statement in the loadout.")]
        public Audio.AudioCue fireCue = Audio.AudioCue.WeaponFireSidearm;

        /// <summary>
        /// Shell-at-a-time weapons get the single-shell insert; everything else
        /// gets the full magazine change. Derived rather than authored, so the
        /// two can never disagree about which reload the weapon actually does.
        /// </summary>
        public Audio.AudioCue ReloadCue => incrementalReload
            ? Audio.AudioCue.WeaponReloadShell
            : Audio.AudioCue.WeaponReloadMagazine;

        /// <summary>Seconds between shots implied by <see cref="roundsPerMinute"/>.</summary>
        public float ShotInterval => roundsPerMinute <= 0f ? 0.2f : 60f / roundsPerMinute;

        /// <summary>Full-health damage of one trigger pull with every pellet connecting.</summary>
        public float BurstDamage => damagePerPellet * Mathf.Max(1, pelletsPerShot);

        /// <summary>Damage multiplier at a given distance from the muzzle.</summary>
        public float FalloffAt(float distance)
        {
            if (distance <= falloffStart)
            {
                return 1f;
            }

            if (distance >= falloffEnd)
            {
                return falloffFloor;
            }

            float t = Mathf.InverseLerp(falloffStart, falloffEnd, distance);
            return Mathf.Lerp(1f, falloffFloor, t);
        }

        /// <summary>Final damage for one pellet, given distance and whether it was a crit.</summary>
        public float ResolveDamage(float distance, bool critical, float damageMultiplier = 1f)
        {
            float d = damagePerPellet * FalloffAt(distance) * Mathf.Max(0f, damageMultiplier);
            if (critical)
            {
                d *= criticalMultiplier;
            }

            return d;
        }

        // ------------------------------------------------------------------
        // Factory defaults. Kept in code (not just as assets) so tests can build a
        // definition without touching the AssetDatabase, and so the scene builder can
        // regenerate the three canonical weapons deterministically.
        // ------------------------------------------------------------------

        public static void ApplyMeridianSidearm(WeaponDefinition d)
        {
            d.displayName = "Meridian Sidearm";
            d.flavourLine = "Station-issue sidearm. Never jams, never impresses.";
            d.salvageCost = 0;
            d.fireMode = FireMode.Semi;
            d.roundsPerMinute = 340f;
            d.pelletsPerShot = 1;
            d.damagePerPellet = 38f;
            d.criticalMultiplier = 2.6f;
            d.falloffStart = 26f;
            d.falloffEnd = 52f;
            d.falloffFloor = 0.55f;
            d.maxRange = 140f;
            d.baseSpreadDegrees = 0.55f;
            d.maxSpreadDegrees = 4.0f;
            d.spreadPerShot = 0.62f;
            d.spreadRecoveryPerSecond = 4.2f;
            d.aimSpreadScale = 0.18f;
            d.recoilVertical = 1.05f;
            d.recoilHorizontal = 0.34f;
            d.recoilRecoveryPerSecond = 8.5f;
            d.kickBack = 0.035f;
            d.magazineSize = 14;
            d.reserveAmmo = 168;
            d.maxReserveAmmo = 280;
            d.reloadSeconds = 1.35f;
            d.incrementalReload = false;
            d.aimFovScale = 0.82f;
            d.aimMoveSpeedScale = 0.68f;
            d.equipSeconds = 0.35f;
            d.accentColor = AshfallPalette.EmergencyAmber;
            d.tracerColor = new Color(1f, 0.80f, 0.45f);
            d.muzzleFlashScale = 0.85f;
            d.cameraShakeAmount = 0.28f;
            d.lowFrequencyRumble = 0.18f;
            d.highFrequencyRumble = 0.40f;
            d.rumbleSeconds = 0.07f;
            d.fireVolume = 0.45f;
            d.firePitch = 1.15f;
            d.fireCue = Audio.AudioCue.WeaponFireSidearm;
        }

        public static void ApplyBreakwaterShotgun(WeaponDefinition d)
        {
            d.displayName = "Breakwater";
            d.flavourLine = "Deck gun for clearing ice. Clears other things too.";
            d.salvageCost = 1250;
            d.fireMode = FireMode.Semi;
            d.roundsPerMinute = 78f;
            d.pelletsPerShot = 9;
            d.damagePerPellet = 21f;
            d.criticalMultiplier = 1.7f;
            d.falloffStart = 6.5f;
            d.falloffEnd = 19f;
            d.falloffFloor = 0.18f;
            d.maxRange = 45f;
            d.baseSpreadDegrees = 5.2f;
            d.maxSpreadDegrees = 8.5f;
            d.spreadPerShot = 1.1f;
            d.spreadRecoveryPerSecond = 5.0f;
            d.aimSpreadScale = 0.62f;
            d.movementSpreadScale = 1.35f;
            d.recoilVertical = 3.4f;
            d.recoilHorizontal = 0.9f;
            d.recoilRecoveryPerSecond = 6.0f;
            d.kickBack = 0.11f;
            d.impactForce = 16f;
            d.magazineSize = 6;
            d.reserveAmmo = 48;
            d.maxReserveAmmo = 84;
            d.reloadSeconds = 0.42f;
            d.incrementalReload = true;
            d.shellsPerReloadStep = 1;
            d.aimFovScale = 0.92f;
            d.aimMoveSpeedScale = 0.62f;
            d.equipSeconds = 0.55f;
            d.accentColor = AshfallPalette.RustDeep;
            d.tracerColor = new Color(1f, 0.62f, 0.30f);
            d.muzzleFlashScale = 1.7f;
            d.cameraShakeAmount = 0.95f;
            d.lowFrequencyRumble = 0.75f;
            d.highFrequencyRumble = 0.55f;
            d.rumbleSeconds = 0.16f;
            d.fireVolume = 0.75f;
            d.firePitch = 0.82f;
            d.fireCue = Audio.AudioCue.WeaponFireShotgun;
        }

        public static void ApplyArc9Rifle(WeaponDefinition d)
        {
            d.displayName = "Arc-9";
            d.flavourLine = "Prototype rail carbine. The storm charges it for free.";
            d.salvageCost = 1900;
            d.fireMode = FireMode.Automatic;
            d.roundsPerMinute = 690f;
            d.pelletsPerShot = 1;
            d.damagePerPellet = 26f;
            d.criticalMultiplier = 2.1f;
            d.falloffStart = 34f;
            d.falloffEnd = 68f;
            d.falloffFloor = 0.60f;
            d.maxRange = 160f;
            d.baseSpreadDegrees = 1.15f;
            d.maxSpreadDegrees = 6.2f;
            d.spreadPerShot = 0.34f;
            d.spreadRecoveryPerSecond = 5.2f;
            d.aimSpreadScale = 0.22f;
            d.movementSpreadScale = 2.1f;
            d.recoilVertical = 0.62f;
            d.recoilHorizontal = 0.30f;
            d.recoilRecoveryPerSecond = 9.5f;
            d.kickBack = 0.028f;
            d.magazineSize = 32;
            d.reserveAmmo = 256;
            d.maxReserveAmmo = 420;
            d.reloadSeconds = 2.05f;
            d.incrementalReload = false;
            d.aimFovScale = 0.70f;
            d.aimMoveSpeedScale = 0.52f;
            d.equipSeconds = 0.5f;
            d.accentColor = AshfallPalette.StormTeal;
            d.tracerColor = new Color(0.55f, 0.98f, 1f);
            d.muzzleFlashScale = 1.05f;
            d.cameraShakeAmount = 0.24f;
            d.lowFrequencyRumble = 0.22f;
            d.highFrequencyRumble = 0.50f;
            d.rumbleSeconds = 0.05f;
            d.fireVolume = 0.5f;
            d.firePitch = 1.32f;
            d.fireCue = Audio.AudioCue.WeaponFireRifle;
        }
    }
}
