using UnityEngine;

// The Dungeon Master watches the game. On a floor with roofless rooms (DungeonBiome.roofsOff) the bedroom stands over
// the player, and he sits in it as a dark shape against the painting: this lights his face from below, from the
// player's side, for as long as the table player is on the floor, so wherever the roof is off he is there over the
// walls, lit like a face over a fire. A room light (it lights only the Room rendering layer: the miniature dungeon
// keeps its own darkness). A boss stage's fight brings its own light on him, so this one stands down for it.
// Added to each floor by DungeonGenerator; it goes with the floor.
public class DungeonMasterWatch : MonoBehaviour
{
    [Tooltip("Strength of the light on his face (the Crypt Lord's fight uses 180 on him).")]
    public float strength = 150f;
    public Color colour = new(1f, .8f, .58f);
    [Min(.01f), Tooltip("Seconds it takes to come up as the player lands on the floor, and to go out.")]
    public float fadeSeconds = 2.5f;

    DungeonGenerator dungeon;
    Light face;
    Transform head;

    public void Initialize()
    {
        dungeon = GetComponent<DungeonGenerator>();
        var hand = FindAnyObjectByType<DungeonMasterHand>();
        var animator = hand != null ? hand.GetComponent<Animator>() : null;
        head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return;
        var go = new GameObject("Dungeon Master watch light");
        go.transform.SetParent(transform, false);
        face = go.AddComponent<Light>();
        face.type = LightType.Spot; face.spotAngle = 55f; face.innerSpotAngle = 27f; face.range = 40f; face.intensity = 0f;
        face.color = colour; face.shadows = LightShadows.None; face.enabled = false;
        FirstPersonLighting.SetLayers(face, LightingManager.RoomLayer);
    }

    void LateUpdate()
    {
        if (face == null) return;
        var player = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table ? PlayerManager.Instance.Active : null;
        var stage = BossStageEncounter.Current;
        bool on = head != null && player != null && dungeon != null && dungeon.RoofsOffRooms.Count > 0 && (stage == null || !stage.Busy);
        face.intensity = Mathf.MoveTowards(face.intensity, on ? strength : 0f, strength * Time.deltaTime / fadeSeconds);
        face.enabled = face.intensity > .01f;
        if (!face.enabled || head == null) return;
        // From below and from the player's side of him, so the lit side of his face is the one the player sees.
        var toward = (player != null ? player.transform.position : transform.position) - head.position;
        toward.y = 0f; toward = toward.sqrMagnitude > .01f ? toward.normalized : Vector3.forward;
        face.transform.position = head.position + toward * 10f - Vector3.up * 8f;
        face.transform.LookAt(head.position);
    }
}
