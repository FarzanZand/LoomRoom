using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

// The practice board (IntroDungeon): the lessons as you go (TutorialStep), the finale and its killer, the
// practice death, and the counters the lessons read. Part of IntroController.
public partial class IntroController
{
    // ── The practice board ────────────────────────────────────────────

    void OnRunStarted()
    {
        if (!running || !PracticeRunning) return;
        StopAllCoroutines(); PopDialogue();   // a stopped scene gives its Dialogue back (the next one takes it again)
        teaching = StartCoroutine(Teach());
    }

    IEnumerator Teach()
    {
        // On the board the lamp lights only the room around it; the board has its own light (its colour is the level's).
        if (lamp != null) FirstPersonLighting.SetLayers(lamp, LightingManager.RoomLayer);
        if (sheet != null) sheet.Hide();
        walked = guarded = 0f; hits = kills = pickups = 0; finale = false; fromStairs = false;
        stillFor = 0f; saidIdle = saidHurt = endedByFinale = false;
        Listen();
        while (loader.Busy) yield return null;
        yield return new WaitForSeconds(firstStepDelay);
        Vector3 last = table.transform.position;
        if (finaleRoom >= 0 && RunsFinale) StartCoroutine(WatchFinaleRoom());
        StartCoroutine(WatchRoomQuests());
        foreach (var step in steps)
        {
            if (step == null) continue;
            // Done before it was given: skipped, but a follow-up with no line of its own still gets its remark.
            if (Met(step)) { if (string.IsNullOrEmpty(step.line?.text)) yield return SayDone(step); continue; }
            while (step.room >= 0 && PlayerRoom() != step.room)
            {
                if (!PracticeRunning || !table.IsAlive) yield break;
                Track(ref last);
                yield return null;
            }
            if (Met(step)) continue;
            // A key named in the line ("{Crouch}") is said as the player's own binding.
            yield return SayAll(new[] { step.line != null && step.line.text != null && step.line.text.Contains("{") ? step.line.With(Hint(step.line.text)) : step.line });
            var hint = Hint(step.hint);
            bool hinted = string.IsNullOrEmpty(hint);
            float started = Time.time;
            while (!Met(step) && (step.giveUpAfter <= 0f || Time.time - started < step.giveUpAfter))
            {
                if (!PracticeRunning || !table.IsAlive) yield break;
                Track(ref last);
                // The controls only if the player has not worked it out.
                if (!hinted && Time.time - started >= step.hintDelay) { hinted = true; MessageLog.Post(hint, MessageKind.Info); }
                if (step.goal == TutorialGoal.Wait && Time.time - started >= step.amount) break;
                yield return null;
            }
            if (Met(step)) yield return SayDone(step);
        }
        if (!RunsFinale) yield break;
        // With a finale room the killer comes only when the player walks into it (WatchFinaleRoom), never on a timer
        // behind them; a level without one ends here.
        if (finaleRoom < 0) yield return Finale();
    }

    // Each room's quest in the log: given when the player walks in, done when the room is dealt with (its enemies all
    // fallen, or for a room without enemies its chest opened) or when the player leaves it, whichever comes first, and
    // all of them when the practice ends.
    IEnumerator WatchRoomQuests()
    {
        if (roomQuests == null || roomQuests.Length == 0 || !ProgressionManager.HasInstance) yield break;
        var meta = ProgressionManager.Instance;
        foreach (var q in roomQuests)
            if (q != null && !string.IsNullOrEmpty(q.id) && meta.FindQuest(q.id) == null)
                meta.quests.Add(new ProgressionManager.Quest { id = q.id, text = q.text });
        roomEnemies.Clear();
        // Each quest room's enemies are listed now, before any of them wanders out of its room.
        foreach (var q in roomQuests) if (q != null && q.room >= 0) RoomDealtWith(q.room);
        int reached = -1;
        RoomQuest current = null;
        float nextCheck = 0f;
        while (PracticeRunning && table != null && table.IsAlive)
        {
            int room = PlayerRoom();
            if (room > reached)
            {
                reached = room;
                foreach (var q in roomQuests)
                {
                    if (q == null || string.IsNullOrEmpty(q.id)) continue;
                    if (q.room < reached) meta.CompleteQuest(q.id);
                    else if (q.room == reached) { meta.GiveQuest(q.id); current = q; }
                }
            }
            if (current != null && meta.QuestState(current.id) == 1)
            {
                // Walked on into another room: done (a step back into the corridor mid-fight doesn't count).
                if (room >= 0 && room != current.room) { meta.CompleteQuest(current.id); current = null; }
                else if (Time.time >= nextCheck) { nextCheck = Time.time + .25f; if (RoomDealtWith(current.room)) { meta.CompleteQuest(current.id); current = null; } }
            }
            yield return null;
        }
        foreach (var q in roomQuests) if (q != null && !string.IsNullOrEmpty(q.id)) meta.CompleteQuest(q.id);
    }

