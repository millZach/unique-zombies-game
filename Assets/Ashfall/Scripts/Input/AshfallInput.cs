using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ashfall.InputLayer
{
    /// <summary>
    /// The project's only input entry point.
    ///
    /// Bindings are built in code rather than loaded from an .inputactions asset. That
    /// removes a whole class of failure -- a missing GUID, a stale generated wrapper, a
    /// package that did not resolve -- and it means the scene builder has nothing to
    /// wire up. When the Input System package is present (the normal case) both
    /// keyboard/mouse and any standard Xbox/PlayStation-style gamepad work with no
    /// setup. If it is ever absent the legacy path below keeps the project compiling
    /// and keyboard/mouse playable.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class AshfallInput : MonoBehaviour
    {
        private static AshfallInput _instance;

        public static AshfallInput Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<AshfallInput>();
                    if (_instance == null)
                    {
                        var go = new GameObject("Ashfall Input (auto)");
                        _instance = go.AddComponent<AshfallInput>();
                        _instance._autoCreated = true;
                    }
                }

                return _instance;
            }
        }

        [Header("Look")]
        [Tooltip("Degrees of yaw per pixel of mouse movement.")]
        [SerializeField, Range(0.02f, 0.6f)] private float mouseSensitivity = 0.14f;

        [Tooltip("Degrees per second at full stick deflection.")]
        [SerializeField, Range(60f, 600f)] private float stickSensitivity = 260f;

        [SerializeField, Range(0f, 1f)] private float stickLookCurve = 0.45f;
        [SerializeField] private bool invertLookY;

        public float MouseSensitivity { get => mouseSensitivity; set => mouseSensitivity = value; }
        public float StickSensitivity { get => stickSensitivity; set => stickSensitivity = value; }
        public bool InvertLookY { get => invertLookY; set => invertLookY = value; }

        public InputFrame Frame { get; private set; }
        public InputScheme LastScheme { get; private set; } = InputScheme.KeyboardMouse;

        /// <summary>True when the Input System package supplied this frame's data.</summary>
        public bool UsingInputSystem { get; private set; }

        private bool _cursorLocked;

#if ENABLE_INPUT_SYSTEM
        private InputActionMap _gameplayMap;
        private InputAction _move, _look, _lookStick, _fire, _aim, _sprint, _crouch, _jump;
        private InputAction _reload, _interact, _pause, _restart, _nextWeapon, _prevWeapon;
        private InputAction _slot1, _slot2, _slot3, _menuNav, _menuSubmit, _menuCancel;
#endif

        /// <summary>True when the bootstrapper conjured this instance rather than a scene.</summary>
        private bool _autoCreated;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                if (_instance._autoCreated)
                {
                    // A scene-authored input rig outranks the bootstrap fallback. The old
                    // object is destroyed whole because nothing else lives on it.
                    Destroy(_instance.gameObject);
                }
                else
                {
                    // Destroy only this component, never the GameObject. This behaviour
                    // shares its object with the rest of the game's systems, and tearing
                    // the object down here took the GameDirector with it -- which turned
                    // "press Play from another scene" into an empty, silent game.
                    Destroy(this);
                    return;
                }
            }

            _instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            BuildActions();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

#if ENABLE_INPUT_SYSTEM
            _gameplayMap?.Disable();
            _gameplayMap?.Dispose();
            _gameplayMap = null;
