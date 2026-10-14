using System.Collections.Generic;
using UnityEngine;

// Room events and room light (DungeonRoomFeatures, on the level or overridden by the biome): which combat rooms are
// ambushes or hold a sleeping pack, which quiet rooms get a shrine, which chests are trapped, which rooms are dark or
// green-lit. Every roll here comes from its own seed streams, so the rest of the floor is built as before.
public partial class DungeonGenerator
{
    static readonly DungeonRoomFeatures DefaultFeatures = new();
    // The room features of this floor: the biome's own when it overrides the level's.
    public DungeonRoomFeatures RoomFeatures => (Biome != null ? Biome.RoomFeatures(data) : data.roomFeatures) ?? DefaultFeatures;

    public enum RoomEvent { None = 0, Ambush = 1, SleepingPack = 2, Shrine = 3 }
    RoomEvent[] roomEvents;
    DungeonRoomLight[] roomLights;
    public RoomEvent EventOf(int room) => roomEvents != null && room >= 0 && room < roomEvents.Length ? roomEvents[room] : RoomEvent.None;
    public DungeonRoomLight LightOf(int room) => roomLights != null && room >= 0 && room < roomLights.Length ? roomLights[room] : DungeonRoomLight.Normal;

    readonly Dictionary<int, List<Character>> sleepingPacks = new();
    readonly Dictionary<int, List<Vector3>> ambushPoints = new();
    System.Random trapRandom;

    static DungeonRoomFeature LightFeature(DungeonRoomLight light) => light switch
    {
        DungeonRoomLight.Dark => DungeonRoomFeature.Dark,
        DungeonRoomLight.Candlelit => DungeonRoomFeature.Candlelit,
        DungeonRoomLight.Moonlit => DungeonRoomFeature.Moonlit,
        _ => DungeonRoomFeature.Eerie,
    };

    // After the templates are chosen, before anything is placed. Hand-made floors (authored maps) keep to what their
    // author put there: their templates' own events and moods, nothing rolled.
    void ChooseRoomEvents()
    {
        int n = Layout.rooms.Count;
        roomEvents = new RoomEvent[n]; roomLights = new DungeonRoomLight[n];
        sleepingPacks.Clear(); ambushPoints.Clear();
        trapRandom = new System.Random(Scramble(unchecked(seed0 + 524287)));
        var f = RoomFeatures;
        var rng = new System.Random(Scramble(unchecked(seed0 + 271829)));
        int ambushes = 0, sleepers = 0;
        // One ambush a floor at most; 0 allows none.
        bool AmbushLeft() => ambushes < Mathf.Min(f.maxAmbushes, 1);
        bool SleepLeft() => f.maxSleepingPacks <= 0 || sleepers < f.maxSleepingPacks;
        bool Special(int i) => i == 0 || i == MerchantRoom || (Roles[i] == RoomRole.Exit && Milestone != null);
        // Rooms built for an event take their places under the caps first; the rest roll for what is left. An authored
        // map's rooms get their template's own event (the author put it there) and nothing rolled.
        for (int i = 0; i < n; i++)
        {
            var t = templates[i];
            if (t == null || Special(i) || Roles[i] != RoomRole.Combat) continue;
            if (t.roomEvent == DungeonRoomEvent.Ambush && f.ambushes && data.ambushGatePrefab != null && AmbushLeft()) { roomEvents[i] = RoomEvent.Ambush; ambushes++; }
            else if (t.roomEvent == DungeonRoomEvent.SleepingPack && f.sleepingPacks && SleepLeft()) { roomEvents[i] = RoomEvent.SleepingPack; sleepers++; }
        }
        for (int i = 0; i < n; i++)
        {
            // Four draws per room whatever is used, so one switch never moves another room's rolls.
            double a = rng.NextDouble(), b = rng.NextDouble(), c = rng.NextDouble(), d = rng.NextDouble();
            var t = templates[i];
            var mood = t != null ? t.GetComponent<DungeonRoomMood>() : null;
            if (mood != null && mood.kind != DungeonRoomLight.Normal) roomLights[i] = f.Allows(LightFeature(mood.kind)) ? mood.kind : DungeonRoomLight.Normal;
            if (Authored) continue;
            bool special = Special(i);
            if (!special && Roles[i] == RoomRole.Combat && (t == null || t.roomEvent == DungeonRoomEvent.Rolled))
            {
                if (f.ambushes && a < f.ambushChance && data.ambushGatePrefab != null && AmbushLeft()) { roomEvents[i] = RoomEvent.Ambush; ambushes++; }
                else if (f.sleepingPacks && b < f.sleepChance && SleepLeft()) { roomEvents[i] = RoomEvent.SleepingPack; sleepers++; }
            }
            if (!special && Roles[i] == RoomRole.Rest && t == null && f.shrineBargains && c < f.shrineChance && data.shrinePrefab != null) roomEvents[i] = RoomEvent.Shrine;
            if (t == null && i != 0 && Roles[i] != RoomRole.Exit)
            {
                if (f.darkRooms && d < f.darkChance) roomLights[i] = DungeonRoomLight.Dark;
                else if (f.eerieRooms && d > 1 - f.eerieChance) roomLights[i] = DungeonRoomLight.Eerie;
            }
        }
    }

