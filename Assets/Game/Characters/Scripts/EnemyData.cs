using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// An enemy's behaviour and attacks on top of the shared character data. Each enemy keeps
// its own EnemyData inside its prefab file (see Tools > LoomRoom > New Enemy).
[CreateAssetMenu(fileName = "NewEnemyData", menuName = "Characters/Enemy Data")]
public class EnemyData : CharacterData
{
    bool UsesInlineBehaviour => behaviour == null;

    [FoldoutGroup("Behaviour", Expanded = true, Order = 1)]
    [Tooltip("Optional shared tuning asset, for when several enemies should always behave alike. Empty uses the settings below, which belong to this enemy only.")]
    public EnemyBehaviourProfile behaviour;
    [FoldoutGroup("Behaviour"), ShowIf(nameof(UsesInlineBehaviour)), InlineProperty, HideLabel]
    public EnemyBehaviourSettings behaviourSettings = new();

    [FoldoutGroup("Attacks", Expanded = true, Order = 2), HideLabel]
    [ListDrawerSettings(ShowFoldout = false, ListElementLabelName = "name")]
    public List<EnemyAttack> attacks = new();

    // What the brain reads: the shared profile when assigned, otherwise this enemy's own.
    public EnemyBehaviourSettings Behaviour => behaviour != null ? behaviour.settings : behaviourSettings;

    protected override void Reset()
    {
        base.Reset();
        faction = Faction.Enemy;
    }
}