    // A room with enemies is dealt with once none of them stands; one without, once its chest is open (a room with
    // neither is never "dealt with": leaving it closes its quest).
    // The enemies first seen in each room: the room is cleared when all of them are dead or gone, wherever they ran.
    readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<EnemyBrain>> roomEnemies = new();
    bool RoomDealtWith(int room)
    {
        var dungeon = loader != null ? loader.Dungeon : null;
        if (dungeon == null || dungeon.Layout == null) return false;
        if (!roomEnemies.TryGetValue(room, out var enemies))
        {
            enemies = new System.Collections.Generic.List<EnemyBrain>();
            foreach (var enemy in dungeon.GetComponentsInChildren<EnemyBrain>(true))
                if (enemy != null && dungeon.Layout.RoomAt(dungeon.CellOf(enemy.transform.position)) == room) enemies.Add(enemy);
            roomEnemies[room] = enemies;
        }
        if (enemies.Count > 0)
        {
            foreach (var enemy in enemies) if (enemy != null && enemy.Character != null && enemy.Character.IsAlive) return false;
            return true;
        }
        foreach (var chest in dungeon.GetComponentsInChildren<DungeonContainer>(true))
            if (chest != null && chest.IsOpen && dungeon.Layout.RoomAt(dungeon.CellOf(chest.transform.position)) == room) return true;
        return false;
    }

    // Every room quest of the practice run is closed when the intro ends: the finale stops the coroutines (the watcher
    // above among them) before it could close them itself.
    void CloseRoomQuests()
    {
        if (roomQuests == null || !ProgressionManager.HasInstance) return;
        foreach (var q in roomQuests) if (q != null && !string.IsNullOrEmpty(q.id)) ProgressionManager.Instance.CompleteQuest(q.id);
    }

    IEnumerator SayDone(TutorialStep step)
    {
        if (step.doneLine != null && !string.IsNullOrEmpty(step.doneLine.text)) yield return SayAll(new[] { step.doneLine });
    }

    bool finale, fromStairs;
    Coroutine teaching;

    // The player's room on the floor plan, -1 in a passage.
    int PlayerRoom()
    {
        var dungeon = loader != null ? loader.Dungeon : null;
        return dungeon != null && dungeon.Layout != null ? dungeon.Layout.RoomAt(dungeon.CellOf(table.transform.position)) : -1;
    }

    // Reaching the finale room ends the lessons wherever they are.
    IEnumerator WatchFinaleRoom()
    {
        while (PracticeRunning && !finale)
        {
            if (PlayerRoom() == finaleRoom || PartyInRoom(finaleRoom)) { fromStairs = true; if (teaching != null) StopCoroutine(teaching); StartCoroutine(Finale()); yield break; }
            yield return null;
        }
    }

    IEnumerator Finale()
    {
        finale = true; endedByFinale = true;
        FinaleStarted();
        // An empty "enough" line is left out: the killer's line alone starts the finale.
        if (enough != null && !enough.IsEmpty)
        {
            yield return SayAll(new[] { enough });
            yield return new WaitForSeconds(.6f);
        }
        yield return SayAll(new[] { killerComing });
        yield return EndPractice();
    }

