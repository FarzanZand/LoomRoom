using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// What every character has: identity, stats, voice and animation triggers. NPCs use this
// type directly; PlayerData adds movement and starting items, EnemyData adds behaviour and
// attacks. Health, damage, factions and death all work from this base type.
[CreateAssetMenu(fileName = "NewCharacterData", menuName = "Characters/NPC Data")]
public class CharacterData : ScriptableObject
{
    protected const string InspectorTabs = "CharacterTabs";

    [HorizontalGroup("Identity", 64), PreviewField(64, ObjectFieldAlignment.Left), HideLabel]
    public Sprite portrait;
    [VerticalGroup("Identity/Right"), LabelWidth(90)]
    public string characterName;
    [VerticalGroup("Identity/Right"), LabelWidth(90), Tooltip("The name is said as it is, with no \"the\" or \"a\" in front (\"Not a witch hits you\").")]
    public bool properName;
    [VerticalGroup("Identity/Right"), LabelWidth(90), Tooltip("Who this character is hostile to.")]
    public Faction faction = Faction.Neutral;
    [VerticalGroup("Identity/Right"), LabelWidth(90), TextArea(2, 4)]
    public string description;
    [VerticalGroup("Identity/Right"), LabelWidth(90), Tooltip("What gear's \"damage to skeletons / beasts / slimes\" counts it as. Auto: skeletons and slimes by their gore (bone dust, slime), anything else a humanoid.")]
    public CreatureKind creatureKind;

    public CreatureKind Kind => creatureKind != CreatureKind.Auto ? creatureKind
        : gore != null && gore.kind == BloodKind.BoneDust ? CreatureKind.Skeleton
        : gore != null && gore.kind == BloodKind.Slime ? CreatureKind.Slime : CreatureKind.Humanoid;

    [TabGroup(InspectorTabs, "Stats", Order = 0), Tooltip("Base values. Anything not listed reads as 0 (or 1 for multiplier stats). Dungeon enemies are scaled per floor by the level's balance asset.")]
    [ListDrawerSettings(ShowFoldout = false, ShowPaging = true, NumberOfItemsPerPage = 8)]
    public List<StatEntry> stats = new();

    // Players have their own footstep system; NPCs and enemies use this.
    protected virtual bool HasCreatureAudio => true;
    [TabGroup(InspectorTabs, "Presentation", Order = 30), TabGroup("CharacterTabs/Presentation/Details", "Audio"), ShowIf(nameof(HasCreatureAudio)), InlineProperty, HideLabel]
    public CreatureAudio audio = new();

    [TabGroup(InspectorTabs, "Presentation"), TabGroup("CharacterTabs/Presentation/Details", "Gore"), InlineProperty, HideLabel]
    public GoreProfile gore = new();

    [TabGroup(InspectorTabs, "Presentation"), TabGroup("CharacterTabs/Presentation/Details", "Animation"), Tooltip("Animator trigger fired when hurt. Leave empty to skip.")]
    public string hurtTrigger = "Hurt";
    [TabGroup("CharacterTabs/Presentation/Details", "Animation"), Tooltip("Animator trigger fired on death. Leave empty to skip.")]
    public string deathTrigger = "Death";
    [TabGroup("CharacterTabs/Presentation/Details", "Animation"), Tooltip("Seconds after death before the object is disabled. 0 = never disable. Dungeon enemies follow CombatManager's corpse lifetime instead.")]
    public float deathDisableDelay = 20f;

    protected virtual void Reset()
    {
        stats = new List<StatEntry>
        {
            new StatEntry(StatType.MaxHealth,    100f),
            new StatEntry(StatType.Armor,      0f),
            new StatEntry(StatType.AttackDamage, 5f),
            new StatEntry(StatType.MoveSpeed,    1f),
            new StatEntry(StatType.AttackSpeed,  1f),
        };
    }
}

// What kind of creature a character is, for gear that hits one kind harder. Serialized by integer; append only.
public enum CreatureKind { Auto = 0, Humanoid = 1, Beast = 2, Skeleton = 3, Slime = 4 }
