using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// The floor everyone is on. Every machine builds it from the same seed, so its enemies, chests, breakables and doors
// line up by the order they were built in: that order is their id. The host runs the enemies and sends snapshots;
// guests turn theirs into puppets (CoopEnemyPuppet) and send their hits to the host. Whatever a player does to the
// floor (a chest, a barrel, a door, loot picked up or dropped) is sent once and done everywhere else.
public static class CoopWorld
{
    public static int FloorKey { get; private set; }
    public static DungeonGenerator Floor { get; private set; }
    // True while an event from another player is being applied: it is not sent on again.
    public static bool Applying { get; private set; }

    static readonly List<Character> enemies = new();
    static readonly Dictionary<Character, ushort> enemyIds = new();
    static readonly List<DungeonContainer> containers = new();
    static readonly List<DungeonDestructible> destructibles = new();
    static readonly List<DungeonDoor> doors = new();
    static readonly List<DungeonSpikeTrap> spikes = new();
    static readonly List<DungeonAmbush> ambushes = new();
    static readonly Dictionary<int, GameObject> loot = new();
    static readonly Dictionary<GameObject, int> lootKeys = new();
    static readonly HashSet<Character> networkSpawned = new();
    static readonly Dictionary<RuntimeAnimatorController, (int[] floats, int[] bools)> parameters = new();
    static int lootScope = -1, lootCounter, dropCounter;
    // Host: what happened to this floor so far, for a player who joins it late (CatchUp).
    static readonly List<CoopEvent> history = new();
    static readonly List<EnemySpawn> spawnHistory = new();
    // Mid-floor spawns take the host's health once their own Start has filled their pools.
    static readonly Dictionary<Character, float> pendingHealth = new();
    static float nextSnapshot;
    static bool hooked;

    const int ContainerScope = 0x100000, DestructibleScope = 0x200000, EnemyScope = 0x300000, DropScope = 0x400000, GeneratedScope = 0x500000;

    // ── Floor ─────────────────────────────────────────────────────────

    public static void Hook()
    {
        if (hooked) return;
        hooked = true;
        CharacterStats.NetworkIntercept = Intercept;
        Perception.ChooseTarget = ChooseTarget;
        Character.Spawned += OnCharacterSpawned;
    }

    public static void Unhook()
    {
        if (!hooked) return;
        hooked = false;
        CharacterStats.NetworkIntercept = null;
        Perception.ChooseTarget = null;
        Character.Spawned -= OnCharacterSpawned;
        Clear();
    }

    public static void Clear()
    {
        Floor = null; FloorKey = 0;
        enemies.Clear(); enemyIds.Clear(); containers.Clear(); destructibles.Clear(); doors.Clear(); spikes.Clear(); ambushes.Clear();
        loot.Clear(); lootKeys.Clear(); networkSpawned.Clear(); pendingHealth.Clear(); history.Clear(); spawnHistory.Clear(); waiting.Clear();
        lootScope = -1; lootCounter = 0; dropCounter = 0;
    }

    public static int KeyFor(int runSeed, int floor) => unchecked(runSeed * 31 + floor * 7919 + 1);

    // Called once a floor is built (TableLevelLoader.Prepare), on every machine.
    public static void FloorBuilt(DungeonGenerator generator, int floorKey)
    {
        if (!Coop.Running || generator == null) return;
        Clear();
        Floor = generator; FloorKey = floorKey;
        foreach (var brain in generator.GetComponentsInChildren<EnemyBrain>(true)) AddEnemy(brain.GetComponent<Character>());
        containers.AddRange(generator.GetComponentsInChildren<DungeonContainer>(true));
        destructibles.AddRange(generator.GetComponentsInChildren<DungeonDestructible>(true));
        doors.AddRange(generator.GetComponentsInChildren<DungeonDoor>(true));
        foreach (var door in doors) door.Opened += OnDoorOpened;
        spikes.AddRange(generator.GetComponentsInChildren<DungeonSpikeTrap>(true));
        ambushes.AddRange(generator.GetComponentsInChildren<DungeonAmbush>(true));
        foreach (var chest in containers) chest.Opened += OnContainerOpened;
        int index = 0;
        foreach (var item in generator.GetComponentsInChildren<WorldItem>(true)) Tag(item.gameObject, (GeneratedScope + index++) * 64);
        foreach (var coins in generator.GetComponentsInChildren<CoinPickup>(true)) Tag(coins.gameObject, (GeneratedScope + index++) * 64);

        int hash = Hash();
        if (Coop.IsGuest) CoopMessages.ToHost(CoopMessages.FloorHash, new CoopInt { a = floorKey, b = hash });
        else MultiplayerManager.Instance.HostFloorHash = (floorKey, hash);
        Debug.Log($"[Coop] Floor {floorKey} indexed: {enemies.Count} enemies, {containers.Count} containers, {destructibles.Count} breakables, {doors.Count} doors, {loot.Count} loot. Hash {hash:X8}.");
    }

