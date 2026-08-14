using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ashfall.InputLayer
{
    /// <summary>
    /// Fire-and-forget controller haptics. Safe to call every shot: if no pad is
    /// connected, or the Input System is absent, every call is a no-op.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    public class GamepadRumble : MonoBehaviour
    {
        private static GamepadRumble _instance;

        private float _stopAt;
        private bool _running;

        public static GamepadRumble Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<GamepadRumble>();
                    if (_instance == null)
                    {
                        var go = new GameObject("Ashfall Rumble (auto)");
                        _instance = go.AddComponent<GamepadRumble>();
                        _instance._autoCreated = true;
                        DontDestroyOnLoad(go);
                    }
                }

                return _instance;
            }
        }

        /// <summary>True when this instance was conjured on demand rather than authored.</summary>
        private bool _autoCreated;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                if (_instance._autoCreated)
                {
                    Destroy(_instance.gameObject);
                }
                else
                {
                    // Component only. See the same guard in AshfallInput: this may share
                    // its GameObject with the rest of the systems.
                    Destroy(this);
                    return;
                }
            }

            _instance = this;
        }

        public static void Pulse(float lowFrequency, float highFrequency, float seconds)
        {
            Instance.PlayPulse(lowFrequency, highFrequency, seconds);
        }

        public void PlayPulse(float lowFrequency, float highFrequency, float seconds)
        {
#if ENABLE_INPUT_SYSTEM
            var pad = Gamepad.current;
            if (pad == null)
            {
                return;
            }

            pad.SetMotorSpeeds(Mathf.Clamp01(lowFrequency), Mathf.Clamp01(highFrequency));
            // Extend rather than truncate: a shotgun blast landing during a hit-rumble
            // should feel like one longer event, not cut the previous one short.
            _stopAt = Mathf.Max(_stopAt, Time.unscaledTime + Mathf.Max(0.01f, seconds));
            _running = true;
#endif
        }

        public static void StopAll()
        {
#if ENABLE_INPUT_SYSTEM
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
#endif
            if (_instance != null)
            {
                _instance._running = false;
                _instance._stopAt = 0f;
            }
        }

        private void Update()
        {
            if (!_running || Time.unscaledTime < _stopAt)
            {
                return;
            }

            _running = false;
#if ENABLE_INPUT_SYSTEM
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
#endif
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
#endif
        }

        private void OnApplicationQuit()
        {
#if ENABLE_INPUT_SYSTEM
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
#endif
        }
    }
}
