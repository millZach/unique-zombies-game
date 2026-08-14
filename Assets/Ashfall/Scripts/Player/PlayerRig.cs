using UnityEngine;
using Ashfall.Core;
using Ashfall.InputLayer;

namespace Ashfall.Player
{
    /// <summary>
    /// The single Update that drives the player.
    ///
    /// Every player subsystem is passed the same immutable <see cref="InputFrame"/> in a
    /// fixed order, so ordering bugs between look, movement, firing and interaction
    /// simply cannot happen -- there is one place that decides what runs when.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class PlayerRig : MonoBehaviour
    {
        [Header("Subsystems")]
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerCameraRig cameraRig;
        [SerializeField] private PlayerLoadout loadout;
        [SerializeField] private PlayerHealth health;
        [SerializeField] private PlayerInteractor interactor;

        [Header("Spawn")]
        [SerializeField] private Transform spawnPoint;

        private AshfallInput _input;
        private bool _controlEnabled = true;
        private bool _combatEnabled = true;
        private Vector3 _spawnPosition;
        private float _spawnYaw;

        public PlayerMotor Motor => motor;
        public PlayerCameraRig CameraRig => cameraRig;
        public PlayerLoadout Loadout => loadout;
        public PlayerHealth Health => health;
        public PlayerInteractor Interactor => interactor;
        public Camera ViewCamera => cameraRig != null ? cameraRig.ViewCamera : null;

        public bool ControlEnabled => _controlEnabled;

        private void Awake()
        {
            motor ??= GetComponent<PlayerMotor>();
            cameraRig ??= GetComponentInChildren<PlayerCameraRig>();
            loadout ??= GetComponent<PlayerLoadout>();
            health ??= GetComponent<PlayerHealth>();
            interactor ??= GetComponent<PlayerInteractor>();

            motor?.SetCameraRig(cameraRig);

            if (spawnPoint != null)
            {
                _spawnPosition = spawnPoint.position;
                _spawnYaw = spawnPoint.eulerAngles.y;
            }
            else
            {
                _spawnPosition = transform.position;
                _spawnYaw = transform.eulerAngles.y;
            }
        }

        private void Start()
        {
            _input = AshfallInput.Instance;
            _input.SetCursorLocked(true);
        }

        public void SetSpawn(Vector3 position, float yaw)
        {
            _spawnPosition = position;
            _spawnYaw = yaw;
        }

        /// <summary>Full control: look, move, shoot, interact.</summary>
        public void SetControlEnabled(bool enabledState)
        {
            _controlEnabled = enabledState;
            if (!enabledState)
            {
                interactor?.ClearTarget();
            }
        }

        /// <summary>Look and move stay live, but shooting and buying are locked out.</summary>
        public void SetCombatEnabled(bool enabledState)
        {
            _combatEnabled = enabledState;
        }

        public void RespawnAtStart()
        {
            motor?.ResetMotor(_spawnPosition, _spawnYaw);
            cameraRig?.ResetRig();
            health?.ResetVitals();
            loadout?.ResetLoadout();
            interactor?.ClearTarget();
            _controlEnabled = true;
            _combatEnabled = true;
        }

        private void Update()
        {
            _input ??= AshfallInput.Instance;

            float dt = Time.deltaTime;
            InputFrame frame = _controlEnabled ? _input.Frame : InputFrame.Empty;

            if (_controlEnabled)
            {
                Vector2 look = _input.ResolveLookDegrees(frame, dt);
                motor.ApplyYawDelta(look.x);
                cameraRig?.ApplyPitchDelta(look.y);
            }

            bool aiming = loadout != null && loadout.IsAiming;
            float aimSpeedScale = loadout?.CurrentDefinition != null
                ? loadout.CurrentDefinition.aimMoveSpeedScale
                : 1f;

            motor.Tick(frame, dt, _controlEnabled, aiming, aimSpeedScale);

            loadout?.Tick(frame, dt, _controlEnabled && _combatEnabled, motor.SpeedFraction);
            interactor?.Tick(frame, dt, _controlEnabled && _combatEnabled);

            float recoilRecovery = loadout?.CurrentDefinition != null
                ? loadout.CurrentDefinition.recoilRecoveryPerSecond
                : 8f;
            float aimFovScale = loadout?.CurrentDefinition != null
                ? loadout.CurrentDefinition.aimFovScale
                : 0.8f;

            cameraRig?.Tick(
                dt,
                motor.PlanarSpeed,
                motor.SpeedFraction,
                motor.IsGrounded,
                aiming,
                motor.IsSprinting,
                aimFovScale,
                recoilRecovery);
        }

        /// <summary>Convenience for the scene builder to wire everything in one call.</summary>
        public void Configure(
            PlayerMotor playerMotor,
            PlayerCameraRig rig,
            PlayerLoadout playerLoadout,
            PlayerHealth playerHealth,
            PlayerInteractor playerInteractor)
        {
            motor = playerMotor;
            cameraRig = rig;
            loadout = playerLoadout;
            health = playerHealth;
            interactor = playerInteractor;
        }
    }
}
