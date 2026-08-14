using UnityEngine;
using Ashfall.Core;

namespace Ashfall.World
{
    /// <summary>
    /// Marks a GameObject as belonging to a range of map phases. The phase controller
    /// switches these on and off wholesale, which is how large chunks of the station --
    /// collapsed walkways, storm debris, sealed hoardings -- appear and disappear
    /// without any bespoke code per prop.
    /// </summary>
    public class PhaseElement : MonoBehaviour
    {
        [SerializeField] private MapPhase firstPhase = MapPhase.Standby;
        [SerializeField] private MapPhase lastPhase = MapPhase.Meridian;
        [SerializeField] private bool invert;

        [Header("Transition")]
        [SerializeField] private bool scaleIn = true;
        [SerializeField] private float scaleInSeconds = 0.7f;

        private Vector3 _restScale = Vector3.one;
        private float _scaleTimer = -1f;
        private bool _restScaleCaptured;

        public MapPhase FirstPhase => firstPhase;
        public MapPhase LastPhase => lastPhase;

        private void Awake()
        {
            CaptureRestScale();
        }

        private void CaptureRestScale()
        {
            if (_restScaleCaptured)
            {
                return;
            }

            _restScale = transform.localScale;
            _restScaleCaptured = true;
        }

        public void Configure(MapPhase first, MapPhase last, bool invertRange = false)
        {
            firstPhase = first;
            lastPhase = last;
            invert = invertRange;
        }

        public bool ShouldBeActive(MapPhase phase)
        {
            bool inRange = phase >= firstPhase && phase <= lastPhase;
            return invert ? !inRange : inRange;
        }

        public void ApplyPhase(MapPhase phase, bool instant)
        {
            CaptureRestScale();

            bool active = ShouldBeActive(phase);
            bool wasActive = gameObject.activeSelf;

            if (active && !wasActive)
            {
                gameObject.SetActive(true);
                if (scaleIn && !instant)
                {
                    _scaleTimer = 0f;
                    transform.localScale = new Vector3(_restScale.x, _restScale.y * 0.02f, _restScale.z);
                }
                else
                {
                    transform.localScale = _restScale;
                }
            }
            else if (!active)
            {
                transform.localScale = _restScale;
                gameObject.SetActive(false);
            }
            else if (instant)
            {
                transform.localScale = _restScale;
            }
        }

        private void Update()
        {
            if (_scaleTimer < 0f)
            {
                return;
            }

            _scaleTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_scaleTimer / Mathf.Max(0.05f, scaleInSeconds));

            // Slight overshoot so a wall of debris slamming into place has weight.
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float overshoot = Mathf.Sin(t * Mathf.PI) * 0.12f;
            transform.localScale = new Vector3(
                _restScale.x * (1f + overshoot * 0.4f),
                _restScale.y * Mathf.Lerp(0.02f, 1f, eased) * (1f + overshoot),
                _restScale.z * (1f + overshoot * 0.4f));

            if (t >= 1f)
            {
                _scaleTimer = -1f;
                transform.localScale = _restScale;
            }
        }
    }
}
