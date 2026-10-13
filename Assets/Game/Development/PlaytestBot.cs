#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

// Testing from an eval: PlaytestBot.Run("name", minutes) plays the table player like a person, through the same
// inputs (move, look, attack, block, interact, cast): it walks the floor room by room along the NavMesh (opening doors
// on the way), fights what it meets (closes in, swings, blocks an enemy's swing, casts a held spell from range), opens
// chests, picks up loot, and takes the stairs once the floor's rooms are seen (or the floor has taken too long),
// floor after floor. Alone or in co-op. Everything it does, and every error and warning the game logs meanwhile, goes
// to Temp/Playtest/<name>.log; a frame every two seconds to Temp/Playtest/<name>/. PlaytestBot.Stop() ends it.
// With mortal off (the default) it is healed when low and the log says so; on, it can die (co-op: watching).
public class PlaytestBot : MonoBehaviour
{
    public static PlaytestBot Instance { get; private set; }
    public static bool Mortal;
    // A spell tome to carry in the left hand (Items/Data or Combat/Spells path), cast at enemies from range.
    public static string Spell;

    string runName, logPath, frameDir;
    float endsAt, nextFrame, nextPath, nextAttack, floorStartedAt, stuckCheckAt, castUntil;
    readonly StringBuilder log = new();
    Player player;
    NavMeshPath path;
    int corner, lastRoom = -2, lastFloor = -1, stuckCount, kills, hitsTaken, errors, warnings, rooms, pickups, chests, doors, casts;
    Vector3 stuckFrom, goal;
    string doing = "starting", goalName = "";
    readonly HashSet<int> visited = new(), unreachable = new();
    readonly HashSet<Object> given = new();
    readonly Dictionary<Character, bool> watched = new();
    bool blocking, primaryHeld, casting;

    public static void Run(string name, float minutes)
    {
        Stop();
        var go = new GameObject("Playtest bot");
        Instance = go.AddComponent<PlaytestBot>();
        Instance.Begin(name, minutes);
    }

    public static void Stop() { if (Instance != null) Destroy(Instance.gameObject); }

    public static string Summary => Instance != null ? Instance.Status() : "not running";

    void Begin(string name, float minutes)
    {
        runName = name;
        endsAt = Time.realtimeSinceStartup + minutes * 60f;
        System.IO.Directory.CreateDirectory("Temp/Playtest");
        logPath = $"Temp/Playtest/{name}.log";
        frameDir = $"Temp/Playtest/{name}";
        if (System.IO.Directory.Exists(frameDir)) System.IO.Directory.Delete(frameDir, true);
        System.IO.Directory.CreateDirectory(frameDir);
        System.IO.File.WriteAllText(logPath, "");
        path = new NavMeshPath();
        Application.logMessageReceived += OnLog;
        Write($"started for {minutes} min, mortal {Mortal}");
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        Release();
        Write($"stopped: {Status()}");
        Flush();
        if (Instance == this) Instance = null;
    }

    string Status() => $"floor {lastFloor} rooms {rooms} kills {kills} hits taken {hitsTaken} pickups {pickups} chests {chests} doors {doors} casts {casts} stuck {stuckCount} errors {errors} warnings {warnings}; {doing} {goalName}";

    // Every error and warning while it plays, with what it was doing.
    void OnLog(string message, string stack, LogType type)
    {
        if (message.StartsWith("[Playtest]") || message.Contains("api/exec")) return;
        if (type == LogType.Warning) { warnings++; Write($"WARNING ({doing}): {message.Split('\n')[0]}"); }
        else if (type != LogType.Log) { errors++; Write($"{type.ToString().ToUpper()} ({doing}): {message.Split('\n')[0]} | {FirstGameLine(stack)}"); }
    }

    static string FirstGameLine(string stack)
    {
        foreach (var line in stack.Split('\n')) if (line.Contains("Assets/Game")) return line.Trim();
        return stack.Split('\n')[0].Trim();
    }

    void Write(string line) { log.AppendLine($"{System.DateTime.Now:HH:mm:ss.f} {line}"); if (log.Length > 4000) Flush(); }
    void Flush() { if (logPath != null && log.Length > 0) { System.IO.File.AppendAllText(logPath, log.ToString()); log.Clear(); } }