#endif
        }

        private void Update()
        {
            Frame = Sample();
        }

        // ------------------------------------------------------------------
        // Cursor
        // ------------------------------------------------------------------

        public void SetCursorLocked(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>Re-applies the lock; Unity drops it on focus loss and alt-tab.</summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && _cursorLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // ------------------------------------------------------------------
        // Input System path
        // ------------------------------------------------------------------

        private void BuildActions()
        {
#if ENABLE_INPUT_SYSTEM
            _gameplayMap = new InputActionMap("AshfallGameplay");

            _move = _gameplayMap.AddAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick").WithProcessor("stickDeadzone(min=0.16,max=0.94)");

            _look = _gameplayMap.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            _look.AddBinding("<Pointer>/delta");

            _lookStick = _gameplayMap.AddAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick");
            _lookStick.ApplyBindingOverride(new InputBinding
            {
                overridePath = "<Gamepad>/rightStick",
                overrideProcessors = "stickDeadzone(min=0.14,max=0.95)"
            });

            _fire = Button("Fire", "<Mouse>/leftButton", "<Gamepad>/rightTrigger", "<Keyboard>/leftCtrl");
            _aim = Button("Aim", "<Mouse>/rightButton", "<Gamepad>/leftTrigger");
            _sprint = Button("Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            _crouch = Button("Crouch", "<Keyboard>/c", "<Gamepad>/buttonEast", "<Keyboard>/leftAlt");
            _jump = Button("Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            _reload = Button("Reload", "<Keyboard>/r", "<Gamepad>/buttonWest");
            _interact = Button("Interact", "<Keyboard>/e", "<Gamepad>/buttonNorth");
            _pause = Button("Pause", "<Keyboard>/escape", "<Gamepad>/start");
            _restart = Button("Restart", "<Keyboard>/f5", "<Gamepad>/select");
            _nextWeapon = Button("NextWeapon", "<Keyboard>/q", "<Gamepad>/rightShoulder");
            _nextWeapon.AddBinding("<Mouse>/scroll/up");
            _prevWeapon = Button("PreviousWeapon", "<Gamepad>/leftShoulder");
            _prevWeapon.AddBinding("<Mouse>/scroll/down");
            _slot1 = Button("Slot1", "<Keyboard>/1");
            _slot2 = Button("Slot2", "<Keyboard>/2");
            _slot3 = Button("Slot3", "<Keyboard>/3");

            _menuNav = _gameplayMap.AddAction("MenuNavigate", InputActionType.Value);
            _menuNav.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _menuNav.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            _menuNav.AddCompositeBinding("2DVector")
                .With("Up", "<Gamepad>/dpad/up")
                .With("Down", "<Gamepad>/dpad/down")
                .With("Left", "<Gamepad>/dpad/left")
                .With("Right", "<Gamepad>/dpad/right");
            _menuNav.AddBinding("<Gamepad>/leftStick").WithProcessor("stickDeadzone(min=0.5,max=0.95)");

            _menuSubmit = Button("MenuSubmit", "<Keyboard>/enter", "<Gamepad>/buttonSouth", "<Keyboard>/space");
            _menuCancel = Button("MenuCancel", "<Keyboard>/escape", "<Gamepad>/buttonEast");

            _gameplayMap.Enable();
            UsingInputSystem = true;
#else
            UsingInputSystem = false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private InputAction Button(string name, params string[] paths)
        {
            var action = _gameplayMap.AddAction(name, InputActionType.Button);
            for (int i = 0; i < paths.Length; i++)
            {
                action.AddBinding(paths[i]);
            }

            return action;
        }
#endif

        private InputFrame Sample()
        {
            var f = InputFrame.Empty;

#if ENABLE_INPUT_SYSTEM
            if (_gameplayMap != null)
            {
                f.Move = Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);
                f.LookDelta = _look.ReadValue<Vector2>();
                f.LookStick = _lookStick.ReadValue<Vector2>();

                f.FireHeld = _fire.IsPressed();
                f.FirePressed = _fire.WasPressedThisFrame();
                f.AimHeld = _aim.IsPressed();
                f.SprintHeld = _sprint.IsPressed();
                f.CrouchHeld = _crouch.IsPressed();
                f.CrouchPressed = _crouch.WasPressedThisFrame();
                f.JumpPressed = _jump.WasPressedThisFrame();
                f.ReloadPressed = _reload.WasPressedThisFrame();
                f.InteractHeld = _interact.IsPressed();
                f.InteractPressed = _interact.WasPressedThisFrame();
                f.PausePressed = _pause.WasPressedThisFrame();
                f.RestartPressed = _restart.WasPressedThisFrame();
                f.NextWeaponPressed = _nextWeapon.WasPressedThisFrame();
                f.PreviousWeaponPressed = _prevWeapon.WasPressedThisFrame();

                f.DirectWeaponSlot = -1;
                if (_slot1.WasPressedThisFrame()) f.DirectWeaponSlot = 0;
                else if (_slot2.WasPressedThisFrame()) f.DirectWeaponSlot = 1;
                else if (_slot3.WasPressedThisFrame()) f.DirectWeaponSlot = 2;

                f.MenuNavigate = _menuNav.ReadValue<Vector2>();
                f.MenuSubmitPressed = _menuSubmit.WasPressedThisFrame();
                f.MenuCancelPressed = _menuCancel.WasPressedThisFrame();

                UpdateScheme(f);
                f.Scheme = LastScheme;
                return f;
            }
#endif

            return SampleLegacy();
        }

#if ENABLE_INPUT_SYSTEM
        private void UpdateScheme(in InputFrame f)
        {
            // Only flip the prompt scheme on a decisive signal so a resting stick or a
            // one-pixel mouse jitter does not make the HUD legend flicker.
            var pad = Gamepad.current;
            if (pad != null)
            {
                bool padActive = f.LookStick.sqrMagnitude > 0.09f
                                 || pad.leftStick.ReadValue().sqrMagnitude > 0.09f
                                 || pad.buttonSouth.wasPressedThisFrame
                                 || pad.buttonEast.wasPressedThisFrame
                                 || pad.buttonWest.wasPressedThisFrame
                                 || pad.buttonNorth.wasPressedThisFrame
                                 || pad.rightTrigger.ReadValue() > 0.3f
                                 || pad.leftTrigger.ReadValue() > 0.3f
                                 || pad.startButton.wasPressedThisFrame
                                 || pad.dpad.ReadValue().sqrMagnitude > 0.25f;
                if (padActive)
                {
                    LastScheme = InputScheme.Gamepad;
                    return;
                }
            }

            bool kbmActive = f.LookDelta.sqrMagnitude > 4f
                             || f.Move.sqrMagnitude > 0.01f
                             || (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                             || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
            if (kbmActive)
            {
                LastScheme = InputScheme.KeyboardMouse;
            }
        }
#endif

        // ------------------------------------------------------------------
        // Legacy fallback -- only compiled when the Input System package is missing,
        // so the project still builds and plays on keyboard/mouse in that case.
        // ------------------------------------------------------------------

        private InputFrame SampleLegacy()
        {
            var f = InputFrame.Empty;
#if ENABLE_LEGACY_INPUT_MANAGER && !ENABLE_INPUT_SYSTEM
            f.Move = new Vector2(
                (UnityEngine.Input.GetKey(KeyCode.D) ? 1f : 0f) - (UnityEngine.Input.GetKey(KeyCode.A) ? 1f : 0f),
                (UnityEngine.Input.GetKey(KeyCode.W) ? 1f : 0f) - (UnityEngine.Input.GetKey(KeyCode.S) ? 1f : 0f));
            f.Move = Vector2.ClampMagnitude(f.Move, 1f);

            f.LookDelta = new Vector2(
                UnityEngine.Input.GetAxisRaw("Mouse X") * 8f,
                UnityEngine.Input.GetAxisRaw("Mouse Y") * 8f);

            f.FireHeld = UnityEngine.Input.GetMouseButton(0);
            f.FirePressed = UnityEngine.Input.GetMouseButtonDown(0);
            f.AimHeld = UnityEngine.Input.GetMouseButton(1);
            f.SprintHeld = UnityEngine.Input.GetKey(KeyCode.LeftShift);
            f.CrouchHeld = UnityEngine.Input.GetKey(KeyCode.C);
            f.CrouchPressed = UnityEngine.Input.GetKeyDown(KeyCode.C);
            f.JumpPressed = UnityEngine.Input.GetKeyDown(KeyCode.Space);
            f.ReloadPressed = UnityEngine.Input.GetKeyDown(KeyCode.R);
            f.InteractHeld = UnityEngine.Input.GetKey(KeyCode.E);
            f.InteractPressed = UnityEngine.Input.GetKeyDown(KeyCode.E);
            f.PausePressed = UnityEngine.Input.GetKeyDown(KeyCode.Escape);
            f.RestartPressed = UnityEngine.Input.GetKeyDown(KeyCode.F5);
            f.NextWeaponPressed = UnityEngine.Input.GetKeyDown(KeyCode.Q);

            f.DirectWeaponSlot = -1;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1)) f.DirectWeaponSlot = 0;
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha2)) f.DirectWeaponSlot = 1;
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha3)) f.DirectWeaponSlot = 2;

            f.MenuNavigate = f.Move;
            f.MenuSubmitPressed = UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.Space);
            f.MenuCancelPressed = UnityEngine.Input.GetKeyDown(KeyCode.Escape);
#else
            f.DirectWeaponSlot = -1;
#endif
            f.Scheme = InputScheme.KeyboardMouse;
            return f;
        }

        /// <summary>Yaw/pitch degrees for this frame, combining mouse and stick.</summary>
        public Vector2 ResolveLookDegrees(in InputFrame frame, float deltaTime)
        {
            Vector2 fromMouse = frame.LookDelta * mouseSensitivity;

            Vector2 stick = frame.LookStick;
            float magnitude = Mathf.Clamp01(stick.magnitude);
            if (magnitude > 0.0001f)
            {
                // Ease the low end so fine aim on a stick is actually possible, then let
                // it open up to the full turn rate near the rim.
                float shaped = Mathf.Lerp(magnitude, magnitude * magnitude * magnitude, stickLookCurve);
                stick = stick.normalized * shaped;
            }

            Vector2 fromStick = stick * (stickSensitivity * deltaTime);

            Vector2 total = fromMouse + fromStick;
            if (invertLookY)
            {
                total.y = -total.y;
            }

            return total;
        }
    }
}
