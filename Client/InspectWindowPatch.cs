using System;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModSource
{
    [HarmonyPatch(typeof(ItemSpecificationPanel), nameof(ItemSpecificationPanel.Show))]
    public static class InspectWindowPatch
    {
        private const string LabelName = "ModSourceLabel";

        private const float Padding = 10f;

        private const float LeftLimit = 0.25f;

        [HarmonyPostfix]
        public static void Postfix(ItemSpecificationPanel __instance, ItemContext itemContext)
        {
            try
            {
                Apply(__instance, itemContext);
            }
            catch (Exception ex)
            {
                ModSourcePlugin.Log.LogError("[ModSource] Inspect patch failed: " + ex);
            }
        }

        private static void Apply(ItemSpecificationPanel panel, ItemContext itemContext)
        {
            var labels = panel._itemLabels;
            if (labels == null || labels._previewPanel == null || labels._weightText == null)
            {
                return;
            }

            var item = itemContext != null ? itemContext.Item : null;
            var entry = item != null ? ModSourceClientDatabase.Lookup(item.TemplateId.ToString()) : null;

            if (ModSourcePlugin.DebugLogging.Value && item != null)
            {
                LogLookup(item, entry);
            }

            var text = BuildText(item, entry);
            var label = GetOrCreateLabel(labels._previewPanel.transform, labels._weightText);
            if (label == null)
            {
                return;
            }

            if (text == null)
            {
                label.gameObject.SetActive(false);
                return;
            }

            label.text = text;
            label.gameObject.SetActive(true);
            Layout(label);
        }

        private static string BuildText(Item item, ModSourceEntry entry)
        {
            if (item == null || entry == null || string.IsNullOrEmpty(entry.ModName))
            {
                return null;
            }

            switch (entry.Source)
            {
                case "NameMatch":
                    return ModSourcePlugin.ShowLowConfidence.Value ? entry.ModName + "?" : null;

                case "LoadOrderDiffLibrary":
                    return "via " + entry.ModName;

                default:
                    return entry.ModName;
            }
        }

        private static TMP_Text GetOrCreateLabel(Transform preview, TMP_Text weightText)
        {
            var existing = preview.Find(LabelName);
            if (existing != null)
            {
                return existing.GetComponent<TMP_Text>();
            }

            var clone = UnityEngine.Object.Instantiate(weightText.gameObject, preview, false);
            clone.name = LabelName;

            for (var i = clone.transform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(clone.transform.GetChild(i).gameObject);
            }

            var layoutElement = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            var fitter = clone.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                UnityEngine.Object.Destroy(fitter);
            }

            var tmp = clone.GetComponent<TMP_Text>();
            if (tmp == null)
            {
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            tmp.richText = false;

            tmp.raycastTarget = false;

            tmp.alignment = TextAlignmentOptions.BottomRight;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            clone.transform.SetAsLastSibling();
            return tmp;
        }

        private static void Layout(TMP_Text label)
        {
            var rect = label.rectTransform;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            rect.anchorMin = new Vector2(LeftLimit, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);

            var height = Mathf.Max(label.preferredHeight, 18f);
            rect.sizeDelta = new Vector2(-Padding, height);
            rect.anchoredPosition = new Vector2(-Padding, Padding);
        }

        internal static void LogLookup(Item item, ModSourceEntry entry)
        {
            var log = ModSourcePlugin.Log;
            log.LogInfo("[ModSource] Item:       " + item.Name);
            log.LogInfo("[ModSource] Template:   " + item.TemplateId);

            if (entry == null)
            {
                log.LogInfo(
                    ModSourceClientDatabase.Loaded
                        ? "[ModSource] Mod:        <vanilla>"
                        : "[ModSource] Mod:        <origin database not loaded>"
                );
                return;
            }

            if (string.IsNullOrEmpty(entry.ModName))
            {
                log.LogInfo("[ModSource] Mod:        <modded, origin could not be determined>");
                return;
            }

            log.LogInfo("[ModSource] Mod:        " + entry.ModName + " v" + entry.ModVersion);
            log.LogInfo("[ModSource] Mod GUID:   " + entry.ModGuid);
            log.LogInfo("[ModSource] Author:     " + entry.ModAuthor);
            log.LogInfo("[ModSource] Bundle:     " + (entry.BundleModName ?? "-"));
            log.LogInfo("[ModSource] Confidence: " + entry.Confidence + " (" + entry.Source + ")");
        }
    }
}
