using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModSource
{
    internal static class ItemTooltipLine
    {
        public static string ForItem(string name, GridItemView view)
        {
            try
            {
                var item = view != null ? view.Item : null;
                if (item != null && view.Examined)
                {
                    OriginTab.SetPending(ModName(ModSourceClientDatabase.Lookup(item.TemplateId.ToString())));
                }
            }
            catch (Exception ex)
            {
                ModSourcePlugin.Log.LogError("[ModSource] Item tooltip failed: " + ex.Message);
            }

            return name;
        }

        public static string ForOffer(string name, TradingItemView view)
        {
            try
            {
                var item = view != null ? view.Item : null;
                if (item == null || !view.Examined)
                {
                    return name;
                }

                var itemEntry = ModSourceClientDatabase.Lookup(item.TemplateId.ToString());
                var offerEntry = ModSourceClientDatabase.LookupAssort(item.Id.ToString());

                var lines = new List<string>();

                var itemLine = ModName(itemEntry);
                if (itemLine != null)
                {
                    lines.Add(itemLine);
                }

                var sameMod = itemEntry != null && offerEntry != null && itemEntry.ModGuid == offerEntry.ModGuid;
                var offerLine = OriginTooltip.Describe("Offer", offerEntry);
                if (offerLine != null && !sameMod)
                {
                    lines.Add(offerLine);
                }

                OriginTab.SetPending(lines.Count > 0 ? string.Join("\n", lines.ToArray()) : null);
            }
            catch (Exception ex)
            {
                ModSourcePlugin.Log.LogError("[ModSource] Trader tooltip failed: " + ex.Message);
            }

            return name;
        }

        private static string ModName(ModSourceEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.ModName))
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
    }

    internal static class OriginTab
    {
        private const string TabName = "ModSourceTab";
        private const string LabelName = "ModSourceTabLabel";

        private const float Gap = 2f;

        private static string _pending;
        private static int _pendingFrame = -1;

        public static void SetPending(string text)
        {
            _pending = text;
            _pendingFrame = Time.frameCount;
        }

        public static void OnTooltipShown(SimpleTooltip tooltip)
        {
            var text = _pendingFrame == Time.frameCount ? _pending : null;
            _pending = null;
            _pendingFrame = -1;

            var box = tooltip != null ? tooltip._mainTransform : null;
            if (box == null)
            {
                return;
            }

            var existing = box.Find(TabName);

            if (string.IsNullOrEmpty(text))
            {
                if (existing != null)
                {
                    existing.gameObject.SetActive(false);
                }

                return;
            }

            var tab = existing != null ? (RectTransform)existing : Create(box);
            if (tab == null)
            {
                return;
            }

            var label = FindLabel(tab);
            if (label != null)
            {
                label.text = text;
                label.ForceMeshUpdate();
            }

            tab.gameObject.SetActive(true);
            tab.SetAsLastSibling();
            LayoutRebuilder.ForceRebuildLayoutImmediate(tab);
        }

        private static RectTransform Create(RectTransform parentBox)
        {
            var template = ItemUiContext.Instance != null ? ItemUiContext.Instance.Tooltip : null;
            var source = template != null ? template._mainTransform : null;
            if (source == null)
            {
                return null;
            }

            var labelPath = template._label != null ? RelativePath(template._label.transform, source) : null;

            var holder = new GameObject("ModSourceTabHolder");
            holder.SetActive(false);

            GameObject copy;
            try
            {
                copy = UnityEngine.Object.Instantiate(source.gameObject, holder.transform, false);
                copy.name = TabName;

                foreach (var nested in copy.GetComponentsInChildren<Transform>(true).Where(t => t != copy.transform && t.name == TabName).ToList())
                {
                    UnityEngine.Object.DestroyImmediate(nested.gameObject);
                }

                foreach (var behaviour in copy.GetComponentsInChildren<UIElement>(true).ToList())
                {
                    UnityEngine.Object.DestroyImmediate(behaviour);
                }

                var label = labelPath != null ? copy.transform.Find(labelPath) : null;
                var labelText = label != null ? label.GetComponent<TMP_Text>() : copy.GetComponentInChildren<TMP_Text>(true);

                foreach (var other in copy.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (other != labelText)
                    {
                        other.gameObject.SetActive(false);
                    }
                }

                if (labelText != null)
                {
                    labelText.gameObject.name = LabelName;
                    labelText.gameObject.SetActive(true);
                    labelText.richText = false;
                    labelText.enableWordWrapping = false;
                    labelText.raycastTarget = false;
                }

                foreach (var graphic in copy.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.raycastTarget = false;
                }

                foreach (var group in copy.GetComponentsInChildren<CanvasGroup>(true))
                {
                    group.alpha = 1f;
                    group.blocksRaycasts = false;
                    group.interactable = false;
                }

                var element = copy.GetComponent<LayoutElement>() ?? copy.AddComponent<LayoutElement>();
                element.preferredWidth = -1f;
                element.ignoreLayout = true;

                var fitter = copy.GetComponent<ContentSizeFitter>() ?? copy.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var rect = (RectTransform)copy.transform;
                rect.SetParent(parentBox, false);
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;

                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = new Vector2(0f, Gap);

                return rect;
            }
            finally
            {
                UnityEngine.Object.Destroy(holder);
            }
        }

        private static TMP_Text FindLabel(RectTransform tab)
        {
            var named = tab.Find(LabelName) ?? tab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == LabelName);
            return named != null ? named.GetComponent<TMP_Text>() : tab.GetComponentInChildren<TMP_Text>(true);
        }

        private static string RelativePath(Transform child, Transform root)
        {
            var parts = new List<string>();
            var current = child;
            while (current != null && current != root)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }

            return current == root && parts.Count > 0 ? string.Join("/", parts.ToArray()) : null;
        }
    }

    [HarmonyPatch(typeof(SimpleTooltip), nameof(SimpleTooltip.Show), new[] { typeof(string), typeof(Vector2?), typeof(float), typeof(float?) })]
    public static class SimpleTooltipShowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(SimpleTooltip __instance)
        {
            try
            {
                OriginTab.OnTooltipShown(__instance);
            }
            catch (Exception ex)
            {
                ModSourcePlugin.Log.LogError("[ModSource] Origin tab failed: " + ex.Message);
            }
        }
    }

    internal static class TooltipTranspiler
    {
        public static IEnumerable<CodeInstruction> InsertAfter(
            IEnumerable<CodeInstruction> instructions,
            Func<MethodInfo, bool> isAnchor,
            MethodInfo hook,
            string label)
        {
            var inserted = false;

            foreach (var instruction in instructions)
            {
                yield return instruction;

                if (!inserted
                    && (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && instruction.operand is MethodInfo called
                    && isAnchor(called))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, hook);
                    inserted = true;
                }
            }

            if (!inserted)
            {
                ModSourcePlugin.Log.LogWarning("[ModSource] " + label + ": tooltip anchor not found, mod tab disabled there.");
            }
        }
    }

    [HarmonyPatch(typeof(GridItemView), nameof(GridItemView.ShowTooltip))]
    public static class GridItemTooltipPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return TooltipTranspiler.InsertAfter(
                instructions,
                m => m.Name == nameof(GridItemView.GetItemName) && m.DeclaringType == typeof(GridItemView),
                AccessTools.Method(typeof(ItemTooltipLine), nameof(ItemTooltipLine.ForItem)),
                "Item tooltip"
            );
        }
    }

    [HarmonyPatch(typeof(TradingItemView), nameof(TradingItemView.ShowTooltip))]
    public static class TradingItemTooltipPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return TooltipTranspiler.InsertAfter(
                instructions,
                m => m.Name == "BriefItemName",
                AccessTools.Method(typeof(ItemTooltipLine), nameof(ItemTooltipLine.ForOffer)),
                "Trader tooltip"
            );
        }
    }
}
