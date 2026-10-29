using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// The wager (on the Card table template's root, with a ScriptedEventRoom): a skeleton sits at a table with three bowls
// turned over and a coin. Put gold down (the table, WagerTable) and he hides the coin under a bowl and shuffles them;
// pick the bowl with the coin (WagerCup) and the bet comes back doubled. Win twice and he starts cheating: the coin is
// under none of them. Hit him once he has cheated and you called it: his friends come out of the walls, and when they
// and he are down his pot is yours. Hit him when he played fair and his friends come all the same, with no pot.
// He deals once a run: after the first bet, a wager table on a later floor of the same run won't take one.
// The coin is hidden while a bowl covers it (it is wider than a bowl and showed round its rim).
public class WagerEvent : MonoBehaviour
{
    [Tooltip("Where the dealer sits (an Enemy socket of the template marks the same spot).")] public Transform seat;
    [Tooltip("The three bowls, left to right as the player sees them.")] public Transform[] cups = new Transform[3];
    [Tooltip("The coin he hides.")] public Transform coin;
    [Tooltip("Where the coin waits in front of the bowls between games.")] public Transform coinRest;
    [Tooltip("His sitting pose (played over his animator while he deals).")] public AnimationClip sitting;
    [Header("Game")]
    [Min(1)] public int bet = 10;
    [Min(0)] public int betPerFloor = 5;
    [Min(1), Tooltip("Games he plays before he has had enough.")] public int games = 6;
    [Min(1), Tooltip("Wins the player can have before he starts cheating.")] public int winsBeforeCheating = 2;
    [Range(2, 20)] public int swaps = 6;
    [Range(0, 6), Tooltip("Extra swaps for each game won.")] public int swapsPerWin = 2;
    [Min(.1f)] public float swapSeconds = .45f;
    [Range(.3f, 1f), Tooltip("Each game won makes the swaps this much quicker.")] public float quicker = .85f;
    [Min(.05f)] public float lift = .28f;
    [Header("Sounds")]
    public AudioClip cupKnock;
    public AudioClip coinSound;
    [Header("A fight")]
    [Tooltip("Who comes out of the walls. Empty: the room's own pick from the biome.")] public GameObject friend;
    [Range(0, 6)] public int friends = 2;
    [Tooltip("Gold in his pot (when he is called out for cheating), on top of everything the player lost to him.")]
    public int potGold = 40;
    [Header("Lines")]
    public DungeonMaster.Line welcomeLine = new() { text = "He wants to play. Find the coin." };
    public DungeonMaster.Line watchLine = new() { text = "Watch closely." };
    public DungeonMaster.Line pickLine = new() { text = "Pick one." };
    public DungeonMaster.Line winLine = new() { text = "Lucky." };
    public DungeonMaster.Line loseLine = new() { text = "Not that one." };
    public DungeonMaster.Line cheatHint = new() { text = "He doesn't like losing." };
    public DungeonMaster.Line cheatReveal = new() { text = "None of them? Funny, that." };
    public DungeonMaster.Line calledLine = new() { text = "You called it. Now deal with his friends." };
    public DungeonMaster.Line soreLine = new() { text = "He played fair, you know." };
    public DungeonMaster.Line potLine = new() { text = "His pot is yours." };
    public DungeonMaster.Line poorLine = new() { text = "You can't cover the bet." };
    public DungeonMaster.Line doneLine = new() { text = "He's done for tonight." };
    [Tooltip("Said on walking in, instead of the welcome, when the player already played him this run.")]
    public DungeonMaster.Line playedLine = new() { text = "He won't play you twice." };

    enum Phase { Idle, Dealing, Picking, Fight, Over }
    Phase phase;
    ScriptedEventRoom room;
    Character dealer;
    EnemyBrain brain;
    PlayableGraph sitGraph;
    int coinCup = -1, wins, played, lost;
    bool cheating, cheated, welcomed;
    readonly List<Character> friendsUp = new();
    float nextScan;

    // Once a run (a ProgressionManager flag, so a continued run keeps it; a new run clears it, NewRun).
    const string PlayedFlag = "run.wager.played";
    public static void NewRun() { if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(PlayedFlag, 0); }
    static bool PlayedThisRun => ProgressionManager.HasInstance && ProgressionManager.Instance.HasFlag(PlayedFlag);

