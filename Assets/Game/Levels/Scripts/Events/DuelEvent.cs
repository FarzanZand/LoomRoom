using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// The duel (on the Duel bridge template's root, with a ScriptedEventRoom): a lone duellist waits in the middle of the
// bridge and pays the player no mind. Walk up to him and he salutes, the Dungeon Master calls a duel, and he fights. Win
// it fair, face to face, and he leaves his reward: a piece of gear rolled Unique and the biggest spoils. Strike him from
// behind, with a spell or before the salute is over and the Dungeon Master stops the duel with a house rule (the first
// time he gives it; after that he quotes it) and the duellist's friends come out at both ends of the bridge.
public class DuelEvent : MonoBehaviour
{
    [Tooltip("Where the duellist waits (an Enemy socket of the template marks the same spot).")]
    public Transform post;
    [Min(1f), Tooltip("Metres from the duellist at which he salutes and the duel is called.")]
    public float challengeDistance = 5f;
    [Range(.1f, 2f), Tooltip("His damage in the duel, times what his own data and the floor give him (the user, 2026-11-12: 25% less, then .55 so that as a Brutal elite, +35%, he still hits as he did at .75). The same enemy elsewhere (the Realms' Undead Knight) keeps his own.")]
    public float damageMultiplier = .55f;
    [Range(.1f, 5f), Tooltip("His health in the duel, times what his own data and the floor give him, full when he is found (the user, 2026-11-12: double). The Realms' Undead Knight elsewhere keeps his own.")]
    public float healthMultiplier = 2f;
    [Tooltip("He is an elite of this kind in the duel (the user, 2026-11-12: Brutal, the red glow): its name, glow, size, steadiness and double experience. No extra floors of growth: his numbers are Damage and Health Multiplier's. Off: a plain duellist.")]
    public bool elite = true;
    public EliteTrait eliteTrait = EliteTrait.Brutal;
    [Tooltip("His name in the duel (said as it is, with no \"the\" in front; the elite's word is left off it). Empty: his own (the Realms' Undead Knight keeps his).")]
    public string duellistName = "Little Undead John";
    [Tooltip("What he says in the dialogue box as the player walks up, before the salute (its intro lines; its placement). Empty: nothing.")]
    public BossDialogueData talk;
    [Tooltip("His salute (a humanoid clip played over his animator), and how long he holds it before he fights.")]
    public AnimationClip salute;
    [Min(.2f)] public float saluteSeconds = 1.6f;
    [Tooltip("The house rule he gives when the duel is broken (Rules/Data/Rule 3).")]
    public HouseRule rule;
    [Header("Friends (a broken duel)")]
    [Tooltip("Who comes out at the bridge's ends. Empty: the room's own pick from the biome.")]
    public GameObject friend;
    [Range(1, 6)] public int friends = 2;
    [Range(0, 3), Tooltip("One more friend every this many floors (0: never more).")]
    public int morePerFloors = 3;
    [Header("Reward (a fair win)")]
    [Tooltip("Highest gear tier of its prize on a level with no Gear by floor table; otherwise the prize is of the floor's tiers.")] [Range(1, 12)] public int maxTier = 2;
    public ItemRarity rarity = ItemRarity.Unique;
    [Tooltip("Dropped too on a fair win while its class (ItemData.unlocksClass) is still locked: picked up, it unlocks the class (the Rogue's poster).")]
    public ItemData classUnlock;
    [Header("Lines")]
    public DungeonMaster.Line challengeLine = new() { text = "He wants a duel. Face him." };
    public DungeonMaster.Line earlyLine = new() { text = "He hadn't even finished his salute." };
    public DungeonMaster.Line friendsLine = new() { text = "His friends saw that too." };
    public DungeonMaster.Line fairLine = new() { text = "He lost fair. It's yours." };
    public DungeonMaster.Line foulWinLine = new() { text = "You won. Not the way he'd have liked." };

    enum Phase { Waiting, Saluting, Duel, Broken, Over }
    Phase phase;
    ScriptedEventRoom room;
    Character duellist;
    EnemyBrain brain;
    PlayableGraph saluteGraph;
    float nextScan;

    public bool Broken => phase == Phase.Broken;
    public bool Over => phase == Phase.Over;
    public Character Duellist => duellist;

    void Start()
    {
        room = GetComponent<ScriptedEventRoom>();
    }

