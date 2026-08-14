using UnityEngine;

namespace Ashfall.Player
{
    /// <summary>
    /// Owns everything the camera does that is not "where the player is standing":
    /// pitch, recoil, trauma shake, head bob, landing dip and aim-down-sights FOV.
    ///
    /// Keeping it separate from the motor means camera feel can be tuned without any
    /// risk of changing how the character actually moves.
    /// </summary>
    public class PlayerCameraRig : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform pitchPivot;
        [SerializeField] private Transform shakeSocket;
        [SerializeField] private Transform weaponSocket;
        [SerializeField] private Camera viewCamera;

        [Header("Look")]
        [SerializeField] private float minPitch = -88f;
        [SerializeField] private float maxPitch = 88f;

        [Header("Field of view")]
        [SerializeField] private float baseFieldOfView = 80f;
        [SerializeField] private float sprintFovBonus = 6f;
        [SerializeField] private float fovLerpSpeed = 9f;

        [Header("Shake")]
        [SerializeField] private float traumaDecayPerSecond = 1.85f;
        [SerializeField] private float shakePositionAmplitude = 0.085f;
        [SerializeField] private float shakeRotationAmplitude = 2.6f;
        [SerializeField] private float shakeFrequency = 26f;

        [Header("Recoil")]
        [SerializeField] private float recoilFollowSpeed = 22f;

        [Header("Head bob")]
        [SerializeField] private float bobFrequency = 8.6f;
        [SerializeField] private float bobVerticalAmplitude = 0.045f;
        [SerializeField] private float bobHorizontalAmplitude = 0.032f;
        [SerializeField] private float bobRollDegrees = 0.6f;

        [Header("Weapon sway")]
        [SerializeField] private Vector3 hipPosition = new Vector3(0.21f, -0.20f, 0.46f);
        [SerializeField] private Vector3 aimPosition = new Vector3(0f, -0.075f, 0.36f);
        [SerializeField] private float swayAmount = 0.014f;
        [SerializeField] private float swaySpeed = 9f;
        [SerializeField] private float weaponFollowSpeed = 14f;

        public Camera ViewCamera => viewCamera;
        public Transform WeaponSocket => weaponSocket;
        public Transform PitchPivot => pitchPivot;

        /// <summary>Current pitch in degrees. Yaw lives on the player body, not here.</summary>
        public float Pitch { get; private set; }

        public float Trauma { get; private set; }

        private Vector2 _recoilCurrent;
        private Vector2 _recoilTarget;
        private float _bobPhase;
        private float _landingDip;
        private float _landingDipVelocity;
        private float _kickBack;
        private Vector3 _weaponPositionCurrent;
        private Vector3 _weaponSwayCurrent;
        private float _fovCurrent;
        private float _shakeSeed;

        private void Awake()
        {
            _shakeSeed = Random.value * 1000f;
            _fovCurrent = baseFieldOfView;
            _weaponPositionCurrent = hipPosition;

            if (viewCamera != null)
            {
                viewCamera.fieldOfView = baseFieldOfView;
            }
        }

        public void Configure(Transform pivot, Transform shake, Transform weapon, Camera cam)
        {
            pitchPivot = pivot;
            shakeSocket = shake;
            weaponSocket = weapon;
            viewCamera = cam;
        }

        public void ResetRig()
        {
            Pitch = 0f;
            Trauma = 0f;
            _recoilCurrent = Vector2.zero;
            _recoilTarget = Vector2.zero;
            _bobPhase = 0f;
            _landingDip = 0f;
            _landingDipVelocity = 0f;
            _kickBack = 0f;
            _weaponPositionCurrent = hipPosition;
            _fovCurrent = baseFieldOfView;

            if (pitchPivot != null)
            {
                pitchPivot.localRotation = Quaternion.identity;
            }

            if (shakeSocket != null)
            {
                shakeSocket.localPosition = Vector3.zero;
                shakeSocket.localRotation = Quaternion.identity;
            }
        }

        /// <summary>Applies this frame's pitch input, clamped, and returns the new pitch.</summary>
        public float ApplyPitchDelta(float delta)
        {
            Pitch = Mathf.Clamp(Pitch - delta, minPitch, maxPitch);
            return Pitch;
        }

        public void AddRecoil(float vertical, float horizontal)
        {
            _recoilTarget.x += vertical;
            _recoilTarget.y += Random.Range(-horizontal, horizontal);
        }

        public void AddTrauma(float amount)
        {
            Trauma = Mathf.Clamp01(Trauma + amount);
        }

        public void AddKickBack(float amount)
        {
            _kickBack = Mathf.Min(0.35f, _kickBack + amount);
        }

        public void NotifyLanded(float impactSpeed)
        {
            float strength = Mathf.InverseLerp(2f, 16f, impactSpeed);
            _landingDip -= Mathf.Lerp(0.02f, 0.16f, strength);
            AddTrauma(strength * 0.35f);
        }

