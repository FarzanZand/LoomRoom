using UnityEngine;
using Sirenix.OdinInspector;

// Which room roles something may go in. Serialized by value: append only.
[System.Flags]
public enum DungeonRoomRoles { None = 0, Entrance = 1, Combat = 2, Treasure = 4, Rest = 8, Storage = 16, Exit = 32, Any = 63 }

// A prefab with a weight, for weighted picks (breakables, wall lights).
[System.Serializable]
public class DungeonWeightedPrefab
{
    [AssetsOnly] public GameObject prefab;
    [Min(0)] public float weight = 1;
    public static GameObject Choose(DungeonWeightedPrefab[] choices, System.Random random, System.Func<GameObject,bool> filter = null)
    {
        var pick = WeightedPick.Choose(choices, c => c != null && c.prefab != null && (filter == null || filter(c.prefab)) ? c.weight : 0, random);
        return pick != null ? pick.prefab : null;
    }
}

// A fountain, altar, grave or bookshelf placed by chance in eligible rooms.
[System.Serializable]
public class DungeonFeature
{
    [AssetsOnly, Tooltip("Fountain, altar, grave or bookshelf prefab. Must fit in one cell.")]
    public GameObject prefab;
    [Range(0, 1)] public float chancePerRoom = .12f;
    [Tooltip("Against Wall, Corner and Centre work as for Furnishing. Anywhere picks any free cell and faces the room centre.")]
    public DungeonPropPlacement placement = DungeonPropPlacement.Anywhere;
    public DungeonRoomRoles rooms = DungeonRoomRoles.Combat | DungeonRoomRoles.Treasure | DungeonRoomRoles.Rest | DungeonRoomRoles.Storage;
    [Min(1)] public int minFloor = 1;
    [Min(0), Tooltip("Zero means no limit.")] public int maxPerFloor = 2;
}
