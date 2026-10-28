#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

// Plays a scripted event (Levels/Scripts/Events) like a player would, for filming and testing it: walks with real
// input (footsteps, head bob, the noise a sleeper hears), turns to look, uses things, fights (CombatTestBot's fight).
// Started from an eval in Play mode once the event's run is up (the dev console's event <name>), usually between
// ClipRecorder.Begin and End: EventFilm.Run("duel"); poll EventFilm.Done. Takes: duel, duelfoul, bell, wager, giant,
// giantquiet, dice, cheats. EventFilm.Log says what happened.
public class EventFilm : MonoBehaviour
{
    public static bool Done { get; private set; } = true;
    public static string Log => log.ToString();
    static readonly System.Text.StringBuilder log = new();

    public static void Run(string take)
    {
        foreach (var old in FindObjectsByType<EventFilm>()) Destroy(old.gameObject);
        log.Clear();
        Done = false;
        var go = new GameObject("Event film");
        go.AddComponent<EventFilm>().StartCoroutine(go.GetComponent<EventFilm>().Play(take));
    }

    static void Note(string s) { log.AppendLine($"{Time.time:0.0} {s}"); }

    Player P => PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
    DungeonGenerator Floor => TableManager.Loader != null ? TableManager.Loader.Dungeon : null;

    IEnumerator Play(string take)
    {
        Note("take " + take);
        IEnumerator body = take switch
        {
            "duel" => Duel(false),
            "duelfoul" => Duel(true),
            "bell" => Bell(),
            "wager" => Wager(),
            "giant" => Giant(false),
            "giantquiet" => Giant(true),
            "dice" => Dice(),
            "cheats" => Cheats(),
            _ => null,
        };
        if (body != null) yield return body; else Note("no such take");
        SetMove(Vector2.zero); Sprint(false);
        Crouch(false);
        Note("done");
        Done = true;
        Destroy(gameObject);
    }

    // ── Moving and looking like a player ─────────────────────────────

    static readonly PropertyInfo MoveProperty = typeof(InputManager).GetProperty("Move");
    static readonly PropertyInfo SprintProperty = typeof(InputManager).GetProperty("SprintHeld");
    static void SetMove(Vector2 v) { if (InputManager.HasInstance) MoveProperty.SetValue(InputManager.Instance, v); }
    static void Sprint(bool on) { if (InputManager.HasInstance) SprintProperty.SetValue(InputManager.Instance, on); }
    void OnDestroy() { SetMove(Vector2.zero); Sprint(false); Crouch(false); }

    // Crouch held (or, with crouch set to toggle, pressed until it matches); the motor stands up only where it can.
    static readonly PropertyInfo CrouchProperty = typeof(InputManager).GetProperty("CrouchHeld");
    static readonly FieldInfo CrouchToggle = typeof(PlayerMotor).GetField("crouchToggleRequested", BindingFlags.NonPublic | BindingFlags.Instance);
    bool crouchWanted;
    void Crouch(bool on)
    {
        crouchWanted = on;
        if (InputManager.HasInstance) CrouchProperty.SetValue(InputManager.Instance, on);
    }
    void Update()
    {
        var p = P;
        if (p == null || p.Motor == null || p.settings == null || p.settings.holdToCrouch) return;
        if (p.Motor.IsCrouching != crouchWanted) CrouchToggle?.SetValue(p.Motor, true);
    }

    float Yaw => P.Look.YawTransform.eulerAngles.y;
    float Pitch { get { float x = P.Look.PitchTransform.localEulerAngles.x; if (x > 180f) x -= 360f; return -x; } }

    void Aim(Vector3 target, out float yaw, out float pitch)
    {
        var to = target - P.Look.PitchTransform.position;
        yaw = Quaternion.LookRotation(new Vector3(to.x, 0f, to.z).sqrMagnitude > .0001f ? new Vector3(to.x, 0f, to.z) : P.transform.forward).eulerAngles.y;
        pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
    }

