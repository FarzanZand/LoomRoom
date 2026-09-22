using UnityEngine;

// A pickup in the world. Either authored as a prefab variant with the mesh already
// inside, or spawned from data at runtime (InventoryManager.Spawn) in which case the
// item's worldPrefab is instantiated as the visual.
public class WorldItem : MonoBehaviour, IInteractable
{
    [SerializeField] ItemData itemData;
    [SerializeField, Min(1)] int count = 1;
    [Tooltip("Which player can pick up this item. Tabletop loot belongs to the Table player.")]
    [SerializeField] PlayerKind pickupPlayer = PlayerKind.Table;
    [Tooltip("Spawn the item's worldPrefab as the visual on Start (for pickups placed without a mesh).")]
    [SerializeField] bool worldPrefabFromData = false;

    bool visualSpawned;
    bool claimed;

    public ItemData Item  => itemData;
    public int      Count => count;

    public string Prompt => itemData != null ? $"Pick up {itemData.itemName}" : "Pick up";
    public bool CanInteract(Character who) =>
        !claimed && itemData != null && who is Player player && player.kind == pickupPlayer;

    void Start()
    {
        if (worldPrefabFromData && !visualSpawned) SpawnVisual();
    }

    public void Init(ItemData data, int amount = 1)
    {
        itemData = data;
        claimed = false;
        count    = Mathf.Max(1, amount);
        worldPrefabFromData = true;
        SpawnVisual();
    }

    void SpawnVisual()
    {
        if (visualSpawned || itemData == null) return;
        var model = itemData.pickupVisualPrefab != null ? itemData.pickupVisualPrefab : itemData.worldPrefab;
        if(model == null && InventoryManager.HasInstance) model = InventoryManager.Instance.defaultPickupVisual;
        if (model == null) return;
        visualSpawned = true;
        var visual = Instantiate(model, transform);
        visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var mc in visual.GetComponentsInChildren<MeshCollider>()) mc.convex = true;
    }

    public void Interact(Character who)
    {
        if (!CanInteract(who) || !(who is Player player) || !InventoryManager.HasInstance) return;
        if (InventoryManager.Instance.Pickup(itemData, player, count))
        {
            claimed = true;
            Destroy(gameObject);
        }
    }
}
