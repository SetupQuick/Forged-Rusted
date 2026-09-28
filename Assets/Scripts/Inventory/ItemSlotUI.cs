using UnityEngine;
using UnityEngine.UI;

public class ItemSlotUI : MonoBehaviour
{
    public Image iconImage;

    public void SetItem(ItemData item)
    {
        if (item != null && item.icon != null && iconImage != null)
        {
            iconImage.sprite = item.icon;
            iconImage.enabled = true;
        }
        else
        {
            ClearSlot();
        }
    }

    public void ClearSlot()
    {
        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }
    }
}