using UnityEngine;

public class GazeLookAt : MonoBehaviour
{
    [SerializeField] Transform neckOrHead;
    [SerializeField] Transform target;
    [SerializeField] Transform characterRoot;
    [SerializeField, Range(0f, 1f)] float weight = 0.7f;
    [Tooltip("Max angle the neck can turn from the character's forward direction.")]
    [SerializeField, Range(0f, 180f)] float maxLookAngle = 70f;

    void LateUpdate()
    {
        if (neckOrHead == null || target == null) return;
        if (PlayerManager.Instance == null ||
            PlayerManager.Instance.CurrentPlayer != PlayerManager.ActivePlayer.TablePlayer) return;

        Vector3 dir = (target.position - neckOrHead.position).normalized;

        if (characterRoot != null)
        {
            float angle = Vector3.Angle(characterRoot.forward, dir);
            if (angle > maxLookAngle)
                dir = Vector3.RotateTowards(characterRoot.forward, dir, maxLookAngle * Mathf.Deg2Rad, 0f);
        }

        Quaternion lookRot = Quaternion.LookRotation(dir);
        neckOrHead.rotation = Quaternion.Slerp(neckOrHead.rotation, lookRot, weight);
    }
}
