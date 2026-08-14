using System.Collections.Generic;
using UnityEngine;
using Ashfall.Core;

namespace Ashfall.Nav
{
    /// <summary>
    /// Path-following locomotion for enemies: A* over the baked <see cref="NavGraph"/>
    /// for the long route, plus local steering (separation, wall slide, corner
    /// smoothing) for the last couple of metres.
    ///
    /// Path requests are staggered across agents and throttled by both time and target
    /// movement, so a full wave costs a handful of searches per second rather than one
    /// per agent per frame.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class SteeringAgent : MonoBehaviour
    {
        [Header("Locomotion")]
        [SerializeField] private float moveSpeed = 3.2f;
        [SerializeField] private float acceleration = 14f;
        [SerializeField] private float turnSpeedDegrees = 420f;
        [SerializeField] private float gravity = -22f;
        [SerializeField] private float stepHeightAssist = 0.35f;

        [Header("Avoidance")]
        [SerializeField] private float separationRadius = 1.15f;
        [SerializeField] private float separationStrength = 1.9f;
        [SerializeField] private float wallProbeDistance = 1.1f;
        [SerializeField] private float wallAvoidStrength = 1.5f;

        [Header("Pathing")]
        [SerializeField] private float repathInterval = 0.55f;
        [SerializeField] private float repathTargetMoveThreshold = 1.6f;
        [SerializeField] private float arriveRadius = 0.85f;

        private static readonly List<SteeringAgent> AllAgents = new(64);

        private CharacterController _controller;
        private NavGraph _graph;
        private readonly List<int> _path = new(48);
        private int _pathCursor;
        private Vector3 _lastPathTarget;
        private float _repathTimer;
        private Vector3 _velocity;
        private float _verticalVelocity;
        private Vector3 _steerTarget;
        private bool _hasSteerTarget;
        private bool _directLineOfSight;

        public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
        public float SpeedMultiplier { get; set; } = 1f;
        public Vector3 DesiredVelocity => _velocity;
        public bool HasPath => _path.Count > 0;
        public bool ReachedTarget { get; private set; }

        /// <summary>Planar speed actually achieved last frame; drives lean and audio.</summary>
        public float CurrentSpeed { get; private set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            // Stagger the very first repath so a wave spawned on one frame does not all
            // search on the same frame forever after.
            _repathTimer = Random.Range(0f, repathInterval);
        }

        private void OnEnable()
        {
            AllAgents.Add(this);
            _graph = NavGraph.Active;
            _path.Clear();
            _pathCursor = 0;
            _velocity = Vector3.zero;
            _verticalVelocity = 0f;
        }

        private void OnDisable()
        {
            AllAgents.Remove(this);
        }

        public void ResetState()
        {
            _path.Clear();
            _pathCursor = 0;
            _velocity = Vector3.zero;
            _verticalVelocity = 0f;
            _repathTimer = Random.Range(0f, repathInterval);
            ReachedTarget = false;
        }

        /// <summary>
        /// Drives the agent toward <paramref name="target"/> for this frame.
        /// Call once per Update from the owning brain.
        /// </summary>
        public void Tick(Vector3 target, float deltaTime, bool allowMovement = true)
        {
            if (_controller == null || !_controller.enabled)
            {
                return;
            }

            _graph ??= NavGraph.Active;

            UpdatePath(target, deltaTime);
            Vector3 desiredDirection = ResolveSteering(target);

            float speed = moveSpeed * Mathf.Max(0f, SpeedMultiplier);
            Vector3 desiredVelocity = allowMovement ? desiredDirection * speed : Vector3.zero;

            _velocity = Vector3.MoveTowards(_velocity, desiredVelocity, acceleration * deltaTime);

            if (_controller.isGrounded)
            {
                // A small constant downforce keeps isGrounded stable on the generated
                // geometry's seams instead of flickering between grounded and airborne.
                _verticalVelocity = -2f;
            }
            else
            {
                _verticalVelocity += gravity * deltaTime;
            }

            Vector3 motion = _velocity;
            motion.y = _verticalVelocity;
            _controller.Move(motion * deltaTime);

            Vector3 planar = new Vector3(_velocity.x, 0f, _velocity.z);
            CurrentSpeed = planar.magnitude;

            if (planar.sqrMagnitude > 0.04f)
            {
                Quaternion look = Quaternion.LookRotation(planar.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeedDegrees * deltaTime);
            }

            Vector3 flatToTarget = target - transform.position;
            flatToTarget.y = 0f;
            ReachedTarget = flatToTarget.sqrMagnitude <= arriveRadius * arriveRadius;
        }

        private void UpdatePath(Vector3 target, float deltaTime)
        {
            _repathTimer -= deltaTime;

            bool targetMoved = (target - _lastPathTarget).sqrMagnitude > repathTargetMoveThreshold * repathTargetMoveThreshold;
            bool exhausted = _pathCursor >= _path.Count;

            // Skip the graph entirely when the target is close and plainly visible. Most
            // of an enemy's life is spent in this case once it has closed the distance.
            _directLineOfSight = HasDirectLine(target);
            if (_directLineOfSight && (target - transform.position).sqrMagnitude < 400f)
            {
                _hasSteerTarget = true;
                _steerTarget = target;
                return;
            }

            if (_repathTimer > 0f && !exhausted && !targetMoved)
            {
                AdvanceCursor();
                return;
            }

            _repathTimer = repathInterval * Random.Range(0.85f, 1.2f);
            _lastPathTarget = target;

            if (_graph == null || _graph.NodeCount == 0)
            {
                _hasSteerTarget = true;
                _steerTarget = target;
                return;
            }

            int start = _graph.NearestNode(transform.position);
            int goal = _graph.NearestNode(target);

            if (start < 0 || goal < 0 || !_graph.FindPath(start, goal, _path))
            {
                // No open route (a gate is shut, or the target is off-graph). Fall back to
                // pushing toward the target so the agent never freezes in place.
                _path.Clear();
                _pathCursor = 0;
                _hasSteerTarget = true;
                _steerTarget = target;
                return;
            }

            _pathCursor = 0;
            AdvanceCursor();
        }

        private void AdvanceCursor()
        {
            if (_path.Count == 0 || _graph == null)
            {
                _hasSteerTarget = false;
                return;
            }

            // String-pull: skip to the furthest corner we can still see directly, which
            // turns the blocky grid path into something that looks intentional.
            int best = _pathCursor;
            int lookAhead = Mathf.Min(_path.Count - 1, _pathCursor + 6);
            for (int i = lookAhead; i >= _pathCursor; i--)
            {
                if (HasDirectLine(_graph.NodePosition(_path[i])))
                {
                    best = i;
                    break;
                }
            }

            _pathCursor = best;

            Vector3 corner = _graph.NodePosition(_path[_pathCursor]);
            Vector3 flat = corner - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1.0f && _pathCursor < _path.Count - 1)
            {
                _pathCursor++;
                corner = _graph.NodePosition(_path[_pathCursor]);
            }

            _hasSteerTarget = true;
            _steerTarget = corner;
        }