    public int Bet => bet + betPerFloor * (ScriptedEventRoom.Floor - 1);
    public bool CanBet => phase == Phase.Idle && dealer != null && dealer.IsAlive && played < games && (played > 0 || !PlayedThisRun);
    public bool Picking => phase == Phase.Picking;
    public string TablePrompt => CanBet ? $"Bet {Bet} gold" : "";
    public bool Cheating => cheating;

    void Start()
    {
        room = GetComponent<ScriptedEventRoom>();
        KeepOffTable();
        if (coin != null && coinRest != null) coin.SetPositionAndRotation(coinRest.position, coinRest.rotation);
    }

    void Update()
    {
        if (room == null || !room.Find()) return;
        if (dealer == null && Time.time >= nextScan && phase == Phase.Idle) { nextScan = Time.time + .5f; FindDealer(); }
        if (dealer != null && !welcomed && room.PlayerInside && room.DepthOf(ScriptedEventRoom.TablePlayer.transform.position) > 1.5f)
        {
            welcomed = true;
            ScriptedEventRoom.Say(PlayedThisRun ? playedLine : welcomeLine);
        }
        if (phase == Phase.Fight) WatchFight();
    }

    void FindDealer()
    {
        var at = seat != null ? seat.position : transform.position;
        Character best = null; float bestDistance = 2.5f;
        foreach (var c in room.EnemiesInside())
        {
            float d = Vector3.Distance(c.transform.position, at);
            if (d < bestDistance) { best = c; bestDistance = d; }
        }
        if (best == null || !best.TryGetComponent(out brain)) return;
        dealer = best;
        brain.Entranced = true;
        if (brain.Perception != null) brain.Perception.NoCloseAwareness = true;
        // Seated behind the table, still (his agent would push him off the chair).
        if (dealer.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent)) agent.enabled = false;
        if (seat != null) dealer.transform.SetPositionAndRotation(seat.position, seat.rotation);
        Sit(true);
        dealer.Damaged += OnHit;
        dealer.Died += OnDealerDied;
    }

    void Sit(bool on)
    {
        if (sitGraph.IsValid()) sitGraph.Destroy();
        if (on && sitting != null && dealer != null && dealer.Animator != null) AnimationPlayableUtilities.PlayClip(dealer.Animator, sitting, out sitGraph);
    }

    void OnDestroy()
    {
        if (dealer != null) { dealer.Damaged -= OnHit; dealer.Died -= OnDealerDied; }
        if (sitGraph.IsValid()) sitGraph.Destroy();
    }

    // ── The game ─────────────────────────────────────────────────────

    public void PlaceBet(Player player)
    {
        if (!CanBet || player == null || player.Wallet == null) return;
        if (!player.Wallet.TrySpend(Bet)) { ScriptedEventRoom.Say(poorLine); return; }
        lost += Bet;
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(PlayedFlag, 1);
        ScriptedEventRoom.Play(coinSound, coin != null ? coin.position : transform.position, .8f);
        StartCoroutine(Deal(player));
    }

    IEnumerator Deal(Player player)
    {
        phase = Phase.Dealing;
        played++;
        cheating = wins >= winsBeforeCheating;
        var rng = new System.Random(room.Dungeon.EventSeed(room.Room, 30 + played));
        if (played == 1) ScriptedEventRoom.Say(watchLine);
        else if (cheating && !cheated) ScriptedEventRoom.Say(cheatHint);
        // The coin goes under the middle bowl: it lifts, the coin slides in, it comes down.
        coinCup = 1;
        yield return Lift(cups[1], true);
        if (coin != null) yield return Move(coin, Under(cups[1]), .3f);
        yield return Lift(cups[1], false);
        ShowCoin(false);
        if (cheating)
        {
            // He palms it as the bowl comes down: the coin is under none of them.
            cheated = true;
            if (coin != null) coin.gameObject.SetActive(false);
            coinCup = -1;
        }
        yield return new WaitForSeconds(.35f);
        int count = swaps + swapsPerWin * wins;
        float seconds = swapSeconds * Mathf.Pow(quicker, wins);
        for (int i = 0; i < count; i++)
        {
            int a = rng.Next(3), b = (a + 1 + rng.Next(2)) % 3;
            yield return Swap(a, b, seconds);
        }
        ScriptedEventRoom.Say(pickLine);
        phase = Phase.Picking;
    }

    // The player picks a bowl (WagerCup).
    public void Pick(Transform cup)
    {
        if (phase != Phase.Picking) return;
        int index = System.Array.IndexOf(cups, cup);
        if (index < 0) return;
        StartCoroutine(Reveal(index));
    }

    IEnumerator Reveal(int picked)
    {
        phase = Phase.Dealing;
        bool won = picked == coinCup;
        if (won) ShowCoin(true);
        yield return Lift(cups[picked], true);
        if (won)
        {
            wins++;
            ScriptedEventRoom.Say(winLine);
            ScriptedEventRoom.Play(coinSound, cups[picked].position, .9f);
            room.GoldAt(Bet * 2, Front());
        }
        else if (cheating)
        {
            // Every bowl comes up empty.
            for (int i = 0; i < cups.Length; i++) if (i != picked) StartCoroutine(Lift(cups[i], true));
            yield return new WaitForSeconds(.4f);
            ScriptedEventRoom.Say(cheatReveal);
        }
        else
        {
            ScriptedEventRoom.Say(loseLine);
            ShowCoin(true);
            yield return Lift(cups[coinCup], true);
        }
        yield return new WaitForSeconds(1.3f);
        if (phase == Phase.Fight) yield break;
        for (int i = 0; i < cups.Length; i++) StartCoroutine(Lift(cups[i], false));
        yield return new WaitForSeconds(.35f);
        // The coin back in front of the bowls.
        if (coin != null && coinRest != null) { coin.gameObject.SetActive(true); coin.SetPositionAndRotation(coinRest.position, coinRest.rotation); }
        ShowCoin(true);
        if (phase == Phase.Fight) yield break;
        phase = Phase.Idle;
        if (played >= games) ScriptedEventRoom.Say(doneLine);
    }

    Vector3 Front() => coinRest != null ? coinRest.position + (ScriptedEventRoom.TablePlayer != null ? (ScriptedEventRoom.TablePlayer.transform.position - coinRest.position).normalized * .5f : Vector3.zero) : transform.position;

    // The coin drawn or not, while it stays in play (Swap moves it under its bowl either way).
    void ShowCoin(bool on)
    {
        if (coin == null) return;
        foreach (var r in coin.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
    }

    // On the table under a bowl (the coin's height is its rest's).
    Vector3 Under(Transform cup) => new(cup.position.x, coinRest != null ? coinRest.position.y : cup.position.y, cup.position.z);

    IEnumerator Lift(Transform cup, bool up)
    {
        if (cup == null) yield break;
        var rest = cup.GetComponent<WagerCup>();
        var from = cup.localPosition;
        var to = rest != null ? rest.RestLocal(cup) + (up ? Vector3.up * lift : Vector3.zero) : from;
        for (float t = 0; t < .22f; t += Time.deltaTime) { cup.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, t / .22f)); yield return null; }
        cup.localPosition = to;
        if (!up) ScriptedEventRoom.Play(cupKnock, cup.position, .5f);
    }

    static IEnumerator Move(Transform t, Vector3 to, float seconds)
    {
        var from = t.position;
        for (float s = 0; s < seconds; s += Time.deltaTime) { t.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, s / seconds)); yield return null; }
        t.position = to;
    }

    // Two bowls change places, one passing in front of the other, the coin under its own.
    IEnumerator Swap(int a, int b, float seconds)
    {
        var ca = cups[a]; var cb = cups[b];
        var wa = ca.GetComponent<WagerCup>(); var wb = cb.GetComponent<WagerCup>();
        if (wa == null || wb == null) yield break;
        Vector3 pa = wa.RestLocal(ca), pb = wb.RestLocal(cb);
        var across = pb - pa;
        var side = Vector3.Cross(Vector3.up, across).normalized * Mathf.Max(.12f, across.magnitude * .35f);
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0, 1, t / seconds), arc = Mathf.Sin(u * Mathf.PI);
            ca.localPosition = Vector3.Lerp(pa, pb, u) + side * arc;
            cb.localPosition = Vector3.Lerp(pb, pa, u) - side * arc;
            if (coin != null && coin.gameObject.activeSelf && coinCup == a) coin.position = Under(ca);
            else if (coin != null && coin.gameObject.activeSelf && coinCup == b) coin.position = Under(cb);
            yield return null;
        }
        wa.SetRest(pb); wb.SetRest(pa);
        ca.localPosition = pb; cb.localPosition = pa;
        // The bowls keep their slots in the list by place, left to right.
        (cups[a], cups[b]) = (cups[b], cups[a]);
        if (coinCup == a) coinCup = b; else if (coinCup == b) coinCup = a;
        if (coin != null && coinCup >= 0 && coin.gameObject.activeSelf) coin.position = Under(cups[coinCup]);
        ScriptedEventRoom.Play(cupKnock, ca.position, .25f);
    }

    // ── A fight ──────────────────────────────────────────────────────

    // Once he has cheated, the bowls can be used to call him out (WagerCup): the same as a blow, without reaching him.
    public bool CanCall => cheated && phase == Phase.Idle && dealer != null && dealer.IsAlive;
    public void CallOut(Player player) { if (CanCall) Confront(player); }

    void OnHit(DamageInfo info)
    {
        if (phase is Phase.Fight or Phase.Over || info.Source is not Player player) return;
        Confront(player);
    }

    void Confront(Player player)
    {
        StopAllCoroutines();
        phase = Phase.Fight;
        bool called = cheated;
        ScriptedEventRoom.Say(called ? calledLine : soreLine);
        Sit(false);
        if (dealer.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent))
        {
            // Off his chair and onto the floor beside the table (never onto the table top, which is walkable too).
            var at = FloorBesideTable();
            dealer.transform.position = at;
            agent.enabled = true; agent.Warp(at);
        }
        ScriptedEventRoom.Free(brain);
        brain.Provoke(player.transform.position);
        StartCoroutine(Friends(player));
        if (!called) potGold = 0;
    }

    // The table top is low enough to be in the floor's NavMesh: cut it out, so in a fight he and his friends go round it,
    // never over it.
    void KeepOffTable()
    {
        // The table's own box (WagerTable's collider stands over the whole table, floor to top).
        var table = GetComponentInChildren<WagerTable>();
        if (table == null || !table.TryGetComponent<BoxCollider>(out var box) || table.GetComponent<UnityEngine.AI.NavMeshObstacle>() != null) return;
        var obstacle = table.gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
        obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
        obstacle.center = box.center;
        obstacle.size = box.size;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
    }

    // The floor at the end of the table on his side: beside his chair, left or right, at the floor's height.
    Vector3 FloorBesideTable()
    {
        var from = seat != null ? seat : transform;
        float floorY = room.Ready ? room.Dungeon.RoomCentre(room.Room).y : from.position.y;
        foreach (float side in new[] { 1.5f, -1.5f, 2.2f, -2.2f })
            foreach (float back in new[] { .2f, .7f, -.3f })
            {
                var p = from.position + from.right * side - from.forward * back;
                if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, .8f, UnityEngine.AI.NavMesh.AllAreas) && Mathf.Abs(hit.position.y - floorY) < .25f) return hit.position;
            }
        return from.position;
    }

    // Out of the walls: puffs at the room's wall-side floor, away from the player.
    IEnumerator Friends(Player player)
    {
        var rng = new System.Random(room.Dungeon.EventSeed(room.Room, 40));
        var points = room.FreeFloor(rng, 1.5f);
        var from = player.transform.position;
        points.Sort((a, b) => (b - from).sqrMagnitude.CompareTo((a - from).sqrMagnitude));
        yield return new WaitForSeconds(.6f);
        for (int i = 0; i < friends && points.Count > 0; i++)
        {
            var e = room.Summon(friend, points[i % points.Count], rng);
            if (e != null) friendsUp.Add(e);
            yield return new WaitForSeconds(.35f);
        }
    }

    void OnDealerDied() => Sit(false);

    void WatchFight()
    {
        if (dealer != null && dealer.IsAlive) return;
        friendsUp.RemoveAll(c => c == null || !c.IsAlive);
        if (friendsUp.Count > 0) return;
        phase = Phase.Over;
        if (potGold <= 0) return;
        ScriptedEventRoom.Say(potLine);
        room.GoldAt(potGold + lost, coinRest != null ? coinRest.position : transform.position);
    }
}
