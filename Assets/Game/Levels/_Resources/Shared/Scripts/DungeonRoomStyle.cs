using System;
using UnityEngine;
using Sirenix.OdinInspector;

[CreateAssetMenu(menuName = "Table/Room Style", fileName = "Room Style")]
public class DungeonRoomStyle : ScriptableObject
{
    [Tooltip("First vertical tile. Empty inherits the level wall material.")]
    public Material bottomWall;
    [Tooltip("All tiles above the first. Empty inherits the level wall material.")]
    public Material upperWall;
    [Tooltip("Empty inherits the level floor material.")]
    public Material floor;
    [Tooltip("Empty inherits the level ceiling material.")]
    public Material ceiling;
    [Tooltip("Do not generate cornices or footings in rooms or corridors using this style. Reload the dungeon to apply.")]
    public bool hideTrims;
    [HideIf(nameof(hideTrims)), Tooltip("Cornice and footing. Empty inherits the level trim material.")]
    public Material trim;

    // Unity serializes empty object references as wrappers that can be CLR-non-null.
    // Use Unity's equality check, not ??, when resolving optional materials.
    public static Material Resolve(Material selected, Material fallback) => selected != null ? selected : fallback;
    public Material Wall(float bottomHeight, float tileSize, Material fallback) =>
        Resolve(bottomHeight < tileSize - .001f ? bottomWall : upperWall, fallback);

    public static DungeonRoomStyle Choose(DungeonStyleChoice[] choices, System.Random random)
    {
        double total = 0;
        if (choices == null) return null;
        foreach (var choice in choices)
            if (Eligible(choice)) total += choice.weight;
        if (total <= 0) return null;
        double roll = random.NextDouble() * total;
        foreach (var choice in choices)
        {
            if (!Eligible(choice)) continue;
            roll -= choice.weight;
            if (roll < 0) return choice.style;
        }
        return null;
    }
    static bool Eligible(DungeonStyleChoice choice) => choice != null && choice.style != null
        && choice.weight > 0 && !float.IsInfinity(choice.weight) && !float.IsNaN(choice.weight);
}

[Serializable]
public class DungeonStyleChoice
{
    [InlineEditor]
    public DungeonRoomStyle style;
    [Min(0), Tooltip("Relative likelihood per whole room or corridor section. Zero disables this entry.")]
    public float weight = 1;
}
