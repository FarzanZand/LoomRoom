using UnityEngine;

public enum TableLevelKind { Town, Dungeon }

[CreateAssetMenu(menuName = "Table/Level", fileName = "TableLevel")]
public class TableLevelData : ScriptableObject
{
    public string displayName;
    [TextArea] public string description;
    public TableLevelKind kind;
    public SceneMood mood;
    public AudioClip backgroundMusic;
    public bool loopMusic = true;
    [Min(0)] public float musicFadeSeconds = 1.5f;
    public GameObject environmentPrefab;
    [Header("Dungeon generation (world units)")]
    [Range(24, 48)] public int width = 32;
    [Range(24, 56)] public int depth = 42;
    [Min(1)] public float cellSize = 2f;
    [Range(6, 18)] public int roomCount = 12;
    [Tooltip("Zero generates a new seed on each entry.")] public int fixedSeed;
    public Material floorMaterial, wallMaterial, trimMaterial, ceilingMaterial, woodMaterial, metalMaterial;
    public GameObject[] enemies;
    public DungeonLootTable loot;
    public ItemData[] startingItems;
    public ItemData[] startingEquipment;
    public ItemData guaranteedHealing;
    public AudioClip containerBreakAudio, chestOpenAudio;
    [Header("Dungeon HUD")]
    public TMPro.TMP_FontAsset hudFont;
    public Sprite hotbarFrame;
}
