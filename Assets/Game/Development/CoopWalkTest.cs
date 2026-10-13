using UnityEngine;

// Testing co-op from an eval: walks the table player in a circle for a few seconds (CoopWalkTest.Run(4f, 3f)), so the
// others can watch how a teammate's body moves.
public class CoopWalkTest : MonoBehaviour
{
    float until, speed;

    public static void Run(float seconds, float metresPerSecond)
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if (player == null) return;
        var test = player.GetComponent<CoopWalkTest>();
        if (test == null) test = player.gameObject.AddComponent<CoopWalkTest>();
        test.until = Time.time + seconds;
        test.speed = metresPerSecond;
    }

    void Update()
    {
        if (Time.time > until) { Destroy(this); return; }
        var player = GetComponent<Player>();
        if (player == null || player.Motor == null || player.Motor.Controller == null) return;
        float yaw = player.Look.YawTransform.eulerAngles.y + 60f * Time.deltaTime;
        player.Look.SetYaw(yaw);
        var forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
        player.Motor.Controller.Move(forward * speed * Time.deltaTime + Vector3.down * 2f * Time.deltaTime);
    }
}
