using UnityEngine;
[CreateAssetMenu(menuName="Table/Dungeon balance")]
public class DungeonBalance : ScriptableObject
{
    [Min(0)] public float damagePerFloor=2;
    [Range(0,.5f)] public float healthGrowthPerFloor=.12f;
    [Range(0,1)] public float armorPerFloor=.25f;
    public void Apply(Character enemy,int floor){
        if(enemy?.Stats==null)return;
        int depth=Mathf.Max(0,floor-1);
        enemy.Stats.AddModifier(new StatModifier(StatType.AttackDamage,damagePerFloor*depth,ModifierType.Flat,this));
        enemy.Stats.AddModifier(new StatModifier(StatType.MaxHealth,healthGrowthPerFloor*depth,ModifierType.PercentAdd,this));
        enemy.Stats.AddModifier(new StatModifier(StatType.Armor,armorPerFloor*depth,ModifierType.Flat,this));
    }
}
