using UnityEditor;
using UnityEngine;

// The Arena as one map template (Levels/Arena/Arena map.prefab): the landing (room 0, furnished by the generator) and the
// pit (room 1, the Arena pit prefab nested under its Room child). Made again by every Build: edit the pit prefab, or
// the map in Prefab Mode and stop rebuilding.
public static partial class ArenaSetup
{
    const string MapPath = Dir + "/Arena map.prefab";

    static DungeonMapTemplate MapTemplate(string[] rows, GameObject pit, TableLevelData level)
    {
        var root = new GameObject("Arena map");
        try
        {
            var map = root.AddComponent<DungeonMapTemplate>();
            map.width = rows[0].Length; map.depth = rows.Length; map.rows = (string[])rows.Clone();
            map.previewLevel = level;
            map.SyncRooms();   // a Room child per painted room, on its centre cell
            var landing = map.RoomChild(0);
            landing.name = "Room 0 Landing"; landing.role = DungeonGenerator.RoomRole.Entrance; landing.generated = true;
            var room = map.RoomChild(1);
            room.name = "Room 1 Pit"; room.role = DungeonGenerator.RoomRole.Exit;
            // The pit's own template is the room's: its settings go on the Room child, its contents under it.
            Object.DestroyImmediate(room.GetComponent<DungeonRoomTemplate>());
            var own = pit.GetComponent<DungeonRoomTemplate>();
            var copy = room.gameObject.AddComponent<DungeonRoomTemplate>();
            EditorUtility.CopySerialized(own, copy);
            copy.paintFootprint = false; copy.footprint = null;   // the map paints the room; its own outline would preview twice
            var inner = (GameObject)PrefabUtility.InstantiatePrefab(pit, room.transform);
            inner.transform.localPosition = Vector3.zero; inner.transform.localRotation = Quaternion.identity;
            // The nested pit keeps its contents; its own template component would be a second room template.
            foreach (var t in inner.GetComponents<DungeonRoomTemplate>()) Object.DestroyImmediate(t);
            foreach (var f in inner.GetComponents<ArenaFight>()) { var moved = room.gameObject.AddComponent<ArenaFight>(); EditorUtility.CopySerialized(f, moved); Object.DestroyImmediate(f); }
            var saved = PrefabUtility.SaveAsPrefabAsset(root, MapPath);
            return saved.GetComponent<DungeonMapTemplate>();
        }
        finally { Object.DestroyImmediate(root); }
    }
}
