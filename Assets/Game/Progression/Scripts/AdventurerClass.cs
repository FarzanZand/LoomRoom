using System;
using Sirenix.OdinInspector;
using UnityEngine;

// Serialized by integer. Skills index AdventurerProgress.Ranks and AdventurerClass.skills.
public enum AdventureSkill { Swords = 0, Maces = 1, Blocking = 2, Sorcery = 3, Thaumaturgy = 4, Athletics = 5, Trading = 6, Stealth = 7 }
public enum LeftHandSpell { Shield = 0, Fireball = 1, Heal = 2, ShadowVeil = 3, ChainLightning = 4, SweepingArc = 5, MoltenBoulder = 6, HurledStone = 7, FrostLance = 8, RaiseTheFallen = 9 }

public static class AdventureSkills
{
    public const int Count = 8;
    public static readonly AdventureSkill[] All = (AdventureSkill[])Enum.GetValues(typeof(AdventureSkill));

    // Proficiency tiers.
    public static string Tier(int rank) => rank >= 100 ? "Legendary" : rank >= 80 ? "Master" : rank >= 60 ? "Expert"
        : rank >= 40 ? "Skilled" : rank >= 20 ? "Basic" : rank >= 1 ? "Novice" : "None";

    public static AdventureSkill ForWeapon(ItemData weapon) =>
        weapon != null && weapon.weaponCategory == WeaponCategory.Mace ? AdventureSkill.Maces : AdventureSkill.Swords;
}

[CreateAssetMenu(menuName = "LoomRoom/Adventurer Class")]
public class AdventurerClass : ScriptableObject
{
    [Tooltip("Stable save identifier; never change it once players have saves.")]
    public string id;
    public string displayName;
    [TextArea] public string description;
    [PreviewField(64)] public Sprite portrait;
    [PreviewField(64), Tooltip("The picture on the character sheet when this class's portrait is picked. Empty: a stick figure stands in.")]
    public Sprite sheetPortrait;
    [PreviewField(64), Tooltip("The same picture in colour, used instead when ProgressionManager > Meta progression > Sheet > Colour Sheet Portraits is on (Tools/sheet_portraits.py makes both).")]
    public Sprite sheetPortraitColour;
    [Tooltip("Model shown as a miniature on the memorial table in the room.")]
    [AssetsOnly] public GameObject miniature;
    [Tooltip("Empty = available from the start. Otherwise the progression flag that unlocks it.")]
    public string unlockFlag;
    [Tooltip("What the Dungeon Master says when this class's poster goes up on the wall by the bed (a class unlocked in play; its poster is World/Art/Class posters/<class> poster).")]
    public DungeonMaster.Line posterLine = new();
    [AssetsOnly, Tooltip("This class's thing on the writing desk under its poster (World/Art/Class props), shown with the poster. The poster wall prefab holds an instance placed by hand.")]
    public GameObject deskProp;

    [Title("Starting kit")]
    public ItemData[] equipment = Array.Empty<ItemData>();
    [Tooltip("Spell tomes placed on the hotbar at the start of a run. Others are found in the dungeon.")] public ItemData[] spells = Array.Empty<ItemData>();
    public ItemStack[] supplies = Array.Empty<ItemStack>();
    public LeftHandSpell leftHand;

    [Title("Attributes"), InfoBox("Added to the character sheet's scores in a run of this class. Each point above the base gives what its track says (ProgressionManager > Meta progression > Tracks): Strength damage, Dexterity critical chance, Constitution health and stamina, Intelligence mana and spell power.")]
    public int strength, dexterity, constitution, intelligence;
    public float health = 30, mana = 20, stamina = 30;

    [Title("Skills"), InfoBox("Swords, Maces, Blocking, Sorcery, Thaumaturgy, Athletics, Trading")]
    public int[] skills = new int[AdventureSkills.Count];

    public bool Unlocked => string.IsNullOrEmpty(unlockFlag) || (ProgressionManager.HasInstance && ProgressionManager.Instance.GetFlag(unlockFlag) > 0);
    // The class's points in an attribute, on top of the sheet's score.
    public int Attribute(MetaAttribute attribute) => attribute switch
    {
        MetaAttribute.Strength => strength,
        MetaAttribute.Dexterity => dexterity,
        MetaAttribute.Constitution => constitution,
        MetaAttribute.Intelligence => intelligence,
        _ => 0,
    };
    public int StartingRank(AdventureSkill skill) => skills != null && (int)skill < skills.Length ? Mathf.Clamp(skills[(int)skill], 0, 100) : 0;
}
