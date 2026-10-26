using UnityEngine;

// Which head the tavern keeper wears. Human is the Synty merchant's own head; every other is a Meshy head under the
// head bone, its neck down inside his collar, on a body with the Synty head cut away. Only the picked head is active.
public enum KeeperFaceKind { Human = 0, Gecko = 1, Hobgoblin = 2, Minotaur = 3, Ogre = 4, Ratfolk = 5 }

public class KeeperFace : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public KeeperFaceKind kind;
        public GameObject face;
    }

    public KeeperFaceKind face = KeeperFaceKind.Gecko;
    public Entry[] faces = new Entry[0];
    [Tooltip("The keeper's body; it wears the whole mesh for Human and the headless one for the rest.")]
    public SkinnedMeshRenderer body;
    public Mesh wholeBody, headlessBody;

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
        var mesh = face == KeeperFaceKind.Human ? wholeBody : headlessBody;
        if (body != null && mesh != null && body.sharedMesh != mesh) body.sharedMesh = mesh;
    }
}
