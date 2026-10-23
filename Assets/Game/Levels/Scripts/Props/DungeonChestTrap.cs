using System.Collections.Generic;
using UnityEngine;

// Trapped treasure (DungeonRoomFeatures.trappedTreasure): the chest sits on a pressure plate and opening it springs the
// spike plates around it (DungeonSpikeTrap). Added by the generator, or placed by hand next to a chest in a room
// template with its traps listed.
[RequireComponent(typeof(DungeonContainer))]
public class DungeonChestTrap : MonoBehaviour
{
    public List<DungeonSpikeTrap> traps = new();

    DungeonContainer chest;

    void Awake() => chest = GetComponent<DungeonContainer>();
    void OnEnable() => chest.Opened += Spring;
    void OnDisable() => chest.Opened -= Spring;

    void Spring(DungeonContainer _)
    {
        foreach (var t in traps) if (t != null) t.Spring();
    }
}
