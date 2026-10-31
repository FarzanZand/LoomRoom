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
public class DuelEvent : MonoBehaviour, ICoopEventRoom
{
    [Tooltip("Where the duellist waits (an Enemy socket of the template marks the same spot).")]
    public Transform post;
    [Min(1f), Tooltip("Metres from the duellist at which he salutes and the duel is called.")]
    public float challengeDistance = 5f;
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
    [Range(1, 12)] public int maxTier = 2;
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
        if (phase != Phase.Waiting || !duellist.IsAlive) return;
        // Co-op: the host's duellist decides, for whichever player walks up first; the guests' copies follow (Step).
        if (Coop.IsGuest) return;
        if (Coop.Running) { foreach (var c in CoopWorld.Party()) if (Challenges(c)) { StartCoroutine(Salute(c)); return; } return; }
        var player = ScriptedEventRoom.TablePlayer;
        if (player != null && player.IsAlive && Challenges(player)) StartCoroutine(Salute(player));
    }

    bool Challenges(Character player)
    {
        var d = player.transform.position - duellist.transform.position; d.y = 0f;
        return room.Inside(player.transform.position) && d.magnitude <= challengeDistance;
    }

    // Co-op guests: the host's duel moved on (1: he salutes the player in slot value; 2: it was broken, value 1 = early).
    public void Used(int what, Character who) { }
    public void Step(int step, int value, int seed)
    {
        if (duellist == null) FindDuellist();
        if (duellist == null) return;
        if (step == 1 && phase == Phase.Waiting) { var who = CoopWorld.CharacterOf(value); if (who != null) StartCoroutine(Salute(who)); }
        else if (step == 2 && phase is not (Phase.Broken or Phase.Over)) StartCoroutine(Break(CoopWorld.CharacterOf(value >> 1), (value & 1) != 0));
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
        brain.Entranced = true;
        if (brain.Perception != null) brain.Perception.NoCloseAwareness = true;
        if (post != null) { var f = post.forward; f.y = 0f; if (f.sqrMagnitude > .01f) duellist.transform.rotation = Quaternion.LookRotation(f); }
        duellist.Damaged += OnHit;
        duellist.Died += OnDied;
    }

    void OnDestroy()
    {
        if (duellist != null) { duellist.Damaged -= OnHit; duellist.Died -= OnDied; }
        StopSalute();
    }

    IEnumerator Salute(Character player)
    {
        phase = Phase.Saluting;
        CoopWorld.EventStep(this, 1, CoopWorld.SlotOf(player));
        ScriptedEventRoom.Say(challengeLine);
        // He turns to face the player, then raises his blade.
        var to = player.transform.position - duellist.transform.position; to.y = 0f;
        var face = to.sqrMagnitude > .01f ? Quaternion.LookRotation(to) : duellist.transform.rotation;
        for (float t = 0; t < .35f && duellist != null; t += Time.deltaTime) { duellist.transform.rotation = Quaternion.Slerp(duellist.transform.rotation, face, t / .35f); yield return null; }
        if (duellist == null || phase != Phase.Saluting) yield break;
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

    void StopSalute() { if (saluteGraph.IsValid()) saluteGraph.Destroy(); }

    // Is this blow outside the rules of a duel: from behind, a spell (not the shield bash, which is the shield's), or
    // before he is done saluting?
    static bool Foul(DamageInfo info, bool early) =>
        early || info.Backstab || info.Magic && !info.FromEffect && info.Weapon == null;

    void OnHit(DamageInfo info)
    {
        // A player's blow (co-op: any player's, judged on the host).
        if (info.Source == null || Coop.IsGuest || !(info.Source is Player || CoopWorld.SlotOf(info.Source) != Coop.NoSlot)) return;
        if (phase == Phase.Duel && !Foul(info, false)) return;
        if (phase is Phase.Broken or Phase.Over) return;
        bool early = phase is Phase.Waiting or Phase.Saluting;
        if (!Foul(info, early)) return;
        StartCoroutine(Break(info.Source, early));
    }

    IEnumerator Break(Character player, bool early)
    {
        phase = Phase.Broken;
        StopSalute();
        CoopWorld.EventStep(this, 2, (Mathf.Max(0, CoopWorld.SlotOf(player)) << 1) | (early ? 1 : 0));
        if (brain != null && player != null) { ScriptedEventRoom.Free(brain); brain.Provoke(player.transform.position); }
        if (early) ScriptedEventRoom.Say(earlyLine);
        // The rule: given the first time (and said), quoted after that.
        if (RuleManager.HasInstance && rule != null)
        {
            if (RuleManager.Instance.Given(rule)) RuleManager.Instance.Quote(rule);
            else ScriptedEventRoom.Say(RuleManager.Instance.Give(rule));
        }
        ScriptedEventRoom.Say(friendsLine);
        // Co-op guests: his friends come from the host.
        if (Coop.IsGuest) yield break;
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
