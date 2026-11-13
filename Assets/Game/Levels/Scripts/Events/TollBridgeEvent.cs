using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The toll bridge (on the Troll bridge template's root, with a ScriptedEventRoom): the Bridge Troll stands in the middle
// of a bridge over lava and pays the player no mind until they walk up. Then he has his say in the dialogue box
// (his conversation asset in Dialogue/Conversations: its intro lines, the last one asking Pay or Fight) and the player
// picks: pay the toll and he steps off the bridge and lets them by for good, or fight him. Picking Pay with too little
// gold gets his Not Enough Gold line and the fight. Struck after taking the toll, he fights anyway (and the Dungeon
// Master has a word for it). Killed, he leaves a piece of gear, the biggest spoils, and every coin he was paid.
public class TollBridgeEvent : MonoBehaviour
{
    [Tooltip("Where the troll waits (an Enemy socket of the template marks the same spot).")]
    public Transform post;
    [Tooltip("Where he steps to once paid, off the bridge, out of the way.")]
    public Transform aside;
    [Min(.5f), Tooltip("Metres a second he walks to Aside once paid.")]
    public float asideSpeed = 2.2f;
    [Min(1f), Tooltip("Metres from him at which he stops the player and asks for the toll.")]
    public float challengeDistance = 5.5f;
    [Min(0), Tooltip("Gold the toll costs.")] public int toll = 30;
    [Tooltip("His name here (said as it is: \"The Bridge Troll hits you\"). Empty: his own.")]
    public string guardName = "The Bridge Troll";
    [Tooltip("What he says (Dialogue/Conversations): its intro lines are said as the player walks up; the last of them has the answers. Its Not Enough Gold Line answers a Pay the player can't afford.")]
    public BossDialogueData talk;
    [Min(0), Tooltip("Which answer of the last line pays the toll (counted from 0); any other answer fights.")]
    public int payAnswer = 0;
    [Range(.1f, 2f), Tooltip("His damage here, times what his own data and the floor give him (the user, 2026-11-12: as Little Undead John's).")]
    public float damageMultiplier = .75f;
    [Range(.1f, 5f), Tooltip("His health here, times what his own data and the floor give him, full when he is found (as Little Undead John's: double).")]
    public float healthMultiplier = 2f;

    [Header("Reward (killed)")]
    [Tooltip("Highest gear tier of its prize on a level with no Gear by floor table; otherwise the prize is of the floor's tiers.")] [Range(1, 12)] public int maxTier = 2;
    public ItemRarity rarity = ItemRarity.Unique;

    [Header("Lines")]
    public DungeonMaster.Line fightLine = new() { text = "Fight it is." };
    public DungeonMaster.Line paidLine = new() { text = "Cheaper than a funeral." };
    public DungeonMaster.Line cheatLine = new() { text = "You paid him and then you hit him. Bold." };
    public DungeonMaster.Line wonLine = new() { text = "No more toll. Help yourself." };

    enum Phase { Waiting, Talking, Paid, Fighting, Over }
    Phase phase;
    ScriptedEventRoom room;
    Character troll;
    EnemyBrain brain;
    float nextScan;
    int paid;
    bool talkPushed, toughened, walkingAside, turningBack;

    public Character Troll => troll;
    public bool Paid => phase == Phase.Paid;

    void Start() => room = GetComponent<ScriptedEventRoom>();

    void Update()
    {
        if (room == null || !room.Find()) return;
        if (troll == null) { if (Time.time >= nextScan) { nextScan = Time.time + .5f; FindTroll(); } return; }
        Toughen();
        if (walkingAside && brain != null && brain.Motor != null && brain.Motor.ReachedDestination(.3f))
        {
            walkingAside = false;
            brain.Motor.Stop();
            brain.Motor.ResetSpeed();
            turningBack = post != null;
        }
        // At his spot he turns round to face the bridge, at a walk's pace (a snap looked like a glitch).
        if (turningBack && brain != null && brain.Motor != null && phase == Phase.Paid)
        {
            brain.Motor.Face(post.position, 160f);
            if (brain.Motor.IsFacing(post.position, 4f)) turningBack = false;
        }
        if (phase != Phase.Waiting) return;
        var player = ScriptedEventRoom.TablePlayer;
        if (player == null || !player.IsAlive || !troll.IsAlive) return;
        var d = player.transform.position - troll.transform.position; d.y = 0f;
        if (room.Inside(player.transform.position)) FaceEnd(troll.transform, d);
        if (room.Inside(player.transform.position) && d.magnitude <= challengeDistance) StartCoroutine(Ask(player));
    }

    // Both ends of the bridge have a door (the user, 2026-11-13): while he waits he turns, at a walk's pace, to the end
    // the player came in at (his post's facing or its reverse; `to` is from him to the player).
    void FaceEnd(Transform who, Vector3 to)
    {
        var along = post != null ? post.forward : who.forward; along.y = 0f;
        if (along.sqrMagnitude < .01f) return;
        if (Vector3.Dot(to, along) < 0f) along = -along;
        who.rotation = Quaternion.RotateTowards(who.rotation, Quaternion.LookRotation(along), 160f * Time.deltaTime);
    }

    void FindTroll()
    {
        var at = post != null ? post.position : transform.position;
        Character best = null; float bestDistance = 4f;
        foreach (var c in room.EnemiesInside())
        {
            float d = Vector3.Distance(c.transform.position, at);
            if (d < bestDistance) { best = c; bestDistance = d; }
        }
        if (best == null || !best.TryGetComponent(out brain)) return;
        troll = best;
        if (!string.IsNullOrWhiteSpace(guardName)) troll.nameOverride = guardName;
        brain.Entranced = true;
        if (brain.Perception != null) brain.Perception.NoCloseAwareness = true;
        if (post != null) { var f = post.forward; f.y = 0f; if (f.sqrMagnitude > .01f) troll.transform.rotation = Quaternion.LookRotation(f); }
        troll.Damaged += OnHit;
        troll.Died += OnDied;
        // Out of reach until the player walks up and he asks (EventProtection; the user, 2026-11-13).
        EventProtection.Hold(troll, this);
    }

    // Damage and Health Multiplier, once, once his stats are set up (after the floor's balance).
    void Toughen()
    {
        if (toughened || !troll.TryGetComponent<CharacterStats>(out var s) || !s.Initialised) return;
        toughened = true;
        if (!Mathf.Approximately(damageMultiplier, 1f)) s.AddModifier(new StatModifier(StatType.AttackDamage, damageMultiplier - 1f, ModifierType.PercentMultiply, this));
        if (!Mathf.Approximately(healthMultiplier, 1f) && s.IsAlive)
        {
            s.AddModifier(new StatModifier(StatType.MaxHealth, healthMultiplier - 1f, ModifierType.PercentMultiply, this));
            s.SetHealthShare(1f);
        }
    }

    void OnDestroy()
    {
        EndTalk();
        EventProtection.Release(troll);
        if (troll != null) { troll.Damaged -= OnHit; troll.Died -= OnDied; }
    }

    IEnumerator Ask(Player player)
    {
        phase = Phase.Talking;
        EventProtection.Release(troll);
        // He turns to the player, then asks.
        var to = player.transform.position - troll.transform.position; to.y = 0f;
        var face = to.sqrMagnitude > .01f ? Quaternion.LookRotation(to) : troll.transform.rotation;
        for (float t = 0; t < .35f && troll != null; t += Time.deltaTime) { troll.transform.rotation = Quaternion.Slerp(troll.transform.rotation, face, t / .35f); yield return null; }
        if (troll == null || phase != Phase.Talking) yield break;
        bool pays = false;
        var box = PortraitDialogueUI.Current;
        if (talk != null && talk.introLines != null && talk.introLines.Count > 0 && box != null)
        {
            if (GameManager.HasInstance) { GameManager.Instance.Push(GameState.Dialogue); talkPushed = true; }
            if (player.Look != null) StartCoroutine(CameraEase.LookAt(player.Look, troll.transform.position + Vector3.up * 1.8f, .5f));
            string name = !string.IsNullOrWhiteSpace(talk.bossName) ? talk.bossName : troll.TheNameCap;
            // The answers' replies are said after the gold is counted: the lines are played with the last answers' replies
            // left out, then the reply that fits.
            var lines = new List<PortraitLine>(talk.introLines);
            var last = lines[lines.Count - 1];
            var asked = last != null && last.choices != null && last.choices.Count > 0 ? last : null;
            if (asked != null) lines[lines.Count - 1] = new PortraitLine { speaker = asked.speaker, name = asked.name, model = asked.model, text = asked.text, voice = asked.voice, choices = asked.choices.ConvertAll(c => new PortraitChoice { label = c.label, leaves = c.leaves }) };
            yield return box.Play(lines, troll, name, null, talk.placement);
            if (troll == null || phase != Phase.Talking) { EndTalk(); yield break; }
            int picked = box.Picked;
            pays = asked != null && picked == payAnswer;
            var wallet = player.GetComponent<Wallet>();
            List<PortraitLine> reply = new();
            if (pays && (wallet == null || !wallet.TrySpend(toll)))
            {
                pays = false;
                if (!string.IsNullOrWhiteSpace(talk.notEnoughGoldLine)) reply.Add(new PortraitLine { speaker = PortraitSpeaker.Boss, text = talk.notEnoughGoldLine });
            }
            else if (asked != null && picked >= 0 && picked < asked.choices.Count && asked.choices[picked]?.reply != null)
                foreach (var r in asked.choices[picked].reply)
                    if (r != null) reply.Add(new PortraitLine { speaker = r.speaker, name = r.name, model = r.model, text = r.text, voice = r.voice });
            if (pays) paid = toll;
            if (reply.Count > 0) yield return box.Play(reply, troll, name, null, talk.placement);
            EndTalk();
            if (troll == null || phase != Phase.Talking) yield break;
        }
        if (pays) StepAside();
        else Fight(player);
    }

    void StepAside()
    {
        phase = Phase.Paid;
        ScriptedEventRoom.Say(paidLine);
        if (aside != null && brain != null && brain.Motor != null) { brain.Motor.Resume(); brain.Motor.SetSpeed(asideSpeed); brain.Motor.MoveTo(AsideSpot()); walkingAside = true; }
    }

    // Where he steps aside to: the landing at the far end from the player, so they pass him going on. That is the Aside
    // marker, or its mirror through his post when the player came in at the marker's end (a door at each end,
    // 2026-11-13): the room is laid out the same turned half round (a brazier on each landing, on opposite sides), so the
    // mirrored spot is as clear of the brazier as the marker is.
    Vector3 AsideSpot()
    {
        var player = ScriptedEventRoom.TablePlayer;
        if (post == null || player == null) return aside.position;
        var along = post.forward; along.y = 0f;
        if (along.sqrMagnitude < .01f) return aside.position;
        along.Normalize();
        float playerSide = Vector3.Dot(player.transform.position - post.position, along), asideSide = Vector3.Dot(aside.position - post.position, along);
        if (playerSide * asideSide <= 0f) return aside.position;
        var mirrored = post.position * 2f - aside.position; mirrored.y = aside.position.y;
        return mirrored;
    }

    void Fight(Player player)
    {
        phase = Phase.Fighting;
        if (walkingAside && brain.Motor != null) brain.Motor.ResetSpeed();
        walkingAside = turningBack = false;
        ScriptedEventRoom.Free(brain);
        brain.Provoke(player.transform.position);
    }

    // A blow before he has had his say, or after he was paid, starts the fight.
    void OnHit(DamageInfo info)
    {
        if (info.Source is not Player player || phase is Phase.Fighting or Phase.Over) return;
        bool wasPaid = phase == Phase.Paid;
        if (phase == Phase.Talking) { StopAllCoroutines(); EndTalk(); }
        if (wasPaid) ScriptedEventRoom.Say(cheatLine);
        else ScriptedEventRoom.Say(fightLine);
        Fight(player);
    }

    void OnDied()
    {
        phase = Phase.Over;
        ScriptedEventRoom.Say(wonLine);
        var rng = new System.Random(room.Dungeon.EventSeed(room.Room, 2));
        var at = troll.transform.position + Vector3.up * .3f;
        room.DropAt(ScriptedEventRoom.Gear(rarity, rng, maxTier), at + troll.transform.right * .6f);
        room.DropAt(ScriptedEventRoom.BestSpoils(), at - troll.transform.right * .6f);
        if (paid > 0) room.GoldAt(paid, at + troll.transform.forward * .6f, 3);
    }

    // The talk's Dialogue state, popped once (also when the room goes mid-talk: a stopped coroutine runs no finally).
    void EndTalk()
    {
        if (!talkPushed) return;
        talkPushed = false;
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dialogue);
        if (PortraitDialogueUI.Current != null && PortraitDialogueUI.Current.Open && !PortraitDialogueUI.Current.Barking) PortraitDialogueUI.Current.Abort();
    }
}
