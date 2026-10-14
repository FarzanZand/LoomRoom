using UnityEngine;

// A boss stage: put on a DungeonRoomTemplate used as a biome guardian's arena (DungeonMilestone.arena). The generator
// keeps the stage at the edge of the table in front of the Dungeon Master, builds it without walls or a ceiling as a
// raised platform with a stair up from its doorway (and invisible edges, so nobody walks off), lifts everything in the
// template onto the platform and runs the fight with BossStageEncounter and the milestone's BossFight. The exit stairs
// appear on the stage when the boss falls.
public class DungeonStage : MonoBehaviour
{
    [Min(.3f), Tooltip("Metres from the dungeon floor up to the top of the stage.")]
    public float height = 2.4f;
    [Min(1), Tooltip("Cells the stair runs in from the doorway.")]
    public int stairCells = 3;
    [Min(.1f), Tooltip("Rise of one step, metres.")]
    public float stepRise = .2f;
    [Min(.5f), Tooltip("Height of the invisible edge round the stage, above its top.")]
    public float edgeHeight = 3f;
    [Tooltip("Stone for the stage's sides and the steps. Empty uses the level's trim.")]
    public Material sideMaterial;
    [Min(.5f), Tooltip("The fight begins when the player comes this close (metres) to the middle of the stage.")]
    public float startRadius = 2f;
    [Tooltip("Where the Dungeon Master sets the boss down. Empty: between the middle and his side of the stage.")]
    public Transform bossSpot;
    [Tooltip("Where the exit stairs appear once the boss falls. Empty: the middle of the stage.")]
    public Transform exitSpot;

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(.9f, .5f, .2f, .6f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * height * .5f, new Vector3(6f, height, 6f));
        Gizmos.color = new Color(1f, .2f, .2f, .5f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * height, startRadius);
    }
}
