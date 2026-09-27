using Sirenix.OdinInspector;
using UnityEngine;

// One spell: its rules, the arm animation it uses and its presentation. Everything a spell
// needs (this asset, its projectile variant, impact and hand effects) lives together in
// Combat/Spells/<Spell>/. New projectiles are variants of Combat/Spells/_Base/Base Projectile.
[CreateAssetMenu(menuName = "LoomRoom/Spell")]
public class SpellDefinition : ScriptableObject
{
    public LeftHandSpell spell;
    public string displayName;
    [TextArea] public string description;
    [PreviewField(48)] public Sprite icon;

    [Title("Rules")]
    [Min(1)] public float manaCost = 12;
    [Tooltip("Damage for attack spells, health restored for healing spells.")]
    [Min(0)] public float power = 18;
    [Min(.1f)] public float castSeconds = .6f;
    [Min(0)] public float recoverySeconds = .35f;
    [Tooltip("Movement speed while winding up, as a multiplier.")]
    [Range(.2f, 1)] public float castMoveSpeed = .6f;

    [Title("Projectile"), ShowIf(nameof(IsProjectile))]
    public SpellProjectile projectile;
    [ShowIf(nameof(IsProjectile)), Min(0)] public float radius = 1.5f;
    [ShowIf(nameof(IsProjectile)), Min(1)] public float speed = 14;
    [ShowIf(nameof(IsProjectile)), Min(.1f)] public float lifetime = 5;

    [Title("Presentation")]
    [Tooltip("Effect held in the palm while the spell is equipped. Grows while charging.")]
    public SpellVisual heldVisual;
    [Tooltip("Projectile spells: spawned where it hits. Healing: spawned around the caster.")]
    public SpellVisual impact;
    [Tooltip("Animator SpellType value: 1 = throw (Fireball), 2 = gather (Heal).")]
    public int animation = 1;
    [Tooltip("Length of the charge clip at speed 1; the animation is scaled to Cast Seconds.")]
    [Min(.1f)] public float chargeClipSeconds = .6f;

    [Title("Audio")]
    public AudioData equipSound;
    public AudioData chargeSound;
    public AudioData releaseSound;
    [ShowIf(nameof(IsProjectile))] public AudioData flightSound;
    public AudioData impactSound;

    public bool IsProjectile => spell != LeftHandSpell.Heal;
    public AdventureSkill Skill => spell == LeftHandSpell.Heal ? AdventureSkill.Thaumaturgy : AdventureSkill.Sorcery;
}