    // After the torches: each lit-up (or darkened) room gets its mood; a template's own mood is used where it has one.
    void ApplyRoomLights()
    {
        if (roomLights == null) return;
        for (int i = 0; i < roomLights.Length; i++)
        {
            var mood = templateInstances[i] != null ? templateInstances[i].GetComponent<DungeonRoomMood>() : null;
            if (roomLights[i] == DungeonRoomLight.Normal)
            {
                // A template mood that was switched off: its own lights stay, but the room looks like any other.
                if (mood != null) mood.kind = DungeonRoomLight.Normal;
                continue;
            }
            if (mood == null)
            {
                var go = new GameObject($"Room light {i}");
                go.transform.SetParent(transform, false);
                go.transform.position = Cell(Layout.RoomCenter(i));
                mood = go.AddComponent<DungeonRoomMood>();
            }
            mood.kind = roomLights[i];
            mood.Build(this, i);
            // A board has no ceiling: the patch of night sky over a moonlit room would hang in the room above the table.
            if (Board && templateInstances[i] != null)
                foreach (var t in templateInstances[i].GetComponentsInChildren<Transform>(true)) if (t.name == "Night sky") t.gameObject.SetActive(false);
        }
    }

    // ── Shrines ──────────────────────────────────────────────────────

    // A quiet room's shrine against a wall, facing in. Takes its cell out of the room's free cells.
    void PlaceShrine(int index, List<Vector2Int> available)
    {
        if (EventOf(index) != RoomEvent.Shrine) return;
        var rng = new System.Random(Scramble(unchecked(seed0 + 7741 * (index + 1))));
        var order = new List<Vector2Int>(available);
        for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        var open = new HashSet<Vector2Int>(available); open.ExceptWith(furnished);
        foreach (var anchor in order)
            foreach (var forward in Sides)
            {
                if (!Fits(DungeonPropPlacement.AgainstWall, Vector2Int.one, index, anchor, forward, open)) continue;
                var go = Instantiate(data.shrinePrefab, Cell(anchor), Quaternion.LookRotation(new Vector3(forward.x, 0, forward.y)), transform);
                foreach (var s in go.GetComponentsInChildren<DungeonShrine>()) s.bloodPrice = RoomFeatures.shrinePrice;
                BackToWall(go, DungeonPropPlacement.AgainstWall, Vector2Int.one);
                furnished.Add(anchor); available.Remove(anchor);
                return;
            }
    }

    // ── Trapped chests ───────────────────────────────────────────────