    static void AddEnemy(Character c)
    {
        if (c == null || enemyIds.ContainsKey(c)) return;
        var id = (ushort)enemies.Count;
        enemies.Add(c);
        enemyIds[c] = id;
        if (Coop.IsGuest) MakePuppet(c);
        else c.Damaged += info => HostEnemyDamaged(c, info);
    }

    static void MakePuppet(Character c)
    {
        var brain = c.GetComponent<EnemyBrain>();
        if (brain != null) brain.NetworkPuppet = true;
        var motor = c.GetComponent<EnemyMotor>();
        if (motor != null)
        {
            if (motor.Agent != null) motor.Agent.enabled = false;
            motor.enabled = false;
        }
        var perception = c.GetComponent<Perception>();
        if (perception != null) perception.enabled = false;
        // Waking a sleeping pack is the host's call too.
        var sleep = c.GetComponent<EnemySleep>();
        if (sleep != null) sleep.enabled = false;
        if (c.GetComponent<CoopEnemyPuppet>() == null) c.gameObject.AddComponent<CoopEnemyPuppet>();
    }

    // Everything that should be the same on every machine, rounded: if this differs, the floor was built differently.
    static int Hash()
    {
        unchecked
        {
            int h = 17;
            void Add(Component c)
            {
                if (c == null) return;
                var p = c.transform.position;
                h = h * 31 + c.name.Replace("(Clone)", "").Trim().GetHashCode();
                h = h * 31 + Mathf.RoundToInt(p.x * 4); h = h * 31 + Mathf.RoundToInt(p.z * 4);
            }
            foreach (var e in enemies) Add(e);
            foreach (var c in containers) Add(c);
            foreach (var d in destructibles) Add(d);
            foreach (var d in doors) Add(d);
            foreach (var pair in loot) { h = h * 31 + pair.Key; if (pair.Value != null) h = h * 31 + pair.Value.name.GetHashCode(); }
            return h;
        }
    }

    // ── The party ─────────────────────────────────────────────────────

    // Everyone alive on the table, as enemies here should see them: the local table player and the others' stand-ins.
    public static IEnumerable<Character> Party()
    {
        if (PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table)
        {
            var local = PlayerManager.Instance.Active;
            if (local != null && local.IsAlive) yield return local;
        }
        if (!MultiplayerManager.HasInstance) yield break;
        foreach (var p in MultiplayerManager.Instance.Players)
            if (p != null && p.Hunted && p.Proxy != null) yield return p.Proxy;
    }

    public static bool AnyoneWithin(Vector3 point, float radius)
    {
        foreach (var c in Party()) if ((c.transform.position - point).sqrMagnitude <= radius * radius) return true;
        return false;
    }

    // Perception's target in co-op: the one it is already after while still reasonably close, else the nearest.
    static Character ChooseTarget(Character self, Character current)
    {
        if (self == null) return null;
        Character nearest = null;
        float best = float.MaxValue, currentDistance = float.MaxValue;
        foreach (var c in Party())
        {
            if (!FactionRules.IsHostile(self.Faction, c.Faction)) continue;
            float d = (c.transform.position - self.transform.position).sqrMagnitude;
            if (c == current) currentDistance = d;
            if (d < best) { best = d; nearest = c; }
        }
        if (current != null && currentDistance < float.MaxValue && currentDistance <= best * 2.25f) return current;
        return nearest;
    }

    public static int SlotOf(Character c)
    {
        if (c == null) return Coop.NoSlot;
        if (c is Player p && p.kind == PlayerKind.Table) return Coop.LocalSlot;
        var coop = c.GetComponent<CoopPlayer>();
        return coop != null ? coop.SlotIndex : Coop.NoSlot;
    }

    public static Character CharacterOf(int slot)
    {
        if (slot == Coop.NoSlot) return null;
        if (slot == Coop.LocalSlot) return PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        var p = MultiplayerManager.Instance.PlayerInSlot(slot);
        return p != null ? p.Proxy : null;
    }

    public static Character Enemy(int id) => id >= 0 && id < enemies.Count ? enemies[id] : null;
    public static bool TryEnemyId(Character c, out ushort id) { id = 0; return c != null && enemyIds.TryGetValue(c, out id); }
    public static IReadOnlyList<Character> Enemies => enemies;

