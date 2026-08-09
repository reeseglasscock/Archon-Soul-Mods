using UnityEngine;
using UnityEngine.InputSystem;

namespace ArchonSoulGamepad
{
    /// <summary>
    /// Turns raw stick/dpad/button state into discrete navigation intents,
    /// including hold-to-repeat. Reads devices directly, so it works even though
    /// the game defines no InputActions of its own.
    /// </summary>
    internal class GamepadReader
    {
        public float Deadzone = 0.5f;
        public float RepeatDelay = 0.4f;
        public float RepeatRate = 0.12f;

        private Vector2 _lastDir;
        private float _nextRepeat;
        private float _baseRepeatDelay = -1f;
        private float _baseRepeatRate = -1f;

        /// <summary>Slower repeat while editing so values step predictably.</summary>
        public void SetRepeatProfile(float delay, float rate)
        {
            if (_baseRepeatDelay < 0f) { _baseRepeatDelay = RepeatDelay; _baseRepeatRate = RepeatRate; }
            RepeatDelay = delay;
            RepeatRate = rate;
        }

        public void RestoreRepeatProfile()
        {
            if (_baseRepeatDelay < 0f) return;
            RepeatDelay = _baseRepeatDelay;
            RepeatRate = _baseRepeatRate;
            _baseRepeatDelay = _baseRepeatRate = -1f;
        }

        public Vector2 NavDirection { get; private set; }
        public bool NavTriggered { get; private set; }

        public bool Submit, Cancel, Prev, Next, Menu, Alt;
        public bool AnyActivity { get; private set; }

        public void Poll()
        {
            NavTriggered = false;
            NavDirection = Vector2.zero;
            Submit = Cancel = Prev = Next = Menu = Alt = false;
            AnyActivity = false;

            var gp = Gamepad.current;
            if (gp == null)
            {
                _lastDir = Vector2.zero;
                return;
            }

            Vector2 stick = gp.leftStick.ReadValue();
            Vector2 dpad = gp.dpad.ReadValue();
            Vector2 raw = dpad.sqrMagnitude > stick.sqrMagnitude ? dpad : stick;

            Vector2 dir = Vector2.zero;
            if (raw.magnitude >= Deadzone)
            {
                // Snap to the dominant axis; diagonal navigation is ambiguous in
                // layouts that mix grids and lists.
                dir = Mathf.Abs(raw.x) > Mathf.Abs(raw.y)
                    ? new Vector2(Mathf.Sign(raw.x), 0f)
                    : new Vector2(0f, Mathf.Sign(raw.y));
            }

            if (dir == Vector2.zero)
            {
                _lastDir = Vector2.zero;
            }
            else if (dir != _lastDir)
            {
                _lastDir = dir;
                _nextRepeat = Time.unscaledTime + RepeatDelay;
                NavDirection = dir;
                NavTriggered = true;
            }
            else if (Time.unscaledTime >= _nextRepeat)
            {
                _nextRepeat = Time.unscaledTime + RepeatRate;
                NavDirection = dir;
                NavTriggered = true;
            }

            Submit = gp.buttonSouth.wasPressedThisFrame;
            Cancel = gp.buttonEast.wasPressedThisFrame;
            Alt = gp.buttonWest.wasPressedThisFrame;
            Prev = gp.leftShoulder.wasPressedThisFrame;
            Next = gp.rightShoulder.wasPressedThisFrame;
            Menu = gp.startButton.wasPressedThisFrame;

            AnyActivity = NavTriggered || Submit || Cancel || Alt || Prev || Next || Menu
                          || raw.magnitude >= Deadzone
                          || gp.buttonNorth.wasPressedThisFrame;
        }

        public static bool GamepadPresent
        {
            get { return Gamepad.current != null; }
        }
    }
}
