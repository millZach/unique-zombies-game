using UnityEngine;

namespace Ashfall.InputLayer
{
    /// <summary>Which physical device most recently produced input, for HUD prompts.</summary>
    public enum InputScheme
    {
        KeyboardMouse,
        Gamepad
    }

    /// <summary>
    /// One frame of fully-resolved player intent. Everything downstream of input reads
    /// this struct, which keeps the controller/keyboard difference in exactly one place.
    /// </summary>
    public struct InputFrame
    {
        public Vector2 Move;

        /// <summary>Mouse delta for this frame. Already frame-quantised: do NOT scale by deltaTime.</summary>
        public Vector2 LookDelta;

        /// <summary>Stick look in [-1,1]. A rate: DO scale by deltaTime.</summary>
        public Vector2 LookStick;

        public bool FireHeld;
        public bool FirePressed;
        public bool AimHeld;
        public bool SprintHeld;
        public bool CrouchHeld;
        public bool CrouchPressed;
        public bool JumpPressed;
        public bool ReloadPressed;
        public bool InteractHeld;
        public bool InteractPressed;
        public bool PausePressed;
        public bool RestartPressed;
        public bool NextWeaponPressed;
        public bool PreviousWeaponPressed;

        /// <summary>-1, 0 or +1 from the direct weapon-slot keys / d-pad.</summary>
        public int DirectWeaponSlot;

        public Vector2 MenuNavigate;
        public bool MenuSubmitPressed;
        public bool MenuCancelPressed;

        public InputScheme Scheme;

        public static InputFrame Empty => default;
    }
}
