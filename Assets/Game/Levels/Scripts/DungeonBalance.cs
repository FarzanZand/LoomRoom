using UnityEngine;
[CreateAssetMenu(menuName="Table/Dungeon balance")]
public class DungeonBalance : ScriptableObject
{
    [Min(0), Tooltip("Every enemy on every floor hits this many times harder (before the per-floor growth). 1 = as authored.")]
    public float damageMultiplier=1;
    [Tooltip("On: the biome guardian (the boss) hits with Boss Damage Multiplier instead, so the boss can be tuned apart from the floors.")]
    public bool separateBossDamage;
    [Min(0), Tooltip("The boss's damage multiplier when Separate Boss Damage is on. 1 = as authored.")]
    public float bossDamageMultiplier=1;
    [Min(0), Tooltip("Every enemy on every floor has this many times its authored health. 1 = as authored. Not the boss (Boss Health Multiplier).")]
    public float healthMultiplier=1;
    [Min(0), Tooltip("The boss's health: this many times its authored health, with no per-floor growth (it is tuned for its own floor). 1 = as authored.")]
    public float bossHealthMultiplier=1;
    [Range(0,1), Tooltip("Damage added per floor beyond the first, as a share of the enemy's damage (0.15 = +15% a floor).")]
    public float damageGrowthPerFloor=.15f;
    [Range(0,.5f), Tooltip("Health added per floor beyond the first, as a share of the enemy's health.")]
    public float healthGrowthPerFloor=.12f;
    [Min(0), Tooltip("Armor added per floor beyond the first.")]
    public float armorPerFloor=.25f;
    public void Apply(Character enemy,int floor,bool boss=false){
        if(enemy==null || enemy.Stats==null)return;
        int depth=Mathf.Max(0,floor-1);
        float damage=boss && separateBossDamage ? bossDamageMultiplier : damageMultiplier;
        float health=boss ? bossHealthMultiplier : healthMultiplier;
        if(!Mathf.Approximately(damage,1))enemy.Stats.AddModifier(new StatModifier(StatType.AttackDamage,damage-1,ModifierType.PercentMultiply,this));
        if(!Mathf.Approximately(health,1))enemy.Stats.AddModifier(new StatModifier(StatType.MaxHealth,health-1,ModifierType.PercentMultiply,this));
        enemy.Stats.AddModifier(new StatModifier(StatType.AttackDamage,damageGrowthPerFloor*depth,ModifierType.PercentAdd,this));
        if(!boss)enemy.Stats.AddModifier(new StatModifier(StatType.MaxHealth,healthGrowthPerFloor*depth,ModifierType.PercentAdd,this));
        enemy.Stats.AddModifier(new StatModifier(StatType.Armor,armorPerFloor*depth,ModifierType.Flat,this));
    }
}
