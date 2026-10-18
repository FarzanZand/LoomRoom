using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// The Skeleton Party room (on its template's root, with RoomMusic for the party's track): every enemy in the room
// dances (danceClip, played straight on its animator) and ignores the player, until someone hits one of them or opens
// the chest in the middle; then the whole party turns on the player. The Dungeon Master has a word for each moment.
public class SkeletonParty : MonoBehaviour
{
    [Tooltip("The dance (any humanoid or rig clip the dancers can play). Empty: they stand where they are.")]
    public AnimationClip danceClip;
    [Range(.5f, 2f), Tooltip("Each dancer's speed varies a little around this, so they aren't in lockstep.")]
    public float danceSpeed = 1f;
    public DungeonMaster.Line spoiledLine = new() { text = "You had to ruin it, didn't you." };
    public DungeonMaster.Line chestLine = new() { text = "Greedy. They noticed." };

    DungeonGenerator dungeon;
    int room = -1;
    readonly List<EnemyBrain> dancers = new();
    readonly List<PlayableGraph> graphs = new();
    bool spoiled;

    readonly HashSet<EnemyBrain> seen = new();
    readonly HashSet<DungeonContainer> chests = new();
    float nextGather;

    void Start()
    {
        dungeon = GetComponentInParent<DungeonGenerator>();
        if (dungeon == null || dungeon.Layout == null) return;
        room = dungeon.Layout.RoomAt(dungeon.CellOf(transform.position));
        Gather();
    }

    // Everyone in the room joins the dance; checked again for a while, since bodies set up their health after this
    // starts (and a late spawn should dance too).
    void Gather()
    {
        if (room < 0 || spoiled) return;
        foreach (var brain in dungeon.GetComponentsInChildren<EnemyBrain>())
        {
            if (brain == null || seen.Contains(brain) || brain.State == EnemyState.Dead || dungeon.Layout.RoomAt(dungeon.CellOf(brain.transform.position)) != room) continue;
            seen.Add(brain);
            brain.Entranced = true;
            dancers.Add(brain);
            brain.Character.Damaged += _ => Spoil(spoiledLine);
            // Facing the middle of the room, each a little out of step with the others.
            var toMiddle = transform.position - brain.transform.position; toMiddle.y = 0f;
            if (toMiddle.sqrMagnitude > .01f) brain.transform.rotation = Quaternion.LookRotation(toMiddle);
            if (danceClip != null && brain.Character.Animator != null)
            {
                int i = dancers.Count - 1;
                var playable = AnimationPlayableUtilities.PlayClip(brain.Character.Animator, danceClip, out var graph);
                playable.SetSpeed(danceSpeed * (.9f + .05f * (i % 5)));
                playable.SetTime(danceClip.length * (i * .37f % 1f));
                graphs.Add(graph);
            }
        }
        foreach (var chest in dungeon.GetComponentsInChildren<DungeonContainer>())
            if (chest != null && !chests.Contains(chest) && dungeon.Layout.RoomAt(dungeon.CellOf(chest.transform.position)) == room)
            {
                chests.Add(chest); chest.Opened += _ => Spoil(chestLine);
                // Its front (the lid's opening) toward the player, who comes in from where they are now: the portal's
                // side room in a pocket, the way they will walk in on a floor.
                var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
                if (player != null)
                {
                    var toPlayer = player.transform.position - chest.transform.position; toPlayer.y = 0f;
                    if (toPlayer.sqrMagnitude > .01f) chest.transform.rotation = Quaternion.LookRotation(SnapToAxis(toPlayer));
                }
            }
    }

    // The nearest of the four room axes, so the chest sits square to the walls.
    static Vector3 SnapToAxis(Vector3 v) => Mathf.Abs(v.x) >= Mathf.Abs(v.z) ? new Vector3(Mathf.Sign(v.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));

    void Update()
    {
        if (room < 0 || dungeon == null || spoiled) return;
        if (Time.time >= nextGather) { nextGather = Time.time + .5f; Gather(); }
    }

    // The music stops mattering: every dancer still standing comes for the player.
    void Spoil(DungeonMaster.Line line)
    {
        if (spoiled) return;
        // The dance ends either way (a last dancer killed in one blow must not dance on as a body).
        spoiled = true;
        StopDancing();
        var music = GetComponent<RoomMusic>();
        if (music != null) music.enabled = false;
        bool anyone = false;
        foreach (var d in dancers) if (d != null && d.Character.IsAlive) anyone = true;
        if (!anyone) return;
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        var at = player != null ? player.transform.position : transform.position;
        foreach (var d in dancers) if (d != null && d.Character.IsAlive) d.Provoke(at);
        if (line != null && !string.IsNullOrEmpty(line.text)) DungeonMaster.Say(line);
    }

    void StopDancing()
    {
        foreach (var g in graphs) if (g.IsValid()) g.Destroy();
        graphs.Clear();
    }

    void OnDestroy() => StopDancing();
}
