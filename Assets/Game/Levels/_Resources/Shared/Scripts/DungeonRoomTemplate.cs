using Sirenix.OdinInspector;
using UnityEngine;

// A hand-built room interior dropped into a generated room: the generator still builds
// the walls, floor, ceiling and doors in the room's style, then places this prefab at the
// room centre instead of rolling random props and containers.
//
// Author the prefab around its pivot (the room centre). Keep the centre cross and the
// doorways walkable: any child with DungeonTemplatePiece that lands on a walkway is
// removed at placement. Put enemies, chests, breakables, the boss and the merchant on
// DungeonSocket children.
//
// Reference it from a DungeonRoomProfile (Room Template) or a level milestone (Arena).
//
// Grown layouts: give it a Footprint (a painted Room Shape) and the room is built in that
// outline, with this prefab's pivot on the footprint's centre cell. The room may be turned in
// quarter turns (never mirrored); the prefab turns with it.
public class DungeonRoomTemplate : MonoBehaviour
{
    [Tooltip("Grown layouts: the room's outline, pillars and door cells. Empty builds a rectangle of at least Minimum Cells.")]
    public DungeonRoomShape footprint;
    [Tooltip("Smallest room, in grid cells, this interior is built for. Smaller rooms still get it, with a warning.")]
    public Vector2Int minimumCells = new Vector2Int(6, 6);
    [Tooltip("Skip the generator's random props, supply container and features in this room.")]
    public bool replaceGeneratedProps = true;
    [Tooltip("Spawn enemies only at Enemy sockets. Off also rolls the room's normal encounter.")]
    public bool replaceGeneratedEnemies = true;
    [Tooltip("Keep the generator's room light. Off when the template brings its own lights.")]
    public bool keepRoomLight = true;
    [Tooltip("Grid spacing used by the editor gizmo. Match the level's Cell Size.")]
    [Min(.5f)] public float previewCellSize = 2f;

    void OnDrawGizmos()
    {
        float c = previewCellSize;
        if (footprint != null)
        {
            // Pivot sits on the centre cell; draw each painted cell around it.
            var shape = footprint.ToShape();
            Gizmos.matrix = transform.localToWorldMatrix;
            for (int x = 0; x < shape.width; x++) for (int y = 0; y < shape.height; y++)
            {
                byte cell = shape[x, y];
                if (cell == DungeonShape.Empty) continue;
                var p = new Vector3((x - shape.center.x) * c, 0, (y - shape.center.y) * c);
                Gizmos.color = cell == DungeonShape.Solid ? new Color(.2f, .2f, .2f, .8f) : shape.DoorAllowed(x, y) ? new Color(1f, .8f, .3f, .5f) : new Color(.4f, .8f, 1f, .25f);
                if (cell == DungeonShape.Solid) Gizmos.DrawCube(p + Vector3.up * c * .5f, new Vector3(c, c, c));
                else Gizmos.DrawCube(p, new Vector3(c * .96f, .02f, c * .96f));
            }
            return;
        }
        var size = new Vector3(minimumCells.x * c, .05f, minimumCells.y * c);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(.4f, .8f, 1f, .6f);
        Gizmos.DrawWireCube(Vector3.zero, size);
        // The centre cross stays walkable: doorways connect through it.
        Gizmos.color = new Color(1f, .8f, .3f, .25f);
        Gizmos.DrawCube(Vector3.zero, new Vector3(size.x, .02f, c));
        Gizmos.DrawCube(Vector3.zero, new Vector3(c, .02f, size.z));
    }

    [Button("Snap children to grid"), PropertyTooltip("Rounds every socket and piece to the cell grid.")]
    void Snap()
    {
        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            if (child == transform || (child.GetComponent<DungeonSocket>() == null && child.GetComponent<DungeonTemplatePiece>() == null)) continue;
            var p = child.localPosition;
            float half = previewCellSize * .5f;
            float Snap1(float v, int cells) => (cells % 2 == 0 ? Mathf.Round((v - half) / previewCellSize) * previewCellSize + half : Mathf.Round(v / previewCellSize) * previewCellSize);
            int cx = footprint != null ? 1 : minimumCells.x, cz = footprint != null ? 1 : minimumCells.y; // a footprint pivot sits on a cell centre
            child.localPosition = new Vector3(Snap1(p.x, cx), p.y, Snap1(p.z, cz));
        }
    }
}
