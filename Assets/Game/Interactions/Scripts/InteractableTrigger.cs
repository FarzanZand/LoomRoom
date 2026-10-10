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
