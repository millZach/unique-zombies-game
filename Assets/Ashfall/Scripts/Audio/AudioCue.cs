using Ashfall.Core;

namespace Ashfall.Audio
{
    /// <summary>
    /// Every sound the game can make, named by what happened rather than by
    /// which file plays.
    ///
    /// Callers ask for <see cref="AudioCue.PlayerHurt"/>, not for a clip: the
    /// mapping from event to file, its volume, its pitch spread and its
    /// anti-spam interval all live in one table on
    /// <see cref="AudioDirector"/>. That is what keeps a mix tunable without
    /// touching gameplay code, and it is why a missing clip is a silent cue
    /// rather than a null reference in the middle of a firefight.
    /// </summary>
    public enum AudioCue
    {
        None = 0,

        WeaponFireSidearm,
        WeaponFireShotgun,
        WeaponFireRifle,
        WeaponReloadMagazine,
        WeaponReloadShell,
        WeaponDryFire,
        WeaponEquip,

        ImpactFlesh,
        ImpactCritical,
        ImpactWorld,

        EnemyAttackShambler,
        EnemyAttackSprinter,
        EnemyAttackBrute,
        EnemyDeathShambler,
        EnemyDeathSprinter,
        EnemyDeathBrute,

        PlayerHurt,
        PlayerDown,
        PlayerLastStand,

        PowerUpPickup,
        PowerUpDrop,

        PurchaseRoute,
        PurchaseWeapon,
        PurchaseDenied,
        BarricadeRepair,

        RoundStart,
        RoundComplete,

        StormThunder
    }

    /// <summary>Cue lookups that would otherwise be a switch in three places.</summary>
    public static class AudioCues
    {
        /// <summary>Highest cue value, used to size the director's lookup arrays.</summary>
        public const int Count = (int)AudioCue.StormThunder + 1;

        public static AudioCue AttackFor(EnemyArchetype archetype)
        {
            switch (archetype)
            {
                case EnemyArchetype.Sprinter: return AudioCue.EnemyAttackSprinter;
                case EnemyArchetype.StormBrute: return AudioCue.EnemyAttackBrute;
                default: return AudioCue.EnemyAttackShambler;
            }
        }

        public static AudioCue DeathFor(EnemyArchetype archetype)
        {
            switch (archetype)
            {
                case EnemyArchetype.Sprinter: return AudioCue.EnemyDeathSprinter;
                case EnemyArchetype.StormBrute: return AudioCue.EnemyDeathBrute;
                default: return AudioCue.EnemyDeathShambler;
            }
        }
    }
}
