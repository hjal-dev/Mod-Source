using System;
using System.Collections.Generic;
using EFT.Achievements;
using EFT.Customization;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;

namespace ModSource
{
    internal sealed class OriginTooltipTag : MonoBehaviour
    {
        public HoverTooltipArea Area;
    }

    internal static class OriginTooltip
    {
        public static void Set(GameObject target, string text)
        {
            Set(target, text == null ? null : (Func<string>)(() => text));
        }

        public static void Set(GameObject target, Func<string> getter)
        {
            if (target == null)
            {
                return;
            }

            var tag = target.GetComponent<OriginTooltipTag>();

            if (getter == null)
            {
                if (tag != null && tag.Area != null)
                {
                    tag.Area.enabled = false;
                }

                return;
            }

            if (tag == null)
            {
                if (ItemUiContext.Instance == null)
                {
                    return;
                }

                tag = target.AddComponent<OriginTooltipTag>();
                tag.Area = target.AddComponent<HoverTooltipArea>();
            }

            tag.Area.enabled = true;

            tag.Area.SetMessageText(getter);
        }

        public static string Describe(string what, ModSourceEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.ModName))
            {
                return null;
            }

            switch (entry.Source)
            {
                case "NameMatch":
                    return ModSourcePlugin.ShowLowConfidence.Value ? what + " possibly added by " + entry.ModName : null;

                case "LoadOrderDiffLibrary":
                    return what + " added via " + entry.ModName;

                default:
                    return what + " added by " + entry.ModName;
            }
        }

        public static void Log(string what, string id, string text)
        {
            if (ModSourcePlugin.DebugLogging.Value)
            {
                ModSourcePlugin.Log.LogInfo("[ModSource] " + what + " " + id + " -> " + (text ?? "<vanilla or unknown>"));
            }
        }

        public static void Guard(string where, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                ModSourcePlugin.Log.LogError("[ModSource] " + where + " tooltip failed: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(NotesTask), nameof(NotesTask.Show))]
    public static class NotesTaskTooltipPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NotesTask __instance, Quest quest)
        {
            OriginTooltip.Guard("Task", () => QuestTooltip.Apply(__instance.gameObject, quest));
        }
    }

    [HarmonyPatch(typeof(QuestListItem), nameof(QuestListItem.Init))]
    public static class QuestListItemTooltipPatch
    {
        [HarmonyPostfix]
        public static void Postfix(QuestListItem __instance, Quest quest)
        {
            OriginTooltip.Guard("Trader task", () => QuestTooltip.Apply(__instance.gameObject, quest));
        }
    }

    internal static class QuestTooltip
    {
        public static void Apply(GameObject target, Quest quest)
        {
            var id = quest != null && quest.Template != null ? quest.Template.Id : null;
            var text = OriginTooltip.Describe("Task", ModSourceClientDatabase.LookupQuest(id));
            OriginTooltip.Log("Quest", id, text);
            OriginTooltip.Set(target, text);
        }
    }

    [HarmonyPatch(typeof(AchievementView), nameof(AchievementView.SetupText))]
    public static class AchievementTooltipPatch
    {
        [HarmonyPostfix]
        public static void Postfix(AchievementView __instance)
        {
            OriginTooltip.Guard("Achievement", () =>
            {
                var title = __instance._titleText;
                if (title == null)
                {
                    return;
                }

                var template = __instance.Achievement != null ? __instance.Achievement.Template : null;
                var id = template != null ? template.Id : null;
                var text = OriginTooltip.Describe("Achievement", ModSourceClientDatabase.LookupAchievement(id));
                OriginTooltip.Log("Achievement", id, text);

                if (text != null)
                {
                    title.raycastTarget = true;
                }

                OriginTooltip.Set(title.gameObject, text);
            });
        }
    }

    [HarmonyPatch(typeof(ClothingItem), nameof(ClothingItem.Init))]
    public static class ClothingItemTooltipPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ClothingItem __instance, ClothingItem.FullOffer offer)
        {
            OriginTooltip.Guard("Clothing", () =>
            {
                var entry = ClothingTooltip.Lookup(offer != null ? offer.Suite : null, offer != null ? offer.Clothing : null);
                var text = OriginTooltip.Describe("Clothing", entry);
                OriginTooltip.Log("Clothing offer", offer != null && offer.Offer != null ? offer.Offer.Id : null, text);
                OriginTooltip.Set(__instance.gameObject, text);
            });
        }
    }

    [HarmonyPatch(typeof(InventoryClothingSelectionPanel), nameof(InventoryClothingSelectionPanel.PrepareDropDownBox))]
    public static class OutfitDropdownTooltipPatch
    {
        [HarmonyPostfix]
        public static void Postfix(DropDownBox dropdownBox, List<CustomizationSuite> suites)
        {
            OriginTooltip.Guard("Outfit dropdown", () =>
            {
                if (dropdownBox == null || suites == null)
                {
                    return;
                }

                OriginTooltip.Set(dropdownBox.gameObject, () =>
                {
                    var index = dropdownBox.CurrentIndex;
                    var suite = index >= 0 && index < suites.Count ? suites[index] : null;
                    return OriginTooltip.Describe("Clothing", ClothingTooltip.Lookup(suite, null)) ?? string.Empty;
                });
            });
        }
    }

    internal static class ClothingTooltip
    {
        public static ModSourceEntry Lookup(CustomizationSuite suite, CustomizationClothing clothing)
        {
            var entry = suite != null ? ModSourceClientDatabase.LookupCustomization(suite.Id.ToString()) : null;
            if (entry == null && clothing != null)
            {
                entry = ModSourceClientDatabase.LookupCustomization(clothing.Id.ToString());
            }

            return entry;
        }
    }
}
