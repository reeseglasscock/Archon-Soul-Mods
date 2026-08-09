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
        private GameObject _focusBeforeTopMenu;
        private bool _wasDragOverride;
        private float _cancelDragUntil;
        private bool _draggingFace;
        private GameObject _faceInsertFor;
        private Vector2 _faceInsertPoint;
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
            if (Plugin.ShuttingDown) return;

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
            bool draggingSpell = GameBridge.IsDraggingSpell();
            bool draggingComponent = GameBridge.IsDraggingComponent();
            bool draggingFace = GameBridge.IsDraggingFace();
            bool dragOverride = draggingSpell || draggingComponent || draggingFace;
            _draggingFace = draggingFace;

            // Picking a spell or a modification component up switches navigation to
            // that item's valid destinations. Focus is chosen once at that moment
            // and then held: these screens re-sort themselves while something is
            // held, and re-acquiring against a shifting list makes the selection
            // crawl on its own.
            if (dragOverride != _wasDragOverride)
            {
                _wasDragOverride = dragOverride;
                _focus.Pinned = false;
                _focus.Rescan(carrying, force: true);

                if (dragOverride)
                {
                    _faceInsertFor = null;
                    _focus.AcquireNearest(_focus.Center);
                    _focus.Pinned = true;

                    // A newly picked up face must not drift. Anchor focus to the slot
                    // the face actually occupies, by index rather than by nearest
                    // distance, so the first directional press moves exactly one
                    // place. Hold it exactly where it already sits until then.
                    if (draggingFace)
                    {
                        int slotIndex = GameBridge.GetDraggedFaceSlotIndex(_focus.FaceSlots);
                        if (slotIndex >= 0 && slotIndex < _focus.FaceSlots.Count)
                            _focus.FocusObject(_focus.FaceSlots[slotIndex].gameObject);

                        Vector2 restPoint;
                        if (GameBridge.TryGetDraggedFaceScreenPoint(out restPoint))
                        {
                            _faceInsertPoint = restPoint;
                            _faceInsertFor = _focus.Focused;
                        }

                        Plugin.LogDiag("face reorder started at slot " + slotIndex +
                                       " of " + _focus.FaceSlots.Count);
                    }

                    Plugin.LogDiag("drag started - anchored to '" +
                                   (_focus.Focused != null ? _focus.Focused.name : "?") + "'");
                }
            }
            else if (dragOverride)
            {
                _focus.Pinned = true;
            }
            else if (!_engaged)
            {
                _focus.Pinned = false;
            }

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

                if (_focus.TopMenuMode && _pad.NavDirection.y < -0.5f)
                {
                    // Down is a natural way out of a bar along the top edge.
                    SetTopMenuMode(false);
                }
                else if (_engaged)
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

            // Leaving the run (or starting a drag) drops out of the top bar.
            if (_focus.TopMenuMode && (!GameBridge.HasTopMenu() || dragOverride || carrying))
                SetTopMenuMode(false);

            if (_engaged && (_engagedTarget == null || !_engagedTarget.activeInHierarchy))
                Disengage("target-gone");

            if (_focus.Focused != _lastFocused)
            {
                _lastFocused = _focus.Focused;
                _confirmTarget = null;
            }

            SuppressUiSelection();
            SyncPointer();
            HandleButtons(carrying, dragOverride);
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

            GameBridge.DumpTopScreenOverlay();

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
            // While a cancel is resolving, keep the pointer away from every drop
            // target so the item cannot be re-captured before the drag ends.
            if (Time.unscaledTime < _cancelDragUntil)
            {
                VirtualPointer.Position = new Vector2(-1000f, -1000f);
                VirtualPointer.PushToInputSystem();
                PointerDispatcher.ClearHover();
                GameBridge.ClearDropHoverTargets();
                return;
            }

            if (_focus.Focused == null) return;

            // Reordering a die's face needs the pointer placed between slots rather
            // than on one, or the pool's x-sort cannot decide an order. The point is
            // computed once per target and then held: recomputing it every frame
            // chases the faces as they reflow, and once the gap opens the nearest
            // face to that slot becomes the next one over, so the dragged face ends
            // up hopping onto it.
            if (_draggingFace)
            {
                if (_focus.Focused != _faceInsertFor)
                {
                    _faceInsertFor = _focus.Focused;
                    int idx = _focus.FaceSlots.IndexOf(_focus.Focused.transform);
                    _faceInsertPoint = idx >= 0
                        ? GameBridge.GetFaceInsertPoint(_focus.Focused.transform, idx, _focus.FaceSlots)
                        : _focus.Center;
                }

                VirtualPointer.Position = _faceInsertPoint;
            }
            else
            {
                _faceInsertFor = null;
                VirtualPointer.Position = _focus.Center;
            }

            VirtualPointer.PushToInputSystem();
            _lastPushed = VirtualPointer.Position;

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

        private void HandleButtons(bool carrying, bool dragOverride)
        {
            var target = _focus.Focused;

            // Y moves in and out of the run's top bar.
            if (_pad.TopMenu && !dragOverride && !carrying && !_engaged && GameBridge.HasTopMenu())
            {
                SetTopMenuMode(!_focus.TopMenuMode);
                return;
            }

            if (_pad.Submit)
            {
                if (_engaged)
                {
                    Disengage("submit");
                }
                else if (dragOverride)
                {
                    // The game ends these drags from its own held-then-released
                    // mouse check, so pulse the virtual button rather than trying
                    // to reproduce its placement logic.
                    Plugin.LogDiag("dropping at '" + (target != null ? target.name : "?") + "'");
                    VirtualPointer.PulseLeft();
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
                else if (target != null && GameBridge.IsDieInEditSlot(target))
                {
                    // Returning a die to the bag is a single press rather than
                    // pick up followed by cancel.
                    Plugin.LogDiag("returning die from edit slot to bag");
                    GameBridge.ReturnDieFromEditSlot(target, _focus.Center);
                    _activationLockUntil = Time.unscaledTime + 0.4f;
                    _focus.Rescan(false, force: true);
                }
                else if (target != null &&
                         (GameBridge.TryPlaceDieIntoEditSlot(target, _focus.Center) ||
                          GameBridge.TrySwapDieIntoEditSlot(target, _focus.Center)))
                {
                    // A die from the bag goes straight into the edit slot,
                    // swapping out whatever is already there.
                    Plugin.LogDiag("placed die from bag into edit slot");
                    _activationLockUntil = Time.unscaledTime + 0.4f;
                    _focus.Rescan(false, force: true);
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
                        bool wasTopMenu = _focus.TopMenuMode;
                        PointerDispatcher.Click(target, _focus.Center, PointerEventData.InputButton.Left);
                        _activationLockUntil = Time.unscaledTime + 0.4f;

                        // Opening something from the top bar hands navigation to
                        // whatever just appeared, rather than keeping focus trapped
                        // on the bar itself.
                        if (wasTopMenu && IsClickable(target))
                        {
                            _focus.TopMenuMode = false;
                            _focusBeforeTopMenu = null;
                            Plugin.LogDiag("top menu: left for opened content");
                        }

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
                else if (_focus.TopMenuMode)
                {
                    // B leaves the top bar rather than exiting the screen behind it.
                    SetTopMenuMode(false);
                }
                else if (dragOverride)
                {
                    if (_draggingFace)
                    {
                        // Reordering has no "undo" in the game: the face simply
                        // settles wherever the order currently puts it. Parking the
                        // pointer off screen would sort it to the far left, so end
                        // the drag in place instead.
                        Plugin.LogDiag("ending face reorder");
                        VirtualPointer.PulseLeft();
                    }
                    else
                    {
                        // Cancel: drop with no target hovered, which is how the game
                        // returns an item to its origin. The pointer is parked off
                        // screen for a few frames so nothing can re-acquire a target
                        // before the drag actually ends.
                        Plugin.LogDiag("cancelling drag - returning item");
                        PointerDispatcher.ClearHover();
                        GameBridge.ClearDropHoverTargets();
                        _cancelDragUntil = Time.unscaledTime + 0.35f;
                        VirtualPointer.PulseLeft();
                    }
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

            if (_pad.Alt && !_engaged && !_focus.TopMenuMode)
            {
                if (dragOverride)
                {
                    // X applies, same as A, so placing an item is consistent across
                    // faces, runes and bodies.
                    Plugin.LogDiag("applying at '" + (target != null ? target.name : "?") + "'");
                    VirtualPointer.PulseLeft();
                }
                else if (target != null)
                {
                    VirtualPointer.PulseRight();
                    PointerDispatcher.Click(target, _focus.Center, PointerEventData.InputButton.Right);
                }
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

        /// <summary>
        /// Enters or leaves the run's top bar, remembering where focus was so the
        /// screen is not re-entered at an arbitrary control.
        /// </summary>
        private void SetTopMenuMode(bool on)
        {
            if (on)
            {
                _focusBeforeTopMenu = _focus.Focused;
                _focus.TopMenuMode = true;
                _focus.Rescan(false, force: true);
                _focus.AcquireNearest(new Vector2(Screen.width * 0.5f, Screen.height));
                Plugin.LogDiag("top menu: entered");
            }
            else
            {
                _focus.TopMenuMode = false;
                _focus.Rescan(false, force: true);

                if (_focusBeforeTopMenu == null || !_focus.FocusObject(_focusBeforeTopMenu))
                    _focus.AcquireNearest(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));

                _focusBeforeTopMenu = null;
                Plugin.LogDiag("top menu: left");
            }
        }

        /// <summary>Whether activating this control actually does something.</summary>
        private static bool IsClickable(GameObject go)
        {
            if (go == null) return false;
            try
            {
                if (go.GetComponent<UnityEngine.UI.Selectable>() != null) return true;
                return go.GetComponent(typeof(IPointerClickHandler)) != null;
            }
            catch { return false; }
        }

        private void DrawHighlight()
        {
            // While reordering, the thing being carried is the face itself, so the
            // outline tracks it rather than the invisible slot anchor that drives
            // the ordering. Otherwise the highlight sits beside the face and looks
            // like it has jumped to a neighbour.
            if (_draggingFace)
            {
                Rect faceRect;
                if (GameBridge.TryGetDraggedFaceScreenRect(out faceRect))
                {
                    _highlight.Show(faceRect, false, true);
                    return;
                }
            }

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
            if (Plugin.ShuttingDown || !_gamepadMode) return;

            // The game's CursorManager re-shows the cursor on many screens.
            if (Cursor.visible) Cursor.visible = false;
        }

        private void OnApplicationQuit()
        {
            // Release the cursor and stop driving input before the engine tears down.
            if (_gamepadMode) ExitGamepadMode();
            _highlight.Destroy();
        }

        private void OnDestroy()
        {
            _highlight.Destroy();
            GameBridge.RestoreClickToDrag();
        }
    }
}
