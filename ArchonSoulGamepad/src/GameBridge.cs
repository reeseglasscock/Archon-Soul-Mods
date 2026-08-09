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
