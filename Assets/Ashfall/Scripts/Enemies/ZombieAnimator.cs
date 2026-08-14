using UnityEngine;
using Ashfall.Nav;

namespace Ashfall.Enemies
{
    /// <summary>
    /// Drives an imported, rigged enemy body from the brain's own state.
    ///
    /// This component only exists on a prefab whose Meshcaster art was rigged by
    /// <c>Tools/Blender/rig_zombie.py</c>. With no rigged art staged the enemy
    /// keeps its procedural body and this component is never added, so the
    /// shipping behaviour of the game is unchanged by its existence.
    ///
    /// It cross-fades directly to states rather than driving a transition graph
    /// with parameters. A graph would need every parameter, condition and
    /// transition to agree with this file; direct cross-fades need only the
    /// state to exist, and <see cref="Animator.HasState"/> answers that at
    /// startup. Anything missing degrades one step at a time -- a missing Walk
    /// falls back to Idle, a missing Idle hands the body back to the procedural
    /// gait -- instead of freezing an enemy in a T-pose mid-round.
    /// </summary>
    [DisallowMultipleComponent]
    public class ZombieAnimator : MonoBehaviour
    {
        /// <summary>The five clips <c>rig_zombie.py</c> authors. Order is not significant.</summary>
        public static readonly string[] ClipNames = { "Idle", "Walk", "Attack", "HitReact", "Death" };

        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private EnemyBrain brain;
        [SerializeField] private EnemyHealth health;
        [SerializeField] private SteeringAgent agent;

        [Header("Tuning")]
        [Tooltip("Metres per second above which Chase plays Walk rather than Idle.")]
        [SerializeField] private float moveThreshold = 0.2f;

        [Tooltip("Seconds a hit reaction holds when the brain did not enter Stagger.")]
        [SerializeField] private float hitReactSeconds = 0.28f;

        [SerializeField] private float crossFadeSeconds = 0.12f;

        private static readonly int IdleHash = Animator.StringToHash("Idle");
        private static readonly int WalkHash = Animator.StringToHash("Walk");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int HitReactHash = Animator.StringToHash("HitReact");
        private static readonly int DeathHash = Animator.StringToHash("Death");

        private bool _hasIdle;
        private bool _hasWalk;
        private bool _hasAttack;
        private bool _hasHitReact;
        private bool _hasDeath;
        private bool _drivesBody;

        private int _currentHash;
        private float _hitReactUntil;
        private bool _dead;

        /// <summary>True when a usable controller was found and this component owns the pose.</summary>
        public bool DrivesBody => _drivesBody;

        /// <summary>Which clip the last evaluation asked for. Exposed for tests.</summary>
        public string CurrentClip { get; private set; } = "Idle";

        /// <summary>Editor-time wiring. Everything is optional; missing pieces disable the bridge.</summary>
        public void Configure(Animator source, EnemyBrain enemyBrain, EnemyHealth enemyHealth, SteeringAgent steering)
        {
            animator = source;
            brain = enemyBrain;
            health = enemyHealth;
            agent = steering;
        }

        /// <summary>
        /// The clip a given brain state asks for, before any fallback for
        /// clips this rig does not have. Pure, so the mapping can be tested
        /// without an Animator, a scene or a definition.
        /// </summary>
        public static string ClipFor(EnemyState state, bool dying, bool reacting, bool moving)
        {
            if (dying || state == EnemyState.Dead)
            {
                return "Death";
            }

            switch (state)
            {
                case EnemyState.AttackWindup:
                case EnemyState.AttackRecover:
                case EnemyState.TearBarricade:
                    return "Attack";

                case EnemyState.Stagger:
                    return "HitReact";
            }

            // A hit that did not stagger still deserves a flinch, but never at
            // the cost of hiding a telegraphed swing -- the attack cases above
            // have already returned.
            if (reacting)
            {
                return "HitReact";
            }

            return state == EnemyState.Chase && moving ? "Walk" : "Idle";
        }

        private void Awake()
        {
            animator ??= GetComponentInChildren<Animator>(true);
            brain ??= GetComponent<EnemyBrain>();
            health ??= GetComponent<EnemyHealth>();
            agent ??= GetComponent<SteeringAgent>();

            ResolveStates();

            if (animator == null)
            {
                return;
            }

            // The CharacterController owns position. Root motion on top of it
            // would double every step and slide the feet.
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            animator.keepAnimatorStateOnDisable = false;

            if (_hasDeath && health != null)
            {
                // The pool recycles a corpse on EnemyHealth's own timer. Left at
                // its default that timer is shorter than the death clip, so the
                // body would vanish mid-collapse.
                health.SetDeathCollapseSeconds(Mathf.Clamp(DeathClipLength(), 0.6f, 1.6f));
            }
        }

