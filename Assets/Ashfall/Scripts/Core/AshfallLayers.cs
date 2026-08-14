using UnityEngine;

namespace Ashfall.Core
{
    /// <summary>
    /// Central place for the project's layer and tag names so runtime code and the
    /// editor scene builder can never drift apart on a typo.
    /// </summary>
    public static class AshfallLayers
    {
        public const string PlayerName = "AshfallPlayer";
        public const string EnemyName = "AshfallEnemy";
        public const string WorldName = "AshfallWorld";
        public const string InteractableName = "AshfallInteractable";
        public const string EnemyHitboxName = "AshfallEnemyHitbox";
        public const string FxName = "AshfallFx";
        public const string NavBlockerName = "AshfallNavBlocker";
        public const string DoorName = "AshfallDoor";

        public static int Player => LayerMask.NameToLayer(PlayerName);
        public static int Enemy => LayerMask.NameToLayer(EnemyName);
        public static int World => LayerMask.NameToLayer(WorldName);
        public static int Interactable => LayerMask.NameToLayer(InteractableName);
        public static int EnemyHitbox => LayerMask.NameToLayer(EnemyHitboxName);
        public static int Fx => LayerMask.NameToLayer(FxName);
        public static int NavBlocker => LayerMask.NameToLayer(NavBlockerName);
        public static int Door => LayerMask.NameToLayer(DoorName);

        /// <summary>
        /// Everything a bullet should be able to hit. Deliberately excludes the enemy
        /// body layer: enemies are shot through their trigger hitboxes, so a head shot
        /// is never swallowed by the CharacterController capsule that encloses them.
        /// </summary>
        public static LayerMask WeaponHitMask =>
            Mask(WorldName, EnemyHitboxName, InteractableName, DoorName, NavBlockerName) | 1;

        /// <summary>Solid geometry used for navigation sampling and enemy avoidance.</summary>
        public static LayerMask BlockingMask =>
            Mask(WorldName, NavBlockerName, DoorName) | 1;

        /// <summary>Surfaces the player can stand on.</summary>
        public static LayerMask GroundMask =>
            Mask(WorldName, DoorName, NavBlockerName) | 1;

        /// <summary>What the interact spherecast considers. Boards live on the blocker layer.</summary>
        public static LayerMask InteractMask =>
            Mask(InteractableName, DoorName, NavBlockerName, WorldName);

        public static LayerMask Mask(params string[] names)
        {
            int mask = 0;
            for (int i = 0; i < names.Length; i++)
            {
                int layer = LayerMask.NameToLayer(names[i]);
                if (layer >= 0)
                {
                    mask |= 1 << layer;
                }
            }

            return mask;
        }
    }

    public static class AshfallTags
    {
        public const string Player = "AshfallPlayer";
        public const string Enemy = "AshfallEnemy";
        public const string Interactable = "AshfallInteractable";
        public const string PowerUp = "AshfallPowerUp";
        public const string Spawn = "AshfallSpawn";
    }
}
