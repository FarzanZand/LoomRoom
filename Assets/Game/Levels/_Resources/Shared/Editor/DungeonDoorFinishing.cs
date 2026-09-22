using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Explicit migration only; importing scripts never overwrites authored door styling.
[InitializeOnLoad]
public static class DungeonDoorFinishing
{
    static DungeonDoorFinishing() => EditorApplication.update += Tick;
    static void Tick()
    {
        const string request = "Temp/DungeonDoorFinish.request";
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        File.Delete(request);
        var mode = EditorSettings.serializationMode;
        EditorSettings.serializationMode = SerializationMode.ForceText;
        GameObject root = null;
        try
        {
            const string path = "Assets/Game/Levels/Dungeon1/Prefabs/Dungeon door.prefab";
            var level = AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
            root = PrefabUtility.LoadPrefabContents(path);
            var door = root.GetComponent<DungeonDoor>();
            if (door.lintel == null)
            {
                var header = GameObject.CreatePrimitive(PrimitiveType.Cube);
                header.name = "Doorway masonry lintel";
                header.transform.SetParent(root.transform, false);
                header.transform.localScale = new Vector3(2.08f, .45f, .26f);
                header.GetComponent<Renderer>().sharedMaterial = level.wallMaterial;
                door.lintel = header.transform;
                door.FitCeiling(3.2f);
            }
            // Save a cropped-UV mesh for the prefab preview as well as runtime fitting.
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.lintelSourceMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(primitive);
            var mesh = UnityEngine.Object.Instantiate(door.lintelSourceMesh);
            var uv = mesh.uv; var normals = mesh.normals;
            float bottom = door.lintel.localPosition.y - door.lintel.localScale.y * .5f;
            for (int i = 0; i < uv.Length; i++)
            {
                if (Mathf.Abs(normals[i].y) > .5f) continue;
                uv[i].x *= Mathf.Abs(normals[i].z) > .5f ? door.lintel.localScale.x / 2f : door.lintel.localScale.z / 2f;
                uv[i].y = (bottom + uv[i].y * door.lintel.localScale.y) / 3.2f;
            }
            mesh.uv = uv; mesh.name = "Doorway lintel";
            const string meshPath = "Assets/Game/Levels/Dungeon1/Prefabs/Doorway lintel.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing == null) AssetDatabase.CreateAsset(mesh, meshPath);
            else { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
            door.lintel.GetComponent<MeshFilter>().sharedMesh = mesh;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/DungeonDoorFinish.report", "PASS: authored stationary masonry lintel saved in Dungeon door.prefab.");
        }
        catch (Exception ex) { File.WriteAllText("Temp/DungeonDoorFinish.report", ex.ToString()); Debug.LogException(ex); }
        finally
        {
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
            EditorSettings.serializationMode = mode;
        }
    }
}
