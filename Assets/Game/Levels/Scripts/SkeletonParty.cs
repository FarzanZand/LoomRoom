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
    public DungeonMaster.Line enterLine = new() { text = "A party. Don't spoil it." };
    public DungeonMaster.Line spoiledLine = new() { text = "You had to ruin it, didn't you." };
    public DungeonMaster.Line chestLine = new() { text = "Greedy. They noticed." };

    DungeonGenerator dungeon;
    int room = -1;
    readonly List<EnemyBrain> dancers = new();
    readonly List<PlayableGraph> graphs = new();
    bool entered, spoiled;

    void Start()
    {
        dungeon = GetComponentInParent<DungeonGenerator>();
        if (dungeon == null || dungeon.Layout == null) return;
        room = dungeon.Layout.RoomAt(dungeon.CellOf(transform.position));
        if (room < 0) return;
        var centre = transform.position;
        int i = 0;
        foreach (var brain in dungeon.GetComponentsInChildren<EnemyBrain>())
        {
            if (brain == null || !brain.Character.IsAlive || dungeon.Layout.RoomAt(dungeon.CellOf(brain.transform.position)) != room) continue;
            brain.Entranced = true;
            dancers.Add(brain);
            brain.Character.Damaged += _ => Spoil(spoiledLine);
            // Facing the middle of the room, each a little out of step with the others.
            var toMiddle = centre - brain.transform.position; toMiddle.y = 0f;
            if (toMiddle.sqrMagnitude > .01f) brain.transform.rotation = Quaternion.LookRotation(toMiddle);
            if (danceClip != null && brain.Character.Animator != null)
            {
                var playable = AnimationPlayableUtilities.PlayClip(brain.Character.Animator, danceClip, out var graph);
                playable.SetSpeed(danceSpeed * (.9f + .05f * (i % 5)));
                playable.SetTime(danceClip.length * (i * .37f % 1f));
                graphs.Add(graph);
            }
            i++;
        }
        foreach (var chest in dungeon.GetComponentsInChildren<DungeonContainer>())
            if (chest != null && dungeon.Layout.RoomAt(dungeon.CellOf(chest.transform.position)) == room) chest.Opened += _ => Spoil(chestLine);
    }

    void Update()
    {
        if (entered || spoiled || room < 0 || dungeon == null || !PlayerManager.HasInstance) return;
        var player = PlayerManager.Instance.ActiveKind == PlayerKind.Table ? PlayerManager.Instance.Active : null;
        if (player == null || dungeon.Layout.RoomAt(dungeon.CellOf(player.transform.position)) != room) return;
        entered = true;
        if (enterLine != null && !string.IsNullOrEmpty(enterLine.text)) DungeonMaster.Say(enterLine);
    }

    // The music stops mattering: every dancer still standing comes for the player.
    void Spoil(DungeonMaster.Line line)
    {
        if (spoiled) return;
        bool anyone = false;
        foreach (var d in dancers) if (d != null && d.Character.IsAlive) anyone = true;
        if (!anyone) return;
        spoiled = true;
        StopDancing();
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        var at = player != null ? player.transform.position : transform.position;
        foreach (var d in dancers) if (d != null && d.Character.IsAlive) d.Provoke(at);
        if (line != null && !string.IsNullOrEmpty(line.text)) DungeonMaster.Say(line);
        var music = GetComponent<RoomMusic>();
        if (music != null) music.enabled = false;
    }

    void StopDancing()
    {
        foreach (var g in graphs) if (g.IsValid()) g.Destroy();
        graphs.Clear();
    }

    void OnDestroy() => StopDancing();
}
