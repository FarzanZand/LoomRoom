using UnityEngine;

public static class WorldItemSpawner
{
    private static GameObject _prefab;

    // Spawns an item pickup at the given position.
    public static WorldItem Spawn(ItemData item, Vector3 position, Quaternion rotation)
    {
        if (item == null) return null;

        if (_prefab == null)
            _prefab = Resources.Load<GameObject>("ItemPickup");

        if (_prefab == null)
        {
            Debug.LogWarning("[WorldItemSpawner] 'ItemPickup' prefab not found in Assets/Resources/.");
            return null;
        }

        var go        = Object.Instantiate(_prefab, position, rotation);
        var worldItem = go.GetComponent<WorldItem>();
        worldItem.Init(item);
        return worldItem;
    }

    public static WorldItem Spawn(ItemData item, Vector3 position)
        => Spawn(item, position, Quaternion.identity);
}
