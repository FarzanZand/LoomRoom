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
    [Range(0,1), Tooltip("Damage growth per floor beyond the first, compounded (the user, 2026-10-30): each floor hits this share harder than the one before (0.15: floor 2 x1.15, floor 3 x1.32, floor 6 x2.01).")]
    public float damageGrowthPerFloor=.15f;
    [Range(0,.5f), Tooltip("Health growth per floor beyond the first, compounded: each floor's enemies have this share more health than the floor before's (0.1: floor 6 x1.61, not x1.5).")]
    public float healthGrowthPerFloor=.12f;
    [Min(0), Tooltip("Armor added per floor beyond the first.")]
    public float armorPerFloor=.25f;
    // How many floors' growth an enemy on this floor gets: the floors past the first plus the level's stage steps up to
    // it (TableLevelData.BiomeStage.extraFloors, TableLevelData.ExtraFloors).
    public static float Depth(int floor,float extraFloors) => Mathf.Max(0,floor-1)+Mathf.Max(0f,extraFloors);
    // The multiplier growth reaches after depth floors, compounded.
    public static float Grown(float growthPerFloor,float depth) => Mathf.Pow(1f+Mathf.Max(0f,growthPerFloor),Mathf.Max(0f,depth));

    // extraFloors: the level's difficulty steps reached by this floor (TableLevelData.ExtraFloors).
    public void Apply(Character enemy,int floor,bool boss=false,float extraFloors=0f){
        if(enemy==null || enemy.Stats==null)return;
        float depth=Depth(floor,extraFloors);
        float damage=boss && separateBossDamage ? bossDamageMultiplier : damageMultiplier;
        float health=boss ? bossHealthMultiplier : healthMultiplier;
        if(!Mathf.Approximately(damage,1))enemy.Stats.AddModifier(new StatModifier(StatType.AttackDamage,damage-1,ModifierType.PercentMultiply,this));
        if(!Mathf.Approximately(health,1))enemy.Stats.AddModifier(new StatModifier(StatType.MaxHealth,health-1,ModifierType.PercentMultiply,this));
        // Compounded per floor (and per stage step): x(1 + growth) for every floor of depth.
        enemy.Stats.AddModifier(new StatModifier(StatType.AttackDamage,Grown(damageGrowthPerFloor,depth)-1,ModifierType.PercentMultiply,this));
        if(!boss)enemy.Stats.AddModifier(new StatModifier(StatType.MaxHealth,Grown(healthGrowthPerFloor,depth)-1,ModifierType.PercentMultiply,this));
        enemy.Stats.AddModifier(new StatModifier(StatType.Armor,armorPerFloor*depth,ModifierType.Flat,this));
        // The game-wide group multipliers (ProgressionManager > Balance).
        if(ProgressionManager.HasInstance)ProgressionManager.Instance.ApplyEnemyBase(enemy);
    }
}
