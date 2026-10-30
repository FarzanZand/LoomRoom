using UnityEngine;

public enum EliteTrait { Tough = 0, Brutal = 1, Swift = 2 }

// An elite: now and then a generated enemy comes out as one (DungeonBiome.eliteChance, rolled in DungeonGenerator.SetUpEnemy).
// It grows as if it were DungeonBiome.eliteFloors deeper (stats and the loot it drops), and has one trait on top: Tough
// (more health), Brutal (harder hits) or Swift (faster swings). Its name says so ("Brutal Crypt Soldier"), a soft light in
// the trait's colour stands round it, and it is worth twice the experience (RunManager).
public class EliteEnemy : MonoBehaviour
{
    public EliteTrait trait;
    [Tooltip("How many floors deeper it grows than the floor it is on.")]
    public float extraFloors;

    public const float ToughHealth = .6f, BrutalDamage = .35f, SwiftAttackSpeed = .25f, Experience = 2f;

    public static string Word(EliteTrait t) => t switch { EliteTrait.Tough => "Tough", EliteTrait.Brutal => "Brutal", _ => "Swift" };
    static Color Glow(EliteTrait t) => t switch { EliteTrait.Tough => new Color(1f, .72f, .3f), EliteTrait.Brutal => new Color(1f, .28f, .2f), _ => new Color(.45f, .85f, 1f) };

    // Made at spawn, right after the floor's balance is applied (so its health is full at the elite's maximum).
    public static EliteEnemy Make(Character character, EliteTrait trait, float extraFloors)
    {
        if (character == null) return null;
        var elite = character.gameObject.AddComponent<EliteEnemy>();
        elite.trait = trait; elite.extraFloors = extraFloors;
        var stats = character.Stats;
        if (stats != null)
            switch (trait)
            {
                case EliteTrait.Tough: stats.AddModifier(new StatModifier(StatType.MaxHealth, ToughHealth, ModifierType.PercentMultiply, elite)); break;
                case EliteTrait.Brutal: stats.AddModifier(new StatModifier(StatType.AttackDamage, BrutalDamage, ModifierType.PercentMultiply, elite)); break;
                case EliteTrait.Swift: stats.AddModifier(new StatModifier(StatType.AttackSpeed, SwiftAttackSpeed, ModifierType.PercentMultiply, elite)); break;
            }
        character.namePrefix = Word(trait);
        // A soft light in the trait's colour about its chest, so an elite reads as one across the room.
        var aura = new GameObject("Elite glow").AddComponent<Light>();
        aura.transform.SetParent(character.transform, false);
        aura.transform.localPosition = Vector3.up * 1.1f;
        aura.type = LightType.Point; aura.color = Glow(trait); aura.range = 2.6f; aura.intensity = 1.4f; aura.shadows = LightShadows.None;
        return elite;
    }
}
