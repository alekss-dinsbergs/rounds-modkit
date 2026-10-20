using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace RoundsModkit.ExampleMod
{
    public enum PickLogStyle { Off, Short, Verbose }

    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Rounds.exe")]
    public class ExamplePlugin : BaseUnityPlugin
    {
        public const string Guid = "com.roundsmodkit.example";
        public const string Name = "Modkit Example";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<PickLogStyle> PickLog;
        internal static ConfigEntry<bool> Enabled;

        private void Awake()
        {
            Log = Logger;
            // These entries show up in the launcher's Config tab after the first game start.
            Enabled = Config.Bind("General", "Enabled", true, "Turns the example patch on or off.");
            PickLog = Config.Bind("General", "PickLogStyle", PickLogStyle.Short, "How much to log when a card pick starts.");

            new Harmony(Guid).PatchAll(typeof(ExamplePlugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }
    }

    [HarmonyPatch(typeof(CardChoice), nameof(CardChoice.StartPick))]
    internal static class StartPickPatch
    {
        private static void Postfix(int picksToSet, int pickerIDToSet)
        {
            if (!ExamplePlugin.Enabled.Value) return;
            switch (ExamplePlugin.PickLog.Value)
            {
                case PickLogStyle.Short:
                    ExamplePlugin.Log.LogInfo($"Player {pickerIDToSet} picking");
                    break;
                case PickLogStyle.Verbose:
                    ExamplePlugin.Log.LogInfo($"StartPick: picker={pickerIDToSet} picks={picksToSet} cards={CardChoice.instance.cards.Length}");
                    break;
            }
        }
    }
}
