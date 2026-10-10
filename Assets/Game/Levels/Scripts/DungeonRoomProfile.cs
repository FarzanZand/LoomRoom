using UnityEngine;

[CreateAssetMenu(menuName="Table/Room profile",fileName="Room profile")]
public class DungeonRoomProfile : ScriptableObject
{
    public DungeonGenerator.RoomRole role=DungeonGenerator.RoomRole.Combat;
    [Min(0)] public float weight=1;
    [Min(1)] public int minFloor=1;
    [Tooltip("Zero permits all later floors.")] [Min(0)] public int maxFloor;
    [Header("Room architecture")]
    [Sirenix.OdinInspector.InlineEditor, Tooltip("Force this room style. Empty keeps normal weighted level style selection.")]
    public DungeonRoomStyle styleOverride;
    [Range(.5f, 3f), Tooltip("Multiplier for floor width and depth, not height or prop scale. 1 keeps normal size. Rounded to grid cells and constrained by neighboring rooms and table bounds.")]
    public float sizeScale = 1;
    [Sirenix.OdinInspector.AssetsOnly, Tooltip("Optional hand-built interior (a prefab with DungeonRoomTemplate). Empty generates the room as usual.")]
    public GameObject roomTemplate;
    [Tooltip("Grown layouts: shapes for this room. Empty uses the level's and the biome's together. A template with a footprint overrides this.")]
    public DungeonShapeChoice[] shapes = new DungeonShapeChoice[0];
    [Tooltip("Leave empty to inherit level enemies.")] public GameObject[] enemies;
    [Tooltip("Overrides rewards for enemies and containers in this room only.")] public LootSource rewards;
    [Tooltip("Empty uses the level props. Models should fit within a cell; reserved routes are never used.")] public GameObject[] props;
    [Range(0,8)] public int minProps=1,maxProps=3;
    [Tooltip("Furnishing rules for this room. Replace the level's and the biome's rules and props when set.")]
    public DungeonPropRule[] propRules = new DungeonPropRule[0];
    [Tooltip("Recolour the wall lights in this room. A room template's or a socket's own colour still wins.")]
    public bool overrideLight;
    public Color lightColor=new Color(1,.75f,.4f);
}