    // ── Hits ──────────────────────────────────────────────────────────

    // CharacterStats.TakeDamage asks first. True: the hit was handled over the network (or ignored) here.
    static bool Intercept(CharacterStats stats, DamageInfo info)
    {
        if (!Coop.Running || Coop.ApplyingRemote) return false;
        var target = stats.Character;
        if (target == null) return false;
        var coopPlayer = target.GetComponent<CoopPlayer>();
        if (coopPlayer != null)
        {
            // Another player's stand-in: only the host's enemies reach it, and their owner takes the blow.
            if (Coop.IsHost && info.Source != null && info.Source.IsEnemy && TryEnemyId(info.Source, out var source) && !coopPlayer.IsOwner)
            {
                var hit = NetDamage.From(info);
                hit.enemy = source;
                hit.slot = (byte)Mathf.Clamp(coopPlayer.SlotIndex, 0, 254);
                coopPlayer.HitRpc(hit);
            }
            return true;
        }
        if (!Coop.IsGuest) return false;
        // On a guest enemies are puppets: their blows come from the host (CoopPlayer.HitRpc), never from here.
        if (info.Source != null && info.Source.IsEnemy) return true;
        if (target.IsEnemy)
        {
            // Only this player's own blows go to the host; spikes, falls and the like hurt enemies on the host already.
            bool mine = info.Source is Player p && p.kind == PlayerKind.Table;
            if (mine && TryEnemyId(target, out var id))
            {
                var strike = NetDamage.From(info);
                strike.enemy = id;
                strike.slot = (byte)Coop.LocalSlot;
                var brain = target.GetComponent<EnemyBrain>();
                if (brain != null) { strike.stun = brain.PendingNetworkStun; brain.PendingNetworkStun = 0f; }
                CoopMessages.ToHost(CoopMessages.Strike, strike);
            }
            return true;
        }
        return false;
    }

    // Guest -> host: a player's blow on an enemy, applied by the host as if it struck here.
    public static void ReceiveStrike(ulong sender, NetDamage strike)
    {
        if (!Coop.IsHost) return;
        var enemy = Enemy(strike.enemy);
        if (enemy == null || enemy.Stats == null || !enemy.IsAlive) return;
        var info = strike.ToInfo(CharacterOf(strike.slot));
        // Stunned first, as on the striker's machine, so the hurt reaction plays.
        if (strike.stun > 0f && enemy.TryGetComponent<EnemyBrain>(out var brain)) brain.Stun(strike.stun);
        enemy.Stats.TakeDamage(info);
    }

    // Host: any damage to an enemy goes to the guests with its health after it.
    static void HostEnemyDamaged(Character enemy, DamageInfo info)
    {
        if (!Coop.IsHost || !TryEnemyId(enemy, out var id)) return;
        var hit = NetDamage.From(info);
        hit.enemy = id;
        int slot = SlotOf(info.Source);
        hit.slot = (byte)(slot == Coop.NoSlot ? Coop.NoSlot : slot);
        hit.health = enemy.Stats != null ? enemy.Stats.CurrentHealth : 0;
        hit.Died = !enemy.IsAlive;
        CoopMessages.ToRunGuests(CoopMessages.EnemyHit, hit);
        if (hit.Died && slot != Coop.LocalSlot && RunManager.HasInstance) RunManager.Instance.AwardPartyKill(enemy);
    }

    // Guest: the host's result of a hit, played on the puppet (blood, bar, death, loot).
    public static void ReceiveEnemyDamaged(ulong sender, NetDamage hit)
    {
        if (!Coop.IsGuest) return;
        var enemy = Enemy(hit.enemy);
        if (enemy == null || enemy.Stats == null || !enemy.IsAlive) return;
        var info = hit.ToInfo(CharacterOf(hit.slot));
        enemy.Stats.ApplyNetworkDamage(info, hit.health);
        if (hit.Died && hit.slot != Coop.LocalSlot && RunManager.HasInstance) RunManager.Instance.AwardPartyKill(enemy);
    }

    // Owner: an enemy's blow from the host, taken (or blocked) by the local table player.
    public static void ReceiveEnemyHit(NetDamage hit)
    {
        if (!PlayerManager.HasInstance) return;
        var player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        if (player == null || !player.IsAlive || PlayerManager.Instance.ActiveKind != PlayerKind.Table) return;
        var info = hit.ToInfo(Enemy(hit.enemy));
        Coop.ApplyingRemote = true;
        try { player.Stats.TakeDamage(info); }
        finally { Coop.ApplyingRemote = false; }
    }

