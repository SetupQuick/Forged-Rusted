using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI
{
    /// <summary>
    /// Small helpers for building uGUI hierarchies from code, so the HUD
    /// works without any scene or prefab setup.
    /// </summary>
    public static class UiBuilder
    {
        public static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.11f, 0.82f);
        public static readonly Color SlotColor = new Color(0.16f, 0.17f, 0.2f, 0.85f);
        public static readonly Color SlotHoverColor = new Color(0.26f, 0.28f, 0.33f, 0.92f);
        public static readonly Color AccentColor = new Color(0.98f, 0.78f, 0.32f, 1f);
        public static readonly Color TextColor = new Color(0.95f, 0.95f, 0.93f, 1f);
        public static readonly Color SubtleTextColor = new Color(0.72f, 0.74f, 0.78f, 1f);

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static RectTransform Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        public static RectTransform Place(
            RectTransform rect,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static Image Image(string name, Transform parent, Color color, bool raycast = false)
        {
            RectTransform rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static TextMeshProUGUI Text(
            string name,
            Transform parent,
            float size,
            TextAlignmentOptions alignment,
            Color color)
        {
            RectTransform rect = Rect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        public static void AddOutline(Graphic graphic, Color color, float distance = 2f)
        {
            var outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(distance, -distance);
        }
    }
}
