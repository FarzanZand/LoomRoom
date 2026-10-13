using UnityEngine;

// Testing from an eval: HeightWatch.Run() logs every frame the table player drops more than half a metre, with what
// it was doing (dashing, airborne, crouching, a warp), to find what puts a player under the floor.
public class HeightWatch : MonoBehaviour
{
    float lastY;
    Vector3 lastPosition;

    public static void Run()
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if (player == null) return;
        var watch = player.GetComponent<HeightWatch>();
        if (watch == null) watch = player.gameObject.AddComponent<HeightWatch>();
        watch.lastPosition = player.transform.position;
        watch.lastY = player.transform.position.y;
    }

    void LateUpdate()
    {
        var p = transform.position;
        if (lastY - p.y > .5f)
        {
            var motor = GetComponent<PlayerMotor>();
            var bash = GetComponent<ShieldBash>();
            Debug.LogWarning($"[HeightWatch] {name} dropped {lastY - p.y:0.00} m: {lastPosition:0.00} -> {p:0.00}, moved {(p - lastPosition).magnitude:0.00} m, " +
                $"dash {(motor != null ? motor.DashVelocity.ToString("0.0") : "-")} bash {(bash != null && bash.Busy)} grounded {(motor != null && motor.IsGrounded)} crouch {(motor != null && motor.IsCrouching)}");
        }
        lastY = p.y;
        lastPosition = p;
    }
}