    // ── Snapshots ─────────────────────────────────────────────────────

    public static void Tick()
    {
        if (Coop.IsGuest && waiting.Count > 0 && TableManager.Loader != null && !TableManager.Loader.Busy)
        {
            var events = waiting.ToArray(); waiting.Clear();
            foreach (var e in events) if (e.floorKey == FloorKey) Play(e);
        }
        if (!Coop.IsHost || Floor == null || enemies.Count == 0) return;
        if (Time.unscaledTime < nextSnapshot) return;
        nextSnapshot = Time.unscaledTime + 1f / Mathf.Max(1f, MultiplayerManager.Instance.snapshotRate);
        var batch = new List<EnemySnapshot>(16);
        for (int i = 0; i < enemies.Count; i++)
        {
            var c = enemies[i];
            if (c == null || !c.IsAlive || !c.gameObject.activeInHierarchy) continue;
            batch.Add(Capture((ushort)i, c));
            if (batch.Count >= 12) { Send(batch); batch.Clear(); }
        }
        if (batch.Count > 0) Send(batch);
    }

    static void Send(List<EnemySnapshot> batch) =>
        CoopMessages.ToRunGuests(CoopMessages.Snapshots, new SnapshotBatch { floorKey = FloorKey, items = batch.ToArray() }, NetworkDelivery.UnreliableSequenced);

