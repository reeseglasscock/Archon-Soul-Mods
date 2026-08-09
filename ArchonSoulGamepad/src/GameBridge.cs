using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArchonSoulGamepad
{
    /// <summary>
    /// All knowledge of Archon Soul's own types lives here and nowhere else, so a
    /// future game patch can only ever disable game-specific behaviour rather than
    /// take the whole plugin down with it.
    /// </summary>
    internal static class GameBridge
    {
        public static bool Available { get; private set; }

        private static SettingsToggle _originalClickToDrag;
        private static bool _overrodeClickToDrag;

        public static void Probe()
        {
            try
            {
                _originalClickToDrag = Settings.clickToDrag;
                Available = true;
                Plugin.LogInfo("game bridge ready (clickToDrag was " + _originalClickToDrag + ")");
            }
            catch (Exception e)
            {
                Available = false;
                Plugin.LogWarn("game bridge unavailable, generic navigation only: " + e.Message);
            }
        }

        public static bool IsDiceSlot(MonoBehaviour mb)
        {
            if (!Available) return false;
            try { return mb is DiceInput || mb is SpellDiceInputAssigner; }
            catch { return false; }
        }

        /// <summary>
        /// Whether this drop target would actually take the die currently held.
        /// Used to snap focus to real placements instead of letting a carried die
        /// roam over the whole screen.
        /// </summary>
        public static bool SlotAcceptsCarriedDice(GameObject go)
        {
            if (!Available || go == null) return false;

            try
            {
                var dice = GlobalVars.currentlyDraggedDice;
                if (dice == null) return false;

                var input = go.GetComponent<DiceInput>();
                if (input != null) return input.CheckInputDice(dice);

                var assigner = go.GetComponent<SpellDiceInputAssigner>();
                if (assigner != null) return assigner.CheckInputDice(dice);
            }
            catch { }

            return false;
        }

        /// <summary>
        /// The drag code branches on this setting. Click-to-drag turns pick up and
        /// put down into two discrete clicks, which is exactly the interaction a
        /// controller can express, and it also disables the hold-detection paths
        /// that poll the (unpatchable) native Input.GetMouseButton.
        /// </summary>
        public static void ApplyClickToDrag()
        {
            if (!Available) return;
            try
            {
                if (Settings.clickToDrag != SettingsToggle.On)
                {
                    if (!_overrodeClickToDrag)
                    {
                        _originalClickToDrag = Settings.clickToDrag;
                        _overrodeClickToDrag = true;
                    }
                    Settings.clickToDrag = SettingsToggle.On;
                }
            }
            catch { }
        }

        public static void RestoreClickToDrag()
        {
            if (!Available || !_overrodeClickToDrag) return;
            try { Settings.clickToDrag = _originalClickToDrag; }
            catch { }
        }

        public static SettingsToggle OriginalClickToDrag
        {
            get { return _overrodeClickToDrag ? _originalClickToDrag : Settings.clickToDrag; }
        }

        /// <summary>
        /// Detects controls that quit the game. These are one press away from
        /// destroying an in-progress run, so the runtime requires a deliberate
        /// double press before activating them.
        /// </summary>
        public static bool IsDestructive(GameObject go)
        {
            if (go == null) return false;

            try
            {
                var btn = go.GetComponent<UnityEngine.UI.Button>();
                if (btn != null && btn.onClick != null)
                {
                    int n = btn.onClick.GetPersistentEventCount();
                    for (int i = 0; i < n; i++)
                    {
                        var method = btn.onClick.GetPersistentMethodName(i);
                        if (method == "QuitToDesktop" || method == "Quit") return true;
                    }
                }
            }
            catch { }

            var name = go.name;
            return !string.IsNullOrEmpty(name) &&
                   name.IndexOf("quit", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Identifies a screen's "go back" control. Many screens are popups with no
        /// Escape handler at all, so B has to press their back button to work.
        /// </summary>
        public static bool IsBackControl(GameObject go)
        {
            if (go == null) return false;
            if (IsDestructive(go)) return false;

            try
            {
                var btn = go.GetComponent<UnityEngine.UI.Button>();
                if (btn != null && btn.onClick != null)
                {
                    int n = btn.onClick.GetPersistentEventCount();
                    for (int i = 0; i < n; i++)
                    {
                        var m = btn.onClick.GetPersistentMethodName(i);
                        if (m == "CloseCharacterSelect" || m == "Back" || m == "Close" ||
                            m == "BackToMainMenu" || m == "Exit" || m == "Return" ||
                            m == "ClosePopup" || m == "BackButtonPressed" || m == "Resume")
                            return true;
                    }
                }
            }
            catch { }

            var name = go.name;
            if (string.IsNullOrEmpty(name)) return false;

            var lower = name.ToLowerInvariant();
            if (lower.Contains("background")) return false;

            // Several in-run screens are left via a Continue button rather than a
            // back button, so treat that as the way out. "ContinueRun" on the main
            // menu is excluded: cancelling should never start a run.
            if (lower.Contains("continue")) return !lower.Contains("run");

            return lower.Contains("back") || lower.Contains("close") || lower.Contains("return");
        }

        /// <summary>
        /// Settings rows are built from a container plus separate arrow buttons.
        /// Treating each arrow as its own focus target is what makes the screen feel
        /// clunky, so a row collapses to a single widget that is edited in place.
        /// </summary>
        public static GameObject GetWidgetRoot(MonoBehaviour mb)
        {
            if (!Available || mb == null) return null;

            try
            {
                var shuffler = mb.GetComponentInParent<SettingsShuffler>();
                if (shuffler != null) return shuffler.gameObject;

                var slider = mb.GetComponentInParent<UnityEngine.UI.Slider>();
                if (slider != null) return slider.gameObject;
            }
            catch { }

            return null;
        }

        public static bool IsEditableWidget(GameObject go)
        {
            if (!Available || go == null) return false;
            try
            {
                if (go.GetComponent<SettingsShuffler>() != null) return true;
                return go.GetComponent<UnityEngine.UI.Slider>() != null;
            }
            catch { return false; }
        }

        public static string DescribeWidgetValue(GameObject go)
        {
            if (!Available || go == null) return null;
            try
            {
                var shuffler = go.GetComponent<SettingsShuffler>();
                if (shuffler != null) return shuffler.GetCurrentShuffleOption();

                var slider = go.GetComponent<UnityEngine.UI.Slider>();
                if (slider != null) return slider.value.ToString("0.00");
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Changes a settings widget by one step. Arrow presses go through the
        /// button's own onClick rather than a synthetic raycast click, so the
        /// game's label refresh and apply logic always run exactly once.
        /// </summary>
        public static bool AdjustWidget(GameObject go, int dir)
        {
            if (!Available || go == null || dir == 0) return false;

            try
            {
                var slider = go.GetComponent<UnityEngine.UI.Slider>();
                if (slider != null)
                {
                    float range = slider.maxValue - slider.minValue;
                    // Whole-number volume sliders span 0-100; stepping by one would
                    // take a hundred presses to cross, so scale to ~20 steps.
                    float step = slider.wholeNumbers
                        ? Mathf.Max(1f, Mathf.Round(range / 20f))
                        : Mathf.Max(range / 20f, 0.0001f);
                    slider.value = Mathf.Clamp(slider.value + step * dir, slider.minValue, slider.maxValue);
                    return true;
                }

                var shuffler = go.GetComponent<SettingsShuffler>();
                if (shuffler == null) return false;

                var arrow = FindArrow(go, dir);
                if (arrow == null) return false;

                arrow.onClick.Invoke();
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.LogDebug("AdjustWidget failed: " + e.Message);
                return false;
            }
        }

        private static UnityEngine.UI.Button FindArrow(GameObject root, int dir)
        {
            var buttons = root.GetComponentsInChildren<UnityEngine.UI.Button>(false);
            UnityEngine.UI.Button best = null;
            float bestX = 0f;

            foreach (var b in buttons)
            {
                if (b == null || !b.IsActive() || !b.interactable) continue;

                var rt = b.transform as RectTransform;
                if (rt == null) continue;

                float x = rt.position.x;
                if (best == null || (dir > 0 ? x > bestX : x < bestX))
                {
                    best = b;
                    bestX = x;
                }
            }

            return best;
        }

        /// <summary>
        /// True while a spell is being dragged on the spell select screen. The flag
        /// is private to the game, so it is read reflectively rather than inferred.
        /// </summary>
        public static bool IsDraggingSpell()
        {
            if (!Available) return false;

            try
            {
                var ctrl = GetSpellController();
                if (ctrl == null || ctrl.spellDragObjects == null) return false;

                if (_spellDraggingField == null)
                    _spellDraggingField = typeof(SpellSelectDrag).GetField("dragging",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (_spellDraggingField == null) return false;

                foreach (var d in ctrl.spellDragObjects)
                {
                    if (d == null) continue;
                    if ((bool)_spellDraggingField.GetValue(d)) return true;
                }
            }
            catch { }

            return false;
        }

        private static System.Reflection.FieldInfo _spellDraggingField;
        private static SpellSelectScreenController _spellCtrl;
        private static float _nextSpellCtrlProbe;

        /// <summary>
        /// Cached lookup. Rescans run several times a second on screens with dozens
        /// of canvases, so a scene-wide search per scan is not affordable.
        /// </summary>
        private static SpellSelectScreenController GetSpellController()
        {
            if (_spellCtrl != null) return _spellCtrl;
            if (Time.unscaledTime < _nextSpellCtrlProbe) return null;

            _nextSpellCtrlProbe = Time.unscaledTime + 1f;
            try { _spellCtrl = UnityEngine.Object.FindFirstObjectByType<SpellSelectScreenController>(); }
            catch { _spellCtrl = null; }
            return _spellCtrl;
        }

        /// <summary>
        /// Fixed slot anchors for every spell zone. The spells themselves reflow
        /// continuously while one is held, so focusing them creates a feedback loop:
        /// focus moves the pointer, the pointer re-sorts the zone, which moves the
        /// focus again. The anchors never move, so they are stable drop targets.
        /// </summary>
        public static bool TryGetSpellSlotAnchors(List<Transform> into)
        {
            into.Clear();
            if (!Available) return false;

            try
            {
                var ctrl = GetSpellController();
                if (ctrl == null) return false;

                AddZoneAnchors(ctrl.equippedSpellsZone, into);
                AddZoneAnchors(ctrl.reserveSpellsZone, into);
                AddZoneAnchors(ctrl.trashZone, into);
            }
            catch { }

            return into.Count > 0;
        }

        private static void AddZoneAnchors(SpellSelectZone zone, List<Transform> into)
        {
            if (zone == null || zone.slotPositions == null) return;

            int max = zone.maxSpellsInZone > 0
                ? Mathf.Min(zone.slotPositions.Count, zone.maxSpellsInZone)
                : zone.slotPositions.Count;

            for (int i = 0; i < max; i++)
            {
                var t = zone.slotPositions[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                // The slot the spell came from stays selectable: putting it back
                // where it started is a legitimate choice.
                into.Add(t);
            }
        }

        private static DiceModificationScreenController _modCtrl;
        private static float _nextModCtrlProbe;
        private static System.Reflection.FieldInfo _componentDraggingField;

        private static DiceModificationScreenController GetModController()
        {
            if (_modCtrl != null) return _modCtrl;
            if (Time.unscaledTime < _nextModCtrlProbe) return null;

            _nextModCtrlProbe = Time.unscaledTime + 1f;
            try { _modCtrl = UnityEngine.Object.FindFirstObjectByType<DiceModificationScreenController>(); }
            catch { _modCtrl = null; }
            return _modCtrl;
        }

        private static ModificationComponentDrag _activeComponentDrag;
        private static int _activeComponentDragFrame = -1;

        /// <summary>
        /// The component currently being dragged, regardless of whether it has
        /// anywhere valid to go. Detecting the drag and finding its destinations
        /// must stay separate: with no die in the edit slot there are no face slots,
        /// and conflating the two made the mod conclude nothing was being dragged.
        /// </summary>
        private static ModificationComponentDrag GetActiveComponentDrag()
        {
            if (_activeComponentDragFrame == Time.frameCount) return _activeComponentDrag;
            _activeComponentDragFrame = Time.frameCount;
            _activeComponentDrag = null;

            if (!Available) return null;
            if (GetModController() == null) return null;

            try
            {
                if (_componentDraggingField == null)
                    _componentDraggingField = typeof(ModificationComponentDrag).GetField("dragging",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (_componentDraggingField == null) return null;

                var drags = UnityEngine.Object.FindObjectsByType<ModificationComponentDrag>(FindObjectsSortMode.None);
                foreach (var d in drags)
                {
                    if (d == null) continue;
                    if ((bool)_componentDraggingField.GetValue(d)) { _activeComponentDrag = d; break; }
                }
            }
            catch { }

            return _activeComponentDrag;
        }

        /// <summary>
        /// Destinations for the component being dragged. Faces and runes only go
        /// into a face slot; a body only goes onto the die itself. Returns true for
        /// the duration of any drag, even with no destinations at all, so the mod
        /// keeps ownership of navigation rather than letting focus wander onto the
        /// dice bag, which can never accept a face or rune.
        /// </summary>
        public static bool TryGetDraggedComponentTargets(List<GameObject> into)
        {
            into.Clear();

            var active = GetActiveComponentDrag();
            if (active == null) return false;

            var ctrl = GetModController();
            if (ctrl == null) return true;

            try
            {
                if (active.componentType == ComponentPoolType.Body)
                {
                    var slot = ctrl.diceModifier != null && ctrl.diceModifier.diceInput != null
                        ? ctrl.diceModifier.diceInput
                        : ctrl.modificationSlotInput;

                    // A body needs a die present to be applied to.
                    if (slot != null && slot.GetContainedDice() != null) into.Add(slot.gameObject);
                }
                else
                {
                    var inputs = UnityEngine.Object.FindObjectsByType<FaceModificationInput>(FindObjectsSortMode.None);
                    foreach (var fi in inputs)
                    {
                        if (fi == null || !fi.gameObject.activeInHierarchy) continue;
                        into.Add(fi.gameObject);
                    }
                }
            }
            catch { }

            return true;
        }

        public static bool IsDraggingComponent()
        {
            return GetActiveComponentDrag() != null;
        }

        /// <summary>
        /// Clears every "currently hovered drop target" the game tracks. Ending a
        /// drag with none of these set is how the game itself returns an item to
        /// where it came from, so this is what makes cancelling possible.
        /// </summary>
        public static void ClearDropHoverTargets()
        {
            if (!Available) return;

            try { GlobalVars.currentHoveredDiceInput = null; } catch { }
            try { GlobalVars.currentHoveredInputAssigner = null; } catch { }

            try
            {
                var ctrl = GetModController();
                if (ctrl != null) ctrl.currentHoveredFaceModInput = null;
            }
            catch { }
        }

        /// <summary>
        /// Controls that B already performs are kept out of d-pad navigation, so a
        /// screen's exit never absorbs a directional move. Note this is decided by
        /// behaviour, not by the on-screen label: the Modify Dice screen's
        /// "Continue" is named BackButton internally, and the spell screen's is
        /// named ContinueButton. "ContinueRun" on the main menu is not an exit and
        /// stays navigable.
        /// </summary>
        private static FaceInputsPool _facePool;
        private static int _facePoolFrame = -1;

        /// <summary>
        /// The die's face currently being dragged for reordering, or null. The pool
        /// publishes this itself, so no reflection is needed.
        /// </summary>
        public static FaceModificationInput GetDraggedFace()
        {
            if (_facePoolFrame != Time.frameCount)
            {
                _facePoolFrame = Time.frameCount;
                _facePool = null;

                if (Available && GetModController() != null)
                {
                    try
                    {
                        var pools = UnityEngine.Object.FindObjectsByType<FaceInputsPool>(FindObjectsSortMode.None);
                        foreach (var p in pools)
                        {
                            if (p == null || p.currentDraggedFaceModInput == null) continue;
                            _facePool = p;
                            break;
                        }
                    }
                    catch { }
                }
            }

            return _facePool != null ? _facePool.currentDraggedFaceModInput : null;
        }

        public static bool IsDraggingFace()
        {
            return GetDraggedFace() != null;
        }

        /// <summary>Current screen position of the face being reordered.</summary>
        public static bool TryGetDraggedFaceScreenPoint(out Vector2 point)
        {
            point = Vector2.zero;
            var dragged = GetDraggedFace();
            if (dragged == null) return false;
            return InteractableScanner.TryGetScreenPoint(dragged.transform, out point);
        }

        /// <summary>Index of the slot the dragged face currently occupies.</summary>
        public static int GetDraggedFaceSlotIndex(List<Transform> slots)
        {
            var dragged = GetDraggedFace();
            if (dragged == null || slots == null || slots.Count == 0) return -1;

            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] == null) continue;
                float d = Mathf.Abs(slots[i].position.x - dragged.transform.position.x);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        /// <summary>Screen rectangle of the face being reordered.</summary>
        public static bool TryGetDraggedFaceScreenRect(out Rect rect)
        {
            rect = default(Rect);
            var dragged = GetDraggedFace();
            if (dragged == null) return false;
            return InteractableScanner.TryGetScreenRectFor(dragged.gameObject, out rect);
        }

        /// <summary>
        /// The fixed slot positions a reordered face can occupy.
        /// </summary>
        public static bool TryGetFaceSlotAnchors(List<Transform> into)
        {
            into.Clear();
            if (GetDraggedFace() == null || _facePool == null) return false;

            try
            {
                var slots = _facePool.slotPositionList;
                if (slots == null) return false;

                foreach (var t in slots)
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    into.Add(t);
                }
            }
            catch { }

            return into.Count > 0;
        }

        /// <summary>
        /// Where to place the dragged face so the pool sorts it into the chosen
        /// slot. The pool orders faces purely by transform.position.x, so sitting
        /// exactly on a slot ties with whichever face occupies it and nothing
        /// shifts aside. The bias therefore only has to clear that one face's
        /// centre, by a few pixels in the direction of travel: enough to decide the
        /// order, small enough that the face still reads as sitting in the gap.
        /// </summary>
        public static Vector2 GetFaceInsertPoint(Transform slot, int targetIndex, List<Transform> allSlots)
        {
            Vector2 slotPoint;
            if (!InteractableScanner.TryGetScreenPoint(slot, out slotPoint)) return slotPoint;

            var dragged = GetDraggedFace();
            if (dragged == null || _facePool == null || allSlots == null || allSlots.Count < 2) return slotPoint;

            // Which slot is the dragged face nearest right now?
            int currentIndex = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < allSlots.Count; i++)
            {
                if (allSlots[i] == null) continue;
                float d = Mathf.Abs(allSlots[i].position.x - dragged.transform.position.x);
                if (d < bestDist) { bestDist = d; currentIndex = i; }
            }

            if (currentIndex < 0 || targetIndex == currentIndex) return slotPoint;
            float dir = targetIndex > currentIndex ? 1f : -1f;
            Plugin.LogDiag("face insert: slot " + currentIndex + " -> " + targetIndex);

            // Clear the centre of whichever face currently sits nearest that slot.
            float occupantX = slotPoint.x;
            try
            {
                var container = _facePool.faceInputContainer;
                if (container != null)
                {
                    var faces = container.GetComponentsInChildren<FaceModificationInput>();
                    float closest = float.MaxValue;
                    foreach (var f in faces)
                    {
                        if (f == null || f == dragged) continue;

                        Vector2 fp;
                        if (!InteractableScanner.TryGetScreenPoint(f.transform, out fp)) continue;

                        float d = Mathf.Abs(fp.x - slotPoint.x);
                        if (d < closest) { closest = d; occupantX = fp.x; }
                    }
                }
            }
            catch { }

            return new Vector2(occupantX + dir * 8f, slotPoint.y);
        }

        private static bool InteractableScannerBridge(Transform t, out Vector2 point)
        {
            return InteractableScanner.TryGetScreenPoint(t, out point);
        }

        /// <summary>
        /// True when the focused object belongs to the die sitting in the modify
        /// screen's edit slot.
        /// </summary>
        public static bool IsDieInEditSlot(GameObject go)
        {
            if (!Available || go == null) return false;

            try
            {
                var ctrl = GetModController();
                if (ctrl == null) return false;

                var slot = GetEditSlot();
                if (slot == null) return false;

                var dice = slot.GetContainedDice();
                if (dice == null) return false;

                return go == dice.gameObject || go.transform.IsChildOf(dice.transform);
            }
            catch { return false; }
        }

        /// <summary>
        /// Takes the die out of the edit slot and puts it straight back in the bag,
        /// in one action. Picking it up and then cancelling is the game's own two
        /// step route; this performs both so a single press does the obvious thing.
        /// </summary>
        public static bool ReturnDieFromEditSlot(GameObject focused, Vector2 center)
        {
            if (!IsDieInEditSlot(focused)) return false;

            PointerDispatcher.Click(focused, center, UnityEngine.EventSystems.PointerEventData.InputButton.Left);

            if (!IsCarryingDice()) return false;

            PointerDispatcher.ClearHover();
            ClearDropHoverTargets();
            return DropCarriedDice();
        }

        private static DiceInput GetEditSlot()
        {
            var ctrl = GetModController();
            if (ctrl == null) return null;

            return ctrl.diceModifier != null && ctrl.diceModifier.diceInput != null
                ? ctrl.diceModifier.diceInput
                : ctrl.modificationSlotInput;
        }

        /// <summary>
        /// Swaps a bag die with the one already in the edit slot, in one action.
        /// The sitting die is evicted through the game's own pick-up (which clears
        /// the face pool and undo history) and returned to the bag, then the chosen
        /// die is placed. Eviction is synchronous: pick-up reparents the die out of
        /// the slot's holder, so the slot reads as empty immediately afterwards.
        /// </summary>
        public static bool TrySwapDieIntoEditSlot(GameObject focused, Vector2 center)
        {
            if (!Available || focused == null) return false;
            if (IsDieInEditSlot(focused)) return false;

            try
            {
                var slot = GetEditSlot();
                if (slot == null) return false;

                var existing = slot.GetContainedDice();
                if (existing == null) return false;

                if (focused.GetComponentInParent<Dice>() == null) return false;

                var drag = existing.GetComponentInChildren<DiceDrag>();
                if (drag == null) return false;

                Rect dragRect;
                if (!InteractableScanner.TryGetScreenRectFor(drag.gameObject, out dragRect)) return false;

                PointerDispatcher.Click(drag.gameObject, dragRect.center,
                    UnityEngine.EventSystems.PointerEventData.InputButton.Left);

                if (IsCarryingDice())
                {
                    PointerDispatcher.ClearHover();
                    ClearDropHoverTargets();
                    DropCarriedDice();
                }

                return TryPlaceDieIntoEditSlot(focused, center);
            }
            catch (System.Exception e)
            {
                Plugin.LogDebug("TrySwapDieIntoEditSlot failed: " + e.Message);
                return false;
            }
        }

        public static bool TryPlaceDieIntoEditSlot(GameObject focused, Vector2 center)
        {
            if (!Available || focused == null) return false;
            if (IsDieInEditSlot(focused)) return false;

            try
            {
                var slot = GetEditSlot();
                if (slot == null || slot.GetContainedDice() != null) return false;

                if (focused.GetComponentInParent<Dice>() == null) return false;

                Rect slotRect;
                if (!InteractableScanner.TryGetScreenRectFor(slot.gameObject, out slotRect)) return false;

                PointerDispatcher.Click(focused, center, UnityEngine.EventSystems.PointerEventData.InputButton.Left);
                if (!IsCarryingDice()) return false;

                // The drop resolves against whatever slot the game believes is
                // hovered, so point it at the edit slot before ending the drag.
                PointerDispatcher.SetHover(slot.gameObject, slotRect.center);
                return DropCarriedDice();
            }
            catch (System.Exception e)
            {
                Plugin.LogDebug("TryPlaceDieIntoEditSlot failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// True for controls in the run's top UI bar. That bar is present on every
        /// in-run screen, so it is kept out of normal navigation and reached with a
        /// dedicated toggle instead.
        /// </summary>
        public static bool IsTopMenuElement(GameObject go)
        {
            if (!Available || go == null) return false;

            try
            {
                var run = RunManager.Instance;
                if (run == null || run.topUIBarTransform == null) return false;
                return go.transform.IsChildOf(run.topUIBarTransform);
            }
            catch { return false; }
        }

        public static bool HasTopMenu()
        {
            if (!Available) return false;
            try
            {
                var run = RunManager.Instance;
                return run != null && run.topUIBarTransform != null
                    && run.topUIBarTransform.gameObject.activeInHierarchy;
            }
            catch { return false; }
        }

        private static float _nextPopupDump;

        /// <summary>
        /// Diagnostic: lists what is inside an open top-screen overlay, with the
        /// pointer interfaces each component implements.
        /// </summary>
        public static void DumpTopScreenOverlay()
        {
            if (!Available) return;
            if (Time.unscaledTime < _nextPopupDump) return;
            if (!TopScreenOverlayOpen()) return;

            _nextPopupDump = Time.unscaledTime + 6f;

            try
            {
                var layer = RunManager.Instance.topScreenLayer;
                Plugin.LogDiag("[popup] topScreenLayer children=" + layer.childCount);

                var all = layer.GetComponentsInChildren<MonoBehaviour>(false);
                int shown = 0;
                foreach (var mb in all)
                {
                    if (mb == null || shown >= 40) continue;

                    string flags = "";
                    if (mb is UnityEngine.EventSystems.IPointerEnterHandler) flags += "enter ";
                    if (mb is UnityEngine.EventSystems.IPointerClickHandler) flags += "click ";
                    if (mb is UnityEngine.EventSystems.IPointerDownHandler) flags += "down ";
                    if (mb is UnityEngine.UI.Selectable) flags += "selectable ";
                    if (flags.Length == 0) continue;

                    shown++;
                    Plugin.LogDiag("[popup]   " + mb.gameObject.name + " : " + mb.GetType().Name + " [" + flags.Trim() + "]");
                }

                if (shown == 0)
                    Plugin.LogDiag("[popup]   nothing on this layer implements pointer interfaces");
            }
            catch (System.Exception e)
            {
                Plugin.LogDiag("[popup] dump failed: " + e.Message);
            }
        }

        private static float _nextPopupProbe;
        private static bool _popupOpen;

        /// <summary>
        /// True while one of the collection popups is on screen. Checked at the
        /// screen level rather than by walking each element's ancestors, because
        /// the item containers are not necessarily parented under the object that
        /// carries the popup component.
        /// </summary>
        public static bool CollectionPopupOpen()
        {
            if (!Available) return false;
            if (Time.unscaledTime < _nextPopupProbe) return _popupOpen;

            _nextPopupProbe = Time.unscaledTime + 0.5f;
            _popupOpen = false;

            try
            {
                _popupOpen =
                    UnityEngine.Object.FindFirstObjectByType<DiceBodyCollectionPopup>() != null ||
                    UnityEngine.Object.FindFirstObjectByType<DiceRunesCollectionPopup>() != null ||
                    UnityEngine.Object.FindFirstObjectByType<SpellsCollectionPopup>() != null ||
                    UnityEngine.Object.FindFirstObjectByType<BestiaryCollectionPopup>() != null ||
                    UnityEngine.Object.FindFirstObjectByType<CharacterStatsCollectionPopup>() != null;
            }
            catch { }

            return _popupOpen;
        }

        /// <summary>
        /// True while an overlay is open on the run's top screen layer. The top bar
        /// buttons clone their panels into RunManager.topScreenLayer rather than
        /// using the CollectionPopup classes, which is why looking for those found
        /// nothing.
        /// </summary>
        public static bool TopScreenOverlayOpen()
        {
            if (!Available) return false;

            try
            {
                var run = RunManager.Instance;
                if (run == null || run.topScreenLayer == null) return false;
                return run.topScreenLayer.childCount > 0
                    && run.topScreenLayer.gameObject.activeInHierarchy;
            }
            catch { return false; }
        }

        public static bool IsOnTopScreenLayer(GameObject go)
        {
            if (!Available || go == null) return false;

            try
            {
                var run = RunManager.Instance;
                if (run == null || run.topScreenLayer == null) return false;
                return go.transform.IsChildOf(run.topScreenLayer);
            }
            catch { return false; }
        }

        /// <summary>
        /// Areas whose contents exist to be read rather than clicked: the run's top
        /// bar, and the panels it opens. Hover-only elements are worth focusing
        /// here because their tooltip is the content.
        /// </summary>
        public static bool IsTooltipBrowsingArea(GameObject go)
        {
            if (go == null) return false;
            return IsTopMenuElement(go) || IsOnTopScreenLayer(go) || CollectionPopupOpen();
        }

        private static float _nextPauseProbe;
        private static bool _pauseOpen;

        /// <summary>
        /// True while the pause menu is up. Its buttons stay fully selectable:
        /// Resume is a legitimate choice there, not just a way out, even though B
        /// also presses it.
        /// </summary>
        public static bool IsPauseMenuOpen()
        {
            if (!Available) return false;
            if (Time.unscaledTime < _nextPauseProbe) return _pauseOpen;

            _nextPauseProbe = Time.unscaledTime + 0.3f;
            try { _pauseOpen = UnityEngine.Object.FindFirstObjectByType<PauseMenu>() != null; }
            catch { _pauseOpen = false; }
            return _pauseOpen;
        }

        /// <summary>
        /// Units a targeting spell can actually hit. The game switches on each
        /// valid unit's targeting square when it asks for a target, so that flag is
        /// the authoritative answer rather than anything the mod infers.
        /// </summary>
        public static bool TryGetTargetableUnits(List<GameObject> into)
        {
            into.Clear();
            if (!Available || !IsTargeting()) return false;

            try
            {
                var cc = CombatController.Instance;
                if (cc == null) return false;

                foreach (var u in cc.AllUnits)
                {
                    if (u == null || u.targetingSquare == null) continue;
                    if (!u.targetingSquare.activeSelf) continue;
                    into.Add(u.gameObject);
                }
            }
            catch { }

            return into.Count > 0;
        }

        public static bool IsCarryingDice()
        {
            if (!Available) return false;
            try { return GlobalVars.currentlyDraggedDice != null; }
            catch { return false; }
        }

        /// <summary>
        /// True while a spell is asking the player to pick a target. In that state
        /// the game listens for a global right-click to cancel, so B must pulse the
        /// right mouse button rather than open the pause menu.
        /// </summary>
        public static bool IsTargeting()
        {
            if (!Available) return false;
            try
            {
                var cc = CombatController.Instance;
                if (cc == null) return false;
                if (cc.spellCurrentlyTargeting) return true;
                return cc.targetingArrow != null && cc.targetingArrow.isActiveAndEnabled;
            }
            catch { return false; }
        }

        /// <summary>
        /// Ends the carry. DiceDrag.ForceStopDrag resolves the drop against whatever
        /// slot is currently hovered, which our focus engine keeps in sync.
        /// </summary>
        public static bool DropCarriedDice()
        {
            if (!Available) return false;
            try
            {
                var dice = GlobalVars.currentlyDraggedDice;
                if (dice == null) return false;

                var drag = dice.GetComponentInChildren<DiceDrag>();
                if (drag == null) return false;

                drag.ForceStopDrag();
                return true;
            }
            catch (Exception e)
            {
                Plugin.LogDebug("DropCarriedDice failed: " + e.Message);
                return false;
            }
        }
    }
}
