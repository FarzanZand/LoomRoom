using UnityEngine;

[CreateAssetMenu(menuName="Table/Room profile",fileName="Room profile")]
public class DungeonRoomProfile : ScriptableObject
{
    public DungeonGenerator.RoomRole role=DungeonGenerator.RoomRole.Combat;
    [Min(0)] public float weight=1;
    [Min(1)] public int minFloor=1;
    [Tooltip("Zero permits all later floors.")] [Min(0)] public int maxFloor;
    [Tooltip("Leave empty to inherit level enemies.")] public GameObject[] enemies;
    [Tooltip("Overrides rewards for enemies and containers in this room only.")] public DungeonLootTable rewards;
    [Tooltip("Empty uses the level props. Models should fit within a cell; reserved routes are never used.")] public GameObject[] props;
    [Range(0,8)] public int minProps=1,maxProps=3;
    public bool overrideLight;
    public Color lightColor=new Color(1,.75f,.4f);
    [Min(0)] public float lightIntensity=5;
    [Min(1)] public float lightRange=20;
}
