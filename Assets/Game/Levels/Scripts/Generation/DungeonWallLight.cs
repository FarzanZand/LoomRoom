using System;
using Sirenix.OdinInspector;
using UnityEngine;

// One wall light the generator can hang (a torch, a lantern, a glowing rune). Listed on a biome
// (DungeonBiome.lightingPrefabs) with the level's list as the fallback. The prefab's origin sits
// on the floor at the wall face, facing into the room; it carries its own mounting height.
[Serializable]
public class DungeonWallLight
{
    [HorizontalGroup("Row"), HideLabel, AssetsOnly]
    public GameObject prefab;
    [HorizontalGroup("Row", 90), LabelWidth(45), Min(0), Tooltip("Relative likelihood. Zero disables this entry.")]
    public float weight = 1;
    [HorizontalGroup("Tint", 120), LabelWidth(95), Tooltip("Recolour every Light in the prefab.")]
    public bool overrideColor;
    [HorizontalGroup("Tint"), HideLabel, ShowIf(nameof(overrideColor))]
    public Color color = new Color(1f, .6f, .3f);

    public Color? Tint => overrideColor ? color : null;

    static bool Eligible(DungeonWallLight l) => l != null && l.prefab != null && l.weight > 0;

    public static bool Any(DungeonWallLight[] lights)
    {
        if (lights != null) foreach (var l in lights) if (Eligible(l)) return true;
        return false;
    }

    public static DungeonWallLight Choose(DungeonWallLight[] lights, System.Random random) =>
        WeightedPick.Choose(lights, l => Eligible(l) ? l.weight : 0, random);

    public static DungeonWallLight First(DungeonWallLight[] lights)
    {
        if (lights != null) foreach (var l in lights) if (Eligible(l)) return l;
        return null;
    }

    // Recolour (null keeps the prefab's colour) and brighten a placed light; flickering torches keep
    // flickering around the new values.
    public static void Apply(GameObject light, Color? color, float brightness = 1f)
    {
        if (!color.HasValue && brightness == 1f) return;
        foreach (var l in light.GetComponentsInChildren<Light>(true))
        {
            if (color.HasValue) l.color = color.Value;
            l.intensity *= brightness;
            var flicker = l.GetComponent<TorchFlicker>();
            if (flicker != null && flicker.isActiveAndEnabled) flicker.Rebase();
        }
    }
}
