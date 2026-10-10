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
        StopAllCoroutines();
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
        if (finaleRoom >= 0) StartCoroutine(WatchFinaleRoom());
        foreach (var step in steps)
        {
            if (step == null || Met(step)) continue;
            while (step.room >= 0 && PlayerRoom() != step.room)
            {
                if (!PracticeRunning || !table.IsAlive) yield break;
                Track(ref last);
                yield return null;
            }
            if (Met(step)) continue;
            yield return SayAll(new[] { step.line });
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
            if (Met(step) && step.doneLine != null) yield return SayAll(new[] { step.doneLine });
        }
        yield return Finale();
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
            if (PlayerRoom() == finaleRoom) { fromStairs = true; if (teaching != null) StopCoroutine(teaching); StartCoroutine(Finale()); yield break; }
            yield return null;
        }
    }

    IEnumerator Finale()
    {
        finale = true; endedByFinale = true;
        yield return SayAll(new[] { enough });
        yield return new WaitForSeconds(.6f);
        yield return SayAll(new[] { killerComing });
        yield return EndPractice();
    }

    // Called by TableLevelLoader at the last stairs. The practice board cannot be won: reaching the
    // stairs early only brings the end sooner.
    public bool StairsReached()
    {
        if (!running || !PracticeRunning) return false;
        if (!finale) { fromStairs = true; StopAllCoroutines(); StartCoroutine(Finale()); }
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
        SpawnKiller();
        yield return SayAll(new[] { killerLine });
        float until = Time.time + killerSeconds;
        while (PracticeRunning && table.IsAlive && Time.time < until) yield return null;
        if (!PracticeRunning || !table.IsAlive) yield break;
        yield return SayAll(new[] { timeUp });
        if (!table.IsAlive || table.Stats == null) yield break;
        // He ends it himself: the figure's memorial says so.
        var info = DamageInfo.Simple(table.Stats.CurrentHealth + 1f);
        info.Magic = true; info.FromEffect = true;
        info.SourceName = "the Dungeon Master";
        table.Stats.TakeDamage(info);
    }

    Character SpawnKiller()
    {
        if (killer == null || loader.Dungeon == null) return null;
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
        StopAllCoroutines();
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
