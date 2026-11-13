using UnityEngine;

// A Relic's unique skill (the Party Crasher's, the user, 2026-11-11): a kill made with a critical hit, or with a charged
// heavy attack (the user, 2026-11-13; not the combo's finisher, which also counts as heavy in the hit), is celebrated.
// The wielder collects Gold through the usual gold pickup (CurrencyManager.Collect: the coin sound and the pickup line),
// a party horn sounds at Horn Volume, a burst of confetti goes off where the blow landed, and now and then (Line
// Chance) the Dungeon Master says one of Lines. Used from an EffectEntry of type Custom on the item, trigger On Hit
// Landed; it shows in the tooltip as the item's unique skill (Skill Text, its own line in its own colour).
[CreateAssetMenu(menuName = "Items/Effects/Crit Kill Reward")]
public class CritKillRewardEffect : ItemEffect
{
    [TextArea, Tooltip("What the tooltip says, on its own line in the unique skill colour.")]
    public string skillText = "When you kill someone with a critical hit, the Dungeon Master celebrates you with a gold reward.";
    [Min(0)] public int gold = 1;
    [Range(0f, 1f), Tooltip("Chance the Dungeon Master says one of the lines on a crit kill.")] public float lineChance = .15f;
    public DungeonMaster.Line[] lines =
    {
        new() { text = "Critical kill. Have a coin." },
        new() { text = "Now that is how you crash a party." },
        new() { text = "Bravo. A gold piece, on the house." },
        new() { text = "Did you see that? I saw that. Here, a coin." },
    };
    [Tooltip("The party horn, played where the blow landed.")] public AudioClip horn;
    [Range(0f, 1f)] public float hornVolume = .5f;
    [Tooltip("The burst at the point of impact (Items/Effects/Party confetti). Removed after Confetti Seconds.")]
    public GameObject confetti;
    [Min(.5f)] public float confettiSeconds = 3f;

    public override bool UniqueSkill => true;
    public override string Describe(EffectEntry entry) => skillText;

    public override void Apply(EffectEntry entry, EffectContext ctx)
    {
        var hit = ctx.Hit;
        // Only a kill, only by a critical hit or a charged heavy attack, only the wielder's own blow.
        bool heavyAttack = hit.Heavy && !hit.Finisher;
        if (!(hit.Critical || heavyAttack) || hit.Target == null || hit.Target.IsAlive || ctx.User == null || hit.Source != ctx.User) return;
        Vector3 at = hit.HitPoint != Vector3.zero ? hit.HitPoint : hit.Target.transform.position + Vector3.up;
        if (gold > 0 && ctx.User is Player player && CurrencyManager.HasInstance) CurrencyManager.Instance.Collect(player, gold, at);
        if (horn != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(horn, at, hornVolume, 0f);
        if (confetti != null) Destroy(Instantiate(confetti, at, Quaternion.identity), confettiSeconds);
        if (lines != null && lines.Length > 0 && Random.value < lineChance)
        {
            var line = lines[Random.Range(0, lines.Length)];
            if (line != null && !line.IsEmpty) DungeonMaster.Say(line);
        }
    }
}
