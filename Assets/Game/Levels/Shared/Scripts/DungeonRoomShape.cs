using Sirenix.OdinInspector;
using UnityEngine;

// A hand-painted room footprint. Reference it from a level, theme or room profile shape list
// (kind Authored), or as a Room Template's footprint so the template brings its own outline.
// Room templates can also paint their outline in place (DungeonRoomTemplate.paintFootprint).
//
// Cells: '.' outside the room, '#' floor, 'O' solid (pillar, inner wall), '+' floor where a
// corridor may attach, '*' the centre (floor; lights, stairs, enemies and the template pivot).
// With no '+' cells, corridors may attach anywhere along the outline.
[CreateAssetMenu(menuName = "Table/Room Shape", fileName = "Room Shape")]
public class DungeonRoomShape : ScriptableObject
{
    public const int MaxSize = DungeonShapeGrid.MaxSize;
    [Tooltip("The generator may turn the room in quarter turns. Room templates turn with it.")]
    public bool allowRotation = true;
    [Tooltip("The generator may mirror the room. Ignored when the shape is a template's footprint.")]
    public bool allowMirror = true;
    [HideInInspector] public int width = 8, height = 8;
    // rows[0] is the south (bottom) row. Kept as text so shapes diff and paste cleanly.
    [HideInInspector] public string[] rows;

    public char Get(int x, int y) => DungeonShapeGrid.Get(rows, x, y);

    public DungeonShape ToShape() => DungeonShapeGrid.ToShape(width, height, rows, name);

    public DungeonShape ToShape(System.Random random, bool mirrorAllowed) =>
        ToShape().Oriented(random, allowRotation, allowMirror && mirrorAllowed);

    void OnValidate()
    {
        width = Mathf.Clamp(width, 1, MaxSize); height = Mathf.Clamp(height, 1, MaxSize);
        rows = DungeonShapeGrid.Resized(rows, width, height);
    }

#if UNITY_EDITOR
    [OnInspectorGUI, PropertyOrder(10)]
    void Painter()
    {
        UnityEditor.EditorGUILayout.Space();
        DungeonShapeGrid.Painter(this, ref width, ref height, ref rows);
    }
#endif
}
