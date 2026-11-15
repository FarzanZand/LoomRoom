using UnityEditor;
using UnityEngine;

// The double door (DungeonDoubleDoor), made from the dungeon door: DoubleDoorSetup.Build() from an eval, safe to run
// again (the prefab keeps its GUID). Two instances of the single door side by side, the left one hinged on its left,
// the right one on its right with its leaf mirrored, so the handles meet in the middle; each leaf is a little narrower
// than the single door's, so the two never lie in each other (the single leaf is 2 cm wider than its cell each side).
// It is then set as the double door of every level whose door is that single door.
public static class DoubleDoorSetup
{
    const string Single = "Assets/Game/Levels/Shared/Doors/Door 128/Dungeon door 128.prefab";
    const string Double = "Assets/Game/Levels/Shared/Doors/Door 128/Dungeon double door 128.prefab";
    // Each leaf's width as a share of the single door's (2.04 m): 1.99 m, a centimetre clear of the middle and the jamb.
    const float LeafWidth = .975f;

    public static string Build()
    {
        var single = AssetDatabase.LoadAssetAtPath<GameObject>(Single);
        if (single == null) return "No " + Single;
        var root = new GameObject("Dungeon double door 128");
        var pair = root.AddComponent<DungeonDoubleDoor>();
        pair.left = Leaf(single, root.transform, "Left leaf", DungeonDoor.Hinge.Left);
        pair.right = Leaf(single, root.transform, "Right leaf", DungeonDoor.Hinge.Right);
        pair.left.partner = pair.right; pair.right.partner = pair.left;
        var made = PrefabUtility.SaveAsPrefabAsset(root, Double);
        Object.DestroyImmediate(root);

        int levels = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:TableLevelData"))
        {
            var level = AssetDatabase.LoadAssetAtPath<TableLevelData>(AssetDatabase.GUIDToAssetPath(guid));
            if (level == null || level.doorPrefab != single || level.doubleDoorPrefab == made) continue;
            level.doubleDoorPrefab = made; EditorUtility.SetDirty(level); AssetDatabase.SaveAssetIfDirty(level); levels++;
        }
        return $"{Double} made; set on {levels} level(s).";
    }

    static DungeonDoor Leaf(GameObject single, Transform parent, string name, DungeonDoor.Hinge hinge)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(single, parent);
        go.name = name;
        float side = hinge == DungeonDoor.Hinge.Left ? -1f : 1f;
        var door = go.GetComponent<DungeonDoor>();
        // Each leaf fills its own cell: the left one's middle a half door to the left of the double door's.
        go.transform.localPosition = new Vector3(side * door.halfWidth, 0f, 0f);
        door.hinge = hinge;
        var at = door.leaf.localPosition; door.leaf.localPosition = new Vector3(side * door.halfWidth, at.y, at.z);
        at = door.leafMesh.localPosition; door.leafMesh.localPosition = new Vector3(-side * door.halfWidth, at.y, at.z);
        // The single door is made hinged on its left: on the right it is mirrored, so its handle is at the middle too.
        var scale = door.leafMesh.localScale;
        door.leafMesh.localScale = new Vector3(Mathf.Abs(scale.x) * LeafWidth * (side < 0f ? 1f : -1f), scale.y, scale.z);
        return door;
    }
}
