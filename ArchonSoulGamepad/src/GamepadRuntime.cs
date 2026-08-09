using UnityEngine;
using UnityEngine.EventSystems;

namespace ArchonSoulGamepad
{
    /// <summary>
    /// Per-frame driver: reads the pad, moves focus, keeps the synthetic pointer
    /// glued to the focused element, and translates buttons into the pointer
    /// events the game already understands.
    /// </summary>
    internal class GamepadRuntime : MonoBehaviour
    {
        private readonly GamepadReader _pad = new GamepadReader();
        private readonly FocusEngine _focus = new FocusEngine();
        private readonly FocusHighlighter _highlight = new FocusHighlighter();

        public FocusEngine FocusEngineRef { get { return _focus; } }

        private bool _gamepadMode;
        private bool _wasCarrying;
        private float _reacquireAt;
        private bool _autoEnable = true;
        private bool _mouseTookOver;
        private Vector2? _lastPushed;
        private float _diagAt;
        private bool _engaged;
        private GameObject _engagedTarget;
        private float _lastPadActivity;
        private float _mouseMoveAccum;
        private GameObject _focusBeforeExit;
        private GameObject _lastFocused;
        private bool _selfTest;
        private bool _selfTestDone;
        private float _selfTestAt;
        private float _activationLockUntil;
        private GameObject _confirmTarget;
        private float _confirmUntil;
        private int _lastCandidateCount = -1;
        private bool _warnedNoGamepad;

        /// <summary>
        /// Steam Input can capture the pad and present it as mouse+keyboard, in
        /// which case the game sees no gamepad at all and this mod stays dormant.
        /// That looks exactly like "my controller became a mouse", so say so.
        /// </summary>
        private void WarnNoGamepadOnce()
        {
            if (_warnedNoGamepad) return;
            if (Time.unscaledTime < 12f) return;
            _warnedNoGamepad = true;

            Plugin.LogWarn("no gamepad detected - controller navigation is INACTIVE.");
            Plugin.LogWarn("If your pad is connected, Steam Input is likely capturing it. " +
                           "In Steam: right-click Archon Soul > Properties > Controller > " +
                           "set 'Override for Archon Soul' to 'Disable Steam Input'.");
            DebugHarness.LogDevices();
        }

        public void Configure(float deadzone, float repeatDelay, float repeatRate,
                              Color color, float thickness, bool autoEnable, bool selfTest)
        {
            _pad.Deadzone = deadzone;
            _pad.RepeatDelay = repeatDelay;
            _pad.RepeatRate = repeatRate;
            _autoEnable = autoEnable;
            _selfTest = selfTest;
            _selfTestAt = Time.unscaledTime + 20f;
            _highlight.Configure(color, thickness);
        }

