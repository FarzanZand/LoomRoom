using System;
using UnityEngine;
using Sirenix.OdinInspector;

[CreateAssetMenu(menuName = "Table/Room Style", fileName = "Room Style")]
public class DungeonRoomStyle : ScriptableObject
{
    [Tooltip("First vertical tile. Empty inherits the level's Default Style.")]
    public Material bottomWall;
    [Tooltip("All tiles above the first. Empty inherits the level's Default Style (its Upper Wall, or else its Bottom Wall).")]
    public Material upperWall;
    [Tooltip("Empty inherits the level's Default Style.")]
    public Material floor;
    [Tooltip("Empty inherits the level's Default Style.")]
    public Material ceiling;
    [Tooltip("Do not generate cornices or footings in rooms or corridors using this style. Reload the dungeon to apply.")]
    public bool hideTrims;
    [HideIf(nameof(hideTrims)), Tooltip("Cornice and footing. Empty inherits the level's Default Style.")]
    public Material trim;
    [Tooltip("Only read from a level's Default Style: exit hatch, procedural chests and barrels, board tile edges without their own material.")]
    public Material wood;
    [Tooltip("Only read from a level's Default Style: exit gate bars, procedural chest latches and barrel bands.")]
    public Material metal;

    // Odin titles don't wrap their subtitle, so a long one widens the whole inspector: keep them short, details in tooltips.
    [Title("Wall variety", "Weighted per wall tile", HorizontalLine = false)]
    [Min(0), LabelText("Base Weight"), Tooltip("Each wall tile draws its material: Bottom Wall / Upper Wall with this weight, or one of the variants with its own. 10 against two variants of weight 1 keeps the base on about 5 of 6 tiles.")]
    public float wallBaseWeight = 10;
    [Tooltip("Other wall materials drawn now and then, each with its weight.")]
    public DungeonSurfaceVariant[] wallVariants = new DungeonSurfaceVariant[0];
    [Title("Floor variety", "Weighted per floor cell", HorizontalLine = false)]
    [Min(0), LabelText("Base Weight"), Tooltip("Each floor cell draws its material the same way as the walls.")]
    public float floorBaseWeight = 10;
    [Tooltip("Other floor materials drawn now and then, each with its weight.")]
    public DungeonSurfaceVariant[] floorVariants = new DungeonSurfaceVariant[0];

    [Title("Wall decorations", "Sconces, banners, chains", HorizontalLine = false)]
    [Tooltip("Optional props hung on walls. Authored facing +Z out of the wall, pivot on the wall face at floor level.")]
    public DungeonWeightedPrefab[] wallDecor = new DungeonWeightedPrefab[0];
    [Range(0, 5), Tooltip("Decorations per 10 wall faces in rooms and corridors using this style.")]
    public float wallDecorPerTenFaces = 1f;

    [Title("Corridor dressing", "Beams, arches, drains", HorizontalLine = false)]
    [AssetsOnly, Tooltip("Repeated across straight corridor sections. Authored for one cell, facing +Z along the corridor, pivot on the floor at the cell centre.")]
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
        var pick = WeightedPick.Choose(choices, c => Eligible(c) ? c.weight : 0, random);
        return pick != null ? pick.style : null;
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
