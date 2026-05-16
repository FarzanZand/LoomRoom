using UnityEngine;

public class GazeLookAt : MonoBehaviour
{
    [SerializeField] Transform neckOrHead;
    [SerializeField] Transform target;
    [SerializeField, Range(0f, 1f)] float weight = 0.7f;

    void LateUpdate()
    {
        if (neckOrHead == null || target == null) return;
        if (PlayerManager.Instance == null ||
            PlayerManager.Instance.CurrentPlayer != PlayerManager.ActivePlayer.TablePlayer) return;

        Vector3 dir = (target.position - neckOrHead.position).normalized;
        Quaternion lookRot = Quaternion.LookRotation(dir);
        neckOrHead.rotation = Quaternion.Slerp(neckOrHead.rotation, lookRot, weight);
    }
}
