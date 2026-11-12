using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModSource
{
    internal sealed class MarqueeText : MonoBehaviour
    {
        private const string ViewportName = "ModSourceMarqueeViewport";
        private const float Speed = 40f;
        private const float Hold = 1.2f;
        private const float EndPadding = 6f;

        private TMP_Text _text;
        private RectTransform _viewport;
        private Vector4 _margin;
        private TextAlignmentOptions _alignment;
        private TextOverflowModes _overflow;
        private bool _wrapping;
        private string _measured;
        private float _distance;
        private float _startedAt;
        private bool _scrolling;
        private bool _hovered;

        public static void Attach(TMP_Text text)
        {
            if (text == null || text.GetComponent<MarqueeText>() != null)
            {
                return;
            }

            var rect = text.rectTransform;
            var parent = rect.parent;

            var viewportObject = new GameObject(ViewportName, typeof(RectTransform), typeof(RectMask2D));
            var viewport = (RectTransform)viewportObject.transform;
            viewport.SetParent(parent, false);
            viewport.SetSiblingIndex(rect.GetSiblingIndex());
            viewport.anchorMin = rect.anchorMin;
            viewport.anchorMax = rect.anchorMax;
            viewport.pivot = rect.pivot;
            viewport.anchoredPosition = rect.anchoredPosition;
            viewport.sizeDelta = rect.sizeDelta;
            viewport.localScale = rect.localScale;
            viewport.localRotation = rect.localRotation;

            var layout = viewportObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;

            rect.SetParent(viewport, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            text.gameObject.AddComponent<MarqueeText>();
        }

        private void Awake()
        {
            _text = GetComponent<TMP_Text>();
            _viewport = transform.parent as RectTransform;
            _margin = _text.margin;
            _alignment = _text.alignment;
            _overflow = _text.overflowMode;
            _wrapping = _text.enableWordWrapping;
        }

        private void OnEnable()
        {
            _measured = null;
        }

        private void LateUpdate()
        {
            if (_text == null || _viewport == null)
            {
                return;
            }

            if (_measured != _text.text)
            {
                Measure();
            }

            var active = _hovered || ModSourcePlugin.AlwaysScrollModNames.Value;
            if (!active && _scrolling)
            {
                Restore();
            }
            else if (active && !_scrolling && _distance > EndPadding)
            {
                Begin();
            }

            if (!_scrolling)
            {
                return;
            }

            var offset = Offset(Time.unscaledTime - _startedAt);
            _text.margin = new Vector4(_margin.x - offset, _margin.y, _margin.z + offset, _margin.w);
        }

        public void SetHovered(bool hovered)
        {
            _hovered = hovered;
        }

        private void Measure()
        {
            _measured = _text.text;
            _distance = 0f;
            Restore();

            var width = _viewport.rect.width - _margin.x - _margin.z;
            if (width <= 1f || string.IsNullOrEmpty(_measured))
            {
                _measured = width <= 1f ? null : _measured;
                return;
            }

            var needed = _text.GetPreferredValues(_measured, float.PositiveInfinity, float.PositiveInfinity).x - _margin.x - _margin.z;
            _distance = needed - width + EndPadding;
        }

        private void Begin()
        {
            _scrolling = true;
            _startedAt = Time.unscaledTime;
            _text.enableWordWrapping = false;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private void Restore()
        {
            _scrolling = false;
            _text.margin = _margin;
            _text.alignment = _alignment;
            _text.overflowMode = _overflow;
            _text.enableWordWrapping = _wrapping;
        }

        private float Offset(float time)
        {
            var travel = _distance / Speed;
            var cycle = (Hold + travel) * 2f;
            var t = time % cycle;

            if (t < Hold)
            {
                return 0f;
            }

            t -= Hold;
            if (t < travel)
            {
                return t * Speed;
            }

            t -= travel;
            if (t < Hold)
            {
                return _distance;
            }

            t -= Hold;
            return _distance - t * Speed;
        }
    }
}
