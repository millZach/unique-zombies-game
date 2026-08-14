using UnityEngine;

namespace Ashfall.Core
{
    /// <summary>
    /// Forwards damage from a child collider (a hitbox) to the owning damageable,
    /// scaling it so head shots read differently from body shots.
    ///
    /// This lives in its own file on purpose. Unity can only serialise a MonoBehaviour
    /// whose class name matches its filename -- while this class sat inside Damage.cs
    /// every hitbox in every enemy prefab loaded as "the referenced script is missing",
    /// which silently cost the game its critical-hit multipliers.
    /// </summary>
    public class DamageRelay : MonoBehaviour, IDamageable
    {
        [SerializeField] private MonoBehaviour targetBehaviour;
        [SerializeField] private float damageMultiplier = 1f;
        [SerializeField] private bool countsAsCritical;

        private IDamageable _target;

        public float DamageMultiplier => damageMultiplier;
        public bool CountsAsCritical => countsAsCritical;

        public void Configure(IDamageable target, float multiplier, bool critical)
        {
            _target = target;
            targetBehaviour = target as MonoBehaviour;
            damageMultiplier = multiplier;
            countsAsCritical = critical;
        }

        private void Awake()
        {
            _target ??= targetBehaviour as IDamageable;
            _target ??= GetComponentInParent<IDamageable>();
        }

        public bool IsAlive => _target != null && _target.IsAlive;

        public float ApplyDamage(in DamageInfo info)
        {
            // The relay may be hit before Awake if a prefab is shot on its first frame
            // out of the pool, so resolve lazily rather than assuming Awake has run.
            _target ??= targetBehaviour as IDamageable;
            _target ??= GetComponentInParent<IDamageable>();

            if (_target == null)
            {
                return 0f;
            }

            DamageInfo scaled = info;
            scaled.Amount *= damageMultiplier;
            scaled.CriticalHit |= countsAsCritical;
            return _target.ApplyDamage(scaled);
        }
    }
}
