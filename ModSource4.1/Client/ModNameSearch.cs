using System;
using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;

namespace ModSource
{
    internal sealed class ModNameGroup
    {
        public string Name;
        public List<MongoID> TemplateIds;
    }

    internal static class ModNameSearch
    {
        private static readonly List<ModNameGroup> Empty = new List<ModNameGroup>();

        private static List<ModNameGroup> _groups;

        public static string Compact(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        public static IReadOnlyList<ModNameGroup> Groups()
        {
            if (_groups != null)
            {
                return _groups;
            }

            if (!ModSourceClientDatabase.Loaded || !Singleton<ItemFactory>.Instantiated)
            {
                return Empty;
            }

            var byGuid = new Dictionary<string, ModNameGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Singleton<ItemFactory>.Instance.ItemTemplates)
            {
                var entry = ModSourceClientDatabase.Lookup(pair.Key.ToString());
                if (!ModSourceClientDatabase.IsShown(entry))
                {
                    continue;
                }

                ModNameGroup group;
                if (!byGuid.TryGetValue(entry.ModGuid, out group))
                {
                    group = new ModNameGroup { Name = Compact(entry.ModName), TemplateIds = new List<MongoID>() };
                    byGuid[entry.ModGuid] = group;
                }

                group.TemplateIds.Add(pair.Key);
            }

            _groups = new List<ModNameGroup>(byGuid.Values);
            return _groups;
        }
    }

    [HarmonyPatch(typeof(StashSearchWindow), nameof(StashSearchWindow.SearchTextChangeHandler))]
    public static class StashSearchModNamePatch
    {
        private static readonly AccessTools.FieldRef<StashSearchWindow, List<MongoID>> Templates =
            AccessTools.FieldRefAccess<StashSearchWindow, List<MongoID>>("_textSearchTemplates");

        [HarmonyPostfix]
        public static void Postfix(StashSearchWindow __instance, string text)
        {
            try
            {
                var query = ModNameSearch.Compact(text);
                if (query.Length == 0)
                {
                    return;
                }

                var list = Templates(__instance);
                HashSet<MongoID> present = null;

                foreach (var group in ModNameSearch.Groups())
                {
                    if (group.Name.IndexOf(query, StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }

                    if (present == null)
                    {
                        present = new HashSet<MongoID>(list);
                    }

                    foreach (var id in group.TemplateIds)
                    {
                        if (present.Add(id))
                        {
                            list.Add(id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModSourcePlugin.Log.LogError("[ModSource] Stash mod name search failed: " + ex);
            }
        }
    }
}
