using UnityEngine;
using Sirenix.OdinInspector;

// A biome's guardian: the boss on its last floor, its arena, music and reward.
[System.Serializable]
public class DungeonMilestone
{
    public bool enabled = true;
    [AssetsOnly, Tooltip("Boss enemy prefab. Spawned at the arena's Boss socket, or the exit room centre.")]
    public GameObject boss;
    [Tooltip("Lets an ordinary enemy serve as a guardian: its health and damage are multiplied.")]
    [Min(.1f)] public float healthMultiplier = 1, damageMultiplier = 1;
    [AssetsOnly, Tooltip("Hand-built arena (DungeonRoomTemplate) placed in the exit room. Empty keeps the generated room.")]
    public GameObject arena;
    [Tooltip("Shown under the boss name on the health bar.")]
    public string title = "Guardian";
    [TextArea] public string introMessage = "Something guards the stairs down.";
    public AudioClip introSting;
    [Range(0, 1)] public float stingVolume = 1f;
    public AudioClip bossMusic;
    [Range(0, 1)] public float bossMusicVolume = 1f;
    [Tooltip("The stairs stay sealed until the boss dies.")]
    public bool sealExit = true;
    [Tooltip("Boss reward table. Empty uses the boss prefab's own.")]
    public LootSource bossLoot;
    [Min(0)] public int bonusGold = 60;
    [Min(1), Tooltip("Exit room is enlarged by at least this factor to fit the arena.")]
    public float arenaSizeScale = 1.5f;
}