        private void Update()
        {
            VirtualPointer.BeginFrame();
            _pad.Poll();
            if (_pad.AnyActivity) _lastPadActivity = Time.unscaledTime;

            if (!GamepadReader.GamepadPresent)
            {
                if (_gamepadMode) ExitGamepadMode();
                WarnNoGamepadOnce();
                return;
            }
            _warnedNoGamepad = true;

            // Last input device wins, but never while a setting is engaged and never
            // straight after controller input.
            if (_gamepadMode && !_engaged && RealMouseMoved() && !GameBridge.IsCarryingDice())
            {
                _mouseTookOver = true;
                ExitGamepadMode();
                return;
            }

            if (!_gamepadMode)
            {
                bool wake = _pad.AnyActivity || (_autoEnable && !_mouseTookOver);
                if (!wake) return;
                if (_pad.AnyActivity) _mouseTookOver = false;
                EnterGamepadMode();
            }

            if (EventSystem.current == null)
            {
                _highlight.Hide();
                return;
            }

            bool carrying = GameBridge.IsCarryingDice();

            // Entering or leaving a carry changes what is worth focusing,
            // so force an immediate rescan rather than waiting for the timer.
            bool carryChanged = carrying != _wasCarrying;
            _wasCarrying = carrying;

            _focus.Rescan(includeDiceSlots: carrying, force: carryChanged);

            if (carryChanged && carrying)
                _focus.AcquirePreferred(_focus.Center, preferDiceSlots: true);

            if (_focus.Focused == null && Time.unscaledTime >= _reacquireAt)
            {
                _reacquireAt = Time.unscaledTime + 0.2f;
                _focus.AcquireNearest(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            }

            if (_pad.NavTriggered)
            {
                int dir = _pad.NavDirection.x > 0.5f ? 1 : (_pad.NavDirection.x < -0.5f ? -1 : 0);

                if (_engaged)
                {
                    // Only horizontal input reaches the value; vertical is swallowed
                    // so the setting under edit cannot change out from under you.
                    if (dir != 0 && GameBridge.AdjustWidget(_engagedTarget, dir))
                        Plugin.LogDiag("adjust '" + _engagedTarget.name + "' -> " +
                                       GameBridge.DescribeWidgetValue(_engagedTarget));
                }
                else
                {
                    _focus.Move(_pad.NavDirection);
                }
            }

            if (_engaged && (_engagedTarget == null || !_engagedTarget.activeInHierarchy))
                Disengage("target-gone");

            if (_focus.Focused != _lastFocused)
            {
                _lastFocused = _focus.Focused;
                _confirmTarget = null;
            }

            SuppressUiSelection();
            SyncPointer();
            HandleButtons(carrying);
            DrawHighlight();
            Diagnostics(carrying);
        }

        private void Diagnostics(bool carrying)
        {
            // Any change to the candidate set means the screen moved. Log the full
            // set (this is what pinpoints unexpected controls such as a quit button
            // still being reachable) and briefly refuse activations.
            if (_focus.CandidateCount != _lastCandidateCount)
            {
                _lastCandidateCount = _focus.CandidateCount;
                _activationLockUntil = Time.unscaledTime + 0.35f;
                _confirmTarget = null;
                Plugin.LogDiag("screen changed -> [" + _focus.DescribeCandidates() + "]");
            }

            if (_selfTest && !_selfTestDone && Time.unscaledTime > _selfTestAt)
            {
                _selfTestDone = true;
                RunNavigationSelfTest();
            }

            if (Time.unscaledTime < _diagAt) return;
            _diagAt = Time.unscaledTime + 3f;

            Plugin.LogDiag(string.Format(
                "focus='{0}' candidates={1} carrying={2} targeting={3} pos={4}",
                _focus.Focused != null ? _focus.Focused.name : "<none>",
                _focus.CandidateCount, carrying, GameBridge.IsTargeting(), _focus.Center));
        }

        /// <summary>
        /// Walks focus through the current screen and logs the order, so navigation
        /// quality can be checked on real screens without a human at the controller.
        /// </summary>
        private void RunNavigationSelfTest()
        {
            Plugin.LogInfo("=== navigation self-test: " + _focus.CandidateCount + " candidates ===");
            LogWalk("DOWN", Vector2.down, 7);
            LogWalk("UP", Vector2.up, 7);
            LogWalk("RIGHT", Vector2.right, 5);
            LogWalk("LEFT", Vector2.left, 5);
            Plugin.LogInfo("=== self-test complete ===");
        }

        private void LogWalk(string label, Vector2 dir, int steps)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(label).Append(": ");
            sb.Append(_focus.Focused != null ? _focus.Focused.name : "<none>");

            for (int i = 0; i < steps; i++)
            {
                if (!_focus.Move(dir)) { sb.Append(" -> [stop]"); break; }
                sb.Append(" -> ").Append(_focus.Focused != null ? _focus.Focused.name : "<none>");
            }

            Plugin.LogInfo(sb.ToString());
        }