    static EnemySnapshot Capture(ushort id, Character c)
    {
        var brain = c.GetComponent<EnemyBrain>();
        var flyer = c.GetComponent<FlyingEnemy>();
        var s = new EnemySnapshot
        {
            id = id, position = c.transform.position, yaw = (short)Mathf.RoundToInt(Mathf.Repeat(c.transform.eulerAngles.y, 360f) * 50f),
            state = brain != null ? (byte)brain.State : (byte)0, asleep = brain != null && brain.Asleep,
            modelHeight = flyer != null ? flyer.Height : 0f,
        };
        var anim = c.Animator;
        if (anim == null || anim.runtimeAnimatorController == null || !anim.isInitialized) { s.states = new int[0]; s.times = new byte[0]; s.floats = new float[0]; return s; }
        int layers = anim.layerCount;
        s.states = new int[layers]; s.times = new byte[layers];
        for (int l = 0; l < layers; l++)
        {
            var info = anim.IsInTransition(l) ? anim.GetNextAnimatorStateInfo(l) : anim.GetCurrentAnimatorStateInfo(l);
            s.states[l] = info.fullPathHash;
            s.times[l] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Repeat(info.normalizedTime, 1f) * 255f), 0, 255);
        }
        var (floats, bools) = Parameters(anim);
        s.floats = new float[floats.Length];
        for (int i = 0; i < floats.Length; i++) s.floats[i] = anim.GetFloat(floats[i]);
        for (int i = 0; i < bools.Length && i < 32; i++) if (anim.GetBool(bools[i])) s.bools |= 1u << i;
        return s;
    }

    public static (int[] floats, int[] bools) Parameters(Animator anim)
    {
        var controller = anim.runtimeAnimatorController;
        if (parameters.TryGetValue(controller, out var cached)) return cached;
        var floats = new List<int>(); var bools = new List<int>();
        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Float) floats.Add(p.nameHash);
            else if (p.type == AnimatorControllerParameterType.Bool) bools.Add(p.nameHash);
        }
        cached = (floats.ToArray(), bools.ToArray());
        parameters[controller] = cached;
        return cached;
    }

    public static void ReceiveSnapshots(ulong sender, SnapshotBatch batch)
    {
        if (!Coop.IsGuest || batch.floorKey != FloorKey || batch.items == null) return;
        foreach (var s in batch.items)
        {
            var c = Enemy(s.id);
            if (c == null || !c.IsAlive) continue;
            var puppet = c.GetComponent<CoopEnemyPuppet>();
            if (puppet != null) puppet.Apply(s);
        }
    }

    // ── Enemies that appear mid-floor ─────────────────────────────────

    static void OnCharacterSpawned(Character c)
    {
        if (c != null && pendingHealth.TryGetValue(c, out var health)) { pendingHealth.Remove(c); if (c.Stats != null && health > 0) c.Stats.SetNetworkHealth(health); }
        if (!Coop.Running || Floor == null || c == null || !c.IsEnemy || enemyIds.ContainsKey(c) || !c.transform.IsChildOf(Floor.transform)) return;
        if (Coop.IsGuest)
        {
            // Made here by a guest's own copy of a room event: the host's copy is the real one and comes as a spawn.
            if (!networkSpawned.Contains(c)) Object.Destroy(c.gameObject);
            return;
        }
        int prefab = MultiplayerManager.Instance.EnemyPrefabIndex(c);
        if (prefab < 0) { Debug.LogWarning($"[Coop] {c.name} appeared mid-floor but is not in MultiplayerManager's enemy prefabs; guests won't see it.", c); return; }
        AddEnemy(c);
        var drop = c.GetComponent<DungeonLootDrop>();
        var spawn = new EnemySpawn
        {
            floorKey = FloorKey, id = enemyIds[c], prefab = prefab, position = c.transform.position, yaw = c.transform.eulerAngles.y,
            lootSeed = drop != null ? drop.seed : 0,
            health = c.Stats != null ? c.Stats.CurrentHealth : 0, maxHealth = c.Stats != null ? c.Stats.MaxHealth : 0,
        };
        spawnHistory.Add(spawn);
        CoopMessages.ToGuests(CoopMessages.Spawn, spawn);
    }

    public static void ReceiveSpawn(ulong sender, EnemySpawn spawn)
    {
        if (!Coop.IsGuest || spawn.floorKey != FloorKey || Floor == null) return;
        var prefab = MultiplayerManager.Instance.EnemyPrefab(spawn.prefab);
        if (prefab == null) return;
        var go = Object.Instantiate(prefab, spawn.position, Quaternion.Euler(0, spawn.yaw, 0), Floor.transform);
        var c = go.GetComponent<Character>();
        if (c == null) { Object.Destroy(go); return; }
        networkSpawned.Add(c);
        Floor.SetUpNetworkEnemy(go, spawn.lootSeed);
        // The host's own changes to it (a buffed killer): the same health here.
        // A multiplier on top of everything else, so the result is exactly the host's.
        if (c.Stats != null && spawn.maxHealth > 0 && c.Stats.MaxHealth > 0 && Mathf.Abs(c.Stats.MaxHealth - spawn.maxHealth) > .01f)
            c.Stats.AddModifier(new StatModifier(StatType.MaxHealth, spawn.maxHealth / c.Stats.MaxHealth - 1f, ModifierType.PercentMultiply, Coop.PartySource));
        pendingHealth[c] = spawn.health;
        while (enemies.Count < spawn.id) enemies.Add(null);
        if (enemies.Count == spawn.id) { enemies.Add(c); enemyIds[c] = spawn.id; }
        else { enemies[spawn.id] = c; enemyIds[c] = spawn.id; }
        MakePuppet(c);
    }

    // ── Loot ──────────────────────────────────────────────────────────

    // Each player's own loot (MultiplayerManager.sharedLoot off): every roll from a seed of their own.
    public static int LootSeed(int seed)
    {
        if (!Coop.Running || MultiplayerManager.Instance.SharedLoot) return seed;
        return unchecked(seed ^ ((Coop.LocalSlot + 1) * 486187739));
    }

    // Everything spawned between Begin and End belongs to that source, numbered in spawn order.
    public static void BeginLoot(Component source)
    {
        if (!Coop.Running || Floor == null) { lootScope = -1; return; }
        int scope = -1;
        if (source is DungeonContainer chest) scope = ContainerScope + containers.IndexOf(chest);
        else if (source is DungeonDestructible breakable) scope = DestructibleScope + destructibles.IndexOf(breakable);
        else if (source is Character c && enemyIds.TryGetValue(c, out var id)) scope = EnemyScope + id;
        lootScope = scope; lootCounter = 0;
    }
    public static void EndLoot() => lootScope = -1;

    // DungeonPickup.Spawn and CurrencyManager.SpawnCoins: a key for each piece of loot.
    public static void Spawned(GameObject piece)
    {
        if (!Coop.Running || piece == null || lootScope < 0) return;
        Tag(piece, lootScope * 64 + Mathf.Min(63, lootCounter++));
    }

    static void Tag(GameObject piece, int key)
    {
        if (loot.TryGetValue(key, out var old) && old != null && old != piece) return;
        loot[key] = piece;
        lootKeys[piece] = key;
    }

    // Shared loot is taken once; each player's own loot stays their own (dropped items are always shared).
    static bool SharedPiece(int key) => MultiplayerManager.Instance.SharedLoot || key / 64 >= DropScope && key / 64 < GeneratedScope;

    public static void PickedUp(WorldItem item)
    {
        if (!Coop.Running || Applying || item == null || !lootKeys.TryGetValue(item.gameObject, out var key) || !SharedPiece(key)) return;
        Send(new CoopEvent { kind = CoopEventKind.PickupTaken, id = key, item = new FixedString64Bytes(item.Item != null ? item.Item.itemName : "") });
    }

    public static void CoinsTaken(CoinPickup coins, int amount)
    {
        if (!Coop.Running || Applying || coins == null || !lootKeys.TryGetValue(coins.gameObject, out var key) || !SharedPiece(key)) return;
        Send(new CoopEvent { kind = CoopEventKind.CoinsTaken, id = key, amount = amount });
    }

    // A player put an item on the floor: everyone sees it and anyone can take it.
    public static void Dropped(WorldItem item, int count)
    {
        if (!Coop.Running || Applying || item == null || Floor == null || !item.transform.IsChildOf(Floor.transform)) return;
        int key = (DropScope + Coop.LocalSlot * 4096 + (dropCounter++ % 4096)) * 64;
        Tag(item.gameObject, key);
        Send(new CoopEvent { kind = CoopEventKind.ItemDropped, id = key, amount = count, position = item.transform.position, item = new FixedString64Bytes(Coop.SaveId(item.Item)) });
    }

    // ── Floor events ──────────────────────────────────────────────────

    static void OnContainerOpened(DungeonContainer chest)
    {
        if (Applying || !Coop.Running) return;
        int id = containers.IndexOf(chest);
        if (id >= 0) Send(new CoopEvent { kind = CoopEventKind.ContainerOpened, id = id });
    }

    // A chest made mid-floor (an ambush's reward), made the same way on every machine: the next id.
    public static void AddContainer(DungeonContainer chest)
    {
        if (!Coop.Running || chest == null || containers.Contains(chest)) return;
        containers.Add(chest);
        chest.Opened += OnContainerOpened;
    }

    public static void SpikeSprung(DungeonSpikeTrap trap)
    {
        if (Applying || !Coop.Running) return;
        int id = spikes.IndexOf(trap);
        if (id >= 0) Send(new CoopEvent { kind = CoopEventKind.SpikeSprung, id = id });
    }

    // Host: an ambush sprang (at someone's position) or was cleared.
    public static void Ambush(DungeonAmbush ambush, bool cleared, Vector3 at)
    {
        if (!Coop.IsHost) return;
        int id = ambushes.IndexOf(ambush);
        if (id >= 0) Send(new CoopEvent { kind = cleared ? CoopEventKind.AmbushCleared : CoopEventKind.AmbushSprung, id = id, position = at });
    }

    // Host: an enemy's whoosh (its attack's swing sound) for the guests, whose puppets never swing for real.
    public static void EnemySwung(Character enemy, int attack)
    {
        if (!Coop.IsHost || !TryEnemyId(enemy, out var id)) return;
        Send(new CoopEvent { kind = CoopEventKind.EnemySwing, id = id, amount = attack });
    }

    // Host: an archer's arrow; guests loose a harmless copy along the same line (the host's hits it).
    public static void ArrowLoosed(Character enemy, Vector3 origin, Vector3 direction)
    {
        if (!Coop.IsHost || !TryEnemyId(enemy, out var id)) return;
        Send(new CoopEvent { kind = CoopEventKind.ArrowLoosed, id = id, position = origin, direction = direction });
    }

    public static void Broken(DungeonDestructible breakable, Vector3 direction)
    {
        if (Applying || !Coop.Running) return;
        int id = destructibles.IndexOf(breakable);
        if (id >= 0) Send(new CoopEvent { kind = CoopEventKind.DestructibleBroken, id = id, direction = direction });
    }

    static void OnDoorOpened(DungeonDoor door)
    {
        if (Applying || !Coop.Running) return;
        int id = doors.IndexOf(door);
        var from = PlayerManager.HasInstance && PlayerManager.Instance.Active != null ? PlayerManager.Instance.Active.transform.position : door.transform.position;
        if (id >= 0) Send(new CoopEvent { kind = CoopEventKind.DoorOpened, id = id, position = from });
    }

    // Host: the lasting changes, in order (not sounds, arrows or spikes that come and go).
    static void Record(CoopEvent e)
    {
        if (!Coop.IsHost || e.floorKey != FloorKey) return;
        switch (e.kind)
        {
            case CoopEventKind.EnemySwing: case CoopEventKind.ArrowLoosed: case CoopEventKind.SpikeSprung: case CoopEventKind.AtExit: return;
        }
        history.Add(e);
    }

    // Host: a player has built this floor (its hash came in). One who joined late is brought up to date: what was
    // made mid-floor, what was opened, broken and taken, and which enemies are gone.
    public static void CatchUp(ulong client, int floorKey)
    {
        if (!Coop.IsHost || floorKey != FloorKey || Floor == null) return;
        foreach (var spawn in spawnHistory) CoopMessages.ToClient(CoopMessages.Spawn, client, spawn);
        foreach (var e in history)
        {
            var past = e;
            // Gold picked up before they came is not theirs.
            if (past.kind == CoopEventKind.CoinsTaken) past.amount = 0;
            CoopMessages.ToClient(CoopMessages.Event, client, past);
        }
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i] == null || !enemies[i].IsAlive)
                CoopMessages.ToClient(CoopMessages.Event, client, new CoopEvent { kind = CoopEventKind.EnemyGone, id = i, floorKey = FloorKey, slot = (byte)Coop.LocalSlot });
            // Wounded: its health as it is (a hit sent while their floor was still loading never landed there).
            else if (enemies[i].Stats != null && enemies[i].Stats.CurrentHealth < enemies[i].Stats.MaxHealth)
                CoopMessages.ToClient(CoopMessages.Event, client, new CoopEvent { kind = CoopEventKind.EnemyHealth, id = i, amount = Mathf.RoundToInt(enemies[i].Stats.CurrentHealth * 100f), floorKey = FloorKey, slot = (byte)Coop.LocalSlot });
        }
    }

    public static void Send(CoopEvent e)
    {
        e.slot = (byte)Coop.LocalSlot;
        e.floorKey = FloorKey;
        Record(e);
        if (Coop.IsHost) CoopMessages.ToGuests(CoopMessages.Event, e);
        else CoopMessages.ToHost(CoopMessages.Event, e);
    }

    // ── The apartment ─────────────────────────────────────────────────

    static List<HingedDoor> roomDoors;

    // The room's doors, in the same order everywhere (the scene is the same): by their place in the hierarchy.
    static List<HingedDoor> RoomDoors()
    {
        if (roomDoors != null && !roomDoors.Exists(d => d == null)) return roomDoors;
        roomDoors = new List<HingedDoor>(Object.FindObjectsByType<HingedDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        roomDoors.Sort((a, b) => string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform)));
        return roomDoors;
    }

    static string PathOf(Transform t) => t.parent == null ? t.name + t.GetSiblingIndex() : PathOf(t.parent) + "/" + t.name + t.GetSiblingIndex();

    public static void RoomDoorChanged(HingedDoor door, bool open)
    {
        if (!Coop.Running || Applying) return;
        int id = RoomDoors().IndexOf(door);
        if (id < 0) return;
        var e = new CoopEvent { kind = CoopEventKind.RoomDoor, id = id, amount = open ? 1 : 0, slot = (byte)Coop.LocalSlot };
        if (Coop.IsHost) CoopMessages.ToGuests(CoopMessages.Event, e); else CoopMessages.ToHost(CoopMessages.Event, e);
    }

    // Host: a new player gets the apartment's doors as they are.
    public static void SendRoom(ulong client)
    {
        if (!Coop.IsHost) return;
        var doors = RoomDoors();
        for (int i = 0; i < doors.Count; i++)
            if (doors[i] != null) CoopMessages.ToClient(CoopMessages.Event, client, new CoopEvent { kind = CoopEventKind.RoomDoor, id = i, amount = doors[i].IsOpen ? 1 : 0 });
    }

    public static void ReceiveEvent(ulong sender, CoopEvent e)
    {
        // The host passes a guest's event on to the other guests before playing it itself.
        if (Coop.IsHost && sender != NetworkManager.ServerClientId && e.kind != CoopEventKind.RoomDoor) { CoopMessages.ToGuests(CoopMessages.Event, e, NetworkDelivery.ReliableSequenced, sender); Record(e); }
        if (e.kind == CoopEventKind.AtExit) { if (Coop.IsHost) MultiplayerManager.Instance.PlayerAtExit(e.slot); return; }
        if (e.kind == CoopEventKind.RoomDoor)
        {
            if (Coop.IsHost && sender != NetworkManager.ServerClientId) CoopMessages.ToGuests(CoopMessages.Event, e, NetworkDelivery.ReliableSequenced, sender);
            var doors = RoomDoors();
            if (e.id >= 0 && e.id < doors.Count && doors[e.id] != null && doors[e.id].IsOpen != (e.amount != 0))
            {
                Applying = true;
                try { doors[e.id].SetOpen(e.amount != 0); }
                finally { Applying = false; }
            }
            return;
        }
        if (e.floorKey != FloorKey || Floor == null) return;
        // A guest still revealing its floor (the reveal switches things on as it builds) plays it once it is done.
        if (Coop.IsGuest && TableManager.Loader != null && TableManager.Loader.Busy) { waiting.Add(e); return; }
        Play(e);
    }

    static readonly List<CoopEvent> waiting = new();

    static void Play(CoopEvent e)
    {
        Applying = true;
        try { Apply(e); }
        catch (System.Exception ex) { Debug.LogException(ex); }
        finally { Applying = false; }
    }

    static void Apply(CoopEvent e)
    {
        switch (e.kind)
        {
            case CoopEventKind.ContainerOpened:
                if (e.id >= 0 && e.id < containers.Count && containers[e.id] != null) containers[e.id].OpenFromNetwork();
                break;
            case CoopEventKind.DestructibleBroken:
                if (e.id >= 0 && e.id < destructibles.Count && destructibles[e.id] != null) destructibles[e.id].Break(e.direction);
                break;
            case CoopEventKind.DoorOpened:
                if (e.id >= 0 && e.id < doors.Count && doors[e.id] != null && !doors[e.id].IsOpen) doors[e.id].Open(e.position);
                break;
            case CoopEventKind.EnemyHealth:
            {
                var enemy = Enemy(e.id);
                if (enemy != null && enemy.IsAlive && enemy.Stats != null) enemy.Stats.SetNetworkHealth(e.amount / 100f);
                break;
            }
            case CoopEventKind.EnemyGone:
            {
                // Killed before this player joined: no body, no loot, it is simply not there.
                var enemy = Enemy(e.id);
                if (enemy != null) enemy.gameObject.SetActive(false);
                break;
            }
            case CoopEventKind.SpikeSprung:
                if (e.id >= 0 && e.id < spikes.Count && spikes[e.id] != null) spikes[e.id].Spring();
                break;
            case CoopEventKind.AmbushSprung:
                if (e.id >= 0 && e.id < ambushes.Count && ambushes[e.id] != null) ambushes[e.id].SprungOnHost();
                break;
            case CoopEventKind.AmbushCleared:
                if (e.id >= 0 && e.id < ambushes.Count && ambushes[e.id] != null) ambushes[e.id].ClearedOnHost();
                break;
            case CoopEventKind.EnemySwing:
            {
                var enemy = Enemy(e.id);
                if (enemy == null || !AudioManager.HasInstance || !(enemy.data is EnemyData data)) break;
                var hit = e.amount >= 0 && e.amount < data.attacks.Count ? data.attacks[e.amount].hit : null;
                AudioData audio = hit == null || hit.useDefaultEffects ? (CombatManager.HasInstance ? CombatManager.Instance.defaultSwingAudio : null) : hit.swingAudio;
                if (audio != null) AudioManager.Instance.PlaySFXData(audio, enemy.transform.position + Vector3.up);
                break;
            }
            case CoopEventKind.ArrowLoosed:
            {
                var enemy = Enemy(e.id);
                var bow = enemy != null && enemy.data is EnemyData { archer: true } data ? data.archery : null;
                if (bow == null || bow.arrow == null || e.direction.sqrMagnitude < .0001f) break;
                if (bow.fireAudio != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData(bow.fireAudio, e.position);
                var arrow = Object.Instantiate(bow.arrow, e.position, Quaternion.LookRotation(e.direction));
                arrow.Launch(enemy, e.direction, bow.arrowSpeed, 0f, bow.hit);
                break;
            }
            case CoopEventKind.PickupTaken:
                // Shared loot a teammate took: say who, so it doesn't just vanish.
                if (loot.TryGetValue(e.id, out var piece) && piece != null && e.item.Length > 0 && MultiplayerManager.Instance.PlayerInSlot(e.slot) is CoopPlayer taker)
                    MessageLog.Post($"{taker.DisplayName} took the {e.item}.", MessageKind.Loot);
                if (loot.TryGetValue(e.id, out piece) && piece != null) Object.Destroy(piece);
                loot.Remove(e.id);
                break;
            case CoopEventKind.CoinsTaken:
                if (loot.TryGetValue(e.id, out var pile) && pile != null) Object.Destroy(pile);
                loot.Remove(e.id);
                // Gold is the party's: everyone gets what anyone picks up.
                var wallet = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table)?.Wallet : null;
                if (wallet != null && e.amount > 0) wallet.Add(e.amount);
                break;
            case CoopEventKind.ItemDropped:
                var item = Coop.Item(e.item.ToString());
                if (item == null || !InventoryManager.HasInstance) break;
                // Put down as floor loot is, resting on the floor below where it was let go (it is not thrown here).
                var dropped = DungeonPickup.Spawn(item, e.position, Floor.transform, Mathf.Max(1, e.amount));
                if (dropped == null) break;
                Tag(dropped.gameObject, e.id);
                break;
        }
    }
}
