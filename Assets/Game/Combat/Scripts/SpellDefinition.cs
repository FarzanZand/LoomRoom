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
    [ShowIf(nameof(IsProjectile)), Min(0), Tooltip("Every character it hurts burns for this share of the hit's damage in all, a tick a second for Burn Seconds (Burning).")]
    public float burn;
    [ShowIf("@IsProjectile && burn > 0"), Min(1)] public float burnSeconds = 3f;
    [ShowIf("@IsProjectile && burn > 0"), AssetsOnly, Tooltip("Fire on a burning character (its BurnCoat chars the body while it burns).")] public SpellVisual burnEffect;
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
    [ShowIf("@effect == SpellEffect.HelpingHand"), InlineProperty, HideLabel] public HelpingHandSettings helpingHand = new();
    [ShowIf("@effect == SpellEffect.Summon"), InlineProperty, HideLabel] public SummonSpellSettings summon = new();

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
    // Thaumaturgy is support only (heals and buffs: Heal, Helping Hand); everything that fights, Raise Skeletons included,
    // is Sorcery (the user, 2026-11-12).
    public AdventureSkill Skill => spell == LeftHandSpell.Heal || effect == SpellEffect.HelpingHand ? AdventureSkill.Thaumaturgy : AdventureSkill.Sorcery;

    // ── Tooltip ────────────────────────────────────────────────────────────────
    // A tome's and a scroll's tooltip (the user, 2026-11-13): the spell's numbers as label and value rows, as gear
    // shows its stats, then its description, one short line saying what it does. No how-to and no flavour.
    // Damage and healing are whole numbers, as every hit lands (CharacterStats.TakeDamage rounds halves up).
    public const string ManaColor = "#7FA8D8";

    public static int Whole(float value) => Mathf.Max(0, (int)Mathf.Floor(value + .5f));

    public bool Heals => spell == LeftHandSpell.Heal || effect == SpellEffect.HelpingHand;
    // Veils and summons have no damage or healing of their own.
    public bool HasPower => power > 0f && effect != SpellEffect.ShadowVeil && effect != SpellEffect.RaiseTheFallen && effect != SpellEffect.Summon;

    // The rows: cost is the mana it takes (a scroll passes its charges instead), power its damage or healing.
    public string TooltipRows(float cost, float power, int scrollCharges = 0)
    {
        var rows = new System.Collections.Generic.List<string>();
        void Row(string label, string value, string colour = ItemData.TextColor) =>
            rows.Add($"<color={ItemData.DimColor}>{label}</color><pos=48%><color={colour}>{value}</color>");
        string Seconds(float s) => $"{s:0.#}s";
        string Metres(float m) => $"{m:0}m";

        if (HasPower) Row(Heals ? "Healing" : "Damage", $"<size=125%>{Whole(power)}</size>", Heals ? ItemData.GoodColor : ItemData.TextColor);
        // The burn after the hit: its damage in all (the share Burn of the hit) and how long it takes.
        if (HasPower && !Heals && IsProjectile && burn > 0f) Row("Burn", $"{Mathf.Max(1, Whole(power * burn))} over {Seconds(burnSeconds)}");
        if (scrollCharges > 0) Row("Charges", scrollCharges.ToString());
        else Row("Mana", $"{Whole(cost)}", ManaColor);
        switch (effect)
        {
            case SpellEffect.ShadowVeil: Row("Duration", Seconds(veil.seconds)); break;
            case SpellEffect.RaiseTheFallen: Row("Duration", Seconds(raise.summon.lifetime)); break;
            case SpellEffect.Summon: Row("Duration", Seconds(summon.summon.lifetime)); break;
            case SpellEffect.HelpingHand: Row("Duration", Seconds(helpingHand.holdSeconds)); break;
            case SpellEffect.ChainLightning: Row("Jumps", chain.jumps.ToString()); Row("Range", Metres(chain.range)); break;
            case SpellEffect.SweepingArc: Row("Range", Metres(sweep.length)); break;
            case SpellEffect.Flick:
                if (flick.stunSeconds > 0f) Row("Stun", Seconds(flick.stunSeconds));
                Row("Range", Metres(flick.range));
                break;
            default:
                if (groundArea) { Row("Radius", Metres(areaRadius)); Row("Range", Metres(areaRange)); }
                else if (IsProjectile)
                {
                    if (pierce > 0) Row("Pierces", pierce.ToString());
                    if (stunSeconds > 0f) Row("Stun", Seconds(stunSeconds));
                    if (chill > 0f) Row("Slow", $"{chill * 100f:0}% for {Seconds(chillSeconds)}");
                }
                break;
        }
        return string.Join("\n", rows);
    }
}

// What a spell does when it leaves the hand. Serialized by integer: append only.
public enum SpellEffect { Standard = 0, ShadowVeil = 1, ChainLightning = 2, SweepingArc = 3, RaiseTheFallen = 4, Flick = 5, HelpingHand = 6, Summon = 7 }