        /// <summary>
        /// Drives every camera offset for the frame.
        /// </summary>
        /// <param name="planarSpeed">Ground speed in m/s, for head bob.</param>
        /// <param name="speedFraction">planarSpeed as a fraction of sprint speed.</param>
        public void Tick(
            float deltaTime,
            float planarSpeed,
            float speedFraction,
            bool grounded,
            bool aiming,
            bool sprinting,
            float aimFovScale,
            float recoilRecoveryPerSecond)
        {
            // --- recoil: snap on, ease off -----------------------------------
            _recoilCurrent = Vector2.Lerp(_recoilCurrent, _recoilTarget, 1f - Mathf.Exp(-recoilFollowSpeed * deltaTime));
            _recoilTarget = Vector2.Lerp(_recoilTarget, Vector2.zero, 1f - Mathf.Exp(-recoilRecoveryPerSecond * deltaTime));

            // --- trauma-based shake ------------------------------------------
            Trauma = Mathf.Max(0f, Trauma - traumaDecayPerSecond * deltaTime);
            float shake = Trauma * Trauma;
            float t = Time.time * shakeFrequency + _shakeSeed;
            Vector3 shakeOffset = new Vector3(
                (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f,
                (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f,
                (Mathf.PerlinNoise(t, t) - 0.5f) * 2f) * (shake * shakePositionAmplitude);

            Vector3 shakeRotation = new Vector3(
                (Mathf.PerlinNoise(t + 11f, 0f) - 0.5f) * 2f,
                (Mathf.PerlinNoise(0f, t + 17f) - 0.5f) * 2f,
                (Mathf.PerlinNoise(t + 23f, t) - 0.5f) * 2f) * (shake * shakeRotationAmplitude);

            // --- head bob -----------------------------------------------------
            Vector3 bobOffset = Vector3.zero;
            float bobRoll = 0f;
            if (grounded && planarSpeed > 0.4f)
            {
                _bobPhase += deltaTime * bobFrequency * Mathf.Clamp(speedFraction, 0.35f, 1.4f);
                float amplitude = Mathf.Clamp01(speedFraction) * (aiming ? 0.35f : 1f);
                bobOffset = new Vector3(
                    Mathf.Sin(_bobPhase) * bobHorizontalAmplitude * amplitude,
                    -Mathf.Abs(Mathf.Cos(_bobPhase)) * bobVerticalAmplitude * amplitude,
                    0f);
                bobRoll = Mathf.Sin(_bobPhase) * bobRollDegrees * amplitude;
            }
            else
            {
                _bobPhase = Mathf.Lerp(_bobPhase, 0f, deltaTime * 6f);
            }

            // --- landing dip (critically damped spring back to zero) ----------
            _landingDip = Mathf.SmoothDamp(_landingDip, 0f, ref _landingDipVelocity, 0.16f, Mathf.Infinity, deltaTime);
            _kickBack = Mathf.Lerp(_kickBack, 0f, 1f - Mathf.Exp(-11f * deltaTime));

            // --- compose ------------------------------------------------------
            if (pitchPivot != null)
            {
                pitchPivot.localRotation = Quaternion.Euler(Pitch - _recoilCurrent.x, _recoilCurrent.y, 0f);
            }

            if (shakeSocket != null)
            {
                shakeSocket.localPosition = shakeOffset + bobOffset + new Vector3(0f, _landingDip, -_kickBack * 0.35f);
                shakeSocket.localRotation = Quaternion.Euler(shakeRotation.x, shakeRotation.y, shakeRotation.z + bobRoll);
            }

            // --- field of view --------------------------------------------------
            float targetFov = baseFieldOfView;
            if (aiming)
            {
                targetFov = baseFieldOfView * aimFovScale;
            }
            else if (sprinting && planarSpeed > 1f)
            {
                targetFov = baseFieldOfView + sprintFovBonus;
            }

            _fovCurrent = Mathf.Lerp(_fovCurrent, targetFov, 1f - Mathf.Exp(-fovLerpSpeed * deltaTime));
            if (viewCamera != null)
            {
                viewCamera.fieldOfView = _fovCurrent;
            }

            TickWeaponSocket(deltaTime, aiming, sprinting, planarSpeed);
        }

        private void TickWeaponSocket(float deltaTime, bool aiming, bool sprinting, float planarSpeed)
        {
            if (weaponSocket == null)
            {
                return;
            }

            Vector3 targetPosition = aiming ? aimPosition : hipPosition;

            // Drop the weapon out of the sightline while sprinting so the run reads.
            if (sprinting && !aiming && planarSpeed > 2f)
            {
                targetPosition += new Vector3(0.045f, -0.075f, -0.06f);
            }

            targetPosition.z -= _kickBack;

            _weaponPositionCurrent = Vector3.Lerp(
                _weaponPositionCurrent,
                targetPosition,
                1f - Mathf.Exp(-weaponFollowSpeed * deltaTime));

            Vector3 swayTarget = new Vector3(
                -_recoilCurrent.y * swayAmount * 2.2f,
                _recoilCurrent.x * swayAmount * 1.6f,
                0f);
            _weaponSwayCurrent = Vector3.Lerp(
                _weaponSwayCurrent,
                swayTarget,
                1f - Mathf.Exp(-swaySpeed * deltaTime));

            weaponSocket.localPosition = _weaponPositionCurrent + _weaponSwayCurrent;

            float tilt = sprinting && !aiming && planarSpeed > 2f ? -14f : 0f;
            weaponSocket.localRotation = Quaternion.Slerp(
                weaponSocket.localRotation,
                Quaternion.Euler(tilt, 0f, tilt * 0.4f),
                1f - Mathf.Exp(-weaponFollowSpeed * deltaTime));
        }

        public void SetEyeHeight(float height)
        {
            if (pitchPivot != null)
            {
                Vector3 p = pitchPivot.localPosition;
                p.y = height;
                pitchPivot.localPosition = p;
            }
        }
    }
}
