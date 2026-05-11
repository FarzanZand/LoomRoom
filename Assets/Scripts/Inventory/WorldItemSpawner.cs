using UnityEngine;

public class WorldItemSpawner : Singleton<WorldItemSpawner>
{
    [SerializeField] private GameObject pickupPrefab;

    public static WorldItem Spawn(ItemData item, Vector3 position, Quaternion rotation)
    {
        if (item == null) return null;

        var prefab = Instance?.pickupPrefab;
        if (prefab == null)
        {
            Debug.LogWarning("[WorldItemSpawner] No pickup prefab assigned.");
            return null;
        }

        var go        = Instantiate(prefab, position, rotation);
        var worldItem = go.GetComponent<WorldItem>();
        worldItem.WorldPrefabFromData = true;
        worldItem.Init(item);
        return worldItem;
    }

    public static WorldItem Spawn(ItemData item, Vector3 position)
        => Spawn(item, position, Quaternion.identity);
}