    // ── Each frame ────────────────────────────────────────────────────

    void Update()
    {
        if (Time.realtimeSinceStartup > endsAt) { Write("time is up"); Destroy(gameObject); return; }
        if (Time.realtimeSinceStartup > nextFrame) { nextFrame = Time.realtimeSinceStartup + 2f; StartCoroutine(Frame()); }

        player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        var loader = TableManager.Loader;
        bool playing = player != null && player.IsActive && loader != null && loader.Dungeon != null && !loader.Busy
            && GameManager.HasInstance && GameManager.Instance.GameplayActive;
        if (!playing) { Idle(player == null || !player.IsActive ? "waiting (not on the table)" : loader != null && loader.Busy ? "waiting (floor loading)" : "waiting (menu or cutscene)"); return; }
        if (!player.IsAlive) { Idle("dead"); return; }

        if (loader.FloorNumber != lastFloor)
        {
            lastFloor = loader.FloorNumber; floorStartedAt = Time.time; visited.Clear(); unreachable.Clear(); doorsTried.Clear(); doorGoal = null; lastRoom = -2;
            Write($"FLOOR {lastFloor}: {RoomCount(loader.Dungeon.Layout)} rooms, {EnemyBrain.Active.Count} enemies");
            player.Damaged -= OnHurt; player.Damaged += OnHurt;
        }
        if (Time.realtimeSinceStartup > nextSync) { nextSync = Time.realtimeSinceStartup + 5f; Sync(); }
        if (!string.IsNullOrEmpty(Spell) && !spellGiven) GiveSpell();
        TrackRoom(loader.Dungeon);
        WatchKills();
        if (!Mortal && player.Stats != null && player.Stats.CurrentHealth < player.Stats.MaxHealth * .25f)
        {
            Write($"low health {player.Stats.CurrentHealth:0}/{player.Stats.MaxHealth:0}: healed (mortal is off)");
            player.Stats.Heal(player.Stats.MaxHealth);
        }

        var enemy = Threat();
        if (enemy != null) { Fight(enemy); return; }
        SetBlock(false);
        var thing = Errand(loader.Dungeon);
        if (thing != null) return;
        Explore(loader.Dungeon, loader);
    }

    // The layout's rooms (corridors are the regions after them).
    static int RoomCount(DungeonLayout layout) => layout.RegionCount - layout.CorridorCount;

    float nextSync, nextStairsPress;
    bool spellGiven;

    // What both players should agree on, every five seconds (co-op logs from two machines are compared line by line).
    void Sync()
    {
        int alive = 0; float hp = 0;
        foreach (var e in CoopWorld.Enemies) if (e != null && e.IsAlive) { alive++; hp += e.Stats.CurrentHealth; }
        var wallet = player.GetComponent<Wallet>();
        string party = "";
        if (MultiplayerManager.HasInstance) foreach (var p in MultiplayerManager.Instance.Players) if (p != null && !p.IsOwner) party += $" {p.DisplayName}:{(p.Current.alive ? "alive" : "dead")},{p.Current.place},hp{p.Current.health}";
        Write($"SYNC floor {lastFloor} enemies alive {alive} hp {hp:0} | me hp {player.Stats.CurrentHealth:0} gold {(wallet != null ? wallet.Gold : -1)} |{party}");
    }

    void GiveSpell()
    {
        spellGiven = true;
        ItemData tome = null;
#if UNITY_EDITOR
        tome = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(Spell);
#endif
        if (tome == null || player.Equipment == null) { Write($"no spell tome at {Spell}"); return; }
        // A hand item is held from the hotbar.
        if (player.Hotbar.IndexOf(tome) < 0 && !player.Hotbar.TryAdd(tome)) { Write("no hotbar room for the tome"); return; }
        bool ok = player.Equipment.Equip(tome, false, true);
        Write($"SPELL tome {tome.name} in hand: {ok}");
    }

    void Idle(string why)
    {
        if (doing != why) Write(why);
        doing = why;
        Release();
    }

