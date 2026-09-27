using System;
using Sirenix.OdinInspector;
using UnityEngine;

// Serialized by integer. Skills index AdventurerProgress.Ranks and AdventurerClass.skills.
public enum AdventureSkill { Swords = 0, Maces = 1, Blocking = 2, Sorcery = 3, Thaumaturgy = 4, Athletics = 5, Trading = 6, Stealth = 7 }
public enum LeftHandSpell { Shield = 0, Fireball = 1, Heal = 2 }

public static class AdventureSkills
{
    public const int Count = 8;
    public static readonly AdventureSkill[] All = (AdventureSkill[])Enum.GetValues(typeof(AdventureSkill));

    // Barony's proficiency tiers.
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
    [Tooltip("Model shown as a miniature on the memorial table in the room.")]
    [AssetsOnly] public GameObject miniature;
    [Tooltip("Empty = available from the start. Otherwise the progression flag that unlocks it.")]
    public string unlockFlag;
    [TextArea(1, 2), ShowIf("@!string.IsNullOrEmpty(unlockFlag)")] public string lockedHint = "Solve the room's puzzle to unlock.";

    [Title("Starting kit")]
    public ItemData[] equipment = Array.Empty<ItemData>();
    [Tooltip("Spell tomes placed on the hotbar at the start of a run. Others are found in the dungeon.")] public ItemData[] spells = Array.Empty<ItemData>();
    public ItemStack[] supplies = Array.Empty<ItemStack>();
    public LeftHandSpell leftHand;

    [Title("Attributes"), InfoBox("STR, DEX, CON, INT, PER, CHR")]
    public int[] attributes = new int[6];
    [Tooltip("Attribute index raised at each character level, repeating (0 STR … 5 CHR).")]
    public int[] growth = { 0, 2, 1 };
    public float health = 30, mana = 20, stamina = 30;

    [Title("Skills"), InfoBox("Swords, Maces, Blocking, Sorcery, Thaumaturgy, Athletics, Trading")]
    public int[] skills = new int[AdventureSkills.Count];

    public bool Unlocked => string.IsNullOrEmpty(unlockFlag) || (ProgressionManager.HasInstance && ProgressionManager.Instance.GetFlag(unlockFlag) > 0);
    public int StartingRank(AdventureSkill skill) => skills != null && (int)skill < skills.Length ? Mathf.Clamp(skills[(int)skill], 0, 100) : 0;
}
