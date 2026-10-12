using Sirenix.OdinInspector;
using UnityEngine;

// How an item's inventory icon is made. The icon is rendered from the item's own model, sized to its bag
// footprint (ItemData.GridSize); worn gear without a real model (armour, helm, gloves, boots, leggings) is cut
// from a Synty character's outfit, picked per tier on Items/Icons/Item Icon Settings. Generated icons live in
// Items/Icons/Generated and are made again whenever what they show changes, unless the icon is custom.
[System.Serializable]
public class ItemIconSource
{
    [Tooltip("Keep the icon assigned above as it is: the generator never replaces it.")]
    public bool custom;

    [HideIf(nameof(custom))]
    [Tooltip("Render this model instead of the world prefab (or instead of the tier's outfit for worn gear).")]
    public GameObject model;

    [HideIf(nameof(custom))]
    [ValueDropdown("OutfitNames")]
    [Tooltip("Worn gear: cut the icon from this outfit (Item Icon Settings). Empty uses the outfit of the item's tier.")]
    public string outfit;

    [HideIf(nameof(custom))]
    [Tooltip("Extra turn on top of the automatic view, in degrees (x tilts towards you, y turns, z rolls).")]
    public Vector3 turn;

    [HideIf(nameof(custom))]
    [Tooltip("Long items: stand it the other way up. Shields and other flat items: show the other side.")]
    public bool flip;

#if UNITY_EDITOR
    public static System.Func<string[]> OutfitList;
    static ValueDropdownList<string> OutfitNames()
    {
        var list = new ValueDropdownList<string> { { "Tier default", "" } };
        if (OutfitList != null) foreach (var name in OutfitList()) list.Add(name, name);
        return list;
    }
#endif
}
