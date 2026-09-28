using System.Collections.Generic;
using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    public PlayerInventory playerInventory;
    public GameObject slotPrefab;
    public Transform gridParent; // Assign InventoryPanel (with Grid Layout Group)

    private List<ItemSlotUI> slotUIList = new List<ItemSlotUI>();

    void Start()
    {
        InitializeSlots();
    }

    void Update()
    {
        // Toggle Inventory with 'I' or 'Tab'
        if (UnityEngine.InputSystem.Keyboard.current.iKey.wasPressedThisFrame)
        {
            gameObject.SetActive(!gameObject.activeSelf);
            if (gameObject.activeSelf)
            {
                RefreshUI();
            }
        }
    }

    private void InitializeSlots()
    {
        // Clear old children
        foreach (Transform child in gridParent)
        {
            Destroy(child.gameObject);
        }
        slotUIList.Clear();

        // Instantiate slots based on max inventory capacity
        for (int i = 0; i < playerInventory.maxSlots; i++)
        {
            GameObject slotObj = Instantiate(slotPrefab, gridParent);
            ItemSlotUI slotUI = slotObj.GetComponent<ItemSlotUI>();
            slotUI.ClearSlot();
            slotUIList.Add(slotUI);
        }

        RefreshUI();
    }

    public void RefreshUI()
    {
        for (int i = 0; i < slotUIList.Count; i++)
        {
            if (i < playerInventory.items.Count)
            {
                slotUIList[i].SetItem(playerInventory.items[i]);
            }
            else
            {
                slotUIList[i].ClearSlot();
            }
        }
    }
}