using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ArchonSoulGamepad
{
    /// <summary>
    /// Single source of truth for the synthetic pointer the mod drives.
    /// The player never sees a cursor: this position is slaved to whatever
    /// element currently has focus, so the game's existing mouse-based drag,
    /// hover and targeting code keeps working untouched.
    /// </summary>
    internal static class VirtualPointer
    {
        public static bool Active;
        public static Vector2 Position;
        public static bool LeftDown;
        public static bool RightDown;

        private static bool _prevLeft;
        private static bool _prevRight;
        private static int _leftPulse;
        private static int _rightPulse;

        public static bool LeftPressedThisFrame { get; private set; }
        public static bool LeftReleasedThisFrame { get; private set; }
        public static bool RightPressedThisFrame { get; private set; }
        public static bool RightReleasedThisFrame { get; private set; }

        /// <summary>
        /// Holds a synthetic button for two frames: long enough for the game's
        /// per-frame GetMouseButtonDown polls (spell targeting cancel) to observe
        /// a clean press followed by a release.
        /// </summary>
        public static void PulseRight() { _rightPulse = 2; }
        public static void PulseLeft() { _leftPulse = 2; }

        public static void BeginFrame()
        {
            if (_leftPulse > 0) { LeftDown = true; _leftPulse--; }
            else LeftDown = false;

            if (_rightPulse > 0) { RightDown = true; _rightPulse--; }
            else RightDown = false;

            LeftPressedThisFrame = LeftDown && !_prevLeft;
            LeftReleasedThisFrame = !LeftDown && _prevLeft;
            RightPressedThisFrame = RightDown && !_prevRight;
            RightReleasedThisFrame = !RightDown && _prevRight;
            _prevLeft = LeftDown;
            _prevRight = RightDown;
        }

        /// <summary>
        /// Pushes the synthetic position into the new Input System's Mouse device.
        /// DiceDrag.Update() reads Mouse.current.position directly, so without this
        /// a picked-up die would snap back to the real hardware cursor.
        /// </summary>
        public static void PushToInputSystem()
        {
            if (!Active) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            try
            {
                InputState.Change(mouse.position, Position);
            }
            catch
            {
                // Device may be mid-reset; harmless to skip a frame.
            }
        }

        public static void Reset()
        {
            LeftDown = RightDown = false;
            _prevLeft = _prevRight = false;
            _leftPulse = _rightPulse = 0;
            LeftPressedThisFrame = LeftReleasedThisFrame = false;
            RightPressedThisFrame = RightReleasedThisFrame = false;
        }
    }
}
