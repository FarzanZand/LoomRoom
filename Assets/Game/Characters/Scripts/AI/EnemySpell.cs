using System;
using Sirenix.OdinInspector;
using UnityEngine;

// One spell an enemy can cast: a SpellDefinition (the same asset the player casts, e.g. Fireball) plus how this enemy
// uses it. Listed on EnemyData.spells and switched on with EnemyData.caster. The brain treats each spell as one more
// attack: it keeps its distance, raises its hand while aiming, and the spell leaves the hand at Release Time (or on the
// cast clip's OnAttackHit event). Any humanoid can cast: the cast clip ("CastSpell") is on the shared HumanoidController.
[Serializable]
public class EnemySpell
{
    [HorizontalGroup("Top"), HideLabel, AssetsOnly, Required]
    public SpellDefinition spell;
    [HorizontalGroup("Top", Width = 110), LabelWidth(50), Min(0.01f), Tooltip("Relative chance among the attacks usable right now.")]
    public float weight = 1f;

    [AssetsOnly, Tooltip("Glows in the caster's hand from the start of the cast until the release: the warning. Empty uses the spell's Held Visual (sized for the player's own palm, too small on an enemy).")]
    public SpellVisual handEffect;

    [FoldoutGroup("Range and timing", Expanded = true), Tooltip("Too close and it backs off or uses its melee attacks instead.")]
    public float minRange = 2.5f;
    [FoldoutGroup("Range and timing")]
    public float maxRange = 12f;
    [FoldoutGroup("Range and timing"), Tooltip("Seconds between casts of this spell, counted from the end of the cast.")]
    public float cooldown = 5f;
    [FoldoutGroup("Range and timing"), Tooltip("Animator trigger for the cast. The cast state carries the 'Attack' tag (HumanoidController: CastSpell).")]
    public string animatorTrigger = "CastSpell";
    [FoldoutGroup("Range and timing"), Tooltip("Seconds into the cast when the spell leaves the hand, unless the clip has an OnAttackHit event. The humanoid cast throws at about 1.35 s.")]
    public float releaseTime = 1.35f;
    [FoldoutGroup("Range and timing"), Tooltip("Degrees per second the caster keeps turning toward the target while casting. 0 (the default): none. The cast holds the legs still (CastSpell), so any turn spins the body on planted feet; the spell aims from the hand at the target anyway.")]
    public float aimTrackSpeed = 0f;
    [FoldoutGroup("Range and timing"), Tooltip("Largest height difference to the target it will cast across.")]
    public float maxHeightDifference = 4f;

    [FoldoutGroup("Aim and damage"), Tooltip("Damage = the enemy's Attack Damage x this (so floor scaling applies). Zero uses the spell's own Power.")]
    [Min(0f)] public float damageMultiplier = 1.2f;
    [FoldoutGroup("Aim and damage"), Tooltip("Random aim error in degrees for projectiles.")]
    [Range(0f, 15f)] public float spread = 2f;
    [FoldoutGroup("Aim and damage"), Tooltip("Aims ahead of a moving target by this fraction of its travel (projectiles: during the flight; ground spells: during the delay).")]
    [Range(0f, 1f)] public float leadTarget = .4f;

    [FoldoutGroup("Volley"), Tooltip("Cast as a volley (projectile spells): the balls appear one by one at the hand, hang in the air, then fly at the target one after another, speeding up (FireballVolley).")]
    public bool volley;
    [FoldoutGroup("Volley"), ShowIf(nameof(volley)), InlineProperty, HideLabel]
    public FireballVolley.Settings volleySettings = new();

    string Label => (spell != null ? (string.IsNullOrEmpty(spell.displayName) ? spell.name : spell.displayName) : "Spell") + (volley ? " volley" : "");

    [NonSerialized] EnemyAttack attack;
    // The brain's attack list entry for this spell, built on first use.
    public EnemyAttack Attack
    {
        get
        {
            attack ??= new EnemyAttack { spellCast = this, facingAngle = 15f, fallbackHitDelay = -1f };
            attack.name = Label;
            attack.weight = weight;
            attack.animatorTrigger = animatorTrigger;
            attack.minRange = minRange;
            attack.maxRange = maxRange;
            attack.cooldown = cooldown;
            return attack;
        }
    }
}