    void Update()
    {
        if (room == null || !room.Find()) return;
        if (duellist == null) { if (Time.time >= nextScan) { nextScan = Time.time + .5f; FindDuellist(); } return; }
        Weaken();
        if (phase != Phase.Waiting) return;
        var player = ScriptedEventRoom.TablePlayer;
        if (player == null || !player.IsAlive || !duellist.IsAlive) return;
        var d = player.transform.position - duellist.transform.position; d.y = 0f;
        if (room.Inside(player.transform.position)) FaceEnd(duellist.transform, d);
        if (room.Inside(player.transform.position) && d.magnitude <= challengeDistance) StartCoroutine(Salute(player));
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

    // The enemy standing nearest his post in the room. He waits there, facing the way the post faces.
    void FindDuellist()
    {
        var at = post != null ? post.position : transform.position;
        Character best = null; float bestDistance = 4f;
        foreach (var c in room.EnemiesInside())
        {
            float d = Vector3.Distance(c.transform.position, at);
            if (d < bestDistance) { best = c; bestDistance = d; }
        }
        if (best == null || !best.TryGetComponent(out brain)) return;
        duellist = best;
        if (!string.IsNullOrWhiteSpace(duellistName)) duellist.nameOverride = duellistName;
        brain.Entranced = true;
        if (brain.Perception != null) brain.Perception.NoCloseAwareness = true;
        if (post != null) { var f = post.forward; f.y = 0f; if (f.sqrMagnitude > .01f) duellist.transform.rotation = Quaternion.LookRotation(f); }
        duellist.Damaged += OnHit;
        duellist.Died += OnDied;
        // Out of reach until the player walks up and the talk starts (the user, 2026-11-13: he was killed with spells
        // from the bridge's far end); the duel's own rules apply from then on.
        EventProtection.Hold(duellist, this);
    }

    // Damage Multiplier on his damage and Health Multiplier on his health (full), once, once his stats are set up
    // (after the floor's balance).
    bool weakened;
    void Weaken()
    {
        if (weakened || !duellist.TryGetComponent<CharacterStats>(out var s) || !s.Initialised) return;
        weakened = true;
        if (!Mathf.Approximately(damageMultiplier, 1f)) s.AddModifier(new StatModifier(StatType.AttackDamage, damageMultiplier - 1f, ModifierType.PercentMultiply, this));
        if (!Mathf.Approximately(healthMultiplier, 1f) && s.IsAlive)
        {
            s.AddModifier(new StatModifier(StatType.MaxHealth, healthMultiplier - 1f, ModifierType.PercentMultiply, this));
            s.SetHealthShare(1f);
        }
        if (elite && !duellist.TryGetComponent<EliteEnemy>(out _))
        {
            EliteEnemy.Make(duellist, eliteTrait, 0f);
            // His own name stays whole ("Little Undead John", not "Brutal Little Undead John").
            if (!string.IsNullOrWhiteSpace(duellistName)) duellist.namePrefix = null;
        }
    }

    void OnDestroy()
    {
        EndTalk();
        EventProtection.Release(duellist);
        if (duellist != null) { duellist.Damaged -= OnHit; duellist.Died -= OnDied; }
        StopSalute();
    }

    IEnumerator Salute(Player player)
    {
        phase = Phase.Saluting;
        EventProtection.Release(duellist);
        // He turns to face the player, then has his say in the box, then the Dungeon Master calls the duel and he
        // raises his blade.
        var to = player.transform.position - duellist.transform.position; to.y = 0f;
        var face = to.sqrMagnitude > .01f ? Quaternion.LookRotation(to) : duellist.transform.rotation;
        for (float t = 0; t < .35f && duellist != null; t += Time.deltaTime) { duellist.transform.rotation = Quaternion.Slerp(duellist.transform.rotation, face, t / .35f); yield return null; }
        if (duellist == null || phase != Phase.Saluting) yield break;
        if (talk != null && talk.introLines != null && talk.introLines.Count > 0 && PortraitDialogueUI.Current != null)
        {
            if (GameManager.HasInstance) { GameManager.Instance.Push(GameState.Dialogue); talkPushed = true; }
            if (player.Look != null) StartCoroutine(CameraEase.LookAt(player.Look, duellist.transform.position + Vector3.up * 1.5f, .5f));
            yield return PortraitDialogueUI.Current.Play(talk.introLines, duellist, !string.IsNullOrWhiteSpace(talk.bossName) ? talk.bossName : duellist.TheNameCap, null, talk.placement);
            EndTalk();
            if (duellist == null || phase != Phase.Saluting) yield break;
        }
        ScriptedEventRoom.Say(challengeLine);
        if (salute != null && duellist.Animator != null)
        {
            var playable = AnimationPlayableUtilities.PlayClip(duellist.Animator, salute, out saluteGraph);
            playable.SetSpeed(salute.length / Mathf.Max(.2f, saluteSeconds) * .9f);
        }
        yield return new WaitForSeconds(saluteSeconds);
        StopSalute();
        if (phase != Phase.Saluting || duellist == null || !duellist.IsAlive) yield break;
        phase = Phase.Duel;
        ScriptedEventRoom.Free(brain);
        brain.Provoke(player.transform.position);
    }

    // The talk's Dialogue state, popped once (also when the room goes mid-talk: a stopped coroutine runs no finally).
    bool talkPushed;
    void EndTalk()
    {
        if (!talkPushed) return;
        talkPushed = false;
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dialogue);
        if (PortraitDialogueUI.Current != null && PortraitDialogueUI.Current.Open && !PortraitDialogueUI.Current.Barking) PortraitDialogueUI.Current.Abort();
    }

