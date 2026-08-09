using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ArchonSoulGamepad
{
    /// <summary>
    /// Reproduction harness. Creates a virtual gamepad and drives a scripted
    /// sequence through the real input path so controller behaviour can be
    /// exercised without a human at the pad. Never activates a control that
    /// GameBridge reports as destructive.
    /// </summary>
    internal class DebugHarness : MonoBehaviour
    {
        public FocusEngine Focus;
        private Gamepad _virtual;

        public static bool VirtualPadActive { get; private set; }

        private void Start()
        {
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(12f);

            LogDevices();

            _virtual = InputSystem.AddDevice<Gamepad>("HarnessPad");
            VirtualPadActive = true;
            Plugin.LogInfo("[harness] virtual gamepad added: " + (_virtual != null ? _virtual.name : "<failed>"));

            yield return new WaitForSecondsRealtime(3f);
            Report("main menu");

            if (Scenario == "continuerun")
            {
                yield return ContinueRunScenario();
                yield break;
            }

            yield return SettingsScenario();
        }

        public static string Scenario = "settings";

        /// <summary>
        /// Resumes the existing save and observes whatever screen it lands on,
        /// without activating anything, so in-run screens can be inspected safely.
        /// </summary>
        private IEnumerator ContinueRunScenario()
        {
            if (!Focus.FocusByName("ContinueRun"))
            {
                Plugin.LogWarn("[harness] no ContinueRun button on this profile");
                yield break;
            }

            Plugin.LogInfo("[harness] continuing existing run (read-only observation)");
            yield return Activate();

            for (int i = 0; i < 14; i++)
            {
                yield return new WaitForSecondsRealtime(2f);
                Report("run t+" + ((i + 1) * 2) + "s");
            }

            InteractableScanner.DebugRejects = true;
            yield return new WaitForSecondsRealtime(0.5f);
            Plugin.LogInfo("[harness] REJECTS: " + InteractableScanner.DrainRejects());
            InteractableScanner.DebugRejects = false;

            Plugin.LogInfo("[harness] observation complete - nothing was activated");
        }

        private IEnumerator SettingsScenario()
        {
            // Settings screen: verify each row collapses to one widget and that
            // stepping a value works without repeated presses.
            if (Focus.FocusByName("Settings"))
            {
                Plugin.LogInfo("[harness] opening Settings");
                yield return Activate();
                yield return new WaitForSecondsRealtime(2.5f);
                Report("settings open");
                DumpSettingsHierarchy();
                Focus.DumpCandidateRects();

                // The three navigation cases reported as wrong.
                yield return NavCase("Slider", 3, -1f, 0f, "SFX slider LEFT");
                yield return NavCase("TextSize", 1, 1f, 0f, "TextSize RIGHT");
                yield return NavCase("ClickToDrag", 1, -1f, 0f, "ClickToDrag LEFT");
                yield return NavCase("Auto-target", 1, 1f, 0f, "AutoTarget RIGHT");
                yield return NavCase("NumericDiceValues", 1, -1f, 0f, "NumericDice LEFT");

                // Full engage cycle on a real row, asserting focus never drifts.
                if (Focus.FocusByName("VSync"))
                {
                    var row = Focus.Focused;
                    int rowId = row.GetInstanceID();
                    var menu0 = Object.FindFirstObjectByType<SettingsMenu>();
                    Plugin.LogInfo("[harness] ENGAGE-TEST target=" + row.name +
                                   " rowId=" + rowId +
                                   " menuId=" + (menu0 != null ? menu0.GetInstanceID() : 0) +
                                   " value=" + GameBridge.DescribeWidgetValue(row));

                    yield return Tap(_virtual, GamepadButton.South);
                    yield return new WaitForSecondsRealtime(0.3f);

                    var menu1 = Object.FindFirstObjectByType<SettingsMenu>();
                    Plugin.LogInfo("[harness]   after A: focus=" + Name(Focus.Focused) +
                                   " rowAlive=" + (row != null) +
                                   " menuId=" + (menu1 != null ? menu1.GetInstanceID() : 0));

                    for (int i = 0; i < 2; i++)
                    {
                        yield return Stick(_virtual, 1f, 0f, 0.25f);
                        yield return new WaitForSecondsRealtime(0.3f);
                        Plugin.LogInfo("[harness]   right: focus=" + Name(Focus.Focused) +
                                       " value=" + GameBridge.DescribeWidgetValue(row));
                    }

                    // Vertical must be ignored while engaged.
                    yield return Stick(_virtual, 0f, -1f, 0.25f);
                    yield return new WaitForSecondsRealtime(0.3f);
                    Plugin.LogInfo("[harness]   down-while-engaged: focus=" + Name(Focus.Focused));

                    yield return Tap(_virtual, GamepadButton.East);
                    yield return new WaitForSecondsRealtime(0.3f);
                    Plugin.LogInfo("[harness]   after B: focus=" + Name(Focus.Focused));

                    yield return Stick(_virtual, 0f, -1f, 0.25f);
                    yield return new WaitForSecondsRealtime(0.3f);
                    Plugin.LogInfo("[harness]   down-after-release: focus=" + Name(Focus.Focused));

                    // Put VSync back the way we found it.
                    GameBridge.AdjustWidget(row, -1);
                    GameBridge.AdjustWidget(row, -1);
                    Plugin.LogInfo("[harness]   restored value=" + GameBridge.DescribeWidgetValue(row));
                }

                GameObject b2; Vector2 c2;
                if (Focus.TryGetBackControl(out b2, out c2))
                {
                    Plugin.LogInfo("[harness] closing settings via '" + b2.name + "'");
                    PointerDispatcher.SetHover(b2, c2);
                    PointerDispatcher.Click(b2, c2, UnityEngine.EventSystems.PointerEventData.InputButton.Left);
                }
                yield return new WaitForSecondsRealtime(2f);
                Report("after settings close");
            }

            Plugin.LogInfo("[harness] sequence complete");
        }

        /// <summary>
        /// Dumps every settings row with its screen rect and the arrow buttons that
        /// would be driven for it, to prove the highlighted row and the adjusted row
        /// are the same object.
        /// </summary>
        private void DumpSettingsHierarchy()
        {
            var shufflers = Object.FindObjectsByType<SettingsShuffler>(FindObjectsSortMode.None);
            Plugin.LogInfo("[harness] --- settings rows: " + shufflers.Length + " ---");

            foreach (var sh in shufflers)
            {
                var rt = sh.transform as RectTransform;
                var corners = new Vector3[4];
                string rectStr = "<none>";
                if (rt != null)
                {
                    rt.GetWorldCorners(corners);
                    rectStr = string.Format("x{0:0}..{1:0} y{2:0}..{3:0}",
                        corners[0].x, corners[2].x, corners[0].y, corners[2].y);
                }

                var sb = new StringBuilder();
                sb.Append("[harness] ROW '").Append(sh.name).Append("' parent='")
                  .Append(sh.transform.parent != null ? sh.transform.parent.name : "-")
                  .Append("' rect=").Append(rectStr)
                  .Append(" value=").Append(sh.GetCurrentShuffleOption())
                  .Append(" | buttons:");

                var buttons = sh.GetComponentsInChildren<UnityEngine.UI.Button>(false);
                foreach (var b in buttons)
                {
                    var brt = b.transform as RectTransform;
                    sb.Append(" ").Append(b.name).Append("@x=")
                      .Append(brt != null ? brt.position.x.ToString("0") : "?");

                    var owner = b.GetComponentInParent<SettingsShuffler>();
                    if (owner != sh) sb.Append("(OWNER=").Append(owner != null ? owner.name : "null").Append(")");
                }

                Plugin.LogInfo(sb.ToString());
            }
        }

        private static string Name(GameObject go) { return go != null ? go.name : "<none>"; }

        // Input must persist across real frames: the plugin polls once per frame,
        // so a press and release inside one frame is never observed.
        private static IEnumerator Tap(Gamepad pad, GamepadButton button)
        {
            var s = new GamepadState();
            s.buttons = (uint)(1 << (int)button);
            InputSystem.QueueStateEvent(pad, s);
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
        }

        private static IEnumerator Stick(Gamepad pad, float x, float y, float holdSeconds)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(x, y) });
            yield return new WaitForSecondsRealtime(holdSeconds);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
            yield return null;
        }

        /// <summary>Focuses a named row, presses a direction, and reports where focus landed.</summary>
        private IEnumerator NavCase(string namePart, int occurrence, float x, float y, string label)
        {
            if (!Focus.FocusByNameOccurrence(namePart, occurrence))
            {
                Plugin.LogInfo("[harness] NAV " + label + ": row not found");
                yield break;
            }

            var from = Name(Focus.Focused);
            yield return Stick(_virtual, x, y, 0.25f);
            yield return new WaitForSecondsRealtime(0.35f);

            Plugin.LogInfo("[harness] NAV " + label + ": " + from + " -> " + Name(Focus.Focused));
        }

        private IEnumerator Activate()
        {
            var target = Focus.Focused;
            if (target == null) yield break;

            if (GameBridge.IsDestructive(target))
            {
                Plugin.LogWarn("[harness] REFUSING to activate destructive control: " + target.name);
                yield break;
            }

            PointerDispatcher.Click(target, Focus.Center, UnityEngine.EventSystems.PointerEventData.InputButton.Left);
            yield return null;
        }

        private void Report(string label)
        {
            var focused = Focus.Focused;
            var sb = new StringBuilder();
            sb.Append("[harness] ").Append(label)
              .Append(" | focus=").Append(focused != null ? focused.name : "<none>")
              .Append(" | destructive=").Append(focused != null && GameBridge.IsDestructive(focused))
              .Append(" | n=").Append(Focus.CandidateCount)
              .Append(" | [").Append(Focus.DescribeCandidates()).Append(']');
            Plugin.LogInfo(sb.ToString());
        }

        public static void LogDevices()
        {
            var sb = new StringBuilder("[harness] input devices: ");
            foreach (var d in InputSystem.devices)
                sb.Append(d.GetType().Name).Append('(').Append(d.name).Append(") ");
            sb.Append(" | Gamepad.current=").Append(Gamepad.current != null ? Gamepad.current.name : "<null>");
            Plugin.LogInfo(sb.ToString());
        }
    }
}
