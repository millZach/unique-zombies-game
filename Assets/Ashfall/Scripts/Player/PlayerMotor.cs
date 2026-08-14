using UnityEngine;
using Ashfall.Core;
using Ashfall.InputLayer;

namespace Ashfall.Player
{
    /// <summary>
    /// First-person locomotion on a CharacterController: walk, sprint, crouch, jump,
    /// air control and slope handling. Deliberately snappy -- this is a survival
    /// shooter where backpedalling out of a swing has to feel reliable.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Speeds (m/s)")]
        [SerializeField] private float walkSpeed = 4.6f;
        [SerializeField] private float sprintSpeed = 7.4f;
        [SerializeField] private float crouchSpeed = 2.4f;
        [SerializeField] private float backpedalScale = 0.78f;

        [Header("Acceleration")]
        [SerializeField] private float groundAcceleration = 62f;
        [SerializeField] private float groundDeceleration = 48f;
        [SerializeField] private float airAcceleration = 14f;

        [Header("Jump / gravity")]
        [SerializeField] private float jumpHeight = 1.15f;
        [SerializeField] private float gravity = -24f;
        [SerializeField] private float coyoteTime = 0.12f;
        [SerializeField] private float jumpBufferTime = 0.14f;
        [SerializeField] private float terminalVelocity = -45f;

        [Header("Stance")]
        [SerializeField] private float standHeight = 1.85f;
        [SerializeField] private float crouchHeight = 1.15f;
        [SerializeField] private float standEyeHeight = 1.66f;
        [SerializeField] private float crouchEyeHeight = 1.0f;
        [SerializeField] private float stanceLerpSpeed = 11f;

        [Header("Feel")]
        [SerializeField] private float sprintRequiresForwardDot = 0.55f;

        private CharacterController _controller;
        private PlayerCameraRig _cameraRig;

