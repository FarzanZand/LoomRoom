using UnityEngine;

public class GazeLookAt : MonoBehaviour
{
    [SerializeField] Transform neckOrHead;
    [SerializeField] Transform target;
    [SerializeField, Range(0f, 1f)] float weight = 0.7f;

    bool active;

    void OnEnable()
    {
        if (PlayerManager.Instance != null)
            PlayerManager.Instance.OnPlayerSwapped += OnPlayerSwapped;

        active = PlayerManager.Instance != null &&
                 PlayerManager.Instance.CurrentPlayer == PlayerManager.ActivePlayer.TablePlayer;
    }

    void OnDisable()
    {
        if (PlayerManager.Instance != null)
            PlayerManager.Instance.OnPlayerSwapped -= OnPlayerSwapped;
    }

    void OnPlayerSwapped(PlayerManager.ActivePlayer player)
    {
        active = player == PlayerManager.ActivePlayer.TablePlayer;
    }

    void LateUpdate()
    {
        if (!active || neckOrHead == null || target == null) return;

        Vector3 dir = (target.position - neckOrHead.position).normalized;
        Quaternion lookRot = Quaternion.LookRotation(dir);
        neckOrHead.rotation = Quaternion.Slerp(neckOrHead.rotation, lookRot, weight);
    }
}
