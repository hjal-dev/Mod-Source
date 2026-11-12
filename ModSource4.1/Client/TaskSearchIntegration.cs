using System;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT.Quests;
using HarmonyLib;

namespace ModSource
{
    [HarmonyPatch]
    public static class TaskSearchModNamePatch
    {
        internal const string TaskSearchGuid = "com.hj.tasksearch";

        private const string BuilderType = "TaskSearch.Search.TaskSearchEntryBuilder";
        private const string EntryType = "TaskSearch.Search.TaskSearchEntry";
        private const string FieldType = "TaskSearch.Search.TaskSearchField";

        private static MethodInfo _add;
        private static object _field;
        private static bool _warned;

        [HarmonyPrepare]
        public static bool Prepare()
        {
            return Resolve() != null;
        }

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            return Resolve();
        }

        [HarmonyPostfix]
        public static void Postfix(object __result, Quest quest)
        {
            if (__result == null || quest == null || _add == null)
            {
                return;
            }

            try
            {
                var template = quest.Template;
                var entry = template != null ? ModSourceClientDatabase.LookupQuest(template.Id) : null;
                if (!ModSourceClientDatabase.IsShown(entry))
                {
                    return;
                }

                _add.Invoke(__result, new[] { _field, entry.ModName });
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    ModSourcePlugin.Log.LogWarning("[ModSource] Could not add mod names to Task Search: " + ex.Message);
                }
            }
        }

        private static MethodBase Resolve()
        {
            BepInEx.PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue(TaskSearchGuid, out info) || info == null || info.Instance == null)
            {
                return null;
            }

            var assembly = info.Instance.GetType().Assembly;
            var builder = assembly.GetType(BuilderType, false);
            var entry = assembly.GetType(EntryType, false);
            var field = assembly.GetType(FieldType, false);
            if (builder == null || entry == null || field == null || !field.IsEnum)
            {
                return null;
            }

            var build = AccessTools.Method(builder, "Build");
            var add = AccessTools.Method(entry, "Add", new[] { field, typeof(string) });
            if (build == null || add == null || build.ReturnType != entry)
            {
                return null;
            }

            var fieldName = Enum.IsDefined(field, "Trader") ? "Trader" : Enum.IsDefined(field, "Description") ? "Description" : null;
            if (fieldName == null)
            {
                return null;
            }

            _add = add;
            _field = Enum.Parse(field, fieldName);
            return build;
        }
    }
}
