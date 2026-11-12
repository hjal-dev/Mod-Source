using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModSource
{
    internal sealed class HoverHighlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const string OverlayName = "ModSourceHoverHighlight";
        private static readonly Color OverlayColor = new Color(1f, 1f, 1f, 0.12f);

        private GameObject _overlay;

        public static void Attach(GameObject target, bool highlight)
        {
            if (target == null || target.GetComponent<HoverHighlight>() != null)
            {
                return;
            }

            if (highlight)
            {
                var overlay = new GameObject(OverlayName, typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)overlay.transform;
                rect.SetParent(target.transform, false);
                rect.SetSiblingIndex(TextSiblingIndex(target.transform));
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                var image = overlay.GetComponent<Image>();
                image.color = OverlayColor;
                image.raycastTarget = false;

                var layout = overlay.AddComponent<LayoutElement>();
                layout.ignoreLayout = true;

                overlay.SetActive(false);
            }

            target.AddComponent<HoverHighlight>();
        }

        private static int TextSiblingIndex(Transform root)
        {
            var texts = root.GetComponentsInChildren<TMPro.TMP_Text>(true);
            if (texts.Length == 0)
            {
                return 0;
            }

            var node = texts[0].transform;
            while (node.parent != null && node.parent != root)
            {
                node = node.parent;
            }

            return node.parent == root ? node.GetSiblingIndex() : 0;
        }

        private void Awake()
        {
            var overlay = transform.Find(OverlayName);
            _overlay = overlay != null ? overlay.gameObject : null;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            SetHovered(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetHovered(false);
        }

        private void OnDisable()
        {
            SetHovered(false);
        }

        private void SetHovered(bool hovered)
        {
            if (_overlay != null)
            {
                _overlay.SetActive(hovered);
            }

            foreach (var marquee in GetComponentsInChildren<MarqueeText>(true))
            {
                marquee.SetHovered(hovered);
            }
        }
    }
}
