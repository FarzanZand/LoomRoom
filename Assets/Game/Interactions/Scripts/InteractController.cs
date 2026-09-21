using System;
using UnityEngine;

// Camera-centred selection shared by pickups and other interactions.
[DefaultExecutionOrder(10000)]
public class InteractController : MonoBehaviour
{
    public event Action<InteractableTrigger> OnActiveChanged;
    [SerializeField] Camera rayCamera;
    [Tooltip("How far from the camera things can be used.")]
    [SerializeField] float rayDistance = 3.5f;
    [SerializeField] LayerMask rayMask = ~0;
    [Header("Aim assistance")]
    [Tooltip("Degrees around an object's silhouette that still target it.")]
    [SerializeField, Range(1, 20)] float acquireAngle = 7f;
    [Tooltip("Degrees before a targeted object is dropped again (wider than acquire so it does not flicker).")]
    [SerializeField, Range(1, 24)] float releaseAngle = 11f;
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
        if (rayCamera == null) rayCamera = Camera.main;
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
        Vector3 origin  = rayCamera.transform.position;
        Vector3 forward = rayCamera.transform.forward;
        Bounds bounds = target.TargetBounds;
        Vector3 toCenter = bounds.center - origin;
        float depth = Vector3.Dot(forward, toCenter);
        if (depth <= 0f) return false;
        if (toCenter.magnitude - bounds.extents.magnitude > rayDistance) return false;

        // Aim against the closest point of the object to the crosshair ray, not its centre:
        // anywhere on a flat shield or a thin blade under the crosshair scores zero, and the
        // acquire angle is pure margin around the object's silhouette.
        Vector3 onRay   = origin + forward * depth;
        Vector3 closest = bounds.ClosestPoint(onRay);
        // Large targets can extend beyond interaction range while their near face is
        // within reach. A direct aim should use that face, not the centre-depth plane.
        if (bounds.IntersectRay(new Ray(origin, forward), out float entryDistance) && entryDistance > 0f)
            closest = origin + forward * entryDistance;
        Vector3 delta   = closest - origin;
        float distance  = delta.magnitude;
        if (distance < .001f || distance > rayDistance) return false;
        score = Vector3.Angle(forward, delta);
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
