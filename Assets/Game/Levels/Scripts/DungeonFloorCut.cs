using UnityEngine;

// A hole in a hand-built room's floor, in whole cells centred on this object: a Pit (a deep chasm; a fall hurts and
// puts you back at the edge, an enemy knocked over the edge goes down for good) or a Sunken floor (a lower court; add
// step blocks inside it so it can be walked into). The generator takes out the floor tiles, lines the hole with the
// room's wall stone and lays a floor at the bottom (DungeonGenerator.Cuts). Bridges are kit blocks laid across it.
public class DungeonFloorCut : MonoBehaviour
{
    public enum Kind { Pit = 0, Sunken = 1 }
    public Kind kind = Kind.Pit;
    [Tooltip("Cells across (x) and deep (z), centred on this object: an odd count sits on a cell centre, an even one on a cell edge.")]
    public Vector2Int cells = new(2, 2);
    [Min(.2f), Tooltip("Metres below the room's floor.")]
    public float depth = 3.5f;
    [Range(0f, 1f), Tooltip("Pit: share of the player's health a fall takes.")]
    public float fallDamage = .25f;

    // How far past an enemy, along a knockback, the pit's edge counts as reached.
    const float EdgeReach = 1.05f;

    public DungeonRoomFeature Feature => kind == Kind.Pit ? DungeonRoomFeature.Pit : DungeonRoomFeature.SunkenFloor;

    // The world points at the centres of the cut's cells (turned with the room).
    public System.Collections.Generic.IEnumerable<Vector3> CellCentres(float cellSize)
    {
        for (int x = 0; x < cells.x; x++)
            for (int z = 0; z < cells.y; z++)
                yield return transform.TransformPoint(new Vector3((x - (cells.x - 1) * .5f) * cellSize, 0f, (z - (cells.y - 1) * .5f) * cellSize));
    }

    // ── At run time (pits) ─────────────────────────────────────────────

    DungeonGenerator dungeon;
    System.Collections.Generic.HashSet<Vector2Int> holes;
    float floorY, fallDrop = 1.2f;
    Vector3 lastSafe;
    bool haveSafe;

    // fallDrop: how far below the floor counts as fallen in (a board's pit is only as deep as the tiles are high).
    public void Initialize(DungeonGenerator generator, System.Collections.Generic.HashSet<Vector2Int> cutCells, float roomFloorY, float fallDrop = 1.2f)
    {
        dungeon = generator; holes = cutCells; floorY = roomFloorY; this.fallDrop = fallDrop;
        enabled = kind == Kind.Pit;
    }

    bool Over(Vector3 p) => holes != null && holes.Contains(dungeon.CellOf(p));

    void Update()
    {
        if (dungeon == null || holes == null) return;
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if (player != null && player.isActiveAndEnabled && player.IsAlive)
        {
            var p = player.transform.position;
            bool over = Over(p);
            if (!over && player.Motor != null && player.Motor.IsGrounded && Mathf.Abs(p.y - floorY) < .6f) { lastSafe = p; haveSafe = true; }
            if (over && p.y < floorY - fallDrop) Fall(player);
        }
        // An enemy knocked back towards the edge goes down: its path keeps it about its own width from the drop, so the
        // edge is looked for a little further on than its body reaches.
        foreach (var brain in EnemyBrain.Active)
        {
            if (brain == null || !brain.Character.IsAlive || brain.Motor == null || !brain.Motor.IsKnockedBack) continue;
            if (brain.Motor.KnockbackVelocity.sqrMagnitude < 1f) continue;
            var p = brain.transform.position + brain.Motor.KnockbackVelocity.normalized * EdgeReach;
            if (Mathf.Abs(brain.transform.position.y - floorY) > .8f || !Over(p)) continue;
            Push(brain);
        }
    }

    void Fall(Player player)
    {
        var info = new DamageInfo { Amount = Mathf.Max(1f, player.Stats.MaxHealth * fallDamage), SourceName = "a fall", Magic = true, HitPoint = player.transform.position };
        player.Stats.TakeDamage(info);
        if (!player.IsAlive) return;
        var back = haveSafe ? lastSafe : player.transform.position + Vector3.up * (floorY - player.transform.position.y + .1f);
        player.Warp(back + Vector3.up * .05f);
        MessageLog.Post("You fall into the pit and climb out, hurt.", MessageKind.Warning);
    }

    void Push(EnemyBrain brain)
    {
        var dir = brain.Motor.KnockbackVelocity; dir.y = 0f;
        var info = new DamageInfo
        {
            Amount = brain.Character.Stats.MaxHealth * 10f, SourceName = "the pit", Magic = true,
            Source = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null,
            HitPoint = brain.transform.position + Vector3.up, Direction = dir.normalized, SwingDirection = dir.normalized + Vector3.down * .4f,
        };
        brain.Character.Stats.TakeDamage(info);
        MessageLog.Post($"The {brain.Character.DisplayName} falls into the pit.", MessageKind.Combat);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = kind == Kind.Pit ? new Color(.05f, .05f, .08f, .8f) : new Color(.4f, .55f, .8f, .45f);
        float c = 2f;
        foreach (var p in CellCentres(c)) Gizmos.DrawCube(p - Vector3.up * depth * .5f, new Vector3(c * .98f, depth, c * .98f));
    }
}
