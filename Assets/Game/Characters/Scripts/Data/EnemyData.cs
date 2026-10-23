using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// An enemy's behaviour and attacks on top of the shared character data. Each enemy keeps
// its own EnemyData inside its prefab file (see Tools > LoomRoom > New Enemy).
[CreateAssetMenu(fileName = "NewEnemyData", menuName = "Characters/Enemy Data")]
public class EnemyData : CharacterData
{
    bool UsesInlineBehaviour => behaviour == null;

    [TabGroup(InspectorTabs, "Behaviour", Order = 10)]
    [Tooltip("Optional shared tuning asset, for when several enemies should always behave alike. Empty uses the settings below, which belong to this enemy only.")]
    public EnemyBehaviourProfile behaviour;
    [TabGroup(InspectorTabs, "Behaviour"), ShowIf(nameof(UsesInlineBehaviour)), InlineProperty, HideLabel]
    public EnemyBehaviourSettings behaviourSettings = new();

    [TabGroup(InspectorTabs, "Combat", Order = 20), TabGroup("CharacterTabs/Combat/Details", "Melee"), HideLabel]
    [ListDrawerSettings(ShowFoldout = false, ShowPaging = true, NumberOfItemsPerPage = 4, ListElementLabelName = "name")]
    public List<EnemyAttack> attacks = new();

    [TabGroup("CharacterTabs/Combat/Details", "Archery")]
    [Tooltip("Carries a bow and shoots from range, keeping its distance. Melee attacks above are still used when the target gets close.")]
    public bool archer;
    [TabGroup("CharacterTabs/Combat/Details", "Archery"), ShowIf(nameof(archer)), InlineProperty, HideLabel]
    public EnemyArchery archery = new();

    [TabGroup("CharacterTabs/Combat/Details", "Spells")]
    [Tooltip("Casts spells from range (raising a hand, then throwing), keeping its distance. Humanoids share the cast animation. Melee attacks are still used up close.")]
    public bool caster;
    [TabGroup("CharacterTabs/Combat/Details", "Spells"), ShowIf(nameof(caster))]
    [ListDrawerSettings(ShowFoldout = false, ListElementLabelName = "Label")]
    public List<EnemySpell> spells = new();
    [TabGroup("CharacterTabs/Combat/Details", "Spells"), ShowIf(nameof(caster))]
    [Tooltip("A fighter who also casts: it keeps closing in to its melee attacks and casts on the way and when the target backs off, instead of keeping spell range.")]
    public bool closesIn;
    [TabGroup("CharacterTabs/Combat/Details", "Spells"), ShowIf(nameof(caster)), Min(0)]
    [Tooltip("Seconds after any cast before it may cast another spell (each spell's own cooldown still applies): without it, spells that are all ready are cast back to back while it stands still.")]
    public float spellGap = 4f;

    [TabGroup(InspectorTabs, "Stats"), Title("Rewards")]
    [Tooltip("Character experience for killing this enemy. Zero derives it from maximum health, so tougher enemies are worth more.")]
    [Min(0)] public float experience;

    // What the brain reads: the shared profile when assigned, otherwise this enemy's own.
    public EnemyBehaviourSettings Behaviour => behaviour != null ? behaviour.settings : behaviourSettings;

    protected override void Reset()
    {
        base.Reset();
        faction = Faction.Enemy;
    }
}
