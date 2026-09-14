using System;
using UnityEngine;

// Camera-centred selection shared by pickups and other interactions.
[DefaultExecutionOrder(10000)]
public class InteractController : MonoBehaviour
{
    public event Action<InteractableTrigger> OnActiveChanged;
    [SerializeField] Camera rayCamera;
    [SerializeField] float rayDistance = 3f;
    [SerializeField] LayerMask rayMask = ~0;
    [Header("Aim assistance")]
    [SerializeField, Range(1, 12)] float acquireAngle = 5f;
    [SerializeField, Range(1, 16)] float releaseAngle = 8f;
    [SerializeField, Min(0)] float missGrace = .15f;
    [SerializeField, Min(0)] float switchAdvantage = 1.5f;

    readonly RaycastHit[] sightHits = new RaycastHit[128];
    InteractableTrigger activeTrigger;
    Character character;
    float lastOnTarget;
    public InteractableTrigger Active => activeTrigger;
    public Camera ViewCamera => rayCamera;

    void Awake()
    {
        character = GetComponent<Character>();
        if (rayCamera == null) rayCamera = GetComponentInChildren<Camera>(true);
    }
    void OnEnable() => BindInput();
    void Start() => BindInput();
    void BindInput()
    {
        if (!InputManager.HasInstance) return;
        InputManager.Instance.InteractPressed -= OnInteractInput;
        InputManager.Instance.InteractPressed += OnInteractInput;
    }
    void OnDisable()
    {
        if (InputManager.HasInstance) InputManager.Instance.InteractPressed -= OnInteractInput;
        SetActiveTrigger(null);
    }
    // Evaluate after camera motion, avoiding a frame of disagreement with the view.
    void LateUpdate() => RefreshBest();
    bool Blocked => rayCamera == null || (character is Player p && !p.IsActive) ||
                    (GameManager.HasInstance && !GameManager.Instance.GameplayActive);

    void RefreshBest()
    {
        if (Blocked) { SetActiveTrigger(null); return; }
        InteractableTrigger best = null;
        float bestScore = float.MaxValue;
        foreach (var candidate in InteractableTrigger.Available)
        {
            if (!Evaluate(candidate, out float score) || score > acquireAngle) continue;
            if (score < bestScore) { best = candidate; bestScore = score; }
        }
        if (Evaluate(activeTrigger, out float activeScore) && activeScore <= releaseAngle)
        {
            if (activeScore <= acquireAngle) lastOnTarget = Time.unscaledTime;
            bool clearlyBetter = best != null && best != activeTrigger && bestScore + switchAdvantage < activeScore;
            if (!clearlyBetter && (activeScore <= acquireAngle || Time.unscaledTime - lastOnTarget < missGrace))
                return;
        }
        SetActiveTrigger(best);
    }

    bool Evaluate(InteractableTrigger target, out float score)
    {
        score = float.MaxValue;
        if (target == null || !target.isActiveAndEnabled || !target.CanInteract(character) ||
            target.transform.IsChildOf(transform) || (rayMask.value & (1 << target.gameObject.layer)) == 0) return false;
        Vector3 origin = rayCamera.transform.position;
        Bounds bounds = target.TargetBounds;
        Vector3 point = bounds.center;
        Vector3 delta = point - origin;
        float distance = delta.magnitude;
        if (distance < .001f || distance > rayDistance || Vector3.Dot(rayCamera.transform.forward, delta) <= 0) return false;
        float angle = Vector3.Angle(rayCamera.transform.forward, delta);
        // Small angular allowance follows visible size, capped so broad triggers cannot steal aim.
        float radius = Mathf.Min(bounds.extents.x, Mathf.Min(bounds.extents.y, bounds.extents.z));
        score = Mathf.Max(0, angle - Mathf.Min(2f, Mathf.Atan2(radius, distance) * Mathf.Rad2Deg));
        if (score > releaseAngle) return false;
        int count = Physics.RaycastNonAlloc(origin, delta / distance, sightHits, distance, rayMask, QueryTriggerInteraction.Ignore);
        if (count == sightHits.Length) return false; // Never assume a truncated visibility query is clear.
        for (int i = 0; i < count; i++)
        {
            var hit = sightHits[i];
            if (hit.transform.IsChildOf(transform) || target.Owns(hit.transform)) continue;
            return false;
        }
        return true;
    }

    void SetActiveTrigger(InteractableTrigger trigger)
    {
        // Reference equality also clears a destroyed Unity object and its visible prompt.
        if (ReferenceEquals(trigger, activeTrigger)) return;
        activeTrigger = trigger;
        lastOnTarget = Time.unscaledTime;
        OnActiveChanged?.Invoke(activeTrigger);
    }
    void OnInteractInput()
    {
        // Revalidate visibility/range on the actual press; grace never permits using through a wall.
        if (Blocked || !Evaluate(activeTrigger, out float score) || score > releaseAngle) return;
        activeTrigger.Interact(character);
        RefreshBest();
    }
}
