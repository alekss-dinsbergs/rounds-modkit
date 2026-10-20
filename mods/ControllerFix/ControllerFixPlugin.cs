using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using InControl;
using UnityEngine;

namespace RoundsModkit.ControllerFix
{
    public enum InputBackend
    {
        /// <summary>XInput when running under Wine/Proton, the game's own setting on Windows.</summary>
        Auto,
        GameDefault,
        XInput,
        Native,
        UnityJoystick,
    }

    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Rounds.exe")]
    public class ControllerFixPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.roundsmodkit.controllerfix";
        public const string Name = "Controller Fix";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<InputBackend> Backend;
        internal static ConfigEntry<bool> LogDevices;

        private readonly HashSet<InputDevice> _reportedInput = new HashSet<InputDevice>();
        private string _lastDeviceSummary = "";
        private float _nextPoll;

        private void Awake()
        {
            Log = Logger;
            Backend = Config.Bind("Input", "Backend", InputBackend.Auto,
                "How the game talks to controllers. Auto picks XInput under Proton/Wine, where the game's native " +
                "HID module often finds no pads, and leaves Windows untouched. Restart the game after changing.");
            LogDevices = Config.Bind("Input", "LogDevices", true,
                "Write detected controllers and their first button press to BepInEx/LogOutput.log.");

            Log.LogInfo($"Running under Wine: {Wine.IsRunning} ({Wine.Version ?? "n/a"})");
            new Harmony(Guid).PatchAll(typeof(ControllerFixPlugin).Assembly);

            // Normally InControl starts with the first scene, after this runs. If it already started,
            // restart it so the chosen backend takes effect.
            if (InputManager.IsSetup && FindObjectOfType<InControlManager>() is InControlManager manager)
            {
                Log.LogWarning("InControl was already running; restarting it with the selected backend.");
                manager.enabled = false;
                manager.enabled = true;
            }
        }

        private void Update()
        {
            if (!LogDevices.Value || !InputManager.IsSetup) return;

            foreach (var device in InputManager.Devices)
                if (device.AnyButtonWasPressed && _reportedInput.Add(device))
                    Log.LogInfo($"Input received from '{device.Name}' ({device.DeviceClass})");

            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 2f;
            var summary = DeviceSummary();
            if (summary == _lastDeviceSummary) return;
            _lastDeviceSummary = summary;
            Log.LogInfo(summary);
        }

        internal static string DeviceSummary()
        {
            var managers = new List<string>();
            if (InputManager.HasDeviceManager<NativeInputDeviceManager>()) managers.Add("Native");
            if (InputManager.HasDeviceManager<XInputDeviceManager>()) managers.Add("XInput");
            if (InputManager.HasDeviceManager<UnityInputDeviceManager>()) managers.Add("UnityJoystick");

            var devices = InputManager.Devices.Select(d => $"'{d.Name}' [{d.DeviceClass}, {d.Meta}]").ToList();
            var joysticks = Input.GetJoystickNames().Where(n => !string.IsNullOrEmpty(n)).ToList();
            return $"InControl backends: {string.Join(", ", managers)}; devices ({devices.Count}): {string.Join("; ", devices)}; " +
                   $"Unity joysticks ({joysticks.Count}): {string.Join("; ", joysticks)}";
        }
    }

    [HarmonyPatch(typeof(InControlManager), "OnEnable")]
    internal static class InControlManagerPatch
    {
        private static void Prefix(InControlManager __instance)
        {
            var backend = ControllerFixPlugin.Backend.Value;
            if (backend == InputBackend.Auto) backend = Wine.IsRunning ? InputBackend.XInput : InputBackend.GameDefault;

            ControllerFixPlugin.Log.LogInfo(
                $"Game input settings: native={__instance.enableNativeInput} (xinput via native={__instance.nativeInputEnableXInput}), " +
                $"xinput={__instance.enableXInput}; applying {backend}");

            // InControl uses exactly one backend: a working native module disables XInput and Unity
            // joysticks, and XInput disables Unity joysticks. So pick by switching the others off.
            switch (backend)
            {
                case InputBackend.XInput:
                    __instance.enableNativeInput = false;
                    __instance.enableXInput = true;
                    break;
                case InputBackend.Native:
                    __instance.enableNativeInput = true;
                    break;
                case InputBackend.UnityJoystick:
                    __instance.enableNativeInput = false;
                    __instance.enableXInput = false;
                    break;
            }
            __instance.logDebugInfo = true;
        }

        private static void Postfix() => ControllerFixPlugin.Log.LogInfo(ControllerFixPlugin.DeviceSummary());
    }

    internal static class Wine
    {
        [DllImport("kernel32", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("ntdll", EntryPoint = "wine_get_version", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr WineGetVersion();

        // Wine reports itself as Windows; its ntdll exporting wine_get_version is the reliable tell.
        public static readonly bool IsRunning = Detect();

        public static string Version => IsRunning ? Marshal.PtrToStringAnsi(WineGetVersion()) : null;

        private static bool Detect()
        {
            try
            {
                var ntdll = GetModuleHandle("ntdll.dll");
                return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
