using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class PlayerInventory : MonoBehaviour
{
    [Header("Settings")]
    public int maxSlots = 20;
    public Camera playerCamera;

    [Header("UI References")]
    public GameObject interactionPromptUI;
    public TMP_Text promptText;

    [Header("Data")]
    public List<ItemData> items = new List<ItemData>();

    private PickupItem currentPickupTarget;
    private float pickupDistance = 5f; // Automatically updated from BlockInteraction

    private void Start()
    {
    }

    void Update()
    {
        CheckForPickupTarget();

        // Press 'E' to pick up
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (currentPickupTarget != null)
            {
                if (AddItem(currentPickupTarget.itemData))
                {
                    currentPickupTarget.OnPickedUp();
                    HidePrompt();
                }
            }
        }
    }

    private void CheckForPickupTarget()
    {
        if (playerCamera == null) playerCamera = Camera.main;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, pickupDistance))
        {
            PickupItem pickup = hit.collider.GetComponentInParent<PickupItem>();
            if (pickup != null)
            {
                currentPickupTarget = pickup;
                ShowPrompt(pickup);
                return;
            }
        }

        // If raycast hits nothing pickable
        currentPickupTarget = null;
        HidePrompt();
    }

    private void ShowPrompt(PickupItem pickup)
    {
        if (interactionPromptUI != null)
        {
            interactionPromptUI.SetActive(true);
            if (promptText != null && pickup.itemData != null)
            {
                promptText.text = $"[E] Pick up {pickup.itemData.itemName}";
            }
        }
    }

    private void HidePrompt()
    {
        if (interactionPromptUI != null)
        {
            interactionPromptUI.SetActive(false);
        }
    }

    public bool AddItem(ItemData newItem)
    {
        if (items.Count >= maxSlots)
        {
            Debug.Log("Inventory is full!");
            return false;
        }

        items.Add(newItem);
        Debug.Log($"Picked up: {newItem.itemName}");
        return true;
    }
}