        private Vector3 _planarVelocity;
        private float _verticalVelocity;
        private float _coyoteTimer;
        private float _jumpBufferTimer;
        private float _currentHeight;
        private float _currentEyeHeight;
        private bool _wasGrounded = true;
        private float _yaw;

        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }
        public float PlanarSpeed { get; private set; }

        /// <summary>Speed as a fraction of the sprint speed. Feeds bob and weapon spread.</summary>
        public float SpeedFraction => sprintSpeed <= 0f ? 0f : Mathf.Clamp01(PlanarSpeed / sprintSpeed);

        /// <summary>Multiplies the top speed. Aiming and Last Stand both write to this.</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        public float StandHeight => standHeight;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _cameraRig = GetComponentInChildren<PlayerCameraRig>();
            _currentHeight = standHeight;
            _currentEyeHeight = standEyeHeight;
            _yaw = transform.eulerAngles.y;
            ApplyStance(standHeight, standEyeHeight);
        }

        public void SetCameraRig(PlayerCameraRig rig)
        {
            _cameraRig = rig;
        }

        public void ResetMotor(Vector3 position, float yaw)
        {
            _controller.enabled = false;
            transform.position = position;
            _yaw = yaw;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            _controller.enabled = true;

            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _coyoteTimer = 0f;
            _jumpBufferTimer = 0f;
            IsCrouching = false;
            IsSprinting = false;
            _currentHeight = standHeight;
            _currentEyeHeight = standEyeHeight;
            ApplyStance(standHeight, standEyeHeight);
        }

        /// <summary>Applies horizontal look. Vertical look is the camera rig's job.</summary>
        public void ApplyYawDelta(float degrees)
        {
            _yaw += degrees;
            if (_yaw > 360f) _yaw -= 360f;
            else if (_yaw < -360f) _yaw += 360f;
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        public void Tick(in InputFrame input, float deltaTime, bool allowMovement, bool aiming, float aimSpeedScale)
        {
            if (_controller == null || !_controller.enabled)
            {
                return;
            }

            IsGrounded = _controller.isGrounded;

            if (IsGrounded && !_wasGrounded)
            {
                _cameraRig?.NotifyLanded(Mathf.Abs(_verticalVelocity));
            }

            _wasGrounded = IsGrounded;
            _coyoteTimer = IsGrounded ? coyoteTime : Mathf.Max(0f, _coyoteTimer - deltaTime);
            _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - deltaTime);

            Vector2 move = allowMovement ? input.Move : Vector2.zero;
            if (allowMovement && input.JumpPressed)
            {
                _jumpBufferTimer = jumpBufferTime;
            }

            UpdateStance(allowMovement && input.CrouchHeld, deltaTime);

            Vector3 wish = transform.right * move.x + transform.forward * move.y;
            if (wish.sqrMagnitude > 1f)
            {
                wish.Normalize();
            }

            // Sprint only counts when actually driving forward, so strafing at full
            // sprint speed is not a thing.
            bool wantsSprint = allowMovement
                               && input.SprintHeld
                               && !IsCrouching
                               && !aiming
                               && move.y > 0.1f
                               && Vector3.Dot(wish.normalized, transform.forward) >= sprintRequiresForwardDot;
            IsSprinting = wantsSprint && IsGrounded;

            float targetSpeed = ResolveTargetSpeed(move, aiming, aimSpeedScale);
            Vector3 targetVelocity = wish * targetSpeed;

            float accel = IsGrounded
                ? (targetVelocity.sqrMagnitude > _planarVelocity.sqrMagnitude ? groundAcceleration : groundDeceleration)
                : airAcceleration;

            _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVelocity, accel * deltaTime);

            // Vertical
            if (IsGrounded && _verticalVelocity <= 0f)
            {
                _verticalVelocity = -3f;
            }

            if (_jumpBufferTimer > 0f && _coyoteTimer > 0f && !IsCrouching)
            {
                _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
                _jumpBufferTimer = 0f;
                _coyoteTimer = 0f;
                IsGrounded = false;
            }
            else
            {
                _verticalVelocity = Mathf.Max(terminalVelocity, _verticalVelocity + gravity * deltaTime);
            }

            Vector3 motion = _planarVelocity;
            motion.y = _verticalVelocity;
            _controller.Move(motion * deltaTime);

            Vector3 planar = _controller.velocity;
            planar.y = 0f;
            PlanarSpeed = planar.magnitude;
        }

        private float ResolveTargetSpeed(Vector2 move, bool aiming, float aimSpeedScale)
        {
            float speed;
            if (IsCrouching)
            {
                speed = crouchSpeed;
            }
            else if (IsSprinting)
            {
                speed = sprintSpeed;
            }
            else
            {
                speed = walkSpeed;
            }

            if (move.y < -0.1f)
            {
                speed *= backpedalScale;
            }

            if (aiming)
            {
                speed *= Mathf.Clamp(aimSpeedScale, 0.2f, 1f);
            }

            return speed * Mathf.Max(0.1f, SpeedMultiplier);
        }

        private void UpdateStance(bool wantsCrouch, float deltaTime)
        {
            if (IsCrouching && !wantsCrouch && !HasHeadroom())
            {
                // Blocked from standing: stay down rather than clipping into the ceiling.
                wantsCrouch = true;
            }

            IsCrouching = wantsCrouch;

            float targetHeight = IsCrouching ? crouchHeight : standHeight;
            float targetEye = IsCrouching ? crouchEyeHeight : standEyeHeight;

            _currentHeight = Mathf.Lerp(_currentHeight, targetHeight, 1f - Mathf.Exp(-stanceLerpSpeed * deltaTime));
            _currentEyeHeight = Mathf.Lerp(_currentEyeHeight, targetEye, 1f - Mathf.Exp(-stanceLerpSpeed * deltaTime));

            ApplyStance(_currentHeight, _currentEyeHeight);
        }

        private void ApplyStance(float height, float eyeHeight)
        {
            if (_controller == null)
            {
                return;
            }

            _controller.height = height;
            _controller.center = new Vector3(0f, height * 0.5f, 0f);
            _cameraRig?.SetEyeHeight(eyeHeight);
        }

        private bool HasHeadroom()
        {
            Vector3 origin = transform.position + Vector3.up * (crouchHeight - _controller.radius + 0.05f);
            float distance = standHeight - crouchHeight;
            return !Physics.SphereCast(
                origin,
                _controller.radius * 0.95f,
                Vector3.up,
                out _,
                distance,
                AshfallLayers.BlockingMask,
                QueryTriggerInteraction.Ignore);
        }

        /// <summary>Shoves the player, used by brute slams. Additive to current motion.</summary>
        public void AddImpulse(Vector3 impulse)
        {
            _planarVelocity += new Vector3(impulse.x, 0f, impulse.z);
            if (impulse.y > 0f)
            {
                _verticalVelocity = Mathf.Max(_verticalVelocity, impulse.y);
            }
        }
    }
}
