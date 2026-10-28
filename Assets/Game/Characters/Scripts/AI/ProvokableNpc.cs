using System.Collections;
using UnityEngine;

// A friendly character the table player can pick a fight with (the tavern keeper). Neutral characters take no damage
// (FactionRules), so a player's blow or spell that lands on one comes here instead (Hitbox, SpellProjectile.Blast). The
// first blow gives the house rule about it (RuleManager, rule two: "New rule added: Do not attack the help..."; once
// given, he quotes it instead), the next ones until Hits To Turn get a warning, and that many blows (each after Calm
// Seconds) end it: it is swapped for Wrath Prefab (an enemy that wears its look and is nearly impossible to kill),
// which comes straight for the player, and the Dungeon Master has his say again.
public class ProvokableNpc : MonoBehaviour
{
    [Min(2), Tooltip("Blows it takes before it turns (the first gives the house rule, the ones between get a warning).")]
    public int hitsToTurn = 3;
    [Tooltip("Said by the Dungeon Master on the blows between the first and the last (one picked at random); on the first too once the rule is given.")]
    public DungeonMaster.Line[] provokedLines =
    {
        new() { text = "I would not do that again.", mumble = true },
        new() { text = "He has been down here longer than you. Leave him be.", mumble = true },
    };
    [Tooltip("Said by the Dungeon Master when it turns (one picked at random).")]
    public DungeonMaster.Line[] wrathLines =
    {
        new() { text = "I did warn you.", mumble = true },
        new() { text = "Now you've done it.", mumble = true },
    };
    [Tooltip("Posted to the message feed the first time. {Name} is its name with \"The\" in front.")]
    public string provokedMessage = "{Name} glares at you.";
    [Tooltip("Posted to the message feed when it turns.")]
    public string wrathMessage = "{Name} drops his wares and draws a blade.";

    [Header("Turning")]
    [Tooltip("The enemy it becomes on the second hit: same place and facing, alerted to the player at once.")]
    public GameObject wrathPrefab;
    [Tooltip("Played where it turns (a puff); optional.")]
    public GameObject turnEffect;
    [Min(0), Tooltip("Seconds after the first hit during which more hits count as the same one (a combo, a spell's blast).")]
    public float calmSeconds = .8f;
    [Min(0), Tooltip("Seconds between the second hit and the swap: it stands a moment, then turns.")]
    public float turnDelay = .35f;
    [Min(0), Tooltip("Camera shake when it turns (impulse force; 0 = none).")]
    public float turnShake = .5f;
    [Min(0), Tooltip("Seconds it reels from a blow before it turns to glare at whoever struck it.")]
    public float staggerSeconds = .45f;

    const int MerchantRule = 2;   // RuleManager's rule two, given on the first blow

    public int Hits { get; private set; }
    public bool Turned { get; private set; }
    float firstHitAt = -100f;

    Character character;
    NpcBrain brain;
    Coroutine glare;

    void Awake()
    {
        character = GetComponent<Character>();
        brain = GetComponent<NpcBrain>();
    }

    // Is this a friendly character the player can provoke? Called with the struck collider's character.
    public static bool TryProvoke(Character target, Character by)
    {
        if (target == null || !(by is Player)) return false;
        var provokable = target.GetComponent<ProvokableNpc>();
        if (provokable == null || !provokable.isActiveAndEnabled) return false;
        provokable.Provoke(by);
        return true;
    }

    public void Provoke(Character by)
    {
        if (Turned || by == null) return;
        if (Hits > 0 && Time.time - firstHitAt < calmSeconds) return;
        Hits++;
        Stagger(by, Hits >= hitsToTurn);
        if (Hits < hitsToTurn)
        {
            firstHitAt = Time.time;
            Post(provokedMessage);
            // House rule two, given on the first blow (pinned to the sheet from then on); once given, the first blow quotes it
            // ("Rule two.") and every blow before the last gets a warning.
            if (Hits == 1 && GiveRule()) return;
            if (Hits == 1) RuleManager.QuoteRule(MerchantRule);
            Say(provokedLines);
            return;
        }
        Turned = true;
        StartCoroutine(Turn(by));
    }

    IEnumerator Turn(Character by)
    {
        Post(wrathMessage);
        Say(wrathLines);
        if (turnDelay > 0f) yield return new WaitForSeconds(turnDelay);
        if (wrathPrefab == null) { Debug.LogWarning($"[ProvokableNpc] {name} has no Wrath Prefab."); yield break; }
        var at = transform.position;
        var facing = by != null ? Quaternion.LookRotation(Flat(by.transform.position - at)) : transform.rotation;
        if (turnEffect != null) Destroy(Instantiate(turnEffect, at, Quaternion.identity, transform.parent), 6f);
        if (turnShake > 0f)
        {
            var impulse = GetComponent<Unity.Cinemachine.CinemachineImpulseSource>();
            if (impulse == null) impulse = gameObject.AddComponent<Unity.Cinemachine.CinemachineImpulseSource>();
            impulse.GenerateImpulseWithForce(turnShake);
        }
        var wrath = Instantiate(wrathPrefab, at, facing, transform.parent);
        wrath.name = wrathPrefab.name;
        var enemy = wrath.GetComponent<EnemyBrain>();
        if (enemy != null) enemy.Alert();
        Destroy(gameObject);
    }

    // It reels the way the blow pushed it (HumanoidController: Hurt, Hurt Left, Hurt Right, Hurt Heavy on HurtDir, as an
    // enemy does), then turns to glare at whoever struck it.
    void Stagger(Character by, bool heavy)
    {
        var animator = character != null ? character.Animator : null;
        if (animator != null)
        {
            var push = Flat(transform.position - by.transform.position);
            float side = push.sqrMagnitude > .0001f ? Vector3.Dot(push.normalized, transform.right) : 0f;
            AnimatorHelper.SetInteger(animator, "HurtDir", heavy ? 3 : Mathf.Abs(side) < .35f ? 0 : side > 0f ? 2 : 1);
            character.TriggerAnimation(character.data != null && !string.IsNullOrEmpty(character.data.hurtTrigger) ? character.data.hurtTrigger : "Hurt");
        }
        if (glare != null) StopCoroutine(glare);
        glare = StartCoroutine(GlareAfter(by));
    }

    IEnumerator GlareAfter(Character by)
    {
        if (staggerSeconds > 0f) yield return new WaitForSeconds(staggerSeconds);
        if (brain != null && by != null) brain.SnapRotationTowards(by.transform.position);
        glare = null;
    }

    // The first blow ever: the rule is given and he says it ("New rule added: ..."). Already given: false.
    static bool GiveRule()
    {
        if (!RuleManager.HasInstance) return false;
        var rule = RuleManager.Instance.Rule(MerchantRule);
        if (rule == null || RuleManager.Instance.Given(rule)) return false;
        var line = RuleManager.Instance.Give(rule);
        if (line != null && !line.IsEmpty) DungeonMaster.Say(line);
        return true;
    }

    void Post(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string theName = character != null ? character.TheNameCap : name;
        MessageLog.Post(text.Replace("{Name}", theName), MessageKind.Warning);
    }

    static void Say(DungeonMaster.Line[] lines)
    {
        if (lines == null || lines.Length == 0) return;
        var line = lines[Random.Range(0, lines.Length)];
        if (line != null && !line.IsEmpty) DungeonMaster.Say(line);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
}
