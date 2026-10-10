using UnityEngine;

// A mesh built at runtime (DeathBurst's body pieces) is destroyed with the object that
// shows it, so meshes don't pile up over a run.
public class OwnedMesh : MonoBehaviour
{
    public Mesh mesh;

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }
}