    void StopSalute() { if (saluteGraph.IsValid()) saluteGraph.Destroy(); }

    // Is this blow outside the rules of a duel: from behind, a spell (not the shield bash, which is the shield's), or
    // before he is done saluting?
    static bool Foul(DamageInfo info, bool early) =>
        early || info.Backstab || info.Magic && !info.FromEffect && info.Weapon == null;

    void OnHit(DamageInfo info)
    {
        if (info.Source == null || info.Source is not Player) return;
        if (phase == Phase.Duel && !Foul(info, false)) return;
        if (phase is Phase.Broken or Phase.Over) return;
        bool early = phase is Phase.Waiting or Phase.Saluting;
        if (!Foul(info, early)) return;
        StartCoroutine(Break(info.Source as Player, early));
    }

    IEnumerator Break(Player player, bool early)
    {
        phase = Phase.Broken;
        StopSalute();
        if (brain != null) { ScriptedEventRoom.Free(brain); brain.Provoke(player.transform.position); }
        if (early) ScriptedEventRoom.Say(earlyLine);
        // The rule: given the first time (and said), quoted after that.
        if (RuleManager.HasInstance && rule != null)
        {
            if (RuleManager.Instance.Given(rule)) RuleManager.Instance.Quote(rule);
            else ScriptedEventRoom.Say(RuleManager.Instance.Give(rule));
        }
        ScriptedEventRoom.Say(friendsLine);
        yield return new WaitForSeconds(.8f);
        // His friends at the bridge's ends, the far one first: out of a puff, between the player and the way back.
        var rng = new System.Random(room.Dungeon.EventSeed(room.Room, 1));
        int count = friends + (morePerFloors > 0 ? (ScriptedEventRoom.Floor - 1) / morePerFloors : 0);
        var ends = new List<Vector3>();
        foreach (var (edge, outward) in room.Dungeon.WaysOut(room.Room)) ends.Add(edge - outward * 1.4f);
        if (ends.Count == 0) ends.AddRange(room.FreeFloor(rng, 0f));
        for (int i = 0; i < count && ends.Count > 0; i++)
        {
            var at = ends[i % ends.Count] + Vector3.Cross(Vector3.up, (ends[i % ends.Count] - room.transform.position).normalized) * ((i / ends.Count) % 2 == 0 ? .7f : -.7f);
            room.Summon(friend, at, rng);
            yield return new WaitForSeconds(.35f);
        }
    }

    void OnDied()
    {
        bool fair = phase == Phase.Duel;
        phase = Phase.Over;
        StopSalute();
        if (!fair) { ScriptedEventRoom.Say(foulWinLine); return; }
        ScriptedEventRoom.Say(fairLine);
        var rng = new System.Random(room.Dungeon.EventSeed(room.Room, 2));
        var at = duellist.transform.position + Vector3.up * .3f;
        room.DropAt(ScriptedEventRoom.Gear(rarity, rng, maxTier), at + duellist.transform.right * .6f);
        room.DropAt(ScriptedEventRoom.BestSpoils(), at - duellist.transform.right * .6f);
        if (classUnlock != null && classUnlock.unlocksClass != null && !classUnlock.unlocksClass.Unlocked)
            room.DropAt(classUnlock, at + duellist.transform.forward * .6f);
    }
}
