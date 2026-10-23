using Sirenix.OdinInspector;
using UnityEngine;

// Enemy balance as a group: every enemy's starting health and damage (the boss included) times these, on top of each
// level's DungeonBalance (which applies them, DungeonBalance.Apply). 1 = as authored.
public partial class ProgressionManager
{
    [FoldoutGroup("Balance"), Min(0), Tooltip("Every enemy's starting damage, times this (the boss too). 1 = as authored.")]
    public float enemyBaseDamage = 1f;
    [FoldoutGroup("Balance"), Min(0), Tooltip("Every enemy's starting health, times this (the boss too). 1 = as authored.")]
    public float enemyBaseHealth = 1f;

    // The two multipliers on an enemy, as their own modifiers (source: this manager).
    public void ApplyEnemyBase(Character enemy)
    {
        if (enemy == null || enemy.Stats == null) return;
        if (!Mathf.Approximately(enemyBaseDamage, 1f)) enemy.Stats.AddModifier(new StatModifier(StatType.AttackDamage, enemyBaseDamage - 1f, ModifierType.PercentMultiply, this));
        if (!Mathf.Approximately(enemyBaseHealth, 1f)) enemy.Stats.AddModifier(new StatModifier(StatType.MaxHealth, enemyBaseHealth - 1f, ModifierType.PercentMultiply, this));
    }
}
