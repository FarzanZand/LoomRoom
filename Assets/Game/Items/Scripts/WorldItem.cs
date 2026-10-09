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
    GameObject sparkle;

    public ItemData Item  => itemData;
    public int      Count => count;

    public string Prompt => itemData != null ? $"Pick up {itemData.itemName}" : "Pick up";
    public bool CanInteract(Character who) =>
        !claimed && itemData != null && who is Player player && player.kind == pickupPlayer;

    void Start()
    {
        if (worldPrefabFromData && !visualSpawned) SpawnVisual();
        AddSparkle();
    }

    // The InventoryManager's sparkle, centred on the item and sized to it. Added once the item has
    // settled (Start runs after DungeonPickup has scaled and placed it).
    void AddSparkle()
    {
        if (sparkle != null || !InventoryManager.HasInstance || InventoryManager.Instance.pickupSparkle == null) return;
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        sparkle = Instantiate(InventoryManager.Instance.pickupSparkle, bounds.center, Quaternion.identity, transform);
        foreach (var ps in sparkle.GetComponentsInChildren<ParticleSystem>())
        {
            var shape = ps.shape;
            shape.scale = Vector3.Max(bounds.size, Vector3.one * .15f);
            shape.scale = new Vector3(shape.scale.x / sparkle.transform.lossyScale.x, shape.scale.y / sparkle.transform.lossyScale.y, shape.scale.z / sparkle.transform.lossyScale.z);
        }
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
