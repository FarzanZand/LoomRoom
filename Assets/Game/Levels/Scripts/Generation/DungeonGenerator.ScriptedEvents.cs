using System.Collections.Generic;
using UnityEngine;

// What the scripted event rooms (Levels/Scripts/Events: the duel, the bell, the wager, the sleeping giant, the dice) and
// the Dungeon Master's meddling ask of the floor once it is built: its enemies, chests and ways in, and where the player
// starts when a run opens in an event room.
public partial class DungeonGenerator
{
    // An enemy for a room, as the room's own pack would pick it (the biome's mix for this floor).
    public GameObject PickEnemy(int room, System.Random rng) => ChooseEnemy(room, rng);

    public int RoomOf(Vector3 world) => Layout != null ? Layout.RoomAt(CellOf(world)) : -1;

    // A seed for one room's event: the same for every machine and a reload, a new one each floor.
    public int EventSeed(int room, int salt = 0) => Scramble(unchecked(seed0 + 7919 * (room + 1) + 104729 * salt + 424242));

    public GameObject AmbushGatePrefab => data.ambushGatePrefab;
    public GameObject SpawnPuffPrefab => data.ambushSpawnPuff;
    public AudioClip GateSlamAudio => data.ambushSlamAudio;
    public AudioClip GateLiftAudio => data.ambushLiftAudio;
    public AudioClip SpawnAudio => data.ambushSpawnAudio;
    public LootSource Loot => FloorLoot;

    // The level's chest tiers from the plainest (the most common) to the rarest: 0 wood, 1 iron, 2 gold on Dungeon1.
    public List<GameObject> ChestTiers()
    {
        var list = new List<DungeonWeightedPrefab>();
        if (data.chestTiers != null) foreach (var t in data.chestTiers) if (t != null && t.prefab != null) list.Add(t);
        list.Sort((a, b) => b.weight.CompareTo(a.weight));
        var tiers = new List<GameObject>();
        foreach (var t in list) tiers.Add(t.prefab);
        if (tiers.Count == 0 && data.chestPrefab != null) tiers.Add(data.chestPrefab);
        return tiers;
    }

    // A chest of one tier (clamped to the level's), stocked with the floor's loot from seed, on the floor at pos.
    public DungeonContainer SpawnChest(int tier, Vector3 pos, Quaternion facing, int seed, bool puff = true)
    {
        var tiers = ChestTiers();
        if (tiers.Count == 0) return null;
        var prefab = tiers[Mathf.Clamp(tier, 0, tiers.Count - 1)];
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
        var go = Instantiate(prefab, pos, facing, transform);
        if (puff && data.ambushSpawnPuff != null) PoolManager.SpawnOrInstantiate(data.ambushSpawnPuff, pos, Quaternion.identity, 3f);
        var chest = go.GetComponent<DungeonContainer>();
        if (chest == null) return null;
        chest.loot = FloorLoot; chest.seed = seed; chest.floorNumber = FloorNumber;
        if (chest.openAudio == null) chest.openAudio = data.chestOpenAudio;
        return chest;
    }

    // Every way out of a room: the middle of the opening's edge and the direction out of the room.
    public List<(Vector3 edge, Vector3 outward)> WaysOut(int room)
    {
        var list = new List<(Vector3, Vector3)>();
        if (Layout == null || room < 0) return list;
        foreach (var cell in Layout.RoomCells(room))
            foreach (var d in DungeonLayout.Neighbours)
            {
                if (!Layout.IsOpening(room, cell, d)) continue;
                var outward = new Vector3(d.x, 0f, d.y);
                list.Add((Cell(cell) + outward * (data.cellSize * .5f), outward));
            }
        return list;
    }

    public IReadOnlyList<Vector2Int> CellsOf(int room) => Layout.RoomCells(room);
    public Vector3 RoomCentre(int room) => Cell(Layout.RoomCenter(room));

    // A run opened in an event room (the dev console's event command) starts at the room's Entrance, not in the middle
    // of the event.
    void StartAtEventEntrance()
    {
        foreach (var e in GetComponentsInChildren<ScriptedEventRoom>(true))
        {
            if (e.entrance == null || Layout.RoomAt(CellOf(e.transform.position)) != 0) continue;
            var at = e.entrance.position;
            if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
            SpawnPoint = at + Vector3.up * .12f;
            var look = e.entrance.forward; look.y = 0f;
            if (look.sqrMagnitude > .01f) SpawnRotation = Quaternion.LookRotation(look);
            return;
        }
    }
}
