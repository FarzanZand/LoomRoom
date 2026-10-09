using UnityEngine;

// Which face the Dungeon Master wears. Each face is a Meshy head under the head bone, fitted inside the hood
// (the body mesh has its Synty face removed); only the picked one is active.
public enum DungeonMasterFaceKind { Sunken = 0, Crinkled = 1, Elder = 2, Tired = 3, GameMaster = 4 }

public class DungeonMasterFace : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public DungeonMasterFaceKind kind;
        public GameObject face;
    }

    public DungeonMasterFaceKind face = DungeonMasterFaceKind.Sunken;
    public Entry[] faces = new Entry[0];

    void Awake() => Apply();

#if UNITY_EDITOR
    // SetActive is not allowed inside OnValidate, so the swap waits a frame. Prefab assets are left alone.
    void OnValidate()
    {
        if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(this)) return;
        UnityEditor.EditorApplication.delayCall += () => { if (this != null) Apply(); };
    }
#endif

    public void Apply()
    {
        foreach (var entry in faces)
        {
            if (entry == null || entry.face == null) continue;
            bool on = entry.kind == face;
            if (entry.face.activeSelf != on) entry.face.SetActive(on);
        }
    }
}
