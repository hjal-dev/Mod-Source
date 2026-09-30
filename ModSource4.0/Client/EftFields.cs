using EFT.Achievements;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ModSource
{
    internal static class EftFields
    {
        public static readonly AccessTools.FieldRef<ItemSpecificationPanel, ItemInfoWindowLabels> ItemLabels =
            AccessTools.FieldRefAccess<ItemSpecificationPanel, ItemInfoWindowLabels>("_itemLabels");

        public static readonly AccessTools.FieldRef<ItemSpecificationPanel, TextMeshProUGUI> MasteringText =
            AccessTools.FieldRefAccess<ItemSpecificationPanel, TextMeshProUGUI>("_masteringText");

        public static readonly AccessTools.FieldRef<ItemInfoWindowLabels, GameObject> PreviewPanel =
            AccessTools.FieldRefAccess<ItemInfoWindowLabels, GameObject>("_previewPanel");

        public static readonly AccessTools.FieldRef<ItemInfoWindowLabels, TextMeshProUGUI> WeightText =
            AccessTools.FieldRefAccess<ItemInfoWindowLabels, TextMeshProUGUI>("_weightText");

        public static readonly AccessTools.FieldRef<Tooltip, RectTransform> MainTransform =
            AccessTools.FieldRefAccess<Tooltip, RectTransform>("_mainTransform");

        public static readonly AccessTools.FieldRef<SimpleTooltip, TextMeshProUGUI> Label =
            AccessTools.FieldRefAccess<SimpleTooltip, TextMeshProUGUI>("_label");

        public static readonly AccessTools.FieldRef<AchievementView, TMP_Text> TitleText =
            AccessTools.FieldRefAccess<AchievementView, TMP_Text>("_titleText");
    }
}
