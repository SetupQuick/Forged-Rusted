using UnityEngine;

public class PickupItem : MonoBehaviour
{
    public ItemData itemData;
    public int amount = 1;

    public void OnPickedUp()
    {
        Destroy(gameObject);
    }
}