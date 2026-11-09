using UnityEngine;

namespace Items
{
    /// <summary>
    /// An item lying in the world that the local player can pick up
    /// (look at it and press E). Needs a collider.
    /// </summary>
    public class PickupItem : MonoBehaviour
    {
        public ItemData itemData;

        [Min(1)]
        public int amount = 1;

        public void OnPickedUp()
        {
            Destroy(gameObject);
        }
    }
}
