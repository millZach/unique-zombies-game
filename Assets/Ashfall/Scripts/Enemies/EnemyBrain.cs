using System;
using UnityEngine;
using Ashfall.Core;
using Ashfall.Nav;
using Ashfall.Player;
using Ashfall.World;

namespace Ashfall.Enemies
{
    public enum EnemyState
    {
        Idle,
        Chase,
        AttackWindup,
        AttackRecover,
        TearBarricade,
        Stagger,
        Dead
    }

    /// <summary>
    /// The enemy's decision layer. Locomotion belongs to <see cref="SteeringAgent"/>;
    /// this decides where to go and when to swing.
    ///
    /// Barricades are handled by probing forward rather than by pathfinding around
    /// them: the nav graph deliberately routes enemies *at* a breach, and if boards are
    /// in the way the enemy stops and tears them off. That keeps the player's repair
    /// work meaningful without the graph having to be rebuilt every time a board moves.
    /// </summary>
    [RequireComponent(typeof(SteeringAgent))]
    [RequireComponent(typeof(EnemyHealth))]
    public class EnemyBrain : MonoBehaviour
    {
        [Header("Definition")]
        [SerializeField] private EnemyDefinition definition;

        [Header("References")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform attackOrigin;

        [Header("Probing")]
        [SerializeField] private float barricadeProbeDistance = 1.6f;
        [SerializeField] private float targetRefreshInterval = 0.35f;

        public event Action<EnemyBrain> ReachedPlayer;

        private SteeringAgent _agent;
        private EnemyHealth _health;
        private CharacterController _controller;
        private Transform _target;
        private PlayerHealth _targetHealth;
        private Barricade _blockingBarricade;

        private EnemyState _state = EnemyState.Idle;
        private float _stateTimer;
        private float _attackCooldown;
        private float _targetRefreshTimer;
        private float _gaitPhase;
        private Vector3 _visualRest;
        private float _speedScale = 1f;
        private float _damageScale = 1f;
        private readonly Collider[] _probeBuffer = new Collider[8];

        public EnemyDefinition Definition => definition;
        public EnemyState State => _state;
        public EnemyHealth Health => _health;
        public bool IsActive => _state != EnemyState.Dead && isActiveAndEnabled;

        /// <summary>
        /// Whether the bob-and-lean gait on <see cref="visualRoot"/> runs.
        ///
        /// A rigged Meshcaster body animates its own limbs, and the bob on top
        /// of a walk cycle reads as a limp in a bouncing castle. <see
        /// cref="ZombieAnimator"/> turns this off when it takes over and turns
        /// it back on if it cannot. Not serialised: the default is on, and the
        /// only thing that ever changes it is a component that proved it has a
        /// working controller.
        /// </summary>
        public bool ProceduralGaitEnabled { get; set; } = true;

        private void Awake()
        {
            _agent = GetComponent<SteeringAgent>();
            _health = GetComponent<EnemyHealth>();
            _controller = GetComponent<CharacterController>();
            visualRoot ??= transform;
            _visualRest = visualRoot.localPosition;
            attackOrigin ??= transform;
        }

        private void OnEnable()
        {
            _health.DamageTaken += OnDamageTaken;
            _health.Died += OnDied;
        }

        private void OnDisable()
        {
            _health.DamageTaken -= OnDamageTaken;
            _health.Died -= OnDied;
        }

        /// <summary>Called by the spawner every time this body comes out of the pool.</summary>
        public void Spawn(EnemyDefinition def, Transform target, float healthScale, float speedScale, float damageScale)
        {
            definition = def;
            _target = target;
            _targetHealth = target != null ? target.GetComponentInParent<PlayerHealth>() : null;
            _speedScale = speedScale;
            _damageScale = damageScale;

            _state = EnemyState.Chase;
            _stateTimer = 0f;
            _attackCooldown = UnityEngine.Random.Range(0f, def.attackInterval * 0.5f);
            _blockingBarricade = null;
            _gaitPhase = UnityEngine.Random.value * 10f;

            if (_controller != null)
            {
                _controller.radius = def.bodyRadius;
                _controller.height = def.bodyHeight;
                _controller.center = new Vector3(0f, def.bodyHeight * 0.5f, 0f);
            }

            _agent.MoveSpeed = def.baseMoveSpeed;
            _agent.SpeedMultiplier = speedScale * UnityEngine.Random.Range(1f - def.accelerationVariance, 1f + def.accelerationVariance);
            _agent.ResetState();

            _health.Initialise(def.baseHealth * healthScale, def.bodyColor, def.corruptionColor * def.corruptionEmission);

            if (visualRoot != null)
            {
                visualRoot.localScale = Vector3.one;
                visualRoot.localPosition = _visualRest;

                // The gait leans the visual root and nothing else put it back,
                // so a recycled body used to come out of the pool still tipped
                // over from its last life.
                visualRoot.localRotation = Quaternion.identity;
            }
        }

        public void SetTarget(Transform target)
        {
            _target = target;
            _targetHealth = target != null ? target.GetComponentInParent<PlayerHealth>() : null;
        }

        private void Update()
        {
            if (_state == EnemyState.Dead || definition == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            _stateTimer += dt;
            _attackCooldown = Mathf.Max(0f, _attackCooldown - dt);
            _targetRefreshTimer -= dt;

            if (_targetRefreshTimer <= 0f)
            {
                _targetRefreshTimer = targetRefreshInterval;
                RefreshTarget();
            }

            switch (_state)
            {
                case EnemyState.Chase:
                    TickChase(dt);
                    break;

                case EnemyState.AttackWindup:
                    TickAttackWindup(dt);
                    break;

                case EnemyState.AttackRecover:
                    _agent.Tick(transform.position, dt, allowMovement: false);
                    if (_stateTimer >= definition.attackInterval * 0.45f)
                    {
                        EnterState(EnemyState.Chase);
                    }

                    break;

                case EnemyState.TearBarricade:
                    TickTearBarricade(dt);
                    break;

                case EnemyState.Stagger:
                    _agent.Tick(transform.position, dt, allowMovement: false);
                    if (_stateTimer >= definition.staggerSeconds)
                    {
                        EnterState(EnemyState.Chase);
                    }

                    break;

                case EnemyState.Idle:
                default:
                    _agent.Tick(transform.position, dt, allowMovement: false);
                    if (_target != null)
                    {
                        EnterState(EnemyState.Chase);
                    }

                    break;
            }

            TickGait(dt);
        }

        private void RefreshTarget()
        {
            if (_target != null && (_targetHealth == null || _targetHealth.IsAlive))
            {
                return;
            }

            PlayerHealth found = FindFirstObjectByType<PlayerHealth>();
            if (found != null && found.IsAlive)
            {
                _target = found.transform;
                _targetHealth = found;
            }
        }

        private void TickChase(float dt)
        {
            if (_target == null)
            {
                _agent.Tick(transform.position, dt, allowMovement: false);
                return;
            }

            Vector3 targetPosition = _target.position;
            float distance = Vector3.Distance(transform.position, targetPosition);

            if (distance <= definition.attackRange && HasLineToTarget(targetPosition))
            {
                if (_attackCooldown <= 0f)
                {
                    EnterState(EnemyState.AttackWindup);
                    return;
                }

                // In range but on cooldown: circle rather than stand still, so packs
                // spread around the player instead of stacking on one face.
                Vector3 tangent = Vector3.Cross(Vector3.up, (targetPosition - transform.position).normalized);
                Vector3 strafeGoal = targetPosition + tangent * (definition.attackRange * 0.85f);
                _agent.Tick(strafeGoal, dt);
                return;
            }

            Barricade barricade = ProbeBarricade();
            if (barricade != null && !barricade.IsBreached)
            {
                _blockingBarricade = barricade;
                EnterState(EnemyState.TearBarricade);
                return;
            }

            _agent.Tick(targetPosition, dt);
        }

        private void TickAttackWindup(float dt)
        {
            _agent.Tick(transform.position, dt, allowMovement: false);

            // Face the target through the windup so the swing is telegraphed.
            if (_target != null)
            {
                Vector3 flat = _target.position - transform.position;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation,
                        Quaternion.LookRotation(flat.normalized, Vector3.up),
                        540f * dt);
                }
            }

            if (definition.attackLungeSpeed > 0f && _controller != null)
            {
                _controller.Move(transform.forward * (definition.attackLungeSpeed * dt));
            }

            if (_stateTimer < definition.attackWindupSeconds)
            {
                return;
            }

            ResolveAttack();
            _attackCooldown = definition.attackInterval;
            EnterState(EnemyState.AttackRecover);
        }

