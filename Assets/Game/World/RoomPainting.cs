using UnityEngine;

// A painting in the room whose picture a boss fight can change (BossFight > Room: Painting). On the painting's own
// object (the one behind the Dungeon Master is World > Apartment > Main Bedroom > Furnishings > Painting). The new
// picture goes on a runtime copy of the painting's material (the asset is never touched; property blocks are left to
// the Lighting Manager, which holds the room's light probe in them), and putting it back is swapping the original in.
// The swap is made while nobody can see it: when the boss floor loads and when that floor is left (BossStageEncounter).
[RequireComponent(typeof(Renderer))]
public class RoomPainting : MonoBehaviour
{
    [Tooltip("The painting on the wall behind the Dungeon Master: the one boss fights change.")]
    public bool behindDungeonMaster = true;

    static readonly int BaseMap = Shader.PropertyToID("_BaseMap"), MainTex = Shader.PropertyToID("_MainTex");
    public static RoomPainting BehindDungeonMaster { get; private set; }

    Renderer art;
    Material original, shown;

    void Awake() => art = GetComponent<Renderer>();
    void OnEnable() { if (behindDungeonMaster) BehindDungeonMaster = this; }
    void OnDisable() { if (BehindDungeonMaster == this) BehindDungeonMaster = null; }
    void OnDestroy() { if (shown != null) Destroy(shown); }

    // Shows this picture instead of the painting's own (null puts the painting's own back).
    public void Show(Texture picture)
    {
        if (art == null) art = GetComponent<Renderer>();
        if (shown != null) { art.sharedMaterial = original; Destroy(shown); shown = null; }
        if (picture == null) return;
        original = art.sharedMaterial;
        shown = new Material(original) { name = original.name + " (boss painting)" };
        if (shown.HasProperty(BaseMap)) shown.SetTexture(BaseMap, picture);
        if (shown.HasProperty(MainTex)) shown.SetTexture(MainTex, picture);
        art.sharedMaterial = shown;
    }

    public void Restore() => Show(null);

    // The middle of the picture and the way it faces (into the room).
    public Vector3 Centre => art != null ? art.bounds.center : transform.position;
    public Vector3 Facing(Vector3 roomPoint)
    {
        // Out of the wall: along the picture's thinnest side, toward the room.
        var size = art != null ? art.bounds.size : Vector3.one;
        var n = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
        return Vector3.Dot(n, roomPoint - Centre) >= 0f ? n : -n;
    }
}
