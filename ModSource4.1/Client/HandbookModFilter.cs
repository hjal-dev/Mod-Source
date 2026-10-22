using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.HandBook;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Ragfair;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ModSource
{
    internal sealed class ModFilterOption
    {
        public string Label;
        public HashSet<string> Ids;
    }

    internal sealed class HandbookModFilter : MonoBehaviour
    {
        private const string ObjectName = "ModSourceModFilter";
        private const string AllLabel = "All mods";
        private const string WishlistId = "Wishlist";
        private const float Margin = 12f;
        private const float MinWidth = 120f;
        private const float MinHeight = 20f;
        private const float MaxHeight = 40f;
        private const float FallbackHeight = 28f;
        private const float ListHeight = 320f;

        private static readonly AccessTools.FieldRef<BrowseCategoriesPanel, BindableAsyncViewList<HandbookNode, CombinedView>> ViewList =
            AccessTools.FieldRefAccess<BrowseCategoriesPanel, BindableAsyncViewList<HandbookNode, CombinedView>>("ViewList");

        private static readonly Vector3[] Corners = new Vector3[4];

        private readonly List<ModFilterOption> _options = new List<ModFilterOption>();

        private HandbookCategoriesPanel _panel;
        private DropDownBox _dropdown;
        private RectTransform _tree;
        private float _height;
        private float _diagnosticAt;

        public static HandbookModFilter Current { get; private set; }

        public HandbookCategoriesPanel Panel
        {
            get { return _panel; }
        }

        public HashSet<string> ActiveIds { get; private set; }

        public static void Attach(HandbookCategoriesPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            var filter = panel.GetComponent<HandbookModFilter>() ?? panel.gameObject.AddComponent<HandbookModFilter>();
            filter._panel = panel;
            Current = filter;

            if (!filter.EnsureDropdown())
            {
                return;
            }

            filter.Refresh();
        }

        public static bool IsActiveFor(BrowseCategoriesPanel panel)
        {
            var filter = Current;
            return filter != null && filter.ActiveIds != null && ReferenceEquals(filter._panel, panel);
        }

        public static bool IsModItem(HandbookNode node, HashSet<string> ids)
        {
            return node != null && node.Data != null && node.Data.Type == ENodeType.Item && ids.Contains(TemplateId(node));
        }

        public bool WasReset()
        {
            foreach (var node in _panel.FilteredNodes.Values)
            {
                if (node != null && ReferenceEquals(_panel.Nodes[node.Id], node))
                {
                    return true;
                }
            }

            return false;
        }

        public void Apply()
        {
            _panel.FilterHandler();

            if (ActiveIds == null)
            {
                _panel.FilteredNodes.AddRange(_panel.Nodes);
            }
            else
            {
                foreach (var root in _panel.Nodes.Values.ToList())
                {
                    if (root == null || root.Id == WishlistId)
                    {
                        continue;
                    }

                    var pruned = Prune(root, root.Parent, ActiveIds);
                    if (pruned != null)
                    {
                        _panel.FilteredNodes.Add(pruned.Id, pruned);
                    }
                }
            }

            var viewList = ViewList(_panel);
            if (viewList != null)
            {
                viewList.OrderViewList();
            }
        }

        private void Refresh()
        {
            _options.Clear();
            _options.Add(new ModFilterOption { Label = AllLabel });

            var byGuid = new Dictionary<string, ModFilterOption>(StringComparer.OrdinalIgnoreCase);
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in Flatten(_panel.Nodes.Values))
            {
                if (node.Data == null || node.Data.Type != ENodeType.Item)
                {
                    continue;
                }

                var id = TemplateId(node);
                var entry = ModSourceClientDatabase.Lookup(id);
                if (!Listed(entry))
                {
                    continue;
                }

                ModFilterOption option;
                if (!byGuid.TryGetValue(entry.ModGuid, out option))
                {
                    option = new ModFilterOption { Ids = new HashSet<string>() };
                    byGuid[entry.ModGuid] = option;
                    names[entry.ModGuid] = entry.ModName;
                }

                option.Ids.Add(id);
            }

            foreach (var pair in byGuid.OrderBy(p => names[p.Key], StringComparer.OrdinalIgnoreCase))
            {
                pair.Value.Label = names[pair.Key] + " (" + pair.Value.Ids.Count + ")";
                _options.Add(pair.Value);
            }

            ActiveIds = null;
            _dropdown.Show(_options.Select(o => o.Label));
            _dropdown.UpdateValue(0, false);
            _dropdown.Interactable = _options.Count > 1;
            _dropdown.gameObject.SetActive(true);
            _dropdown.ToggleMenu(false);
            _dropdown.SetPanelState(false);
            Normalize(_dropdown);

            if (ModSourcePlugin.DebugLogging.Value)
            {
                ModSourcePlugin.Log.LogInfo("[ModSource] Handbook filter options: " + (_options.Count - 1) + " mods.");
                LogTree(_dropdown.transform, 1);
                _diagnosticAt = Time.unscaledTime + 1f;
            }
        }

        private static void Normalize(DropDownBox dropdown)
        {
            foreach (var group in dropdown.GetComponentsInChildren<CanvasGroup>(true))
            {
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
            }

            foreach (var behaviour in dropdown.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour.GetType().Name == "Animator")
                {
                    behaviour.enabled = false;
                }
            }

            if (dropdown.OpenPanel != null)
            {
                dropdown.OpenPanel.gameObject.SetActive(false);
            }
        }

        private void LogPlacement()
        {
            var log = ModSourcePlugin.Log;
            var rect = (RectTransform)_dropdown.transform;
            rect.GetWorldCorners(Corners);
            log.LogInfo("[ModSource] Handbook filter world rect: " + Corners[0] + " - " + Corners[2] + " active=" + _dropdown.gameObject.activeInHierarchy);
            _tree.GetWorldCorners(Corners);
            log.LogInfo("[ModSource] Handbook filter tree rect: " + Corners[0] + " - " + Corners[2]);

            var alphas = new List<string>();
            for (var t = rect.transform; t != null; t = t.parent)
            {
                foreach (var group in t.GetComponents<CanvasGroup>())
                {
                    alphas.Add(t.name + "=" + group.alpha);
                }
            }

            log.LogInfo("[ModSource] Handbook filter alphas: " + string.Join(", ", alphas));
            log.LogInfo(
                "[ModSource] Handbook filter open panel: active=" + (_dropdown.OpenPanel != null && _dropdown.OpenPanel.gameObject.activeSelf)
                    + " parent=" + (_dropdown.OpenPanel != null ? Path(_dropdown.OpenPanel.parent) : "-")
            );
        }

        private void OnSelected(int index)
        {
            if (index < 0 || index >= _options.Count || _panel == null)
            {
                return;
            }

            Current = this;
            ActiveIds = _options[index].Ids;

            var search = _panel.SearchInputField != null ? _panel.SearchInputField.text : null;
            if (!string.IsNullOrEmpty(search) && search.Length >= 3 && !search.StartsWith("#"))
            {
                Watch(_panel.Filter(search));
                return;
            }

            Apply();
        }

        private bool EnsureDropdown()
        {
            if (_dropdown != null && _tree != null)
            {
                return true;
            }

            var template = FindTemplate();
            var scroll = _panel.CategoryViewsContainer != null ? _panel.CategoryViewsContainer.GetComponentInParent<ScrollRect>() : null;
            var screen = _panel.GetComponentInParent<HandbookScreen>();
            var host = screen != null ? screen.transform as RectTransform : _panel.transform.parent as RectTransform;

            if (template == null || scroll == null || host == null)
            {
                ModSourcePlugin.Log.LogWarning("[ModSource] Could not set up the handbook mod filter; it is disabled.");
                return false;
            }

            if (ModSourcePlugin.DebugLogging.Value)
            {
                ModSourcePlugin.Log.LogInfo("[ModSource] Handbook filter host: " + Path(host) + ", template: " + Path(template.transform));
            }

            var templateHeight = ((RectTransform)template.transform).rect.height;
            _height = templateHeight >= MinHeight && templateHeight <= MaxHeight ? templateHeight : FallbackHeight;
            _tree = (RectTransform)scroll.transform;

            var copy = Instantiate(template.gameObject, host, false);
            copy.name = ObjectName;
            copy.SetActive(false);

            var layout = copy.GetComponent<LayoutElement>() ?? copy.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;

            _dropdown = copy.GetComponent<DropDownBox>();
            _dropdown._maxVisibleHeight = Mathf.Max(_dropdown._maxVisibleHeight, ListHeight);
            _dropdown.OnValueChanged.Subscribe(OnSelected);

            copy.transform.SetAsLastSibling();
            Sync();
            return true;
        }

        private void LateUpdate()
        {
            Sync();

            if (_diagnosticAt > 0f && Time.unscaledTime >= _diagnosticAt && _dropdown != null)
            {
                _diagnosticAt = 0f;
                LogPlacement();
            }
        }

        private void Sync()
        {
            if (_dropdown == null || _tree == null)
            {
                return;
            }

            var rect = (RectTransform)_dropdown.transform;
            var parent = rect.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            ((RectTransform)_panel.transform).GetWorldCorners(Corners);
            var panelLeft = parent.InverseTransformPoint(Corners[0]).x;

            _tree.GetWorldCorners(Corners);
            var top = parent.InverseTransformPoint(Corners[1]).y;

            var bounds = parent.rect;
            var left = bounds.xMin + Margin;
            var right = panelLeft - Margin;
            if (right - left < MinWidth)
            {
                left = right - MinWidth;
            }

            var position = new Vector2(left - bounds.xMin, top - _height - bounds.yMin);
            var size = new Vector2(right - left, _height);

            if (rect.anchorMin != Vector2.zero || rect.anchorMax != Vector2.zero || rect.pivot != Vector2.zero)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = Vector2.zero;
            }

            if ((rect.anchoredPosition - position).sqrMagnitude > 0.01f)
            {
                rect.anchoredPosition = position;
            }

            if ((rect.sizeDelta - size).sqrMagnitude > 0.01f)
            {
                rect.sizeDelta = size;
            }
        }

        private static DropDownBox FindTemplate()
        {
            DropDownBox fallback = null;
            DropDownBox settings = null;

            foreach (var box in Resources.FindObjectsOfTypeAll<DropDownBox>())
            {
                if (box == null || box.GetType() != typeof(DropDownBox) || box.name == ObjectName)
                {
                    continue;
                }

                if (box._elementTemplate == null || box._openPanel == null)
                {
                    continue;
                }

                if (box.GetComponentInParent<InventoryClothingSelectionPanel>(true) != null)
                {
                    return box;
                }

                if (Path(box.transform).IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (settings == null)
                    {
                        settings = box;
                    }

                    continue;
                }

                if (fallback == null)
                {
                    fallback = box;
                }
            }

            return fallback ?? settings;
        }

        private static HandbookNode Prune(HandbookNode node, HandbookNode parent, HashSet<string> ids)
        {
            if (node.Data.Type == ENodeType.Item)
            {
                return ids.Contains(TemplateId(node)) ? node : null;
            }

            var copy = (HandbookNode)node.Clone();
            copy.Parent = parent;
            copy.ChildrenDict = new Dictionary<string, HandbookNode>();

            var count = 0;
            foreach (var child in node.Children)
            {
                var kept = Prune(child, copy, ids);
                if (kept == null)
                {
                    continue;
                }

                copy.ChildrenDict[kept.Id] = kept;
                count += kept.Data.Type == ENodeType.Item ? 1 : kept.Count;
            }

            if (copy.ChildrenDict.Count == 0)
            {
                return null;
            }

            copy.Count = count;
            return copy;
        }

        private static IEnumerable<HandbookNode> Flatten(IEnumerable<HandbookNode> roots)
        {
            var stack = new Stack<HandbookNode>();
            foreach (var root in roots)
            {
                if (root != null && root.Id != WishlistId)
                {
                    stack.Push(root);
                }
            }

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;

                foreach (var child in node.Children)
                {
                    stack.Push(child);
                }
            }
        }

        private static string TemplateId(HandbookNode node)
        {
            var item = node.Data.Item;
            return item != null ? item.TemplateId.ToString() : node.Data.Id;
        }

        private static bool Listed(ModSourceEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.ModName) || string.IsNullOrEmpty(entry.ModGuid))
            {
                return false;
            }

            return entry.Source != "NameMatch" || ModSourcePlugin.ShowLowConfidence.Value;
        }

        private static void Watch(Task task)
        {
            task.ContinueWith(
                t => ModSourcePlugin.Log.LogError("[ModSource] Handbook search failed: " + t.Exception),
                TaskContinuationOptions.OnlyOnFaulted
            );
        }

        private static void LogTree(Transform node, int depth)
        {
            var rect = node as RectTransform;
            var text = node.GetComponent<TMPro.TMP_Text>();
            ModSourcePlugin.Log.LogInfo(
                "[ModSource] " + new string(' ', depth * 2) + node.name + " active=" + node.gameObject.activeSelf
                    + (rect != null ? " size=" + rect.rect.size + " anchors=" + rect.anchorMin + "-" + rect.anchorMax : "")
                    + (text != null ? " text='" + text.text + "' color=" + text.color : "")
            );

            if (depth > 6)
            {
                return;
            }

            foreach (Transform child in node)
            {
                LogTree(child, depth + 1);
            }
        }

        private static string Path(Transform transform)
        {
            var parts = new List<string>();
            for (var t = transform; t != null; t = t.parent)
            {
                parts.Add(t.name);
            }

            parts.Reverse();
            return string.Join("/", parts);
        }
    }

    [HarmonyPatch(
        typeof(HandbookScreen),
        nameof(HandbookScreen.Show),
        typeof(InventoryController),
        typeof(IEftSession),
        typeof(Handbook),
        typeof(IHealthController)
    )]
    public static class HandbookScreenShowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(HandbookScreen __instance)
        {
            try
            {
                HandbookModFilter.Attach(__instance._categoriesPanel);
            }
            catch (Exception ex)
            {
                ModSourcePlugin.Log.LogError("[ModSource] Handbook mod filter failed: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(BrowseCategoriesPanel), nameof(BrowseCategoriesPanel.Allowed))]
    public static class HandbookAllowedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BrowseCategoriesPanel __instance, HandbookNode node, ref bool __result)
        {
            if (!__result || !HandbookModFilter.IsActiveFor(__instance))
            {
                return;
            }

            __result = HandbookModFilter.IsModItem(node, HandbookModFilter.Current.ActiveIds);
        }
    }

    [HarmonyPatch(typeof(BrowseCategoriesPanel), nameof(BrowseCategoriesPanel.Filter))]
    public static class HandbookFilterPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BrowseCategoriesPanel __instance, string value)
        {
            try
            {
                if (!HandbookModFilter.IsActiveFor(__instance) || (value != null && value.Length >= 3))
                {
                    return;
                }

                var filter = HandbookModFilter.Current;
                if (filter.WasReset())
                {
                    filter.Apply();
                }
            }
            catch (Exception ex)
            {
                ModSourcePlugin.Log.LogError("[ModSource] Handbook mod filter failed: " + ex);
            }
        }
    }
}