    void Release() { SetMove(Vector2.zero); SetBlock(false); if (primaryHeld) Primary(false); if (casting) { Secondary(false); casting = false; } }

    void OnHurt(DamageInfo info)
    {
        if (info.Amount <= 0) return;
        hitsTaken++;
        Write($"hit for {info.Amount:0.0}{(info.Blocked ? " (blocked)" : "")} by {(info.Source != null ? info.Source.name : "?")}, health {player.Stats.CurrentHealth:0}/{player.Stats.MaxHealth:0}");
    }

    void TrackRoom(DungeonGenerator dungeon)
    {
        int room = dungeon.Layout.RoomAt(dungeon.CellOf(player.transform.position));
        if (room == lastRoom || room < 0) return;
        lastRoom = room;
        if (visited.Add(room)) { rooms++; Write($"entered room {room} ({visited.Count}/{RoomCount(dungeon.Layout)} seen)"); }
    }

    void WatchKills()
    {
        foreach (var brain in EnemyBrain.Active)
            if (brain != null && brain.Character != null && !watched.ContainsKey(brain.Character)) watched[brain.Character] = brain.Character.IsAlive;
        var dead = new List<Character>();
        foreach (var pair in watched)
            if (pair.Key == null || (pair.Value && !pair.Key.IsAlive)) dead.Add(pair.Key);
        foreach (var c in dead)
        {
            watched.Remove(c);
            if (c != null && Vector3.Distance(c.transform.position, player.transform.position) < 6f) { kills++; Write($"KILL {c.name}"); }
        }
    }

    // ── Fighting ──────────────────────────────────────────────────────

    // The nearest living, hostile enemy in sight that is close or after someone.
    EnemyBrain Threat()
    {
        EnemyBrain best = null; float bestD = float.MaxValue;
        foreach (var brain in EnemyBrain.Active)
        {
            if (brain == null || brain.Character == null || !brain.Character.IsAlive || brain.State == EnemyState.Dead) continue;
            if (!FactionRules.IsHostile(player.Faction, brain.Character.Faction)) continue;
            float d = Vector3.Distance(brain.transform.position, player.transform.position);
            bool engaged = brain.Perception != null && brain.Perception.Target != null;
            if (d > (engaged ? 12f : 7f) || d >= bestD) continue;
            if (Mathf.Abs(brain.transform.position.y - player.transform.position.y) > 2.5f) continue;
            if (!LineOfSight.Clear(Eye, Chest(brain.Character), ~0, player.transform, brain.transform)) continue;
            best = brain; bestD = d;
        }
        return best;
    }

    Vector3 Eye => player.Look != null ? player.Look.PitchTransform.position : player.transform.position + Vector3.up * 1.6f;
    static Vector3 Chest(Character c) { var col = c.GetComponent<Collider>(); return col != null ? col.bounds.center : c.transform.position + Vector3.up; }

    void Fight(EnemyBrain enemy)
    {
        string what = $"fighting {enemy.name}";
        if (doing != what) Write($"{what} ({Vector3.Distance(enemy.transform.position, player.transform.position):0.0} m, hp {enemy.Character.Stats.CurrentHealth:0})");
        doing = what;
        Aim(Chest(enemy.Character));
        float d = Vector3.Distance(Flat(enemy.transform.position), Flat(player.transform.position));
        var spells = player.GetComponent<PlayerSpellcasting>();

        // A spell in hand: cast from range.
        if (spells != null && spells.Selected != null && d > 2.5f)
        {
            SetMove(d > 9f ? new Vector2(0, 1) : Vector2.zero);
            if (!casting && !spells.Busy && player.Stats.CurrentMana >= spells.Cost(spells.Selected))
            { casting = true; castUntil = Time.time + spells.CastTime(spells.Selected) + .15f; Secondary(true); casts++; Write($"CAST {spells.Selected.name} at {enemy.name}"); }
            if (casting && Time.time > castUntil) { Secondary(false); casting = false; }
            return;
        }
        if (casting) { Secondary(false); casting = false; }

        // Its swing coming: the shield up, if there is one, until it lands.
        bool shield = player.Equipment != null && player.Equipment.Get(EquipmentSlot.LeftHand)?.itemType == ItemType.Shield;
        bool incoming = enemy.IsSwinging && d < 3f && (enemy.SwingHitAt < 0f || enemy.SwingHitAt - Time.time < .45f);
        SetBlock(shield && incoming);
        if (blocking) { SetMove(Vector2.zero); return; }

        // In close, swing; too close, step back; else walk in (along the path if it is round a corner).
        if (d > 1.9f) Walk(enemy.transform.position, false);
        else SetMove(d < .9f ? new Vector2(0, -1) : Vector2.zero);
        if (d < 2.3f && Time.time > nextAttack)
        {
            nextAttack = Time.time + .7f;
            StartCoroutine(Click());
        }
    }

