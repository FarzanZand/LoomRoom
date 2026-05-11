using UnityEngine;

public class WorldItem : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemData itemData;
    [SerializeField] private AudioClip pickupAudio;
    [SerializeField] [Range(0f, 1f)] private float pickupAudioVolume = 1f;
    [SerializeField] private bool worldPrefabFromData = false;
    public bool WorldPrefabFromData { get => worldPrefabFromData; set => worldPrefabFromData = value; }

    private bool _visualSpawned;

    public string PromptMessage => itemData != null ? $"Pick up {itemData.itemName}" : "Pick up";

    private void Start()
    {
        if (worldPrefabFromData && !_visualSpawned)
            SpawnVisual();
    }

    // Called by WorldItemSpawner at runtime — sets itemData and immediately spawns the visual.
    public void Init(ItemData data)
    {
        itemData = data;
        SpawnVisual();
    }

    private void SpawnVisual()
    {
        if (_visualSpawned || itemData?.worldPrefab == null) return;
        _visualSpawned = true;
        var visual = Instantiate(itemData.worldPrefab, transform);
        visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var mc in visual.GetComponentsInChildren<MeshCollider>())
            mc.convex = true;
    }

    public void Interact(GameObject interactor)
    {
        if (itemData == null) return;

        bool hotbarCandidate = itemData.itemType == ItemType.Weapon
                            || itemData.itemType == ItemType.Shield
                            || itemData.itemType == ItemType.Tool
                            || itemData.itemType == ItemType.Consumable;

        bool added = hotbarCandidate && HotbarSystem.Instance.TryAdd(itemData);
        if (!added) added = InventorySystem.Instance.TryAdd(itemData);

        if (!added)
        {
            Debug.Log("[WorldItem] Inventory full.");
            return;
        }

        if (pickupAudio != null)
            AudioManager.Instance.PlaySFX2D(pickupAudio, pickupAudioVolume);
        if (itemData.equipOnPickup && ItemHolder.Instance.GetHeldItem(itemData.equipSlot) == null)
            ItemHolder.Instance.HoldItem(itemData);
        Destroy(transform.parent != null ? transform.parent.gameObject : gameObject);
    }
}
