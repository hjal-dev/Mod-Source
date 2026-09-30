using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ModSource
{
    [BepInPlugin("com.hj.modsource", "Hj's Mod Source", "0.9.0")]
    public class ModSourcePlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> DebugLogging;
        internal static ConfigEntry<bool> ShowLowConfidence;

        private void Awake()
        {
            Log = Logger;

            DebugLogging = Config.Bind(
                "General",
                "debugLogging",
                false,
                "Log every inspected item's resolved origin to the BepInEx console."
            );

            ShowLowConfidence = Config.Bind(
                "General",
                "showLowConfidence",
                false,
                "Show guesses based only on the item's internal name matching an installed mod's GUID or "
                    + "assembly name. Off by default because that is a convention some mods follow, not a rule, "
                    + "and a cloned item can carry the original mod's name. Shown with a trailing '?'."
            );

            ModSourceClientDatabase.BeginLoad();

            ApplyPatches();
        }

        private static void ApplyPatches()
        {
            var harmony = new Harmony("com.hj.modsource");

            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(ModSourcePlugin).Assembly))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                {
                    continue;
                }

                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (System.Exception ex)
                {
                    Log.LogError("[ModSource] Could not apply " + type.Name + ", that feature is disabled: " + ex.Message);
                }
            }
        }
    }
}
