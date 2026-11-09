using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GameUI
{
    /// <summary>
    /// Menu building blocks created from code: buttons, sliders, option
    /// selectors ("&lt; value &gt;"), text fields, headers and rows. Shared
    /// look for the main menu, pause menu and settings.
    /// </summary>
    public static class MenuWidgets
    {
        public static readonly Color ButtonColor = new Color(0.17f, 0.18f, 0.22f, 0.92f);
        public static readonly Color ButtonHover = new Color(0.27f, 0.29f, 0.35f, 0.96f);
        public static readonly Color ButtonPressed = new Color(0.36f, 0.3f, 0.18f, 1f);
        public static readonly Color PrimaryColor = new Color(0.86f, 0.6f, 0.2f, 0.95f);
        public static readonly Color FieldColor = new Color(0.09f, 0.1f, 0.12f, 0.95f);

        public const float RowHeight = 54f;

        /// <summary>A vertical stack that sizes to its children.</summary>
        public static RectTransform Column(Transform parent, float spacing = 10f, float width = 520f)
        {
            RectTransform rect = UiBuilder.Rect("Column", parent);
            rect.sizeDelta = new Vector2(width, 0f);

            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return rect;
        }

        public static TextMeshProUGUI Header(Transform parent, string text, float size = 26f)
        {
            TextMeshProUGUI label = UiBuilder.Text("Header", parent, size, TextAlignmentOptions.BottomLeft, UiBuilder.AccentColor);
            label.rectTransform.sizeDelta = new Vector2(0f, size + 18f);
            label.text = text;
            label.fontStyle = FontStyles.Bold;
            return label;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float size = 20f, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            TextMeshProUGUI label = UiBuilder.Text("Label", parent, size, alignment, UiBuilder.TextColor);
            label.rectTransform.sizeDelta = new Vector2(0f, size + 14f);
            label.text = text;
            return label;
        }

        public static Button Button(Transform parent, string text, Action onClick, bool primary = false, float height = RowHeight)
        {
            Image background = UiBuilder.Image("Button " + text, parent, primary ? PrimaryColor : ButtonColor, raycast: true);
            background.rectTransform.sizeDelta = new Vector2(0f, height);
            UiBuilder.AddOutline(background, new Color(0f, 0f, 0f, 0.5f), 2f);

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            colors.colorMultiplier = 1.4f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            TextMeshProUGUI label = UiBuilder.Text("Text", background.rectTransform, 24, TextAlignmentOptions.Center, UiBuilder.TextColor);
            UiBuilder.Stretch(label.rectTransform);
            label.text = text;
            label.fontStyle = FontStyles.Bold;

            if (onClick != null)
                button.onClick.AddListener(new UnityAction(onClick));

            return button;
        }

        /// <summary>Row with a label on the left and a slider on the right.</summary>
        public static Slider Slider(
            Transform parent,
            string text,
            float min,
            float max,
            float value,
            Func<float, string> format,
            Action<float> onChanged,
            bool wholeNumbers = false)
        {
            RectTransform row = Row(parent, text, out RectTransform right);

            TextMeshProUGUI valueText = UiBuilder.Text("Value", right, 20, TextAlignmentOptions.Right, UiBuilder.SubtleTextColor);
            UiBuilder.Place(valueText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(90f, 30f));

            // Track
            RectTransform sliderRect = UiBuilder.Rect("Slider", right);
            sliderRect.anchorMin = new Vector2(0f, 0.5f);
            sliderRect.anchorMax = new Vector2(1f, 0.5f);
            sliderRect.offsetMin = new Vector2(0f, -10f);
            sliderRect.offsetMax = new Vector2(-104f, 10f);

            Image track = UiBuilder.Image("Track", sliderRect, FieldColor, raycast: true);
            UiBuilder.Stretch(track.rectTransform);
            track.rectTransform.offsetMin = new Vector2(0f, 6f);
            track.rectTransform.offsetMax = new Vector2(0f, -6f);

            RectTransform fillArea = UiBuilder.Rect("Fill Area", sliderRect);
            UiBuilder.Stretch(fillArea);
            fillArea.offsetMin = new Vector2(0f, 6f);
            fillArea.offsetMax = new Vector2(0f, -6f);

            Image fill = UiBuilder.Image("Fill", fillArea, PrimaryColor);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;

            RectTransform handleArea = UiBuilder.Rect("Handle Area", sliderRect);
            UiBuilder.Stretch(handleArea);
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);

            Image handle = UiBuilder.Image("Handle", handleArea, UiBuilder.TextColor, raycast: true);
            handle.rectTransform.sizeDelta = new Vector2(16f, 0f);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0f, 1f);

            var slider = sliderRect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            slider.SetValueWithoutNotify(value);

            valueText.text = format(value);

            slider.onValueChanged.AddListener(v =>
            {
                valueText.text = format(v);
                onChanged(v);
            });

            return slider;
        }

        /// <summary>Row with "&lt; option &gt;" to cycle through choices.</summary>
        public static void Selector(
            Transform parent,
            string text,
            string[] options,
            int index,
            Action<int> onChanged)
        {
            RectTransform row = Row(parent, text, out RectTransform right);

            if (options.Length == 0)
                options = new[] { "-" };

            int current = Mathf.Clamp(index, 0, options.Length - 1);

            TextMeshProUGUI value = UiBuilder.Text("Value", right, 21, TextAlignmentOptions.Center, UiBuilder.TextColor);
            UiBuilder.Stretch(value.rectTransform);
            value.rectTransform.offsetMin = new Vector2(48f, 0f);
            value.rectTransform.offsetMax = new Vector2(-48f, 0f);
            value.text = options[current];

            void Step(int delta)
            {
                current = (current + delta + options.Length) % options.Length;
                value.text = options[current];
                onChanged(current);
            }

            Button left = SmallButton(right, "<", () => Step(-1));
            UiBuilder.Place((RectTransform)left.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(42f, 40f));

            Button rightButton = SmallButton(right, ">", () => Step(1));
            UiBuilder.Place((RectTransform)rightButton.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(42f, 40f));
        }

        public static void Toggle(Transform parent, string text, bool value, Action<bool> onChanged)
        {
            Selector(parent, text, new[] { "Off", "On" }, value ? 1 : 0, i => onChanged(i == 1));
        }

        /// <summary>Row with a label and a text field.</summary>
        public static TMP_InputField TextField(Transform parent, string text, string value, string placeholder, Action<string> onChanged, TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard)
        {
            RectTransform row = Row(parent, text, out RectTransform right);

            Image background = UiBuilder.Image("Field", right, FieldColor, raycast: true);
            UiBuilder.Stretch(background.rectTransform);
            background.rectTransform.offsetMin = new Vector2(0f, 6f);
            background.rectTransform.offsetMax = new Vector2(0f, -6f);

            RectTransform viewport = UiBuilder.Rect("Text Area", background.rectTransform);
            UiBuilder.Stretch(viewport);
            viewport.offsetMin = new Vector2(12f, 4f);
            viewport.offsetMax = new Vector2(-12f, -4f);
            viewport.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI hint = UiBuilder.Text("Placeholder", viewport, 21, TextAlignmentOptions.Left, new Color(1f, 1f, 1f, 0.3f));
            UiBuilder.Stretch(hint.rectTransform);
            hint.text = placeholder;

            TextMeshProUGUI textComponent = UiBuilder.Text("Text", viewport, 21, TextAlignmentOptions.Left, UiBuilder.TextColor);
            UiBuilder.Stretch(textComponent.rectTransform);

            var field = background.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = viewport;
            field.textComponent = textComponent;
            field.placeholder = hint;
            field.contentType = contentType;
            field.targetGraphic = background;
            field.text = value;
            field.onValueChanged.AddListener(v => onChanged(v));

            return field;
        }

        /// <summary>A row with a label on the left; returns the right-hand area.</summary>
        public static RectTransform Row(Transform parent, string text, out RectTransform right)
        {
            RectTransform row = UiBuilder.Rect("Row " + text, parent);
            row.sizeDelta = new Vector2(0f, RowHeight);

            TextMeshProUGUI label = UiBuilder.Text("Label", row, 21, TextAlignmentOptions.Left, UiBuilder.TextColor);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(0.45f, 1f);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.text = text;

            right = UiBuilder.Rect("Control", row);
            right.anchorMin = new Vector2(0.45f, 0f);
            right.anchorMax = new Vector2(1f, 1f);
            right.offsetMin = Vector2.zero;
            right.offsetMax = Vector2.zero;

            return row;
        }

        /// <summary>Two-column text row (controls list).</summary>
        public static void KeyValue(Transform parent, string key, string value)
        {
            RectTransform row = UiBuilder.Rect("Row " + key, parent);
            row.sizeDelta = new Vector2(0f, 34f);

            TextMeshProUGUI left = UiBuilder.Text("Action", row, 20, TextAlignmentOptions.Left, UiBuilder.TextColor);
            left.rectTransform.anchorMin = Vector2.zero;
            left.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            left.rectTransform.offsetMin = Vector2.zero;
            left.rectTransform.offsetMax = Vector2.zero;
            left.text = key;

            TextMeshProUGUI right = UiBuilder.Text("Keys", row, 20, TextAlignmentOptions.Right, UiBuilder.AccentColor);
            right.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            right.rectTransform.anchorMax = Vector2.one;
            right.rectTransform.offsetMin = Vector2.zero;
            right.rectTransform.offsetMax = Vector2.zero;
            right.text = value;
        }

        public static Image Panel(Transform parent, Vector2 size)
        {
            Image panel = UiBuilder.Image("Panel", parent, UiBuilder.PanelColor, raycast: true);
            UiBuilder.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            UiBuilder.AddOutline(panel, new Color(0f, 0f, 0f, 0.6f), 3f);
            return panel;
        }

        private static Button SmallButton(Transform parent, string text, Action onClick)
        {
            Image background = UiBuilder.Image("Button " + text, parent, ButtonColor, raycast: true);

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            ColorBlock colors = button.colors;
            colors.colorMultiplier = 1.4f;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            button.colors = colors;

            TextMeshProUGUI label = UiBuilder.Text("Text", background.rectTransform, 24, TextAlignmentOptions.Center, UiBuilder.TextColor);
            UiBuilder.Stretch(label.rectTransform);
            label.text = text;
            label.fontStyle = FontStyles.Bold;

            button.onClick.AddListener(new UnityAction(onClick));
            return button;
        }
    }
}
