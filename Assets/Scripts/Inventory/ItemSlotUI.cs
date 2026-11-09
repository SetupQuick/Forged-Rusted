using System;
using Items;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameUI
{
    /// <summary>
    /// One inventory/hotbar slot: background, item icon, stack count and
    /// a selection frame. Forwards clicks and hover to its owner.
    /// </summary>
    public class ItemSlotUI : MonoBehaviour,
        IPointerClickHandler,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        public Image iconImage;

        private Image _background;
        private Outline _frame;
        private TextMeshProUGUI _countText;
        private bool _hovered;
        private bool _selected;

        public int SlotIndex { get; private set; }

        public event Action<ItemSlotUI, PointerEventData> Clicked;
        public event Action<ItemSlotUI> HoverChanged;

        public bool IsHovered => _hovered;

        /// <summary>Builds a slot from code.</summary>
        public static ItemSlotUI Create(Transform parent, int index, float size, bool interactive)
        {
            Image background = UiBuilder.Image($"Slot {index}", parent, UiBuilder.SlotColor, interactive);
            RectTransform rect = background.rectTransform;
            rect.sizeDelta = new Vector2(size, size);

            // Selection frame: an outline around the background.
            var frame = background.gameObject.AddComponent<Outline>();
            frame.effectColor = UiBuilder.AccentColor;
            frame.effectDistance = new Vector2(3f, -3f);
            frame.enabled = false;

            Image icon = UiBuilder.Image("Icon", rect, Color.white);
            UiBuilder.Stretch(icon.rectTransform);
            float inset = size * 0.12f;
            icon.rectTransform.offsetMin = new Vector2(inset, inset);
            icon.rectTransform.offsetMax = new Vector2(-inset, -inset);
            icon.preserveAspect = true;

            TextMeshProUGUI count = UiBuilder.Text("Count", rect, size * 0.3f, TextAlignmentOptions.BottomRight, UiBuilder.TextColor);
            UiBuilder.Stretch(count.rectTransform);
            count.rectTransform.offsetMin = new Vector2(2, 1);
            count.rectTransform.offsetMax = new Vector2(-4, -2);
            count.fontStyle = FontStyles.Bold;
            UiBuilder.AddOutline(count, new Color(0, 0, 0, 0.8f), 1.5f);

            var slot = background.gameObject.AddComponent<ItemSlotUI>();
            slot.SlotIndex = index;
            slot.iconImage = icon;
            slot._background = background;
            slot._frame = frame;
            slot._countText = count;
            slot.ClearSlot();

            return slot;
        }

        public void SetStack(ItemStack stack)
        {
            if (stack.IsEmpty)
            {
                ClearSlot();
                return;
            }

            if (iconImage != null)
            {
                iconImage.sprite = ItemIcons.Get(stack.Item);
                iconImage.enabled = iconImage.sprite != null;
            }

            if (_countText != null)
                _countText.text = stack.Count > 1 ? stack.Count.ToString() : string.Empty;
        }

        /// <summary>Kept for older callers.</summary>
        public void SetItem(ItemData item)
        {
            SetStack(new ItemStack(item, 1));
        }

        public void ClearSlot()
        {
            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
            }

            if (_countText != null)
                _countText.text = string.Empty;
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;

            if (_frame != null)
                _frame.enabled = selected;

            RefreshBackground();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Clicked?.Invoke(this, eventData);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            RefreshBackground();
            HoverChanged?.Invoke(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            RefreshBackground();
            HoverChanged?.Invoke(this);
        }

        private void OnDisable()
        {
            _hovered = false;
            RefreshBackground();
        }

        private void RefreshBackground()
        {
            if (_background != null)
                _background.color = _hovered || _selected ? UiBuilder.SlotHoverColor : UiBuilder.SlotColor;
        }
    }
}
