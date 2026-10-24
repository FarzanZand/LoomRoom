using UnityEngine;

// Which body the room player has (seen from outside: mirrors, cutscenes, co-op). Both bodies share one skeleton and
// avatar (PolygonTown), so only the meshes and their heads change. Each body has its Synty face cut away and a Meshy
// head under the head bone, like the Dungeon Master's faces.
public enum BodyType { BodyA = 0, BodyB = 1 }

public class RoomBodyType : MonoBehaviour
{
    [Tooltip("BodyA: the woman. BodyB: the man.")]
    public BodyType body = BodyType.BodyA;
    [Tooltip("BodyA's skinned mesh and its head.")]
    public GameObject bodyA, headA;
    [Tooltip("BodyB's skinned mesh and its head.")]
    public GameObject bodyB, headB;

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
        bool a = body == BodyType.BodyA;
        Set(bodyA, a); Set(headA, a);
        Set(bodyB, !a); Set(headB, !a);
    }

    static void Set(GameObject go, bool on) { if (go != null && go.activeSelf != on) go.SetActive(on); }
}