    System.Collections.IEnumerator Click() { Primary(true); yield return new WaitForSeconds(.06f); Primary(false); }

    // ── Errands: loot, chests, doors close by ─────────────────────────

    Object Errand(DungeonGenerator dungeon)
    {
        Component best = null; float bestD = 9f;
        foreach (var item in Object.FindObjectsByType<WorldItem>())
        {
            if (item == null || given.Contains(item) || !item.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(item.transform.position, player.transform.position);
            if (d < bestD && Mathf.Abs(item.transform.position.y - player.transform.position.y) < 2f) { best = item; bestD = d; }
        }
        foreach (var chest in Object.FindObjectsByType<DungeonContainer>())
        {
            if (chest == null || given.Contains(chest) || !chest.CanInteract(player)) continue;
            float d = Vector3.Distance(chest.transform.position, player.transform.position);
            if (d < bestD) { best = chest; bestD = d; }
        }
        if (best == null) return null;
        string what = $"going for {best.name}";
        if (doing != what) Write($"{what} ({bestD:0.0} m)");
        doing = what;
        if (bestD > 1.6f) { if (!Walk(best.transform.position, true)) given.Add(best); return best; }
        SetMove(Vector2.zero);
        Aim(best.transform.position + Vector3.up * .3f);
        Interact();
        given.Add(best);
        if (best is DungeonContainer) { chests++; Write($"CHEST opened {best.name}"); }
        else { pickups++; Write($"PICKUP {best.name}"); }
        return best;
    }

    // ── Exploring ─────────────────────────────────────────────────────

    void Explore(DungeonGenerator dungeon, TableLevelLoader loader)
    {
        // On the way to a door: only that, until it is open or given up.
        if (doorGoal != null && !doorGoal.IsOpen) { doing = $"to door {doorGoal.name}"; GoToDoor(); return; }
        var layout = dungeon.Layout;
        bool seenAll = visited.Count + unreachable.Count >= RoomCount(layout);
        bool tooLong = Time.time - floorStartedAt > 240f;
        if (seenAll || tooLong)
        {
            DungeonExit exit = null;
            foreach (var e in Object.FindObjectsByType<DungeonExit>()) if (!e.entrance) exit = e;
            if (exit != null)
            {
                string what = "to the stairs";
                if (doing != what) Write($"{what} ({(seenAll ? "every room seen" : "floor taking too long")}, {Vector3.Distance(exit.transform.position, player.transform.position):0} m)");
                doing = what; goalName = exit.name;
                // Close enough for its prompt: look at it and press interact, as a player does.
                if (Vector3.Distance(Flat(exit.transform.position), Flat(player.transform.position)) < 2.2f)
                {
                    SetMove(Vector2.zero);
                    Aim(exit.transform.position);
                    if (Time.time > nextStairsPress) { nextStairsPress = Time.time + 1f; Interact(); Write("STAIRS pressed interact"); }
                    return;
                }
                if (!Walk(exit.transform.position, true)) DoorOrJump();
                return;
            }
        }
        // The nearest room not yet seen.
        int target = -1; float bestD = float.MaxValue;
        for (int r = 0; r < RoomCount(layout); r++)
        {
            if (visited.Contains(r) || unreachable.Contains(r)) continue;
            float d = Vector3.Distance(dungeon.Cell(layout.RoomCenter(r)), player.transform.position);
            if (d < bestD) { bestD = d; target = r; }
        }
        if (target < 0) return;
        string goingTo = $"exploring to room {target}";
        if (doing != goingTo) { Write($"{goingTo} ({bestD:0} m)"); stuckCount = 0; }
        doing = goingTo; goalName = "";
        if (!Walk(dungeon.Cell(layout.RoomCenter(target)), true))
        {
            if (!DoorOrJump() && ++stuckCount > 6) { unreachable.Add(target); Write($"room {target} given up (can't reach it)"); stuckCount = 0; }
        }
    }

    // Along the NavMesh to a point; false when stuck or the path ends short of it.
    bool Walk(Vector3 to, bool sprint)
    {
        if (Time.time > nextPath || goal != to)
        {
            goal = to; nextPath = Time.time + .5f;
            // Off the NavMesh for a moment (mid-jump, on a chest): keep the last path.
            if (NavMesh.SamplePosition(player.transform.position, out var from, 2f, NavMesh.AllAreas))
            {
                NavMesh.SamplePosition(to, out var dest, 3f, NavMesh.AllAreas);
                if (!NavMesh.CalculatePath(from.position, dest.hit ? dest.position : to, NavMesh.AllAreas, path)) path.ClearCorners();
            }
            corner = 1;
        }
        var corners = path.corners;
        if (corners.Length < 2) { SetMove(new Vector2(0, 1)); AimFlat(to); return Unstuck(); }
        while (corner < corners.Length - 1 && Vector3.Distance(Flat(corners[corner]), Flat(player.transform.position)) < .6f) corner++;
        var next = corners[Mathf.Min(corner, corners.Length - 1)];
        AimFlat(next);
        SetMove(new Vector2(0, 1));
        SetSprint(sprint && Vector3.Distance(player.transform.position, to) > 6f);
        bool partial = path.status != NavMeshPathStatus.PathComplete && Vector3.Distance(Flat(corners[^1]), Flat(player.transform.position)) < 1f;
        return !partial && Unstuck();
    }

    // Not moving for three seconds while trying to: stuck.
    bool Unstuck()
    {
        if (Time.time < stuckCheckAt) return true;
        stuckCheckAt = Time.time + 3f;
        bool moved = Vector3.Distance(stuckFrom, player.transform.position) > .4f;
        stuckFrom = player.transform.position;
        if (moved) return true;
        Write($"STUCK at {player.transform.position:0.0} ({doing})");
        return false;
    }

    // A closed door close in front: open it; else hop. At most every second and a half (a door takes a moment).
    float nextUnstick;
    // The way on is shut: walk to the nearest closed door it can reach (a closed door cuts the NavMesh, so its path
    // ends short of the room behind) and open it, as a player would; else hop.
    DungeonDoor doorGoal;
    readonly HashSet<DungeonDoor> doorsTried = new();
    bool DoorOrJump()
    {
        if (doorGoal != null && !doorGoal.IsOpen) return GoToDoor();
        doorGoal = null;
        if (Time.time < nextUnstick) return true;
        nextUnstick = Time.time + 1.5f;
        float bestD = 30f;
        foreach (var d in Object.FindObjectsByType<DungeonDoor>())
        {
            if (d == null || d.IsOpen || doorsTried.Contains(d)) continue;
            float dist = Vector3.Distance(d.transform.position, player.transform.position);
            if (dist < bestD && Mathf.Abs(d.transform.position.y - player.transform.position.y) < 2f) { bestD = dist; doorGoal = d; }
        }
        if (doorGoal != null) { Write($"to door {doorGoal.name} ({bestD:0.0} m)"); doorGoalAt = Time.time; return GoToDoor(); }
        Event("jumpQueued", typeof(PlayerMotor), player.Motor);
        return false;
    }

    float doorGoalAt;

    bool GoToDoor()
    {
        var door = doorGoal;
        if (Time.time - doorGoalAt > 15f) { Write($"door {door.name} not reached"); doorsTried.Add(door); doorGoal = null; return false; }
        float d = Vector3.Distance(Flat(door.transform.position), Flat(player.transform.position));
        if (d > 2f)
        {
            // Path to the door's near side (the door itself cuts the mesh).
            if (Time.time > nextPath || goal != door.transform.position)
            {
                goal = door.transform.position; nextPath = Time.time + .5f;
                if (NavMesh.SamplePosition(player.transform.position, out var from, 2f, NavMesh.AllAreas) && NavMesh.SamplePosition(door.transform.position, out var near, 3f, NavMesh.AllAreas))
                    NavMesh.CalculatePath(from.position, near.position, NavMesh.AllAreas, path);
                corner = 1;
            }
            var corners = path.corners;
            var next = corners.Length > 1 ? corners[Mathf.Min(corner, corners.Length - 1)] : door.transform.position;
            if (corners.Length > 1 && Vector3.Distance(Flat(next), Flat(player.transform.position)) < .6f && corner < corners.Length - 1) corner++;
            AimFlat(corners.Length > 1 && Vector3.Distance(Flat(corners[^1]), Flat(player.transform.position)) > .8f ? next : door.transform.position);
            SetMove(new Vector2(0, 1));
            return true;
        }
        SetMove(Vector2.zero);
        Aim(door.transform.position + Vector3.up);
        Interact();
        doors++;
        Write($"DOOR {door.name} {(door.IsOpen ? "opened" : "pressed, still shut")}");
        if (door.IsOpen && unreachable.Count > 0) { Write($"retrying {unreachable.Count} rooms given up"); unreachable.Clear(); }
        if (!door.IsOpen) { doorsTried.Add(door); doorGoal = null; }
        return true;
    }

    // ── Input, the way the devices give it ────────────────────────────

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

    void Aim(Vector3 at)
    {
        if (player.Look == null) return;
        CameraEase.YawPitchTo(player.Look, at, out float yaw, out float pitch);
        float current = player.Look.YawTransform.eulerAngles.y;
        player.Look.SetYaw(Mathf.MoveTowardsAngle(current, yaw, 540f * Time.deltaTime));
        player.Look.SetPitch(pitch);
    }

    void AimFlat(Vector3 at) { var eye = Eye; Aim(new Vector3(at.x, eye.y - .2f, at.z)); }

    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static InputManager Input => InputManager.HasInstance ? InputManager.Instance : null;

    static void SetProperty(string name, object value) { var im = Input; if (im != null) typeof(InputManager).GetProperty(name)?.GetSetMethod(true)?.Invoke(im, new[] { value }); }
    static void Raise(string evt) { var im = Input; if (im != null) (typeof(InputManager).GetField(evt, Hidden)?.GetValue(im) as System.Action)?.Invoke(); }
    static void Event(string field, System.Type type, object target) { var f = type.GetField(field, Hidden); if (f != null && target != null) f.SetValue(target, true); }

    void SetMove(Vector2 move) => SetProperty("Move", move);
    void SetSprint(bool on) => SetProperty("SprintHeld", on);
    void Interact() => Raise("InteractPressed");

    void Primary(bool down) { primaryHeld = down; SetProperty("PrimaryHeld", down); Raise(down ? "PrimaryPressed" : "PrimaryReleased"); }
    void Secondary(bool down) { SetProperty("SecondaryHeld", down); Raise(down ? "SecondaryPressed" : "SecondaryReleased"); }
    void SetBlock(bool on) { if (blocking == on) return; blocking = on; Secondary(on); }

    // ── Frames ────────────────────────────────────────────────────────

    System.Collections.IEnumerator Frame()
    {
        yield return new WaitForEndOfFrame();
        var shot = ScreenCapture.CaptureScreenshotAsTexture();
        var was = RenderTexture.active;
        var small = RenderTexture.GetTemporary(shot.width / 2, shot.height / 2, 0);
        Graphics.Blit(shot, small);
        var half = new Texture2D(small.width, small.height, TextureFormat.RGB24, false);
        RenderTexture.active = small;
        half.ReadPixels(new Rect(0, 0, small.width, small.height), 0, 0); half.Apply();
        RenderTexture.active = was;
        long ms = (long)System.DateTime.Now.TimeOfDay.TotalMilliseconds;
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(frameDir, $"{ms:00000000}.jpg"), half.EncodeToJPG(80));
        Destroy(shot); Destroy(half); RenderTexture.ReleaseTemporary(small);
        Flush();
    }
}
#endif
