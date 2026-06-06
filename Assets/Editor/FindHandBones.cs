using UnityEngine;
using UnityEditor;

public class FindHandBones
{
    [MenuItem("Tools/Find Table Hand Bones")]
    public static void Find()
    {
        var root = GameObject.Find("TableArmsMesh");
        if (root == null) { Debug.Log("TableArmsMesh NOT FOUND"); return; }
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.ToLower().Contains("hand"))
                Debug.Log($"{t.name} | ID: {t.gameObject.GetInstanceID()}");
        }
    }
}