        private void ResolveAttack()
        {
            if (_target == null)
            {
                return;
            }

            Vector3 origin = attackOrigin != null ? attackOrigin.position : transform.position + Vector3.up;
            float distance = Vector3.Distance(origin, _target.position + Vector3.up);

            // The swing is audible whether or not it lands: a miss the player
            // heard is the feedback that backing off worked.
            Audio.AudioDirector.Instance?.PlayAt(
                Audio.AudioCues.AttackFor(definition.archetype), origin);

            // Re-check on the swing rather than on the windup: backing out of range in
            // time is the core defensive skill in the game.
            if (distance > definition.attackRange * 1.25f)
            {
                return;
            }

            var health = _targetHealth;
            if (health == null || !health.IsAlive)
            {
                return;
            }

            Vector3 direction = (_target.position - transform.position).normalized;
            float damage = definition.attackDamage * _damageScale;
            health.ApplyDamage(DamageInfo.Melee(damage, _target.position, direction, gameObject));

            if (definition.knockbackForce > 0f)
            {
                var motor = _target.GetComponentInParent<PlayerMotor>();
                motor?.AddImpulse(direction * definition.knockbackForce + Vector3.up * 1.5f);
            }

            ReachedPlayer?.Invoke(this);
        }

        private void TickTearBarricade(float dt)
        {
            _agent.Tick(transform.position, dt, allowMovement: false);

            if (_blockingBarricade == null || _blockingBarricade.IsBreached)
            {
                _blockingBarricade = null;
                EnterState(EnemyState.Chase);
                return;
            }

            // Face the boards.
            Vector3 flat = _blockingBarricade.transform.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(flat.normalized, Vector3.up),
                    360f * dt);
            }