    // Called by TableLevelLoader at the last stairs. The practice board cannot be won: reaching the
    // stairs early only brings the end sooner.
    public bool StairsReached()
    {
        if (!running || !PracticeRunning) return false;
        if (!finale) { fromStairs = true; StopAllCoroutines(); PopDialogue(); CloseRoomQuests(); StartCoroutine(Finale()); }
        return true;
    }

    float stillFor;
    bool saidIdle, saidHurt, endedByFinale;

    void Track(ref Vector3 last)
    {
        var p = table.transform.position;
        float moved = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(last.x, 0f, last.z));
        walked += moved;
        // Standing about: one remark, once.
        stillFor = moved > .001f ? 0f : stillFor + Time.deltaTime;
        if (!saidIdle && idleLine != null && stillFor > idleSeconds && !DungeonMaster.Instance.Speaking) { saidIdle = true; DungeonMaster.Say(idleLine); }
        last = p;
        if (table.Combat != null && table.Combat.IsGuarding) guarded += Time.deltaTime;
    }

    bool Met(TutorialStep step) => step.goal switch
    {
        TutorialGoal.Walk => walked >= step.amount,
        TutorialGoal.Hit => hits >= step.amount,
        TutorialGoal.Kill => kills >= step.amount,
        TutorialGoal.PickUp => pickups >= step.amount,
        TutorialGoal.Guard => guarded >= step.amount,
        _ => false,
    };

    // Something the player cannot beat comes up behind them. If they keep away from it for long
    // enough, the Dungeon Master ends it anyway.
    IEnumerator EndPractice()
    {
        var hunter = SpawnKiller();
        int landed = 0;
        System.Action<DamageInfo> counted = info => { if (info.Source == table && info.Amount > 0f) landed++; };
        if (hunter != null) hunter.Damaged += counted;
        yield return SayAll(new[] { killerLine });
        float until = Time.time + killerSeconds;
        while (PracticeRunning && table.IsAlive && Time.time < until)
        {
            // Co-op: once someone has fallen the rest follow soon (they would lie dead watching a black screen).
            if (TeammateFell()) until = Mathf.Min(until, Time.time + TeammateFallenEnds);
            // Holding it off too well: the Dungeon Master has had enough, and the killer's volley finds the player wherever they run.
            if (hunter != null && hunter.IsAlive && landed >= killerHitsToVolley && volleySpell != null)
            {
                hunter.Damaged -= counted;
                yield return SayAll(new[] { tooLong });
                yield return new WaitForSeconds(volleyDelay);
                if (!PracticeRunning || !table.IsAlive || hunter == null || !hunter.IsAlive) break;
                yield return KillerVolley(hunter);
                // The volley finishes it; the clock is only a fallback now.
                until = Time.time + 8f;
                landed = int.MinValue;
            }
            yield return null;
        }
        if (hunter != null) hunter.Damaged -= counted;
        if (!PracticeRunning || !table.IsAlive) yield break;
        yield return SayAll(new[] { timeUp });
        if (!table.IsAlive || table.Stats == null) yield break;
        // He ends it himself: the figure's memorial says so.
        var info = DamageInfo.Simple(table.Stats.CurrentHealth + 1f);
        info.Magic = true; info.FromEffect = true;
        info.SourceName = "the Dungeon Master";
        table.Stats.TakeDamage(info);
    }


    // The killer stops, raises its hand and lets the homing volley go.
    IEnumerator KillerVolley(Character hunter)
    {
        var brain = hunter.GetComponent<EnemyBrain>();
        var animator = hunter.GetComponentInChildren<Animator>();
        var hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        float casting = killerVolley.count * killerVolley.spawnInterval + killerVolley.riseSeconds;
        if (brain != null) brain.Stun(casting);
        AnimatorHelper.SetTrigger(animator, "CastSpell");
        var body = hunter.transform;
        FireballVolley.Cast(volleySpell, hunter, () => hand != null ? hand.position + Vector3.up * .3f : body.position + Vector3.up * 2f,
            table, volleyDamage, killerVolley);
        yield return new WaitForSeconds(casting);
    }

    Character SpawnKiller()
    {
        // Co-op: the host's killer comes for everyone (guests get it as one of the host's enemies).
        if (killer == null || loader.Dungeon == null || !RunsFinale) return null;
        // Up the stairs, when the player has come to them: out of the stairwell's mouth, not inside its stone
        // housing (a board's exit is a solid stairwell, and the exit point is its middle).
        if (fromStairs && StairsMouth(out var stairs))
            return Configure(Instantiate(killer, stairs, Quaternion.LookRotation(Vector3.ProjectOnPlane(table.transform.position - stairs, Vector3.up).sqrMagnitude > .001f ? Vector3.ProjectOnPlane(table.transform.position - stairs, Vector3.up) : Vector3.forward), loader.Dungeon.transform));
        var view = table.Look != null ? table.Look.YawTransform.forward : table.transform.forward;
        view.y = 0f; view.Normalize();
        // Behind the player if there is floor there in plain sight, otherwise to a side, otherwise ahead.
        var from = table.transform.position + Vector3.up * .8f;
        Vector3 at = table.transform.position;
        bool found = false;
        foreach (var dir in new[] { -view, Vector3.Cross(Vector3.up, view), -Vector3.Cross(Vector3.up, view), view })
            foreach (var distance in new[] { 5f, 4f, 3f, 2f })
            {
                var probe = table.transform.position + dir * distance;
                if (!NavMesh.SamplePosition(probe, out var hit, .8f, NavMesh.AllAreas)) continue;
                if (Physics.Linecast(from, hit.position + Vector3.up * .8f, out var wall, ~0, QueryTriggerInteraction.Ignore)
                    && wall.collider.GetComponentInParent<Character>() == null) continue;
                at = hit.position; found = true; break;
            }
        if (!found && NavMesh.SamplePosition(table.transform.position, out var near, 2f, NavMesh.AllAreas)) at = near.position;
        return Configure(Instantiate(killer, at, Quaternion.LookRotation(Vector3.ProjectOnPlane(table.transform.position - at, Vector3.up).sqrMagnitude > .001f ? Vector3.ProjectOnPlane(table.transform.position - at, Vector3.up) : Vector3.forward), loader.Dungeon.transform));
    }

    // Floor just outside the exit stairwell's opening (its local -z), clear of the stonework.
    bool StairsMouth(out Vector3 at)
    {
        at = loader.Dungeon.ExitPoint;
        DungeonExit exit = null;
        foreach (var e in loader.Dungeon.GetComponentsInChildren<DungeonExit>(true)) if (!e.entrance) { exit = e; break; }
        var mouth = exit != null ? exit.transform.position - exit.transform.forward * 1.6f : at;
        foreach (var distance in new[] { 0f, .6f, 1.2f })
        {
            var probe = mouth - (exit != null ? exit.transform.forward : Vector3.zero) * distance;
            if (!NavMesh.SamplePosition(probe, out var hit, 1f, NavMesh.AllAreas)) continue;
            // Not inside anything solid.
            if (Physics.CheckCapsule(hit.position + Vector3.up * .5f, hit.position + Vector3.up * 1.4f, .3f, ~0, QueryTriggerInteraction.Ignore)) continue;
            at = hit.position;
            return true;
        }
        return false;
    }

    Character Configure(GameObject go)
    {
        var character = go.GetComponent<Character>();
        if (character != null && character.Stats != null)
        {
            character.Stats.AddModifier(new StatModifier(StatType.MaxHealth, killerHealth - 1f, ModifierType.PercentMultiply, this));
            character.Stats.AddModifier(new StatModifier(StatType.AttackDamage, killerDamage - 1f, ModifierType.PercentMultiply, this));
            character.Stats.Heal(character.Stats.MaxHealth);
        }
        var drop = go.GetComponent<DungeonLootDrop>();
        if (drop != null) drop.enabled = false;
        go.GetComponent<EnemyBrain>()?.Alert();
        return character;
    }

    // Called by RunManager instead of the run recap. True: the practice death is handled here.
    public bool TakeOverDeath()
    {
        if (!running || !PracticeRunning) return false;
        StopAllCoroutines(); PopDialogue();   // a stopped scene gives its Dialogue back (the next one takes it again)
        Unlisten();
        StartCoroutine(AfterPracticeDeath());
        return true;
    }

    IEnumerator AfterPracticeDeath()
    {
        yield return new WaitForSecondsRealtime(.6f);
        bool dark = false, started = false;
        yield return TableManager.ReturnToRoom(loader, () => dark = true, ok => started = ok, this);
        if (!started) yield break; // logged by ReturnToRoom
        while (!dark) yield return null;
        // The board is gone by the time the lamp shows the table again.
        while (loader.Busy) yield return null;
        loader.ClearTable();
        StartCoroutine(BackAtTable());
    }

    // ── Listening ─────────────────────────────────────────────────────

    bool listening;

    void Listen()
    {
        if (listening || table == null) return;
        listening = true;
        table.HitLanded += OnHit;
        table.Damaged += OnHurt;
        Character.AnyDied += OnAnyDied;
        if (InventoryManager.HasInstance) InventoryManager.Instance.ItemPickedUp += OnPickedUp;
    }

    void Unlisten()
    {
        if (!listening) return;
        listening = false;
        if (table != null) { table.HitLanded -= OnHit; table.Damaged -= OnHurt; }
        Character.AnyDied -= OnAnyDied;
        if (InventoryManager.HasInstance) InventoryManager.Instance.ItemPickedUp -= OnPickedUp;
    }

    void OnHit(DamageInfo info) { if (!info.FromEffect) hits++; }
    void OnHurt(DamageInfo info)
    {
        if (saidHurt || finale || hurtLine == null || info.Amount <= 0f || info.Blocked || !table.IsAlive) return;
        saidHurt = true; DungeonMaster.Say(hurtLine);
    }
    void OnAnyDied(Character c) { if (c != null && !(c is Player) && c.GetComponent<EnemyBrain>() != null && c.LastDamage.Source == table) kills++; }
    void OnPickedUp(ItemData item, Player who, int count) { if (who == table) pickups++; }

    // "{PrimaryAction}" becomes the Table map's binding, "Left Button" and the like.
    static string Hint(string text)
    {
        if (string.IsNullOrEmpty(text) || !InputManager.HasInstance || InputManager.Instance.Actions == null) return text;
        var asset = InputManager.Instance.Actions.asset;
        var result = new System.Text.StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf('{', i), close = open < 0 ? -1 : text.IndexOf('}', open);
            if (open < 0 || close < 0) { result.Append(text, i, text.Length - i); break; }
            result.Append(text, i, open - i);
            string name = text.Substring(open + 1, close - open - 1);
            var action = asset.FindAction("Table/" + name);
            result.Append(action != null ? KeyboardBinding(action) : name);
            i = close + 1;
        }
        return result.ToString();
    }

    // The keyboard and mouse binding only: "W/A/S/D", not every device's.
    static string KeyboardBinding(InputAction action)
    {
        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            var b = bindings[i];
            if (b.isPartOfComposite) continue;
            bool keyboard = b.isComposite
                ? i + 1 < bindings.Count && bindings[i + 1].effectivePath.StartsWith("<Keyboard>")
                : b.effectivePath.StartsWith("<Keyboard>") || b.effectivePath.StartsWith("<Mouse>");
            if (!keyboard) continue;
            string text = action.GetBindingDisplayString(i);
            return text == "LMB" ? "left mouse" : text == "RMB" ? "right mouse" : text;
        }
        return action.GetBindingDisplayString();
    }
}
