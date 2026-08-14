using UnityEngine;

namespace Ashfall.Core
{
    public enum DamageKind
    {
        Ballistic,
        Melee,
        Environment,
        Storm
    }

    /// <summary>
    /// A single damage event. Passed by value; carries enough context for hit
    /// reactions, damage numbers, knockback and score attribution.
    /// </summary>
    public struct DamageInfo
    {
        public float Amount;
        public Vector3 Point;
        public Vector3 Direction;
        public Vector3 Normal;
        public DamageKind Kind;
        public bool CriticalHit;
        public GameObject Instigator;

        public static DamageInfo Ballistic(float amount, Vector3 point, Vector3 direction, Vector3 normal, bool critical, GameObject instigator)
        {
            return new DamageInfo
            {
                Amount = amount,
                Point = point,
                Direction = direction,
                Normal = normal,
                Kind = DamageKind.Ballistic,
                CriticalHit = critical,
                Instigator = instigator
            };
        }

        public static DamageInfo Melee(float amount, Vector3 point, Vector3 direction, GameObject instigator)
        {
            return new DamageInfo
            {
                Amount = amount,
                Point = point,
                Direction = direction,
                Normal = -direction,
                Kind = DamageKind.Melee,
                CriticalHit = false,
                Instigator = instigator
            };
        }
    }

    /// <summary>Anything a weapon or hazard can damage.</summary>
    public interface IDamageable
    {
        bool IsAlive { get; }

        /// <summary>Applies damage and returns how much was actually absorbed.</summary>
        float ApplyDamage(in DamageInfo info);
    }
}
