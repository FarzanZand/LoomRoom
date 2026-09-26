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

    [Title("Wall variety", "Each wall tile draws its material: the wall material above with Base Weight, or one of these with its own weight.", HorizontalLine = false)]
    [Min(0), LabelText("Base Weight"), Tooltip("Weight of Bottom Wall / Upper Wall in the draw. 10 against two variants of weight 1 keeps the base on about 5 of 6 tiles.")]
    public float wallBaseWeight = 10;
    public DungeonSurfaceVariant[] wallVariants = new DungeonSurfaceVariant[0];
    [Title("Floor variety", "Each floor cell draws its material the same way.", HorizontalLine = false)]
    [Min(0), LabelText("Base Weight")]
    public float floorBaseWeight = 10;
    public DungeonSurfaceVariant[] floorVariants = new DungeonSurfaceVariant[0];

    [Title("Wall decorations", "Optional props hung on walls: sconces, banners, chains. Authored facing +Z out of the wall, pivot on the wall face at floor level.", HorizontalLine = false)]
    public DungeonWeightedPrefab[] wallDecor = new DungeonWeightedPrefab[0];
    [Range(0, 5), Tooltip("Decorations per 10 wall faces in rooms and corridors using this style.")]
    public float wallDecorPerTenFaces = 1f;

    [Title("Corridor dressing", "Repeated across straight corridor sections: support beams, arches, drains.", HorizontalLine = false)]
    [AssetsOnly, Tooltip("Authored for one cell, facing +Z along the corridor, pivot on the floor at the cell centre.")]
    public GameObject corridorDressing;
    [ShowIf(nameof(corridorDressing)), Range(2, 8), Tooltip("Cells between repeats.")]
    public int dressingSpacing = 3;
    [ShowIf(nameof(corridorDressing)), Tooltip("Scale the dressing's height to taller corridors (authored for one tile).")]
    public bool stretchDressingToCeiling = true;

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

// Serialized by integer: append only.
public enum DungeonVariantRows { Any = 0, BottomOnly = 1, UpperOnly = 2 }

[Serializable]
public class DungeonSurfaceVariant
{
    [HorizontalGroup("Row"), HideLabel]
    public Material material;
    [HorizontalGroup("Row", 90), LabelWidth(45), Min(0), Tooltip("Relative to the other variants and the style's Base Weight.")]
    public float weight = 1;
    [HorizontalGroup("Row", 110), HideLabel, Tooltip("Walls only: limit to the first tile (moss at the foot) or the tiles above it.")]
    public DungeonVariantRows rows = DungeonVariantRows.Any;
}

[Serializable]
public class DungeonStyleChoice
{
    [InlineEditor]
    public DungeonRoomStyle style;
    [Min(0), Tooltip("Relative likelihood per whole room or corridor section. Zero disables this entry.")]
    public float weight = 1;
}
