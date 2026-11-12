using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The toll bridge (on the Troll bridge template's root, with a ScriptedEventRoom): the Bridge Troll stands in the middle
// of a bridge over lava and pays the player no mind until they walk up. Then he has his say in the dialogue box
// (his conversation asset in Dialogue/Conversations: its intro lines, the last one asking Pay or Fight) and the player
// picks: pay the toll and he steps off the bridge and lets them by for good, or fight him. Picking Pay with too little
// gold gets his Not Enough Gold line and the fight. Struck after taking the toll, he fights anyway (and the Dungeon
// Master has a word for it). Killed, he leaves a piece of gear, the biggest spoils, and every coin he was paid.
// Co-op: the host's troll decides, for whichever player walks up first. His say and the choice are played only in that
// player's box and paid from their own purse; the host waits for their answer (Used) and the guests follow (Step).
public class TollBridgeEvent : MonoBehaviour, ICoopEventRoom
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
        if (!Coop.IsGuest && walkingAside && brain != null && brain.Motor != null && brain.Motor.ReachedDestination(.3f))
        {
            walkingAside = false;
            brain.Motor.Stop();
            brain.Motor.ResetSpeed();
            turningBack = post != null;
        }
        // At his spot he turns round to face the bridge, at a walk's pace (a snap looked like a glitch).
        if (!Coop.IsGuest && turningBack && brain != null && brain.Motor != null && phase == Phase.Paid)
        {
            brain.Motor.Face(post.position, 160f);
            if (brain.Motor.IsFacing(post.position, 4f)) turningBack = false;
        }
        if (phase != Phase.Waiting || !troll.IsAlive) return;
        if (Coop.IsGuest) return;
        if (Coop.Running) { foreach (var c in CoopWorld.Party()) if (Stops(c)) { StartCoroutine(Ask(c)); return; } return; }
        var player = ScriptedEventRoom.TablePlayer;
        if (player != null && player.IsAlive && Stops(player)) StartCoroutine(Ask(player));
    }

    bool Stops(Character player)
    {
        if (player == null || !player.IsAlive) return false;
        var d = player.transform.position - troll.transform.position; d.y = 0f;
        return room.Inside(player.transform.position) && d.magnitude <= challengeDistance;
    }

    // -- Co-op --
    // Steps (host to guests): 1 he turns to the player in slot value and asks; 2 he was paid; 3 the fight is on
    // (value: slot << 2 | 0 picked, 1 struck before paying, 2 struck after). Used (guest to host): the answer of the
    // player he stopped, 1 paid, 2 fight.
    int answer;
    public void Used(int what, Character who) { if (Coop.IsHost && phase == Phase.Talking && (what == 1 || what == 2)) answer = what; }
    public void Step(int step, int value, int seed)
    {
        if (troll == null) FindTroll();
        if (troll == null) return;
        if (step == 1 && phase == Phase.Waiting) { var who = CoopWorld.CharacterOf(value); if (who != null) StartCoroutine(Ask(who)); }
        else if (step == 2 && phase is Phase.Waiting or Phase.Talking) { StopAllCoroutines(); EndTalk(); StepAside(); }
        else if (step == 3 && phase is not (Phase.Fighting or Phase.Over))
        {
            StopAllCoroutines(); EndTalk();
            int kind = value & 3;
            if (kind == 1) ScriptedEventRoom.Say(fightLine); else if (kind == 2) ScriptedEventRoom.Say(cheatLine);
            Fight(CoopWorld.CharacterOf(value >> 2));
        }
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
        if (troll != null) { troll.Damaged -= OnHit; troll.Died -= OnDied; }
    }

    IEnumerator Ask(Character player)
    {
        phase = Phase.Talking;
        answer = 0;
        CoopWorld.EventStep(this, 1, CoopWorld.SlotOf(player));
        // He turns to the player, then asks.
        var to = player.transform.position - troll.transform.position; to.y = 0f;
        var face = to.sqrMagnitude > .01f ? Quaternion.LookRotation(to) : troll.transform.rotation;
        for (float t = 0; t < .35f && troll != null; t += Time.deltaTime) { troll.transform.rotation = Quaternion.Slerp(troll.transform.rotation, face, t / .35f); yield return null; }
        if (troll == null || phase != Phase.Talking) yield break;
        bool pays = false;
        var box = PortraitDialogueUI.Current;
        bool mine = !Coop.Running || player == (Character)ScriptedEventRoom.TablePlayer;
        if (Coop.Running && !mine)
        {
            // Another player is asked: the host waits for their answer (or for them to fall or leave); a guest waits for the host.
            if (Coop.IsGuest) yield break;
            for (float t = 0; t < 120f && answer == 0 && player != null && player.IsAlive && phase == Phase.Talking; t += Time.deltaTime) yield return null;
            if (troll == null || phase != Phase.Talking) yield break;
            if (answer == 1) { CoopWorld.EventStep(this, 2); StepAside(); }
            else { CoopWorld.EventStep(this, 3, Mathf.Max(0, CoopWorld.SlotOf(player)) << 2); Fight(player); }
            yield break;
        }
        if (talk != null && talk.introLines != null && talk.introLines.Count > 0 && box != null && player is Player asking)
        {
            if (GameManager.HasInstance) { GameManager.Instance.Push(GameState.Dialogue); talkPushed = true; }
            if (asking.Look != null) StartCoroutine(CameraEase.LookAt(asking.Look, troll.transform.position + Vector3.up * 1.8f, .5f));
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
        // Co-op: a guest's answer goes to the host, whose troll steps aside or fights; this copy follows its word (Step).
        if (Coop.IsGuest) { CoopWorld.EventUsed(this, pays ? 1 : 2); yield break; }
        if (pays) { CoopWorld.EventStep(this, 2); StepAside(); }
        else { CoopWorld.EventStep(this, 3, Mathf.Max(0, CoopWorld.SlotOf(player)) << 2); Fight(player); }
    }

    void StepAside()
    {
        phase = Phase.Paid;
        ScriptedEventRoom.Say(paidLine);
        if (!Coop.IsGuest && aside != null && brain != null && brain.Motor != null) { brain.Motor.Resume(); brain.Motor.SetSpeed(asideSpeed); brain.Motor.MoveTo(aside.position); walkingAside = true; }
    }

    void Fight(Character player)
    {
        phase = Phase.Fighting;
        if (walkingAside && brain.Motor != null) brain.Motor.ResetSpeed();
        walkingAside = turningBack = false;
        ScriptedEventRoom.Free(brain);
        if (player != null) brain.Provoke(player.transform.position);
    }

    // A blow before he has had his say, or after he was paid, starts the fight.
    void OnHit(DamageInfo info)
    {
        // A player's blow (co-op: any player's, judged on the host; the guests follow its word).
        var player = info.Source;
        if (player == null || Coop.IsGuest || !(player is Player || CoopWorld.SlotOf(player) != Coop.NoSlot) || phase is Phase.Fighting or Phase.Over) return;
        bool wasPaid = phase == Phase.Paid;
        if (phase == Phase.Talking) { StopAllCoroutines(); EndTalk(); }
        if (wasPaid) ScriptedEventRoom.Say(cheatLine);
        else ScriptedEventRoom.Say(fightLine);
        CoopWorld.EventStep(this, 3, (Mathf.Max(0, CoopWorld.SlotOf(player)) << 2) | (wasPaid ? 2 : 1));
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
