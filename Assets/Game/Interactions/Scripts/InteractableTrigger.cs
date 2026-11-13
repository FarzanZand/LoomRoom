using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

// An available camera-targeted interaction. On use it runs its effects, the UnityEvent, and the
// IInteractable found on a parent (WorldItem, NpcBrain, TableManager...).
[RequireComponent(typeof(Collider))]
public class InteractableTrigger : MonoBehaviour
{
    [Delayed]
    [Tooltip("Prompt shown when no IInteractable parent provides one.")]
    [SerializeField] string promptMessage = "Interact";
    [Tooltip("Can only be used once.")]
    [SerializeField] bool consumable = false;
    [ShowIf("consumable")]
    [SerializeField] bool destroyOnConsume = false;
    [Tooltip("Seconds before it can be used again. 0 = no cooldown.")]
    [SerializeField] float cooldown = 0f;

    [Tooltip("Inline effects run in order. If any GiveItem fails (inventory full) the trigger is not consumed.")]
    [ListDrawerSettings(ShowFoldout = false)]
    [SerializeField] List<InteractionEffect> effects = new();

    [Tooltip("Designer hook. Receives the interactor's GameObject.")]
    public UnityEvent<GameObject> onInteract;

    public IInteractable Interactable { get; private set; }

    public string PromptMessage
    {
        get
        {
            string p = Interactable?.Prompt;
            return string.IsNullOrEmpty(p) ? promptMessage : p;
        }
    }

    static readonly HashSet<InteractableTrigger> available = new();
    public static System.Collections.Generic.IEnumerable<InteractableTrigger> Available => available;
    WorldItem worldItem;
    Collider targetCollider;
    Renderer[] itemRenderers;
    float nextRendererLookup;
    float readyAt;
    public WorldItem WorldItem => worldItem;
    public Bounds TargetBounds
    {
        get
        {
            // Visuals may be spawned by WorldItem.Start after this component's Awake: look again while there
            // are none, a few times a second rather than on every query.
            if (worldItem != null && (itemRenderers == null || itemRenderers.Length == 0) && Time.unscaledTime >= nextRendererLookup)
            {
                itemRenderers = worldItem.GetComponentsInChildren<Renderer>();
                nextRendererLookup = Time.unscaledTime + .25f;
            }
            bool found = false;
            Bounds result = new(transform.position, Vector3.zero);
            if (itemRenderers != null)
                foreach (var renderer in itemRenderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    if (!found) { result = renderer.bounds; found = true; }
                    else result.Encapsulate(renderer.bounds);
                }
            return found ? result : targetCollider != null ? targetCollider.bounds : result;
        }
    }
    // The corners the HUD's target frame is fitted round: the item's own model only, each mesh's box turned with it
    // (a sword lying across the view is framed along its blade, not by a world-aligned box round it). The loot beam
    // (1.5 m of line), the pickup sparkle and any other effect are left out: with them the frame was a tall box
    // standing over the item (the user, 2026-11-13). Falls back to TargetBounds when the item has no model.
    public void FrameCorners(List<Vector3> into)
    {
        into.Clear();
        _ = TargetBounds;   // looks the renderers up again while there are none yet
        if (itemRenderers != null)
            foreach (var renderer in itemRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    renderer is LineRenderer || renderer is TrailRenderer || renderer is ParticleSystemRenderer) continue;
                var filter = renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>() : null;
                var mesh = filter != null ? filter.sharedMesh : null;
                var hull = mesh != null ? Hull(mesh) : null;
                if (hull != null)
                {
                    var toWorld = renderer.transform.localToWorldMatrix;
                    foreach (var p in hull) into.Add(toWorld.MultiplyPoint3x4(p));
                }
                else if (mesh != null)
                    AddCorners(into, mesh.bounds, renderer.transform.localToWorldMatrix);
                else
                    AddCorners(into, renderer.bounds, Matrix4x4.identity);
            }
        if (into.Count == 0) AddCorners(into, TargetBounds, Matrix4x4.identity);
    }
    // A mesh's outermost vertices along 64 directions spread over the sphere, worked out once per mesh: the frame
    // fitted round them hugs the model from any side (its bounding box stood well clear of body armour lying on
    // its back). Null for a mesh whose vertices can't be read; the box is used then.
    static readonly Dictionary<Mesh, Vector3[]> hulls = new();
    static Vector3[] directions;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => hulls.Clear();
    static Vector3[] Hull(Mesh mesh)
    {
        if (hulls.TryGetValue(mesh, out var hull)) return hull;
        if (mesh.isReadable && mesh.vertexCount > 0)
        {
            if (directions == null)
            {
                directions = new Vector3[64];
                for (int i = 0; i < directions.Length; i++)   // a Fibonacci sphere: even spacing, no pole crowding
                {
                    float y = 1f - (i + .5f) * 2f / directions.Length, r = Mathf.Sqrt(1f - y * y), a = i * 2.39996323f;
                    directions[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                }
            }
            var vertices = mesh.vertices;
            var extreme = new HashSet<int>();
            foreach (var d in directions)
            {
                int best = 0; float most = float.MinValue;
                for (int v = 0; v < vertices.Length; v++) { float s = Vector3.Dot(vertices[v], d); if (s > most) { most = s; best = v; } }
                extreme.Add(best);
            }
            hull = new Vector3[extreme.Count];
            int n = 0; foreach (int v in extreme) hull[n++] = vertices[v];
        }
        hulls[mesh] = hull;
        return hull;
    }
    static void AddCorners(List<Vector3> into, Bounds box, Matrix4x4 toWorld)
    {
        for (int i = 0; i < 8; i++)
            into.Add(toWorld.MultiplyPoint3x4(box.center + Vector3.Scale(box.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
    }
    // Cheap reach test before the full scoring: the trigger collider (which encloses the item) within distance
    // of point. True when there is no enabled collider to measure.
    public bool RoughlyWithin(Vector3 point, float distance) =>
        targetCollider == null || !targetCollider.enabled || targetCollider.bounds.SqrDistance(point) <= distance * distance;

    // Hits on these never block the line of sight to this trigger: its own object, and the whole
    // character it belongs to (an NPC's body stands between the eye and its interaction sphere up close).
    public bool Owns(Transform other)
    {
        Transform owner = worldItem != null ? worldItem.transform :
            Interactable is Component component ? component.transform : transform;
        if (other.IsChildOf(owner)) return true;
        if (character == null) character = GetComponentInParent<Character>();
        return character != null && other.IsChildOf(character.transform);
    }
    Character character;
    void OnEnable() => available.Add(this);

    void Awake()
    {
        Interactable = GetComponentInParent<IInteractable>();
        worldItem = GetComponentInParent<WorldItem>();
        targetCollider = GetComponent<Collider>();
        targetCollider.isTrigger = true;
    }

    public bool CanInteract(Character who)
    {
        if (!enabled || Time.time < readyAt) return false;
        return Interactable == null || Interactable.CanInteract(who);
    }

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;

        var ctx = new InteractionContext { Who = who, Trigger = this };
        bool ok = true;
        foreach (var e in effects)
            if (e != null && !e.Execute(ctx)) ok = false;

        onInteract?.Invoke(who != null ? who.gameObject : null);
        Interactable?.Interact(who);

        if (!ok) return;

        if (consumable)
        {
            if (destroyOnConsume) Destroy(gameObject);
            else enabled = false;
        }
        else if (cooldown > 0f)
            readyAt = Time.time + cooldown;
    }

    void OnDisable() => available.Remove(this);
}