    // A chest just placed may be trapped: spike plates in the floor cells around it (in its room). Returns the cells
    // the plates took (so nothing else is set down on them).
    List<Vector2Int> MaybeTrap(DungeonContainer chest, bool always = false)
    {
        var taken = new List<Vector2Int>();
        if (chest == null || !chest.chest || data.spikeTrapPrefab == null || Authored || trapRandom == null) return taken;
        var f = RoomFeatures;
        double roll = trapRandom.NextDouble();
        if (!f.trappedTreasure || (!always && roll >= f.trapChance)) return taken;
        var cell = CellOf(chest.transform.position);
        int room = Layout.RoomAt(cell);
        if (room < 0) return taken;
        var trap = chest.GetComponent<DungeonChestTrap>();
        if (trap == null) trap = chest.gameObject.AddComponent<DungeonChestTrap>();
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                var c = cell + new Vector2Int(dx, dz);
                if ((dx == 0 && dz == 0) || !Layout.InRoom(room, c) || cutCells.Contains(c)) continue;
                var plate = Instantiate(data.spikeTrapPrefab, Cell(c), Quaternion.Euler(0, 90 * ((dx + dz) & 3), 0), transform);
                var spikes = plate.GetComponent<DungeonSpikeTrap>();
                if (spikes != null) { spikes.playerDamage = f.spikeDamage; trap.traps.Add(spikes); }
                taken.Add(c); furnished.Add(c);
            }
        return taken;
    }

    // ── Ambushes ─────────────────────────────────────────────────────

    void SetUpAmbush(int index)
    {
        var rng = new System.Random(Scramble(unchecked(seed0 + 99991 * (index + 1))));
        var root = new GameObject($"Ambush {index}");
        root.transform.SetParent(transform, false);
        root.transform.position = Cell(Layout.RoomCenter(index));
        var ambush = root.AddComponent<DungeonAmbush>();
        ambush.dungeon = this; ambush.room = index;
        ambush.spawnPuff = data.ambushSpawnPuff; ambush.slamAudio = data.ambushSlamAudio; ambush.liftAudio = data.ambushLiftAudio; ambush.spawnAudio = data.ambushSpawnAudio;
        // A gate across every way out, just inside the room.
        foreach (var cell in Layout.RoomCells(index))
            foreach (var d in DungeonLayout.Neighbours)
            {
                var n = cell + d;
                if (!Open(n) || Layout.InRoom(index, n)) continue;
                var outward = new Vector3(d.x, 0f, d.y);
                var edge = Cell(cell) + outward * (data.cellSize * .5f);
                ambush.doorways.Add(edge);
                var gate = Instantiate(data.ambushGatePrefab, edge - outward * .32f, Quaternion.LookRotation(outward), root.transform);
                ambush.gates.Add(gate.transform);
            }
        ambush.OpenGates();
        // Where they come from: the room's own Enemy sockets, then free floor away from the ways in.
        if (ambushPoints.TryGetValue(index, out var points)) ambush.spawnPoints.AddRange(points);
        var cells = new List<Vector2Int>(Layout.RoomCells(index));
        for (int i = cells.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (cells[i], cells[j]) = (cells[j], cells[i]); }
        foreach (var c in cells)
        {
            if (ambush.spawnPoints.Count >= 8) break;
            var p = Cell(c);
            bool nearDoor = false;
            foreach (var e in ambush.doorways) if ((e - p).sqrMagnitude < 9f) { nearDoor = true; break; }
            if (nearDoor || cutCells.Contains(c) || !UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, .8f, UnityEngine.AI.NavMesh.AllAreas)) continue;
            ambush.spawnPoints.Add(hit.position);
        }
        int lo = MinEnemiesPerRoom, hi = Mathf.Clamp(MaxEnemiesPerRoom, lo, 6);
        int first = rng.Next(lo, hi + 1), second = rng.Next(Mathf.Max(1, lo - 1), Mathf.Max(1, hi - 1) + 1);
        for (int i = 0; i < first; i++) { var e = ChooseEnemy(index, rng); if (e != null) ambush.waveOne.Add(e); }
        for (int i = 0; i < second; i++) { var e = ChooseEnemy(index, rng); if (e != null) ambush.waveTwo.Add(e); }
        ambush.chestAt = Cell(Layout.RoomCenter(index));
        var toDoor = ambush.doorways.Count > 0 ? ambush.doorways[0] - ambush.chestAt : Vector3.forward; toDoor.y = 0f;
        ambush.chestFacing = Quaternion.LookRotation(toDoor.sqrMagnitude > .01f ? toDoor : Vector3.forward);
        ambush.chestSeed = rng.Next();
    }

    // The ambush's reward: the better of two chest tiers, out of a puff of dust.
    public DungeonContainer SpawnRewardChest(Vector3 pos, Quaternion facing, int seed)
    {
        var rng = new System.Random(seed);
        GameObject prefab = null;
        if (data.chestTiers != null && data.chestTiers.Length > 0)
        {
            var a = DungeonWeightedPrefab.Choose(data.chestTiers, rng); var b = DungeonWeightedPrefab.Choose(data.chestTiers, rng);
            prefab = Rarer(a, b);
        }
        if (prefab == null) prefab = data.chestPrefab;
        if (prefab == null) return null;
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
        var go = Instantiate(prefab, pos, facing, transform);
        if (data.ambushSpawnPuff != null) PoolManager.SpawnOrInstantiate(data.ambushSpawnPuff, pos, Quaternion.identity, 3f);
        var chest = go.GetComponent<DungeonContainer>();
        if (chest == null) return null;
        chest.loot = FloorLoot; chest.seed = rng.Next(); chest.floorNumber = FloorNumber;
        if (chest.openAudio == null) chest.openAudio = data.chestOpenAudio;
        return chest;
    }

    // The boss chest (DungeonMilestone.rewardChest): where the guardian fell, out of a puff of dust, facing the player,
    // holding the boss's reward and the milestone's Chest Items.
    public DungeonContainer SpawnBossChest(Vector3 pos, Vector3 face)
    {
        var m = Milestone;
        if (m == null || !m.rewardChest) return null;
        var prefab = m.chestPrefab;
        if (prefab == null && data.chestTiers != null)
            foreach (var t in data.chestTiers) if (t != null && t.prefab != null) prefab = prefab == null ? t.prefab : Rarer(prefab, t.prefab);
        if (prefab == null) prefab = data.chestPrefab;
        if (prefab == null) return null;
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
        var look = face - pos; look.y = 0;
        var go = Instantiate(prefab, pos, Quaternion.LookRotation(look.sqrMagnitude > .01f ? look : Vector3.forward), transform);
        if (data.ambushSpawnPuff != null) PoolManager.SpawnOrInstantiate(data.ambushSpawnPuff, pos, Quaternion.identity, 3f);
        var chest = go.GetComponent<DungeonContainer>();
        if (chest == null) return null;
        chest.loot = m.bossLoot != null ? m.bossLoot : FloorLoot;
        chest.seed = new System.Random(FloorNumber * 7919 + 17).Next(); chest.floorNumber = FloorNumber;
        chest.guaranteedItems = new List<ItemData>(m.chestItems ?? new List<ItemData>());
        chest.bonusGold = m.bonusGold;
        if (chest.openAudio == null) chest.openAudio = data.chestOpenAudio;
        return chest;
    }

    GameObject Rarer(GameObject a, GameObject b)
    {
        float Weight(GameObject p) { foreach (var t in data.chestTiers) if (t != null && t.prefab == p) return t.weight; return float.MaxValue; }
        return a == null ? b : b == null ? a : Weight(b) < Weight(a) ? b : a;
    }

    // An archer for a ledge socket: the room's pick among the biome's archers, or any enemy when there are none.
    GameObject ChooseArcher(int room, System.Random rng)
    {
        if (Biome != null && Biome.enemies != null)
        {
            var archers = new List<DungeonBiome.Spawn>();
            foreach (var s in Biome.enemies)
                if (s != null && s.enemy != null && FloorInBiome >= s.fromFloor && s.enemy.TryGetComponent<Character>(out var c) && c.data is EnemyData { archer: true }) archers.Add(s);
            var pick = WeightedPick.Choose(archers, s => s.weight, rng);
            if (pick != null) return pick.enemy;
        }
        return ChooseEnemy(room, rng);
    }

    // After every enemy is placed: packs to sleep, ambushes set.
    void FinishRoomEvents()
    {
        if (roomEvents == null) return;
        foreach (var pair in sleepingPacks)
        {
            var centre = Layout.RoomCenter(pair.Key);
            EnemySleep.SleepPack(pair.Value, Cell(centre).y + HeightAt(centre.x, centre.y) - .55f, data.sleepAudio);
        }
        for (int i = 0; i < roomEvents.Length; i++) if (roomEvents[i] == RoomEvent.Ambush) SetUpAmbush(i);
    }

    // A spawned enemy of a sleeping-pack room joins the pack.
    void JoinPack(int room, Character enemy)
    {
        if (enemy == null || EventOf(room) != RoomEvent.SleepingPack) return;
        if (!sleepingPacks.TryGetValue(room, out var list)) sleepingPacks[room] = list = new List<Character>();
        list.Add(enemy);
    }
}