        /// <summary>
        /// The game's UI module carries Unity's default gamepad bindings, so A also
        /// fires uGUI "Submit" on whatever is still selected — and a synthetic click
        /// leaves its target selected. That made A re-activate a previous button
        /// (reopening screens, flipping settings) on top of our own handling.
        /// Clearing the selection each frame removes that second, invisible path.
        /// Text fields are left alone so typing still works.
        /// </summary>
        private void SuppressUiSelection()
        {
            var es = EventSystem.current;
            if (es == null) return;

            var sel = es.currentSelectedGameObject;
            if (sel == null) return;

            var comps = sel.GetComponents<MonoBehaviour>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                if (c.GetType().Name.IndexOf("InputField", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
            }

            es.SetSelectedGameObject(null);
        }

        private void SyncPointer()
        {
            if (_focus.Focused == null) return;

            VirtualPointer.Position = _focus.Center;
            VirtualPointer.PushToInputSystem();
            _lastPushed = _focus.Center;

            // Hover must track focus continuously: the game resolves dice drops and
            // unit targeting from whatever it believes the pointer is over.
            PointerDispatcher.SetHover(_focus.Focused, _focus.Center);
        }

        /// <summary>
        /// Real movement is detected from the hardware delta rather than by
        /// comparing positions: we overwrite the position every frame, and changing
        /// Window Mode or Resolution warps the OS cursor, which previously looked
        /// like the player had grabbed the mouse and dropped controller focus.
        /// </summary>
        private bool RealMouseMoved()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return false;

            // While the pad is in use the mouse is ignored outright.
            if (Time.unscaledTime - _lastPadActivity < 1.5f)
            {
                _mouseMoveAccum = 0f;
                return false;
            }

            Vector2 d;
            try { d = mouse.delta.ReadValue(); }
            catch { return false; }

            _mouseMoveAccum = Mathf.Max(0f, _mouseMoveAccum - Time.unscaledDeltaTime * 60f) + d.magnitude;

            if (_mouseMoveAccum > 90f)
            {
                _mouseMoveAccum = 0f;
                return true;
            }
            return false;
        }

        private void HandleButtons(bool carrying)
        {
            var target = _focus.Focused;

            if (_pad.Submit)
            {
                if (_engaged)
                {
                    Disengage("submit");
                }
                else if (Time.unscaledTime < _activationLockUntil)
                {
                    // The screen changed moments ago. Swallow the press so a quick
                    // double tap cannot land on whatever slid into place.
                    Plugin.LogInfo("ignored activation: screen still settling");
                }
                else if (carrying)
                {
                    if (!GameBridge.DropCarriedDice() && target != null)
                        PointerDispatcher.Click(target, _focus.Center, PointerEventData.InputButton.Left);
                    _focus.Rescan(carrying, force: true);
                }
                else if (target != null && GameBridge.IsEditableWidget(target))
                {
                    Engage(target);
                }
                else if (target != null)
                {
                    if (GameBridge.IsDestructive(target) && !ConfirmDestructive(target))
                    {
                        Plugin.LogInfo("press again to confirm: " + target.name);
                    }
                    else
                    {
                        Plugin.LogDiag("activate '" + target.name + "'");
                        PointerDispatcher.Click(target, _focus.Center, PointerEventData.InputButton.Left);
                        _activationLockUntil = Time.unscaledTime + 0.4f;
                        _focus.Rescan(carrying, force: true);
                    }
                }
            }

            if (_pad.Cancel)
            {
                GameObject backGo;
                Vector2 backCenter;

                if (_engaged)
                {
                    Disengage("cancel");
                }
                else if (carrying)
                {
                    // Un-hover first so the drop resolves as "return to pool"
                    // instead of being inserted into whatever slot is focused.
                    PointerDispatcher.ClearHover();
                    GameBridge.DropCarriedDice();
                    _focus.Rescan(false, force: true);
                }
                else if (GameBridge.IsTargeting())
                {
                    VirtualPointer.PulseRight();
                }
                else if (_focus.TryGetBackControl(out backGo, out backCenter))
                {
                    // Popup screens such as character select have no Escape handler;
                    // their own back button is the only way out.
                    Plugin.LogDiag("cancel -> back control '" + backGo.name + "'");
                    PointerDispatcher.SetHover(backGo, backCenter);
                    PointerDispatcher.Click(backGo, backCenter, PointerEventData.InputButton.Left);
                    _activationLockUntil = Time.unscaledTime + 0.4f;
                    _focus.Rescan(false, force: true);
                }
                else
                {
                    Patches.QueueEscape();
                }
            }

            if (_pad.Menu)
                Patches.QueueEscape();

            if (_pad.Alt && !_engaged && target != null)
            {
                VirtualPointer.PulseRight();
                PointerDispatcher.Click(target, _focus.Center, PointerEventData.InputButton.Right);
            }

            // Shoulders jump between groups of controls, which on the settings screen
            // are the layout columns.
            if (!_engaged)
            {
                if (_pad.Prev && !_focus.CycleGroup(-1)) _focus.Move(Vector2.left);
                if (_pad.Next && !_focus.CycleGroup(1)) _focus.Move(Vector2.right);
            }
        }