        private bool HasDirectLine(Vector3 worldTarget)
        {
            Vector3 origin = transform.position + Vector3.up * 0.9f;
            Vector3 to = (worldTarget + Vector3.up * 0.9f) - origin;
            float distance = to.magnitude;
            if (distance < 0.1f)
            {
                return true;
            }

            return !Physics.SphereCast(
                origin,
                Mathf.Max(0.2f, _controller != null ? _controller.radius * 0.7f : 0.3f),
                to / distance,
                out _,
                distance,
                AshfallLayers.BlockingMask,
                QueryTriggerInteraction.Ignore);
        }

        private Vector3 ResolveSteering(Vector3 finalTarget)
        {
            Vector3 goal = _hasSteerTarget ? _steerTarget : finalTarget;

            Vector3 seek = goal - transform.position;
            seek.y = 0f;
            if (seek.sqrMagnitude < 0.0001f)
            {
                return Vector3.zero;
            }

            seek.Normalize();

            Vector3 steer = seek;
            steer += Separation() * separationStrength;
            steer += WallAvoidance(seek) * wallAvoidStrength;

            steer.y = 0f;
            if (steer.sqrMagnitude < 0.0001f)
            {
                return seek;
            }

            return steer.normalized;
        }

        private Vector3 Separation()
        {
            Vector3 push = Vector3.zero;
            Vector3 self = transform.position;
            float radiusSqr = separationRadius * separationRadius;

            for (int i = 0; i < AllAgents.Count; i++)
            {
                SteeringAgent other = AllAgents[i];
                if (other == this || other == null)
                {
                    continue;
                }

                Vector3 delta = self - other.transform.position;
                delta.y = 0f;
                float sqr = delta.sqrMagnitude;
                if (sqr > radiusSqr || sqr < 0.0001f)
                {
                    continue;
                }

                // Inverse-distance weighting: crowding is only unpleasant up close.
                push += delta / sqr;
            }

            return Vector3.ClampMagnitude(push, 2f);
        }

        private Vector3 WallAvoidance(Vector3 forward)
        {
            Vector3 origin = transform.position + Vector3.up * (stepHeightAssist + 0.5f);
            Vector3 avoid = Vector3.zero;

            // Three feelers: straight ahead plus a pair splayed out, which is enough to
            // slide along a wall instead of grinding into it.
            for (int i = -1; i <= 1; i++)
            {
                Vector3 dir = Quaternion.AngleAxis(i * 38f, Vector3.up) * forward;
                if (Physics.Raycast(origin, dir, out RaycastHit hit, wallProbeDistance,
                        AshfallLayers.BlockingMask, QueryTriggerInteraction.Ignore))
                {
                    float weight = 1f - (hit.distance / wallProbeDistance);
                    Vector3 normal = hit.normal;
                    normal.y = 0f;
                    avoid += normal.normalized * weight;
                }
            }

            return avoid;
        }

        /// <summary>Teleport safely: resets the controller so it does not fight the move.</summary>
        public void Warp(Vector3 position)
        {
            if (_controller != null)
            {
                _controller.enabled = false;
                transform.position = position;
                _controller.enabled = true;
            }
            else
            {
                transform.position = position;
            }

            ResetState();
        }
    }
}
