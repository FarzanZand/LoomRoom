using UnityEngine;

// A paper card in the room. Put away, it lies flat on the table; shown, it lifts by itself into the air
// in front of the player, turns to face them and hovers with a slight bob; Flip turns it over to change
// what it says; hidden, it settles back onto the table. Its world-space canvas gets the player's camera
// so its buttons can be clicked.
public class FloatingCard : MonoBehaviour
{
    [Tooltip("Where the card lies on the table while put away (face up).")]
    public Transform rest;
    [Tooltip("Where it hovers while shown. It turns to face the camera. Empty: placed from the view (below).")]
    public Transform hover;
    [Tooltip("Hovering in front of the camera: distance ahead, to the side (negative is left) and up (world units).")]
    public Vector3 viewOffset = new(-7.5f, -1f, 22f);
    [Min(0), Tooltip("Seconds to catch up with the view when the camera turns.")]
    public float follow = .25f;
    [Min(.05f)] public float liftSeconds = .75f;
    [Tooltip("How far the card rises above the straight path while lifting (world units).")]
    public float liftArc = 3f;
    [Min(.05f)] public float flipSeconds = .32f;
    [Tooltip("Hover bob (world units) and its speed.")]
    public float bobHeight = .25f, bobSpeed = 1.2f;
    [Range(0, 30), Tooltip("Degrees the hovering card leans back, like a card held up to read.")]
    public float leanBack = 6f;
    public Canvas canvas;

    bool showing;
    float shown;            // 0 on the table, 1 hovering
    float flip = 1f;        // 0..1 while turning over; the content changes halfway
    System.Action swap;

    public bool Showing => showing;

    void Awake()
    {
        if (canvas == null) canvas = GetComponent<Canvas>();
        Place(0f);
        SetVisible(false);
    }

    public void Show()
    {
        showing = true;
        SetVisible(true);
        if (canvas != null && PlayerManager.HasInstance) canvas.worldCamera = PlayerManager.Instance.OutputCamera;
    }

    public void Hide() => showing = false;

    // Turns the card over; change runs when it is edge-on, so the new side is the one that comes round.
    public void Flip(System.Action change)
    {
        if (!showing || shown < .5f) { change?.Invoke(); return; }
        if (flip < 1f) { swap += change; return; }
        swap = change;
        flip = 0f;
    }

    void SetVisible(bool on)
    {
        if (canvas != null) canvas.enabled = on;
    }

    void LateUpdate()
    {
        float target = showing ? 1f : 0f;
        if (!Mathf.Approximately(shown, target))
            shown = Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime / liftSeconds);
        if (flip < 1f)
        {
            float before = flip;
            flip = Mathf.Min(1f, flip + Time.unscaledDeltaTime / flipSeconds);
            if (before < .5f && flip >= .5f) { var s = swap; swap = null; s?.Invoke(); }
        }
        Place(shown);
        if (!showing && shown <= 0f) SetVisible(false);
    }

    Vector3 anchor, anchorVelocity;
    bool anchored;

    // Where the card hovers: the authored spot, or a place in front of the camera that follows it smoothly.
    Vector3 Anchor(Camera cam)
    {
        if (hover != null || cam == null) return hover != null ? hover.position : transform.position;
        var c = cam.transform;
        var target = c.position + c.forward * viewOffset.z + c.right * viewOffset.x + c.up * viewOffset.y;
        if (!anchored || shown <= 0f) { anchor = target; anchorVelocity = Vector3.zero; anchored = true; }
        else anchor = Vector3.SmoothDamp(anchor, target, ref anchorVelocity, follow, Mathf.Infinity, Time.unscaledDeltaTime);
        return anchor;
    }

    void Place(float t)
    {
        if (rest == null) return;
        float k = t * t * (3f - 2f * t);
        var cam = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : null;
        Vector3 hoverPos = Anchor(cam) + Vector3.up * (Mathf.Sin(Time.unscaledTime * bobSpeed) * bobHeight * k);
        // Facing the camera: the canvas's front looks back along its forward.
        Quaternion face = cam != null ? Quaternion.LookRotation(hoverPos - cam.transform.position, Vector3.up) : rest.rotation;
        face *= Quaternion.Euler(-leanBack, 0f, 0f);
        // Turning over: a quarter turn away, the change, and a quarter turn back.
        if (flip < 1f) face *= Quaternion.Euler(0f, flip < .5f ? flip * 180f : (flip - 1f) * 180f, 0f);
        var pos = Vector3.Lerp(rest.position, hoverPos, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * liftArc);
        transform.SetPositionAndRotation(pos, Quaternion.Slerp(rest.rotation, face, k));
    }
}
