using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// One asset type for players, enemies and NPCs. They differ by which optional sections
// are filled in: a player has movement settings and starting items, an enemy has a
// behaviour profile and attacks, an NPC has neither.
[CreateAssetMenu(fileName = "NewCharacterData", menuName = "Characters/Character Data")]
public class CharacterData : ScriptableObject
{
    [Header("Identity")]
    public string characterName;
    [TextArea] public string description;
    public Faction faction = Faction.Neutral;
    public Sprite portrait;
    [Tooltip("Optional impact sound for this creature's body, played by CombatManager through the SFX mixer.")]
    public AudioData bodyImpactAudio;

    [Header("Stats")]
    [Tooltip("Base values. Anything not listed reads as 0 (or 1 for multiplier stats).")]
    [ListDrawerSettings(ShowFoldout = true)]
    public List<StatEntry> stats = new();

    [Header("Movement (players)")]
    [Header("Speeds (m/s)")]
    public float walkSpeed   = 3f;
    public float sprintSpeed = 6f;
    public float crouchSpeed = 1.75f;

    [Header("Jump & Gravity")]
    public float jumpForce         = 8f;
    [Tooltip("Multiplier applied to gravity while airborne. Higher values make falling faster.")]
    public float gravityMultiplier = 2.5f;

    // Retained only for old assets/editor migrations. Movement no longer spends a resource.
    [HideInInspector] public float sprintStaminaPerSecond;
    [HideInInspector] public float jumpStaminaCost;
    [HideInInspector] public float staminaRegenDelay = .6f;
    [HideInInspector] public float sprintRecoveryFraction = .25f;

    [Header("Starting Items (players)")]
    [Tooltip("Added to the bag or hotbar on first spawn.")]
    public List<ItemData> startingItems = new();
    [Tooltip("Equipped directly on first spawn.")]
    public List<ItemData> startingEquipment = new();

    [Header("Animation")]
    [Tooltip("Animator trigger fired when hurt. Leave empty to skip.")]
    public string hurtTrigger = "Hurt";
    [Tooltip("Animator trigger fired on death. Leave empty to skip.")]
    public string deathTrigger = "Death";
    [Tooltip("Seconds after death before the object is disabled. 0 = never disable.")]
    public float deathDisableDelay = 20f;

    [Header("Enemy")]
    [Tooltip("Perception, movement and chase tuning. Only enemies need this.")]
    public EnemyBehaviourProfile behaviour;
    [ShowIf("@behaviour != null")]
    [ListDrawerSettings(ShowFoldout = true)]
    public List<EnemyAttack> attacks = new();

    public float GetBaseStat(StatType type)
    {
        foreach (var s in stats)
            if (s.stat == type) return s.baseValue;
        return 0f;
    }

    void Reset()
    {
        stats = new List<StatEntry>
        {
            new StatEntry(StatType.MaxHealth,    100f),
            new StatEntry(StatType.Defense,      0f),
            new StatEntry(StatType.AttackDamage, 5f),
            new StatEntry(StatType.MoveSpeed,    1f),
            new StatEntry(StatType.AttackSpeed,  1f),
        };
    }
    void OnValidate()
    {
        walkSpeed   = Mathf.Max(0.1f, walkSpeed);
        sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
        crouchSpeed = Mathf.Clamp(crouchSpeed, 0.1f, walkSpeed);
    }
}
