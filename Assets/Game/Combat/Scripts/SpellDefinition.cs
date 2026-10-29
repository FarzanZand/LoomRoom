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
    [ShowIf(nameof(IsProjectile)), Tooltip("It keeps turning toward its target in flight (can't be stepped away from). Off: it flies straight at where the target was when it left.")]
    public bool homing;
    [ShowIf("@IsProjectile && homing"), Min(10), Tooltip("Degrees a second it turns toward its target.")]
    public float homingTurn = 540f;
    [ShowIf(nameof(IsProjectile)), Min(0), Tooltip("Pulls it down, in metres a second squared: a thrown rock flies in an arc, aimed so it still lands where the crosshair is. 0 flies straight.")]
    public float gravity;
    [ShowIf(nameof(IsProjectile)), Min(0), Tooltip("Characters it passes through, hurting each, before it bursts on the next one or on a wall.")]
    public int pierce;
    [ShowIf(nameof(IsProjectile)), Min(0), Tooltip("Every character it hurts stands stunned this long (its swing dropped).")]
    public float stunSeconds;
    [ShowIf(nameof(IsProjectile)), Range(0, .9f), Tooltip("Every character it hurts is slowed by this share (moves and swings slower) for Chill Seconds.")]
    public float chill;
    [ShowIf("@IsProjectile && chill > 0"), Min(0)] public float chillSeconds = 3f;
    [ShowIf("@IsProjectile && chill > 0"), AssetsOnly, Tooltip("Frost that clings to a chilled character.")] public SpellVisual chillEffect;
    [ShowIf(nameof(IsProjectile)), AssetsOnly, Tooltip("Left burning where it bursts: hurts every hostile standing in it.")]
    public GroundFire groundFire;
    [ShowIf("@IsProjectile && groundFire != null"), Min(.1f)] public float groundFireSeconds = 3f;
    [ShowIf("@IsProjectile && groundFire != null"), Min(.1f)] public float groundFireRadius = 1.6f;
    [ShowIf("@IsProjectile && groundFire != null"), Min(0), Tooltip("Damage a second, as a share of the spell's power.")] public float groundFirePower = .25f;

    [Title("Effect")]
    [Tooltip("Standard: a projectile, a ground area or a heal, by the settings here. The others do their own thing, set below.")]
    public SpellEffect effect;
    [ShowIf("@effect == SpellEffect.ShadowVeil"), InlineProperty, HideLabel] public ShadowVeilSettings veil = new();
    [ShowIf("@effect == SpellEffect.ChainLightning"), InlineProperty, HideLabel] public ChainLightningSettings chain = new();
    [ShowIf("@effect == SpellEffect.SweepingArc"), InlineProperty, HideLabel] public SweepingArcSettings sweep = new();
    [ShowIf("@effect == SpellEffect.RaiseTheFallen"), InlineProperty, HideLabel] public RaiseSettings raise = new();
    [ShowIf("@effect == SpellEffect.Flick"), InlineProperty, HideLabel] public FlickSettings flick = new();

    [Title("Ground area")]
    [Tooltip("Lands on the ground at the target instead of flying: the spot is marked (Show Marker), then it erupts after Area Delay and hits everything hostile inside Area Radius. The eruption is the Impact effect.")]
    public bool groundArea;
    [ShowIf(nameof(groundArea)), Min(.1f), Tooltip("Metres.")] public float areaRadius = 2f;
    [ShowIf(nameof(groundArea)), Min(0f), Tooltip("Seconds between the mark and the eruption: the time to step out.")] public float areaDelay = 1.2f;
    [ShowIf(nameof(groundArea)), Tooltip("Show where it will land before it erupts. Off: it erupts with no warning (keep Area Delay short then).")] public bool showMarker = true;
    [ShowIf(nameof(ShowsMarker)), AssetsOnly, Tooltip("Laid flat on the ground at the spot, scaled to Area Radius (authored for a radius of Marker Radius). A child named 'Fill' grows from the centre to full size over the delay.")]
    public GameObject areaMarker;
    [ShowIf(nameof(ShowsMarker)), Min(.1f), Tooltip("The radius the marker prefab is drawn at.")] public float markerRadius = 2f;
    [ShowIf(nameof(groundArea)), Min(1f), Tooltip("Furthest the player can place it (where the crosshair meets the ground). Enemies use their own range.")] public float areaRange = 14f;
    bool ShowsMarker => groundArea && showMarker;

    [Title("Presentation")]
    [Tooltip("Effect held in the palm while the spell is equipped. Grows while charging.")]
    public SpellVisual heldVisual;
    [Tooltip("Projectile spells: spawned where it hits. Ground spells: the eruption. Healing: spawned around the caster.")]
    public SpellVisual impact;
    [Tooltip("Animator SpellType value: 1 = throw (Fireball), 2 = gather (Heal).")]
    public int animation = 1;
    [Tooltip("Length of the charge clip at speed 1; the animation is scaled to Cast Seconds.")]
    [Min(.1f)] public float chargeClipSeconds = .6f;

    [Title("Audio")]
    public AudioClip equipSound;
    [Range(0, 1)] public float equipVolume = 1f;
    public AudioClip chargeSound;
    [Range(0, 1)] public float chargeVolume = 1f;
    public AudioClip releaseSound;
    [Range(0, 1)] public float releaseVolume = 1f;
    [ShowIf(nameof(IsProjectile))] public AudioClip flightSound;
    [Range(0, 1)] public float flightVolume = 1f;
    public AudioClip impactSound;
    [Range(0, 1)] public float impactVolume = 1f;

    public bool IsProjectile => effect == SpellEffect.Standard && spell != LeftHandSpell.Heal && !groundArea;
    public AdventureSkill Skill => spell == LeftHandSpell.Heal || effect == SpellEffect.RaiseTheFallen ? AdventureSkill.Thaumaturgy : AdventureSkill.Sorcery;

    // The tome's rules line: cost and what the spell does.
    public string RulesLine(float cost, float power) => effect switch
    {
        SpellEffect.ShadowVeil => $"{cost:0.#} mana, unseen for {veil.seconds:0.#} seconds",
        SpellEffect.RaiseTheFallen => $"{cost:0.#} mana, raises a fallen skeleton for {raise.summon.lifetime:0} seconds",
        SpellEffect.ChainLightning => $"{cost:0.#} mana, {power:0.#} damage, jumps to {chain.jumps} more",
        SpellEffect.Flick => $"{cost:0.#} mana, {power:0.#} damage, the Dungeon Master flicks it across the room",
        _ => $"{cost:0.#} mana, {power:0.#} {(spell == LeftHandSpell.Heal ? "healing" : "damage")}",
    };
}

// What a spell does when it leaves the hand. Serialized by integer: append only.
public enum SpellEffect { Standard = 0, ShadowVeil = 1, ChainLightning = 2, SweepingArc = 3, RaiseTheFallen = 4, Flick = 5 }