        private void ResolveStates()
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                _drivesBody = false;
                return;
            }

            _hasIdle = animator.HasState(0, IdleHash);
            _hasWalk = animator.HasState(0, WalkHash);
            _hasAttack = animator.HasState(0, AttackHash);
            _hasHitReact = animator.HasState(0, HitReactHash);
            _hasDeath = animator.HasState(0, DeathHash);

            // Idle is the floor every other fallback lands on. Without it there
            // is nothing safe to play, so the procedural gait keeps the body.
            _drivesBody = _hasIdle;
        }

        private void OnEnable()
        {
            _dead = false;
            _hitReactUntil = 0f;
            _currentHash = 0;
            CurrentClip = "Idle";

            if (health != null)
            {
                health.DamageTaken += OnDamageTaken;
                health.Died += OnDied;
            }

            ApplyOwnership();

            if (_drivesBody && animator != null && animator.isActiveAndEnabled)
            {
                animator.Rebind();
                Play(IdleHash, "Idle", 0f);
            }
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.DamageTaken -= OnDamageTaken;
                health.Died -= OnDied;
            }

            // Hand the body back on the way out, so a pooled object that is
            // later reused without a rig is not left frozen.
            if (brain != null)
            {
                brain.ProceduralGaitEnabled = true;
            }

            if (health != null)
            {
                health.ProceduralDeathCollapse = true;
            }
        }

        private void ApplyOwnership()
        {
            if (brain != null)
            {
                brain.ProceduralGaitEnabled = !_drivesBody;
            }

            if (health != null)
            {
                health.ProceduralDeathCollapse = !(_drivesBody && _hasDeath);
            }
        }

        private void Update()
        {
            if (!_drivesBody || animator == null || brain == null)
            {
                return;
            }

            bool reacting = Time.time < _hitReactUntil;
            bool moving = agent != null && agent.CurrentSpeed > moveThreshold;
            bool dying = _dead || (health != null && (health.IsDying || !health.IsAlive));

            string wanted = ClipFor(brain.State, dying, reacting, moving);
            CurrentClip = wanted;

            switch (wanted)
            {
                case "Death":
                    Play(_hasDeath ? DeathHash : IdleHash, wanted, _hasDeath ? crossFadeSeconds : 0f);
                    break;

                case "Attack":
                    Play(_hasAttack ? AttackHash : IdleHash, wanted, crossFadeSeconds);
                    break;

                case "HitReact":
                    Play(_hasHitReact ? HitReactHash : IdleHash, wanted, crossFadeSeconds * 0.5f);
                    break;

                case "Walk":
                    Play(_hasWalk ? WalkHash : IdleHash, wanted, crossFadeSeconds);
                    break;

                default:
                    Play(IdleHash, wanted, crossFadeSeconds);
                    break;
            }
        }

        private void Play(int hash, string clipName, float fade)
        {
            if (hash == _currentHash)
            {
                return;
            }

            _currentHash = hash;

            // A one-shot has to restart from frame zero even when it is already
            // the current state, which is why the guard above is on the hash we
            // last asked for rather than on the Animator's own state.
            if (fade <= 0f)
            {
                animator.Play(hash, 0, 0f);
            }
            else
            {
                animator.CrossFadeInFixedTime(hash, fade, 0, 0f);
            }
        }

        private void OnDamageTaken(float amount, bool critical)
        {
            if (_dead)
            {
                return;
            }

            _hitReactUntil = Time.time + hitReactSeconds;

            // Re-arm the one-shot: without this a second hit inside the window
            // would leave the first flinch playing out its tail.
            if (_currentHash == HitReactHash)
            {
                _currentHash = 0;
            }
        }

        private void OnDied(EnemyHealth source, GameObject killer)
        {
            _dead = true;
            _hitReactUntil = 0f;
        }

        private float DeathClipLength()
        {
            RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller == null)
            {
                return 0.85f;
            }

            AnimationClip[] clips = controller.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && MatchesClip(clips[i].name, "Death"))
                {
                    return clips[i].length;
                }
            }

            return 0.85f;
        }

        /// <summary>
        /// True when an imported clip is the named one.
        ///
        /// Unity names an FBX take <c>&lt;rig object&gt;|&lt;action&gt;</c>, and
        /// Blender's exporter has been known to prefix it twice, so the match is
        /// on the last segment rather than on the whole string.
        /// </summary>
        public static bool MatchesClip(string clipName, string wanted)
        {
            if (string.IsNullOrEmpty(clipName))
            {
                return false;
            }

            int bar = clipName.LastIndexOf('|');
            string tail = bar >= 0 ? clipName.Substring(bar + 1) : clipName;
            return string.Equals(tail.Trim(), wanted, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