    // Turns the view onto a point over seconds, eased.
    IEnumerator Look(Vector3 target, float seconds = .8f)
    {
        float y0 = Yaw, p0 = Pitch;
        Aim(target, out float y1, out float p1);
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0, 1, t / seconds);
            P.Look.SetYaw(Mathf.LerpAngle(y0, y1, u)); P.Look.SetPitch(Mathf.Lerp(p0, p1, u));
            yield return null;
        }
        P.Look.SetYaw(y1); P.Look.SetPitch(p1);
    }

    // Walks (or runs) to within stop metres of a point, steering the view toward it.
    IEnumerator WalkTo(Vector3 target, float stop, bool run = false, float maxSeconds = 12f, float lookHeight = 1.3f)
    {
        Sprint(run);
        for (float t = 0; t < maxSeconds; t += Time.deltaTime)
        {
            var flat = target - P.transform.position; flat.y = 0f;
            if (flat.magnitude <= stop) break;
            Aim(target + Vector3.up * lookHeight, out float y, out float p);
            P.Look.SetYaw(Mathf.MoveTowardsAngle(Yaw, y, 140f * Time.deltaTime));
            P.Look.SetPitch(Mathf.MoveTowards(Pitch, Mathf.Clamp(p, -25f, 15f), 60f * Time.deltaTime));
            float facing = Mathf.Abs(Mathf.DeltaAngle(Yaw, y));
            SetMove(new Vector2(0f, facing < 60f ? 1f : .3f));
            yield return null;
        }
        SetMove(Vector2.zero); Sprint(false);
    }

    IEnumerator Wait(float s) { yield return new WaitForSeconds(s); }

    IEnumerator Fight(float seconds, float range, System.Func<bool> over)
    {
        CombatTestBot.Run($"fight {seconds} {range} .8");
        yield return null;
        float retry = 0f;
        for (float t = 0; t < seconds + 1f; t += Time.deltaTime)
        {
            if (over != null && over()) break;
            // The bot stops when no enemy is in reach; with no end to wait for that is the end, otherwise it fights
            // the next to come (a second wave).
            if (!CombatTestBot.Running && t >= retry) { if (over == null) break; retry = t + .5f; CombatTestBot.Run($"fight {seconds - t} {range} .8"); }
            yield return null;
        }
        if (CombatTestBot.Running) CombatTestBot.Run("wait 0");
    }

    // A spell thrown at a point as the player's own (from just under the eye, a little left, where the hand would be),
    // with no tome in hand: the spell from the item catalog's tome.
    void Throw(string spellName, Vector3 at)
    {
        SpellDefinition spell = null;
        if (InventoryManager.HasInstance)
            foreach (var i in InventoryManager.Instance.itemCatalog) if (i != null && i.spell != null && i.spell.name == spellName) { spell = i.spell; break; }
        if (spell == null || spell.projectile == null) { Note("no " + spellName); return; }
        var eye = P.Look.PitchTransform;
        var from = eye.position - eye.right * .25f - eye.up * .2f + eye.forward * .4f;
        var ball = Instantiate(spell.projectile, from, Quaternion.LookRotation(at - from));
        ball.Launch(P, spell, spell.power, null);
    }

    IEnumerator Attack() { CombatTestBot.Run("attack; wait .7"); yield return new WaitForSeconds(.8f); }

    static T Find<T>() where T : Component => TableManager.Loader != null && TableManager.Loader.Dungeon != null ? TableManager.Loader.Dungeon.GetComponentInChildren<T>() : null;

    // ── The takes ────────────────────────────────────────────────────

    // Up the bridge, the salute and the call, then the duel; foul: rush in and strike while he salutes.
    IEnumerator Duel(bool foul)
    {
        var duel = Find<DuelEvent>();
        if (duel == null) { Note("no duel"); yield break; }
        for (float t = 0; duel.Duellist == null && t < 5f; t += Time.deltaTime) yield return null;
        var him = duel.Duellist;
        yield return Look(him.transform.position + Vector3.up * 1.4f, 1.2f);
        yield return Wait(1f);
        if (foul)
        {
            // A fireball from the way in, before he has even saluted: a spell is no way to fight a duel. (The warrior's
            // mana is short of a fireball's: more for the take.)
            Throw("Fireball", him.transform.position + Vector3.up * 1.2f);
            for (float t = 0; !duel.Broken && t < 4f; t += Time.deltaTime) yield return null;
            Note("fireball, broken " + duel.Broken);
            yield return Wait(2.5f);
            // Turn to the friends coming in behind.
            var room = duel.GetComponent<ScriptedEventRoom>();
            foreach (var c in room.EnemiesInside()) if (c != him) { yield return Look(c.transform.position + Vector3.up * 1.2f, 1f); break; }
            yield return Wait(1.5f);
            yield return Fight(70f, 12f, () => room.EnemiesInside().Count == 0);
            Note("broken " + duel.Broken + " over " + duel.Over);
            yield break;
        }
        yield return WalkTo(him.transform.position, 4.2f, false, 8f);
        yield return Look(him.transform.position + Vector3.up * 1.4f, .5f);
        yield return Wait(3f);
        yield return Fight(45f, 10f, () => duel.Over);
        Note("over " + duel.Over + " broken " + duel.Broken);
        yield return Wait(1.5f);
        yield return Look(him.transform.position, 1f);
        yield return Wait(3f);
    }

    // Look up at the bell, walk to its rope and pull; the gates and the wave; fight; the better chest, opened.
    IEnumerator Bell()
    {
        var bell = Find<BellEvent>();
        if (bell == null) { Note("no bell"); yield break; }
        var rope = bell.GetComponentInChildren<BellRope>();
        yield return Look(bell.bell.position, 1.4f);
        yield return Wait(1.2f);
        yield return WalkTo(rope.transform.position, 1.1f, false, 8f, -2.2f);
        yield return Look(rope.transform.position + Vector3.down * 2.2f, .5f);
        rope.Interact(P);
        Note("pulled");
        yield return Look(bell.bell.position, .6f);
        yield return Wait(1.6f);
        var room = bell.GetComponent<ScriptedEventRoom>();
        foreach (var c in room.EnemiesInside()) { yield return Look(c.transform.position + Vector3.up, .8f); break; }
        yield return Fight(50f, 14f, () => !bell.Busy);
        for (float t = 0; bell.Busy && t < 10f; t += Time.deltaTime) yield return null;
        Note("rings " + bell.Rings + " chest " + (bell.chest ? bell.chest.name : "-"));
        yield return Wait(.6f);
        if (bell.chest != null)
        {
            yield return WalkTo(bell.chest.transform.position, 1.8f, false, 8f, -.8f);
            yield return Look(bell.chest.transform.position + Vector3.up * .4f, .6f);
            yield return Wait(1f);
            bell.chest.Interact(P);
            yield return Wait(3f);
        }
    }

    // Up to the table; three games: a win, a win, then he cheats; called out with a blow, his friends, and the pot.
    IEnumerator Wager()
    {
        var w = Find<WagerEvent>();
        if (w == null) { Note("no wager"); yield break; }
        if (P.Wallet != null && P.Wallet.Gold < 60) P.Wallet.Add(60 - P.Wallet.Gold);
        var front = w.coinRest.position + w.seat.forward * .95f;
        yield return WalkTo(front, .3f, false, 8f, -.6f);
        yield return Look(w.coinRest.position + Vector3.up * .1f, .8f);
        yield return Wait(1.5f);
        var coinField = typeof(WagerEvent).GetField("coinCup", BindingFlags.NonPublic | BindingFlags.Instance);
        for (int game = 0; game < 3; game++)
        {
            w.GetComponentInChildren<WagerTable>().Interact(P);
            Note("bet " + (game + 1));
            for (float t = 0; !w.Picking && t < 15f; t += Time.deltaTime) yield return null;
            int coin = (int)coinField.GetValue(w);
            int pick = coin >= 0 ? coin : 2;
            yield return Look(w.cups[pick].position, .5f);
            yield return Wait(.6f);
            w.cups[pick].GetComponent<WagerCup>().Interact(P);
            Note($"picked {pick} coin {coin} cheating {w.Cheating}");
            yield return Wait(3.2f);
            yield return Look(w.coinRest.position + Vector3.up * .1f, .4f);
            for (float t = 0; !w.CanBet && t < 6f; t += Time.deltaTime) yield return null;
        }
        // He cheated: call him out at the bowls.
        yield return Look(w.cups[1].position, .5f);
        yield return Wait(.8f);
        if (w.CanCall) w.cups[1].GetComponent<WagerCup>().Interact(P);
        Note("called him out");
        var dealer = w.GetComponent<ScriptedEventRoom>().EnemiesInside();
        if (dealer.Count > 0) yield return Look(dealer[0].transform.position + Vector3.up * 1.1f, .6f);
        yield return Wait(1.5f);
        var room = w.GetComponent<ScriptedEventRoom>();
        yield return Fight(50f, 12f, () => room.EnemiesInside().Count == 0);
        Note("left " + room.EnemiesInside().Count + " gold " + (P.Wallet != null ? P.Wallet.Gold : -1));
        yield return Look(w.coinRest.position, .8f);
        yield return Wait(3f);
    }

    // Creep in crouched, take gold beside it, then stand and walk: it wakes; the fight.
    IEnumerator Giant(bool quiet)
    {
        var g = Find<SleepingGiantEvent>();
        if (g == null) { Note("no giant"); yield break; }
        System.Action<Vector3, float, Character> heard = (at, r, by) => Note($"noise {r:0.0} at {Vector3.Distance(at, g.bed.position):0.0} m from the bed, crouching {P.Motor.IsCrouching}");
        NoiseEvents.Noise += heard;
        try { yield return quiet ? GiantQuiet(g) : GiantTake(g); }
        finally { NoiseEvents.Noise -= heard; }
    }

    IEnumerator GiantTake(SleepingGiantEvent g)
    {
        var room = g.GetComponent<ScriptedEventRoom>();
        yield return Look(g.bed.position + Vector3.up * .5f, 1.2f);
        yield return Wait(2f);
        Crouch(true);
        yield return Wait(.4f);
        // The nearest two hoard spots on this side, one after the other.
        var spots = new System.Collections.Generic.List<Transform>(g.hoard);
        spots.Sort((a, b) => (a.position - P.transform.position).sqrMagnitude.CompareTo((b.position - P.transform.position).sqrMagnitude));
        for (int i = 0; i < 2 && i < spots.Count; i++)
        {
            yield return WalkTo(spots[i].position, .4f, false, 12f, -.4f);
            yield return Wait(.6f);
        }
        yield return Look(g.bed.position + Vector3.up * .5f, .8f);
        yield return Wait(1f);
        Note("crouched by the hoard, asleep " + Asleep(room));
        Crouch(false);
        for (float t = 0; P.Motor.IsCrouching && t < 3f; t += Time.deltaTime) yield return null;
        yield return Wait(.4f);
        // Upright now, a few steps past it: a walking step this close is heard.
        var past = g.bed.position + Vector3.Cross(Vector3.up, (g.bed.position - P.transform.position).normalized) * 2.2f;
        yield return WalkTo(past, .4f, false, 4f, .6f);
        for (float t = 0; Asleep(room) && t < 3f; t += Time.deltaTime) yield return null;
        Note("walking, asleep " + Asleep(room));
        yield return Look(g.bed.position + Vector3.up * 2f, .6f);
        yield return Wait(2.2f);
        var at = g.bed.position;
        foreach (var c in room.EnemiesInside()) at = c.transform.position;
        yield return Fight(50f, 10f, () => room.EnemiesInside().Count == 0);
        Note("left " + room.EnemiesInside().Count);
        yield return Wait(1.2f);
        yield return Look(at, 1f);
        yield return Wait(2.5f);
    }

    // Crouched all the way: two pieces of the hoard and back out the way in, and it never stirs.
    IEnumerator GiantQuiet(SleepingGiantEvent g)
    {
        var room = g.GetComponent<ScriptedEventRoom>();
        var start = P.transform.position;
        yield return Look(g.bed.position + Vector3.up * .5f, 1.2f);
        yield return Wait(2f);
        Crouch(true);
        yield return Wait(.4f);
        var spots = new System.Collections.Generic.List<Transform>(g.hoard);
        spots.Sort((a, b) => (a.position - P.transform.position).sqrMagnitude.CompareTo((b.position - P.transform.position).sqrMagnitude));
        for (int i = 0; i < 2 && i < spots.Count; i++)
        {
            yield return WalkTo(spots[i].position, .4f, false, 12f, -.4f);
            yield return Wait(.6f);
        }
        yield return Look(g.bed.position + Vector3.up * .5f, .8f);
        yield return Wait(1f);
        Note("crouched by the hoard, asleep " + Asleep(room));
        // Out the way in and a little past it, still crouched, then up.
        // (A run opened in the room starts in it: out is through its nearest doorway, a few metres on.)
        Vector3 door = start, outward = Flat(start - g.bed.position); float nearest = float.MaxValue;
        foreach (var (edge, dir) in room.Dungeon.WaysOut(room.Room))
        {
            float d = (edge - P.transform.position).sqrMagnitude;
            if (d < nearest) { nearest = d; door = edge; outward = dir; }
        }
        yield return WalkTo(door - outward * 1.2f, .5f, false, 16f, .4f);
        // A closed door in the way: open it as a player would.
        foreach (var d in room.Dungeon.GetComponentsInChildren<DungeonDoor>())
            if (!d.IsOpen && Vector3.Distance(d.transform.position, door) < 2.5f) { yield return Look(d.transform.position + Vector3.up * 1.2f, .5f); d.Interact(P); yield return Wait(1.2f); break; }
        yield return WalkTo(door + outward * 3.5f, .5f, false, 8f, .4f);
        Note("outside " + !room.PlayerInside + ", asleep " + Asleep(room));
        Crouch(false);
        yield return Look(door + Vector3.up * 1.2f, 1f);
        yield return Wait(3.5f);
    }

    // Floor about this far from the player in the direction with the most open floor round it (not in a pillar), where
    // the Dungeon Master can reach.
    Vector3 OpenFloor(DungeonGenerator floor, float distance)
    {
        var from = P.transform.position;
        Vector3 best = from + P.transform.forward * distance; float bestRoom = -1f;
        for (int i = 0; i < 16; i++)
        {
            var dir = Quaternion.Euler(0f, Yaw + i * 22.5f, 0f) * Vector3.forward;
            if (UnityEngine.AI.NavMesh.Raycast(from, from + dir * (distance + 1.5f), out _, UnityEngine.AI.NavMesh.AllAreas)) continue;
            if (!UnityEngine.AI.NavMesh.SamplePosition(from + dir * distance, out var hit, .5f, UnityEngine.AI.NavMesh.AllAreas)) continue;
            // Where his hand reaches, or he can't take it away again.
            if (!DungeonMasterReach.CanReach(hit.position)) continue;
            UnityEngine.AI.NavMesh.FindClosestEdge(hit.position, out var edge, UnityEngine.AI.NavMesh.AllAreas);
            // Facing the way it already looks counts for a little.
            float score = edge.distance - i % 8 * .05f;
            if (score > bestRoom) { bestRoom = score; best = hit.position; }
        }
        return best;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > .0001f ? v.normalized : Vector3.forward; }

    static bool Asleep(ScriptedEventRoom room)
    {
        foreach (var c in room.EnemiesInside()) if (c.TryGetComponent<EnemyBrain>(out var b) && b.Asleep) return true;
        return false;
    }

    // Into the room, looking up through the opening; the die comes down and is followed to the floor; what it brings.
    IEnumerator Dice()
    {
        var d = Find<DiceEvent>();
        if (d == null) { Note("no dice"); yield break; }
        var room = d.GetComponent<ScriptedEventRoom>();
        var sky = d.landing.position + Vector3.up * 7f;
        yield return Look(d.landing.position + Vector3.up * 1.5f, 1f);
        yield return Wait(.8f);
        // A few steps in sets it off.
        yield return WalkTo(d.landing.position, 3.2f, false, 4f, 1f);
        yield return Look(sky, 1f);
        for (float t = 0; d.Result == 0 && t < 5f; t += Time.deltaTime) yield return null;
        // Follow the die down once it is in the room's air.
        GameObject die = null;
        for (float t = 0; t < 14f; t += Time.deltaTime)
        {
            if (die == null) die = GameObject.Find("DM die(Clone)");
            // (Until he lets go it waits far below the floor: only a die in the room's air counts.)
            if (die != null && room.Inside(die.transform.position) && die.transform.position.y < sky.y + 6f && die.transform.position.y > P.transform.position.y - 1f)
            {
                Aim(die.transform.position, out float y, out float p);
                P.Look.SetYaw(Mathf.MoveTowardsAngle(Yaw, y, 260f * Time.deltaTime));
                P.Look.SetPitch(Mathf.MoveTowards(Pitch, p, 160f * Time.deltaTime));
                if (die.transform.position.y < P.transform.position.y + .6f && t > 1.5f) break;
            }
            yield return null;
        }
        Note("result " + d.Result);
        if (die != null) yield return Look(die.transform.position, .4f);
        yield return Wait(2.5f);
        if (d.Result == 5 || d.Result == 6 || d.Result == 2)
        {
            // What he brings in: watch the air over the die, where it comes down, then follow it.
            yield return Look(die != null ? die.transform.position + Vector3.up * 2.6f : sky, .8f);
            for (float t = 0; DungeonMasterReach.Busy && t < 25f; t += Time.deltaTime)
            {
                // Only once it is over the room: carried in, it passes behind the walls first.
                var brought = Brought(room, die);
                if (brought != null && room.Inside(brought.position) && brought.position.y < sky.y + 6f && brought.position.y > P.transform.position.y - 1f)
                {
                    Aim(brought.position, out float y, out float p);
                    P.Look.SetYaw(Mathf.MoveTowardsAngle(Yaw, y, 260f * Time.deltaTime));
                    P.Look.SetPitch(Mathf.MoveTowards(Pitch, p, 160f * Time.deltaTime));
                }
                yield return null;
            }
        }
        // The ambush shuts the gates and calls its first wave a moment after the die lands.
        if (d.Result == 1) for (float t = 0; room.EnemiesInside().Count == 0 && t < 6f; t += Time.deltaTime) yield return null;
        // (Clear for a few seconds: the ambush's second wave comes a moment after the first falls.)
        float clearSince = -1f;
        bool Cleared()
        {
            if (room.EnemiesInside().Count > 0 || room.GatesShut) { clearSince = -1f; return false; }
            if (clearSince < 0f) clearSince = Time.time;
            return Time.time - clearSince > 3f;
        }
        if (d.Result == 1 || d.Result == 2) yield return Fight(80f, 12f, Cleared);
        else
        {
            var brought = Brought(room, die);
            if (brought != null) yield return Look(brought.position + Vector3.up * .4f, .8f);
            if (brought != null && brought.TryGetComponent<DungeonContainer>(out var chest)) { yield return Wait(1f); chest.Interact(P); }
        }
        yield return Wait(3f);
    }

    // What the dice brought: the closed chest nearest the die that isn't the room's or another room's own (it is in his
    // hand outside the room while he brings it), else an enemy in the room or in his hand.
    static Transform Brought(ScriptedEventRoom room, GameObject die)
    {
        Transform best = null; float bestDistance = float.MaxValue;
        var near = die != null ? die.transform.position : room.transform.position;
        foreach (var c in room.Dungeon.GetComponentsInChildren<DungeonContainer>())
        {
            if (!c.chest || c.IsOpen || c.GetComponentInParent<ScriptedEventRoom>() != null) continue;
            int at = room.Dungeon.RoomOf(c.transform.position);
            if (at >= 0 && at != room.Room) continue;
            float d = (c.transform.position - near).sqrMagnitude;
            if (d < bestDistance) { best = c.transform; bestDistance = d; }
        }
        if (best != null) return best;
        foreach (var e in room.EnemiesInside()) return e.transform;
        foreach (var b in room.Dungeon.GetComponentsInChildren<EnemyBrain>()) if (!b.enabled) return b.transform;   // in his hand
        return null;
    }

    // In a room he can reach: one more enemy after a quick fight, then a chest taken away.
    IEnumerator Cheats()
    {
        var floor = Floor;
        var m = floor != null ? floor.GetComponent<DungeonMasterMeddling>() : null;
        if (m == null) { Note("no meddling"); yield break; }
        yield return Wait(1f);
        var said = m.Now("add");
        Note(said);
        if (!said.StartsWith("He adds")) yield break;
        yield return Look(DungeonMasterReach.StandFor(P.transform.position + P.transform.forward * 4f) + Vector3.up * 28f, 1.5f);
        var floorRoom = floor.RoomOf(P.transform.position);
        Character added = null;
        for (float t = 0; added == null && t < 40f; t += Time.deltaTime)
        {
            foreach (var b in floor.GetComponentsInChildren<EnemyBrain>())
                if (b.enabled && b.Character.IsAlive && floor.RoomOf(b.transform.position) == floorRoom) { added = b.Character; break; }
            yield return null;
        }
        if (added != null) yield return Look(added.transform.position + Vector3.up * 1.1f, .6f);
        // The room cleared, he is home again: a chest in front of the player, and he takes it.
        bool Clear() { foreach (var b in floor.GetComponentsInChildren<EnemyBrain>()) if (b.Character.IsAlive && floor.RoomOf(b.transform.position) == floorRoom) return false; return true; }
        yield return Fight(40f, 10f, Clear);
        for (float t = 0; DungeonMasterReach.Busy && t < 40f; t += Time.deltaTime) yield return null;
        yield return Wait(1f);
        // A chest set down on open floor in front of the player (turned toward the room's open middle first).
        // (He takes the nearest closed chest: any other one close by goes first, so it is this one, in view.)
        foreach (var c in floor.GetComponentsInChildren<DungeonContainer>())
            if (c.chest && !c.IsOpen && Vector3.Distance(c.transform.position, P.transform.position) < 9f) Destroy(c.gameObject);
        var open = OpenFloor(floor, 3.2f);
        yield return Look(open + Vector3.up * .3f, .8f);
        var set = floor.SpawnChest(0, open, Quaternion.LookRotation(Flat(P.transform.position - open)), 7);
        var stolen = set != null ? set.transform : null;
        yield return Wait(1.4f);
        Note(m.Now("steal"));
        // Watch the chest go up and away, then where it stood.
        var stood = stolen != null ? stolen.position : P.transform.position + P.transform.forward * 3f;
        for (float t = 0; DungeonMasterReach.Busy && t < 30f; t += Time.deltaTime)
        {
            var at = stolen != null ? stolen.position + Vector3.up * .3f : stood + Vector3.up * 3f;
            Aim(at, out float y, out float p);
            P.Look.SetYaw(Mathf.MoveTowardsAngle(Yaw, y, 200f * Time.deltaTime));
            P.Look.SetPitch(Mathf.MoveTowards(Pitch, Mathf.Min(p, 55f), 120f * Time.deltaTime));
            yield return null;
        }
        yield return Look(stood, .6f);
        yield return Wait(2.5f);
    }
}
#endif
