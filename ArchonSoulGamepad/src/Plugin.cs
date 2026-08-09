using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ArchonSoulGamepad
{
    [BepInPlugin(Guid, "Archon Soul Gamepad Support", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.community.archonsoul.gamepad";

        private static ManualLogSource _log;
        private static ConfigEntry<bool> _verbose;
        private static ConfigEntry<bool> _diagnostics;
        private ConfigEntry<bool> _autoEnable;
        private ConfigEntry<bool> _selfTestCfg;

        private ConfigEntry<float> _deadzone;
        private ConfigEntry<float> _repeatDelay;
        private ConfigEntry<float> _repeatRate;
        private ConfigEntry<string> _highlightColor;
        private ConfigEntry<float> _highlightThickness;

        private void Awake()
        {
            _log = Logger;

            _verbose = Config.Bind("General", "VerboseLogging", false,
                "Log per-frame focus diagnostics. Noisy; only enable when debugging.");
            _diagnostics = Config.Bind("General", "Diagnostics", true,
                "Log a focus summary every few seconds. Useful for reporting problems.");
            _autoEnable = Config.Bind("General", "AutoEnableWithGamepad", true,
                "Switch to controller mode as soon as a gamepad is present. Moving the mouse hands control back.");
            _deadzone = Config.Bind("Input", "StickDeadzone", 0.5f,
                "How far the stick must move before it counts as a direction.");
            _repeatDelay = Config.Bind("Input", "RepeatDelay", 0.4f,
                "Seconds held before focus starts auto-repeating.");
            _repeatRate = Config.Bind("Input", "RepeatRate", 0.12f,
                "Seconds between repeats once auto-repeat begins.");
            _highlightColor = Config.Bind("Appearance", "HighlightColor", "FFD940",
                "Focus outline colour as RRGGBB hex.");
            _highlightThickness = Config.Bind("Appearance", "HighlightThickness", 3f,
                "Focus outline thickness in pixels.");

            _selfTestCfg = Config.Bind("General", "NavigationSelfTest", false,
                "Log a one-off focus walk of the current screen shortly after startup. Diagnostic aid.");

            LogInfo("initialising");

            GameBridge.Probe();

            try
            {
                var harmony = new Harmony(Guid);
                Patches.Apply(harmony);
            }
            catch (Exception e)
            {
                LogWarn("harmony setup failed: " + e);
            }

            var host = new GameObject("ArchonSoulGamepadRuntime");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;

            var runtime = host.AddComponent<GamepadRuntime>();
            runtime.Configure(_deadzone.Value, _repeatDelay.Value, _repeatRate.Value,
                              ParseColor(_highlightColor.Value), _highlightThickness.Value,
                              _autoEnable.Value, _selfTestCfg.Value);

            if (Config.Bind("General", "DebugHarness", false,
                    "Developer only: drives a scripted controller sequence for reproduction.").Value)
            {
                var harness = host.AddComponent<DebugHarness>();
                harness.Focus = runtime.FocusEngineRef;
                LogWarn("debug harness ENABLED - the mod will drive itself");
            }

            UnityEngine.InputSystem.InputSystem.onDeviceChange += (device, change) =>
                LogInfo("input device " + change + ": " + device.GetType().Name + " (" + device.name + ")");

            LogInfo("ready - connect a controller and press any button");
        }

        private static Color ParseColor(string hex)
        {
            Color c;
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString("#" + hex.TrimStart('#'), out c))
                return c;
            return new Color(1f, 0.85f, 0.25f, 1f);
        }

        internal static void LogInfo(string msg) { if (_log != null) _log.LogInfo(msg); }
        internal static void LogWarn(string msg) { if (_log != null) _log.LogWarning(msg); }

        internal static void LogDebug(string msg)
        {
            if (_log != null && _verbose != null && _verbose.Value) _log.LogInfo(msg);
        }

        internal static void LogDiag(string msg)
        {
            if (_log != null && _diagnostics != null && _diagnostics.Value) _log.LogInfo(msg);
        }
    }
}
