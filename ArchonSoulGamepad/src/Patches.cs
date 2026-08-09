using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ArchonSoulGamepad
{
    /// <summary>
    /// Redirects the game's legacy input reads to the synthetic pointer.
    /// Note: Input.GetMouseButton* are native InternalCall methods with no IL body,
    /// so they cannot be prefixed. We avoid needing them by forcing click-to-drag
    /// mode, which routes dice handling through pointer events instead.
    /// </summary>
    internal static class Patches
    {
        private static int _escapeFrame = -999;
        private static bool _escapePending;

        public static void QueueEscape()
        {
            _escapePending = true;
            _escapeFrame = Time.frameCount;
        }

        /// <summary>
        /// Consume-once semantics: exactly one caller per press observes the key,
        /// so the pause menu cannot open and close in the same frame regardless of
        /// script execution order. The request also expires, otherwise a press made
        /// on a screen with no listener would fire later in an unrelated scene.
        /// </summary>
        private static bool ConsumeEscape()
        {
            if (!_escapePending) return false;
            if (Time.frameCount - _escapeFrame > 10) { _escapePending = false; return false; }
            _escapePending = false;
            return true;
        }

        public static void Apply(Harmony harmony)
        {
            TryPatch(harmony,
                AccessTools.PropertyGetter(typeof(Input), "mousePosition"),
                new HarmonyMethod(typeof(Patches), nameof(MousePositionPrefix)),
                "Input.mousePosition");

            TryPatch(harmony,
                AccessTools.Method(typeof(Input), "GetKeyDown", new[] { typeof(KeyCode) }),
                new HarmonyMethod(typeof(Patches), nameof(GetKeyDownPrefix)),
                "Input.GetKeyDown");

            // Best effort only; these are extern on most Unity builds.
            TryPatch(harmony,
                AccessTools.Method(typeof(Input), "GetMouseButton", new[] { typeof(int) }),
                new HarmonyMethod(typeof(Patches), nameof(GetMouseButtonPrefix)),
                "Input.GetMouseButton", optional: true);

            TryPatch(harmony,
                AccessTools.Method(typeof(Input), "GetMouseButtonDown", new[] { typeof(int) }),
                new HarmonyMethod(typeof(Patches), nameof(GetMouseButtonDownPrefix)),
                "Input.GetMouseButtonDown", optional: true);

            // Keep our click-to-drag override out of the player's saved settings.
            try
            {
                var m = AccessTools.Method(typeof(Settings), "CurrentSettingsSaveData");
                if (m != null)
                {
                    harmony.Patch(m,
                        prefix: new HarmonyMethod(typeof(Patches), nameof(SettingsSavePrefix)),
                        postfix: new HarmonyMethod(typeof(Patches), nameof(SettingsSavePostfix)));
                    Plugin.LogInfo("patched Settings.CurrentSettingsSaveData");
                }
            }
            catch (Exception e)
            {
                Plugin.LogWarn("could not guard settings save: " + e.Message);
            }
        }

        private static void TryPatch(Harmony harmony, MethodBase target, HarmonyMethod prefix,
                                     string label, bool optional = false)
        {
            if (target == null)
            {
                Plugin.LogWarn("patch target not found: " + label);
                return;
            }

            try
            {
                harmony.Patch(target, prefix: prefix);
                Plugin.LogInfo("patched " + label);
                if (label == "Input.GetMouseButton") MouseButtonPatched = true;
            }
            catch (Exception e)
            {
                if (optional)
                    Plugin.LogInfo("skipped " + label + " (native method, expected): " + e.GetType().Name);
                else
                    Plugin.LogWarn("failed to patch " + label + ": " + e.Message);
            }
        }

        public static bool MouseButtonPatched;

        private static bool MousePositionPrefix(ref Vector3 __result)
        {
            if (Plugin.ShuttingDown || !VirtualPointer.Active) return true;
            __result = new Vector3(VirtualPointer.Position.x, VirtualPointer.Position.y, 0f);
            return false;
        }

        private static bool GetKeyDownPrefix(KeyCode key, ref bool __result)
        {
            if (Plugin.ShuttingDown || !VirtualPointer.Active) return true;
            if (key != KeyCode.Escape) return true;
            if (!ConsumeEscape()) return true;

            __result = true;
            return false;
        }

        private static bool GetMouseButtonPrefix(int button, ref bool __result)
        {
            if (Plugin.ShuttingDown || !VirtualPointer.Active) return true;
            if (button == 0 && VirtualPointer.LeftDown) { __result = true; return false; }
            if (button == 1 && VirtualPointer.RightDown) { __result = true; return false; }
            return true;
        }

        private static bool GetMouseButtonDownPrefix(int button, ref bool __result)
        {
            if (Plugin.ShuttingDown || !VirtualPointer.Active) return true;
            if (button == 0 && VirtualPointer.LeftPressedThisFrame) { __result = true; return false; }
            if (button == 1 && VirtualPointer.RightPressedThisFrame) { __result = true; return false; }
            return true;
        }

        private static void SettingsSavePrefix()
        {
            GameBridge.RestoreClickToDrag();
        }

        private static void SettingsSavePostfix()
        {
            if (VirtualPointer.Active) GameBridge.ApplyClickToDrag();
        }
    }
}
