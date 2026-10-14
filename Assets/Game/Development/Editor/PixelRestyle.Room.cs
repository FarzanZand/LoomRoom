using System.IO;
using UnityEditor;
using UnityEngine;

// Painted restyles for the room's props. Room objects are Synty prefab instances in Room.unity with the restyled mesh as
// an override, scaled up about 20 times (the room is built ~20 units to the metre). The painted pipeline works on
// prefab files, so each prop is first copied into a holder prefab at the scale it stands at in the room
// (World/Prefabs/Bedroom), painted there at the room's density, and the painted meshes and materials are then put
// back on every instance in the scene that uses the same model.
//   MakeRoomHolder(scene path, name) -> RenderPropSheet(holder, name) -> repaint -> BuildPaintedProp(holder, name,
//   RoomTexelsPerUnit, "Room") -> RenderPropTurntable -> ApplyRoomHolder(name, scene root).
public static partial class PixelRestyle
{
    public const string RoomHolderFolder = "Assets/Game/World/Prefabs/Bedroom";
    // 128 texels per real metre at ~20 world units to the metre.
    public const float RoomTexelsPerUnit = 6.4f;

    public static string RoomHolderPath(string name) => $"{RoomHolderFolder}/{name}.prefab";

    // A holder prefab with a copy of the scene object: no position or turn, the scale it has in the room, only its
    // meshes (scripts, lights and colliders are left out).
    // alone: only the object's own mesh, without the things standing on or in it (a bookcase without its books).
    public static string MakeRoomHolder(string scenePath, string name, bool alone = false)
    {
        var source = GameObject.Find(scenePath);
        if (source == null) return $"{name}: no object at {scenePath}";
        Directory.CreateDirectory(RoomHolderFolder);
        // The scale sits on a child: the painting tools measure and frame the prop in the holder root's space.
        var holder = new GameObject(name);
        var copy = Object.Instantiate(source, holder.transform);
        try
        {
            copy.name = source.name;
            copy.transform.localPosition = Vector3.zero; copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = source.transform.lossyScale;
            if (alone) for (int i = copy.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(copy.transform.GetChild(i).gameObject);
            foreach (var c in copy.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer) continue;
                Object.DestroyImmediate(c);
            }
            PrefabUtility.SaveAsPrefabAsset(holder, RoomHolderPath(name));
        }
        finally { Object.DestroyImmediate(holder); }
        return $"{name}: holder saved";
    }

    // Puts the holder's painted meshes and materials on every renderer under the scene root that shows the same model
    // (matched by the part's plain mesh name), as prefab overrides.
    public static string ApplyRoomHolder(string name, string sceneRoot)
    {
        var holder = AssetDatabase.LoadAssetAtPath<GameObject>(RoomHolderPath(name));
        var root = GameObject.Find(sceneRoot);
        if (holder == null || root == null) return $"{name}: holder or scene root missing";
        int n = 0;
        foreach (var part in holder.GetComponentsInChildren<MeshFilter>(true))
        {
            var pr = part.GetComponent<MeshRenderer>(); if (pr == null || part.sharedMesh == null) continue;
            string baseName = part.sharedMesh.name.Replace(" (Synty Pixel)", "").Replace(" painted", "");
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                // A rebuild deletes and recreates mesh assets, so a scene copy may point at a mesh that no longer
                // exists: it is still found through the Synty prefab it is an instance of.
                var mesh = mf.sharedMesh;
                if (mesh == null && PrefabUtility.GetCorrespondingObjectFromOriginalSource(mf) is MeshFilter srcMf) mesh = srcMf.sharedMesh;
                if (mesh == null) continue;
                string other = mesh.name.Replace(" (Synty Pixel)", "").Replace(" painted", "");
                if (other != baseName) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                Undo.RecordObject(mf, "Room restyle"); Undo.RecordObject(mr, "Room restyle");
                mf.sharedMesh = part.sharedMesh; mr.sharedMaterials = pr.sharedMaterials;
                foreach (var mc in mf.GetComponents<MeshCollider>()) { Undo.RecordObject(mc, "Room restyle"); mc.sharedMesh = part.sharedMesh; PrefabUtility.RecordPrefabInstancePropertyModifications(mc); }
                PrefabUtility.RecordPrefabInstancePropertyModifications(mf); PrefabUtility.RecordPrefabInstancePropertyModifications(mr);
                n++;
            }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        return $"{name}: {n} renderers";
    }
}
