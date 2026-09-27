using UnityEngine;

// Tuning for character growth shared by every class. One asset: Progression/Data/Adventure Rules.
[CreateAssetMenu(menuName = "LoomRoom/Adventure Rules")]
public class AdventureRules : ScriptableObject
{
    public AdventurerClass[] classes;
    public AdventureSkillData[] skills;
    [Tooltip("Stealth trains while crouching within this many metres of an enemy that hasn't noticed you.")]
    [Min(1)] public float sneakPractiseRange = 4f;
    [Header("Character growth")]
    public float xpPerEnemy = 15, xpPerFloor = 35, levelXp = 70, levelXpGrowth = 35;
    public float healthPerLevel = 3, manaPerLevel = 2;
    [Header("Skills")]
    [Tooltip("Metres of new ground sprinted per Athletics roll.")]
    public float athleticsMetres = 12;
    [Tooltip("Flat Attack added by a Legendary weapon skill.")]
    public float legendaryAttack = 5;
    [Tooltip("Maximum stamina added by Legendary Athletics.")]
    public float legendaryStamina = 20;
    [Header("Attributes")]
    public float strengthAttack = 1, constitutionArmor = .25f, constitutionHealth = 0;
    public float intelligenceMana = 0, intelligenceSpell = .035f;
    public float dexteritySpeed = .01f;
    public float perceptionRangePerPoint = .04f, perceptionRangeCap = .5f;
    public float charismaDiscountPerPoint = .02f, charismaDiscountCap = .3f;
}
