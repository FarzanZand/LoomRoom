using UnityEngine;

// An invisible plane across the middle leg of a loop corridor, blue arrow pointing away from the room.
// Walking across it outward puts the player at the same spot relative to the destination portal,
// facing back toward the room. No fade: the corridors hide the jump, so each one must bend before and
// after the portal (you can't see past either corner) and look the same on both sides of it.
[DefaultExecutionOrder(20)]
public class RoomLoopPortal : MonoBehaviour
{
    [Tooltip("Where crossing this portal outward takes you. May be this same portal.")]
    [SerializeField] RoomLoopPortal destination;
    [Tooltip("Opening width and height. Centred on this transform, bottom edge at its origin.")]
    [SerializeField] Vector2 size = new(1.6f, 2.6f);

    float lastSide = float.NaN;
    Player lastPlayer;

    void OnEnable() => lastSide = float.NaN;

    void Update()
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        if (player != lastPlayer) { lastPlayer = player; lastSide = float.NaN; }
        if (player == null || destination == null) return;

        Vector3 local = transform.InverseTransformPoint(player.transform.position);
        bool withinOpening = Mathf.Abs(local.x) <= size.x * 0.5f && local.y > -0.5f && local.y < size.y;
        if (withinOpening && lastSide < 0f && local.z >= 0f) Send(player);
        else lastSide = local.z;
    }

    void Send(Player player)
    {
        float yawDelta = Vector3.SignedAngle(transform.forward, -destination.transform.forward, Vector3.up);
        Vector3 offset = Quaternion.AngleAxis(yawDelta, Vector3.up) * (player.transform.position - transform.position);
        Vector3 target = destination.transform.position + offset;
        player.Warp(target, yawDelta);

        lastSide = float.NaN;
        destination.lastPlayer = player;
        destination.lastSide = destination.transform.InverseTransformPoint(target).z;
    }

    void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireCube(new Vector3(0f, size.y * 0.5f, 0f), new Vector3(size.x, size.y, 0f));
        Gizmos.DrawLine(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 0.6f));
        Gizmos.matrix = Matrix4x4.identity;
        if (destination != null && destination != this)
            Gizmos.DrawLine(transform.position + Vector3.up, destination.transform.position + Vector3.up);
    }
}
