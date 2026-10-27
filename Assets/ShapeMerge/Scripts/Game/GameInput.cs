using UnityEngine;
using UnityEngine.InputSystem;

namespace ShapeMerge
{
    /// <summary>
    /// Unified input for mouse, touch, keyboard and gamepad. One "fire" button that is held to
    /// charge, a pointer to aim with, and a horizontal axis for non-pointer devices.
    /// </summary>
    public sealed class GameInput
    {
        public Vector2 PointerWorld { get; private set; }
        public bool UsingPointer { get; private set; } = true;
        public bool Down { get; private set; }
        public bool Pressed { get; private set; }
        public bool Released { get; private set; }
        public bool Cancel { get; private set; }
        public bool Restart { get; private set; }
        public float Move { get; private set; }

        /// <summary>When set, a bot drives the pointer instead of real devices (see AutoplayCapture).</summary>
        public static bool AutopilotActive;
        public static Vector2 AutopilotPointer;
        public static bool AutopilotDown;

        Vector2 lastScreen;
        bool wasDown;

        public void Poll(Camera cam)
        {
            bool down = false;
            Cancel = false;
            Restart = false;
            Move = 0f;

            if (AutopilotActive)
            {
                UsingPointer = true;
                PointerWorld = AutopilotPointer;
                Down = AutopilotDown;
                Pressed = Down && !wasDown;
                Released = !Down && wasDown;
                wasDown = Down;
                return;
            }

            Pointer pointer = Pointer.current;
            if (pointer != null)
            {
                Vector2 screen = pointer.position.ReadValue();
                if ((screen - lastScreen).sqrMagnitude > 0.25f || pointer.press.wasPressedThisFrame) UsingPointer = true;
                lastScreen = screen;
                Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
                PointerWorld = new Vector2(world.x, world.y);
                down |= pointer.press.isPressed;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame) Cancel = true;

            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                float move = 0f;
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) move -= 1f;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) move += 1f;
                if (move != 0f) { Move = move; UsingPointer = false; }
                if (kb.spaceKey.isPressed || kb.enterKey.isPressed)
                {
                    down = true;
                    if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) UsingPointer = false;
                }
                if (kb.escapeKey.wasPressedThisFrame) Cancel = true;
                if (kb.rKey.wasPressedThisFrame) Restart = true;
            }

            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                float x = pad.leftStick.ReadValue().x + pad.dpad.ReadValue().x;
                if (Mathf.Abs(x) > 0.2f) { Move = Mathf.Clamp(x, -1f, 1f); UsingPointer = false; }
                if (pad.buttonSouth.isPressed || pad.rightTrigger.ReadValue() > 0.35f) { down = true; UsingPointer = false; }
                if (pad.buttonEast.wasPressedThisFrame) Cancel = true;
                if (pad.startButton.wasPressedThisFrame) Restart = true;
            }

            Down = down;
            Pressed = down && !wasDown;
            Released = !down && wasDown;
            wasDown = down;
        }
    }
}
