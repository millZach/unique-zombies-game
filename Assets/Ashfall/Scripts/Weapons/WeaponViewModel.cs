using UnityEngine;

namespace Ashfall.Weapons
{
    /// <summary>
    /// The thing the player actually sees in their hands. Animation here is entirely
    /// procedural -- there are no imported clips in the slice -- which keeps the whole
    /// viewmodel reproducible from the scene builder.
    /// </summary>
    public class WeaponViewModel : MonoBehaviour
    {
        [SerializeField] private Transform muzzle;
        [SerializeField] private Transform slide;
        [SerializeField] private Transform magazine;
        [SerializeField] private ParticleSystem muzzleFlash;
        [SerializeField] private Light muzzleLight;

        [Header("Procedural animation")]
        [SerializeField] private float slideTravel = 0.055f;
        [SerializeField] private float slideReturnSpeed = 26f;
        [SerializeField] private float reloadDropDistance = 0.14f;
        [SerializeField] private float reloadTiltDegrees = 22f;
        [SerializeField] private float muzzleLightSeconds = 0.045f;

        private Vector3 _slideRest;
        private Vector3 _magazineRest;
        private Quaternion _rootRest;
        private float _slideOffset;
        private float _muzzleLightUntil;

        public Transform Muzzle => muzzle != null ? muzzle : transform;

        private void Awake()
        {
            if (slide != null) _slideRest = slide.localPosition;
            if (magazine != null) _magazineRest = magazine.localPosition;
            _rootRest = transform.localRotation;

            if (muzzleLight != null)
            {
                muzzleLight.enabled = false;
            }
        }

        public void Configure(Transform muzzleTransform, Transform slideTransform, Transform magazineTransform, ParticleSystem flash, Light light)
        {
            muzzle = muzzleTransform;
            slide = slideTransform;
            magazine = magazineTransform;
            muzzleFlash = flash;
            muzzleLight = light;
        }

        public void PlayFire(float flashScale, Color accent)
        {
            _slideOffset = 1f;

            if (muzzleFlash != null)
            {
                ParticleSystem.MainModule main = muzzleFlash.main;
                main.startColor = accent;
                main.startSizeMultiplier = Mathf.Max(0.05f, flashScale * 0.5f);
                muzzleFlash.Clear(true);
                muzzleFlash.Play(true);
            }

            if (muzzleLight != null)
            {
                muzzleLight.color = accent;
                muzzleLight.intensity = 5.5f * flashScale;
                muzzleLight.range = 6.5f * flashScale;
                muzzleLight.enabled = true;
                _muzzleLightUntil = Time.time + muzzleLightSeconds;
            }
        }

        /// <summary>
        /// Drives the reload pose. <paramref name="progress"/> is 0..1, or negative when
        /// no reload is running.
        /// </summary>
        public void Tick(float deltaTime, float reloadProgress, bool reloading)
        {
            _slideOffset = Mathf.MoveTowards(_slideOffset, 0f, slideReturnSpeed * deltaTime);

            if (slide != null)
            {
                slide.localPosition = _slideRest + new Vector3(0f, 0f, -slideTravel * _slideOffset);
            }

            if (muzzleLight != null && muzzleLight.enabled && Time.time >= _muzzleLightUntil)
            {
                muzzleLight.enabled = false;
            }

            if (reloading)
            {
                // A single sine arc reads as "drop the mag, slap a new one in" without
                // needing an authored clip.
                float arc = Mathf.Sin(Mathf.Clamp01(reloadProgress) * Mathf.PI);
                if (magazine != null)
                {
                    magazine.localPosition = _magazineRest + Vector3.down * (reloadDropDistance * arc);
                }

                transform.localRotation = _rootRest * Quaternion.Euler(reloadTiltDegrees * arc, 0f, -reloadTiltDegrees * 0.5f * arc);
            }
            else
            {
                if (magazine != null)
                {
                    magazine.localPosition = Vector3.Lerp(magazine.localPosition, _magazineRest, 1f - Mathf.Exp(-18f * deltaTime));
                }

                transform.localRotation = Quaternion.Slerp(transform.localRotation, _rootRest, 1f - Mathf.Exp(-18f * deltaTime));
            }
        }

        /// <summary>Slides the weapon up into view when equipped.</summary>
        public void ApplyEquipPose(float equipProgress)
        {
            float t = Mathf.Clamp01(equipProgress);
            float eased = 1f - (1f - t) * (1f - t);
            transform.localPosition = new Vector3(0f, -0.35f * (1f - eased), 0f);
        }
    }
}