        private void Engage(GameObject target)
        {
            _engaged = true;
            _engagedTarget = target;
            _focus.Pinned = true;
            _pad.SetRepeatProfile(0.45f, 0.18f);
            Plugin.LogDiag("engaged '" + target.name + "' (" +
                           GameBridge.DescribeWidgetValue(target) + ") - left/right changes it, B exits");
        }

        private void Disengage(string reason)
        {
            if (!_engaged) return;
            Plugin.LogDiag("released '" + (_engagedTarget != null ? _engagedTarget.name : "?") +
                           "' reason=" + reason);
            _engaged = false;
            _engagedTarget = null;
            _focus.Pinned = false;
            _pad.RestoreRepeatProfile();
        }

        private void DrawHighlight()
        {
            if (_focus.Focused != null)
            {
                bool armed = _confirmTarget == _focus.Focused && Time.unscaledTime < _confirmUntil;
                // Cyan while engaged: left/right is changing this control.
                _highlight.Show(_focus.FocusedRect, armed, _engaged);
            }
            else _highlight.Hide();
        }

        /// <summary>
        /// Returns true only on a second press of the same control within the
        /// confirmation window, so a stray press can never quit the game.
        /// </summary>
        private bool ConfirmDestructive(GameObject target)
        {
            if (_confirmTarget == target && Time.unscaledTime < _confirmUntil)
            {
                _confirmTarget = null;
                return true;
            }

            _confirmTarget = target;
            _confirmUntil = Time.unscaledTime + 3f;
            return false;
        }

        private void EnterGamepadMode()
        {
            _gamepadMode = true;
            VirtualPointer.Active = true;
            GameBridge.ApplyClickToDrag();
            _focus.Rescan(GameBridge.IsCarryingDice(), force: true);

            // Restore where the player was rather than snapping to screen centre.
            if (_focusBeforeExit != null && _focus.FocusObject(_focusBeforeExit))
                Plugin.LogDiag("restored focus to '" + _focusBeforeExit.name + "'");

            Plugin.LogInfo("gamepad mode enabled");
        }

        private void ExitGamepadMode()
        {
            _focusBeforeExit = _focus.Focused;
            Disengage("mode-exit");
            _gamepadMode = false;
            VirtualPointer.Active = false;
            VirtualPointer.Reset();
            PointerDispatcher.ClearHover();
            PointerDispatcher.Reset();
            GameBridge.RestoreClickToDrag();
            _focus.Clear();
            _highlight.Hide();
            Cursor.visible = true;
            Plugin.LogInfo("gamepad mode disabled");
        }

        private void LateUpdate()
        {
            if (!_gamepadMode) return;

            // The game's CursorManager re-shows the cursor on many screens.
            if (Cursor.visible) Cursor.visible = false;
        }

        private void OnDestroy()
        {
            _highlight.Destroy();
            GameBridge.RestoreClickToDrag();
        }
    }
}
