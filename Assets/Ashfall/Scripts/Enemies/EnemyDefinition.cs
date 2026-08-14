using UnityEngine;
using Ashfall.Core;

namespace Ashfall.Enemies
{
    /// <summary>
    /// Data-only description of one enemy archetype. Round scaling multiplies these
    /// numbers rather than replacing them, so the identity of a shambler holds all the
    /// way from round 1 to round 12.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashfall/Enemy Definition", fileName = "Enemy")]
    public class EnemyDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Shambler";
        public EnemyArchetype archetype = EnemyArchetype.Shambler;

        [Header("Vitals")]
        public float baseHealth = 150f;
        public float criticalMultiplier = 2.5f;

        [Header("Movement")]
        public float baseMoveSpeed = 2.15f;
        public float accelerationVariance = 0.12f;
        public float bodyRadius = 0.42f;
        public float bodyHeight = 1.85f;

        [Header("Attack")]
        public float attackDamage = 22f;
        public float attackRange = 1.75f;
        public float attackInterval = 1.25f;
        public float attackWindupSeconds = 0.35f;
        public float attackLungeSpeed = 0f;
        public float knockbackForce = 0f;

        [Header("Barricades")]
        public float barricadeDamagePerHit = 45f;
        public float barricadeAttackInterval = 0.85f;

        [Header("Reaction")]
        [Tooltip("Damage in one hit needed to interrupt the enemy.")]
        public float staggerThreshold = 45f;
        public float staggerSeconds = 0.28f;
        [Range(0f, 1f)] public float staggerResistance = 0f;

        [Header("Reward")]
        public int salvageValue = 55;
        [Range(0f, 1f)] public float powerUpDropChance = 0.035f;

        [Header("Presentation")]
        public Color bodyColor = AshfallPalette.EnemyFlesh;
        public Color corruptionColor = AshfallPalette.EnemyCorrupt;
        public float corruptionEmission = 1.6f;
        public float visualScale = 1f;
        [Tooltip("Vertical bob added while walking, in metres.")]
        public float gaitBobAmplitude = 0.06f;
        public float gaitBobFrequency = 5.5f;
        public float leanDegrees = 8f;

        // ------------------------------------------------------------------
        // Canonical archetypes. Held in code so the scene builder regenerates them
        // deterministically and tests can construct them without the AssetDatabase.
        // ------------------------------------------------------------------

        public static void ApplyShambler(EnemyDefinition d)
        {
            d.displayName = "Shambler";
            d.archetype = EnemyArchetype.Shambler;
            d.baseHealth = 155f;
            d.criticalMultiplier = 2.6f;
            d.baseMoveSpeed = 2.05f;
            d.bodyRadius = 0.42f;
            d.bodyHeight = 1.85f;
            d.attackDamage = 24f;
            d.attackRange = 1.8f;
            d.attackInterval = 1.30f;
            d.attackWindupSeconds = 0.38f;
            d.barricadeDamagePerHit = 42f;
            d.barricadeAttackInterval = 0.9f;
            d.staggerThreshold = 40f;
            d.staggerSeconds = 0.30f;
            d.staggerResistance = 0f;
            d.salvageValue = 55;
            d.powerUpDropChance = 0.035f;
            d.bodyColor = AshfallPalette.EnemyFlesh;
            d.corruptionColor = AshfallPalette.EnemyCorrupt;
            d.corruptionEmission = 1.4f;
            d.visualScale = 1f;
            d.gaitBobAmplitude = 0.075f;
            d.gaitBobFrequency = 4.4f;
            d.leanDegrees = 11f;
        }

        public static void ApplySprinter(EnemyDefinition d)
        {
            d.displayName = "Sprinter";
            d.archetype = EnemyArchetype.Sprinter;
            d.baseHealth = 95f;
            d.criticalMultiplier = 3.0f;
            d.baseMoveSpeed = 4.85f;
            d.bodyRadius = 0.34f;
            d.bodyHeight = 1.68f;
            d.attackDamage = 17f;
            d.attackRange = 1.6f;
            d.attackInterval = 0.72f;
            d.attackWindupSeconds = 0.20f;
            d.attackLungeSpeed = 3.5f;
            d.barricadeDamagePerHit = 30f;
            d.barricadeAttackInterval = 0.55f;
            d.staggerThreshold = 26f;
            d.staggerSeconds = 0.22f;
            d.staggerResistance = 0f;
            d.salvageValue = 70;
            d.powerUpDropChance = 0.05f;
            d.bodyColor = new Color(0.255f, 0.267f, 0.278f);
            d.corruptionColor = AshfallPalette.StormTeal;
            d.corruptionEmission = 2.6f;
            d.visualScale = 0.92f;
            d.gaitBobAmplitude = 0.11f;
            d.gaitBobFrequency = 9.5f;
            d.leanDegrees = 19f;
        }

        public static void ApplyStormBrute(EnemyDefinition d)
        {
            d.displayName = "Storm Brute";
            d.archetype = EnemyArchetype.StormBrute;
            d.baseHealth = 1250f;
            d.criticalMultiplier = 1.8f;
            d.baseMoveSpeed = 2.55f;
            d.bodyRadius = 0.78f;
            d.bodyHeight = 2.85f;
            d.attackDamage = 58f;
            d.attackRange = 2.9f;
            d.attackInterval = 1.85f;
            d.attackWindupSeconds = 0.62f;
            d.knockbackForce = 9.5f;
            d.barricadeDamagePerHit = 400f;
            d.barricadeAttackInterval = 0.7f;
            d.staggerThreshold = 260f;
            d.staggerSeconds = 0.34f;
            d.staggerResistance = 0.65f;
            d.salvageValue = 520;
            d.powerUpDropChance = 1f;
            d.bodyColor = AshfallPalette.BruteArmour;
            d.corruptionColor = AshfallPalette.StormTeal;
            d.corruptionEmission = 3.4f;
            d.visualScale = 1.55f;
            d.gaitBobAmplitude = 0.14f;
            d.gaitBobFrequency = 3.1f;
            d.leanDegrees = 6f;
        }
    }
}
