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
    [TextArea, Tooltip("Posted as the fight starts. {boss} is the boss's name.")] public string introMessage = "{boss} guards the stairs down.";
    public string Intro(Character boss, string name = null) => introMessage.Trim().Replace("{boss}", !string.IsNullOrWhiteSpace(name) ? name : boss != null ? boss.TheNameCap : "The guardian");
    public AudioClip introSting;
    [Range(0, 1)] public float stingVolume = 1f;
    public AudioClip bossMusic;
    [Range(0, 1)] public float bossMusicVolume = 1f;
    [Tooltip("The stairs stay sealed until the boss dies.")]
    public bool sealExit = true;
    [Tooltip("Boss reward table: rolled in the boss chest (or dropped by the boss when there is no chest). Empty uses the floor's loot (chest) or the boss prefab's own (body).")]
    public LootSource bossLoot;
    [Min(0), Tooltip("Gold on top of the reward, in the chest (or on the boss's body).")] public int bonusGold = 60;
    [Title("Boss chest", HorizontalLine = false)]
    [Tooltip("A chest appears where the boss falls, holding the boss's reward (Boss Loot, Bonus Gold) and the Chest Items. Off: the reward drops from the boss's body.")]
    public bool rewardChest = true;
    [ShowIf(nameof(rewardChest)), AssetsOnly, Tooltip("The chest. Empty: the level's rarest chest tier (the gold chest).")]
    public GameObject chestPrefab;
    [ShowIf(nameof(rewardChest)), AssetsOnly, Tooltip("Always in the chest, one of each (list an item twice for two), on top of the rolled reward.")]
    public System.Collections.Generic.List<ItemData> chestItems = new();
    [Min(1), Tooltip("Exit room is enlarged by at least this factor to fit the arena.")]
    public float arenaSizeScale = 1.5f;
    [AssetsOnly, Tooltip("The fight's script: music and beat, how the DM sets the boss down, his beam, timed events and his comments. Used when the arena is a stage (a template with a DungeonStage), which is always built at the edge in front of the Dungeon Master.")]
    public BossFight fight;
}