            if (_attackCooldown > 0f)
            {
                return;
            }

            _attackCooldown = definition.barricadeAttackInterval;
            _blockingBarricade.ApplyDamage(new DamageInfo
            {
                Amount = definition.barricadeDamagePerHit,
                Point = _blockingBarricade.transform.position,
                Direction = transform.forward,
                Normal = -transform.forward,
                Kind = DamageKind.Melee,
                Instigator = gameObject
            });

            if (_blockingBarricade.IsBreached)
            {
                _blockingBarricade = null;
                EnterState(EnemyState.Chase);
            }
        }

        private Barricade ProbeBarricade()
        {
            Vector3 origin = transform.position + Vector3.up * (definition.bodyHeight * 0.5f);
            int count = Physics.OverlapSphereNonAlloc(
                origin + transform.forward * (barricadeProbeDistance * 0.5f),
                barricadeProbeDistance * 0.6f,
                _probeBuffer,
                AshfallLayers.Mask(AshfallLayers.InteractableName, AshfallLayers.NavBlockerName),
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                if (_probeBuffer[i] == null)
                {
                    continue;
                }

                var barricade = _probeBuffer[i].GetComponentInParent<Barricade>();
                if (barricade != null && !barricade.IsBreached)
                {
                    return barricade;
                }
            }

            return null;
        }

        private bool HasLineToTarget(Vector3 targetPosition)
        {
            Vector3 origin = transform.position + Vector3.up * (definition.bodyHeight * 0.6f);
            Vector3 to = (targetPosition + Vector3.up) - origin;
            float distance = to.magnitude;
            if (distance < 0.05f)
            {
                return true;
            }

            return !Physics.Raycast(origin, to / distance, distance,
                AshfallLayers.BlockingMask, QueryTriggerInteraction.Ignore);
        }

        private void EnterState(EnemyState next)
        {
            _state = next;
            _stateTimer = 0f;
        }

        private void TickGait(float dt)
        {
            if (visualRoot == null || !ProceduralGaitEnabled)
            {
                return;
            }

            float speed = _agent.CurrentSpeed;
            _gaitPhase += dt * definition.gaitBobFrequency * Mathf.Clamp(speed / Mathf.Max(0.1f, definition.baseMoveSpeed), 0.15f, 2f);

            float amplitude = definition.gaitBobAmplitude * Mathf.Clamp01(speed / Mathf.Max(0.1f, definition.baseMoveSpeed));
            float bob = Mathf.Abs(Mathf.Sin(_gaitPhase)) * amplitude;
            float sway = Mathf.Sin(_gaitPhase * 0.5f) * amplitude * 0.6f;

            visualRoot.localPosition = _visualRest + new Vector3(sway, bob, 0f);

            float leanTarget = _state == EnemyState.AttackWindup
                ? -definition.leanDegrees * 1.6f
                : Mathf.Clamp01(speed / Mathf.Max(0.1f, definition.baseMoveSpeed)) * definition.leanDegrees;

            visualRoot.localRotation = Quaternion.Slerp(
                visualRoot.localRotation,
                Quaternion.Euler(leanTarget, 0f, Mathf.Sin(_gaitPhase) * definition.leanDegrees * 0.35f),
                1f - Mathf.Exp(-9f * dt));
        }

        private void OnDamageTaken(float amount, bool critical)
        {
            if (_state == EnemyState.Dead)
            {
                return;
            }

            if (definition == null)
            {
                return;
            }

            float threshold = definition.staggerThreshold;
            if (amount < threshold)
            {
                return;
            }

            if (definition.staggerResistance > 0f && UnityEngine.Random.value < definition.staggerResistance)
            {
                return;
            }

            // Do not let a stagger cancel a swing that has already been telegraphed past
            // the halfway point -- that would make brutes feel weightless.
            if (_state == EnemyState.AttackWindup && _stateTimer > definition.attackWindupSeconds * 0.5f)
            {
                return;
            }

            EnterState(EnemyState.Stagger);
        }

        private void OnDied(EnemyHealth health, GameObject killer)
        {
            EnterState(EnemyState.Dead);
            _agent.enabled = false;
        }

        /// <summary>Re-enables locomotion when the body is recycled.</summary>
        public void PrepareForReuse()
        {
            _state = EnemyState.Idle;
            _stateTimer = 0f;
            _blockingBarricade = null;
            if (_agent != null)
            {
                _agent.enabled = true;
            }
        }
    }
}
