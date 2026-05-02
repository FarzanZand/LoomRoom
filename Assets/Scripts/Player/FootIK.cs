using UnityEngine;

public class FootIK : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float raycastDistance = 1.5f;
    [SerializeField] private float footGroundOffset = 0.08f;

    void OnAnimatorIK(int layerIndex)
    {
        if (animator == null) return;
        PlaceFoot(AvatarIKGoal.LeftFoot);
        PlaceFoot(AvatarIKGoal.RightFoot);
    }

    void PlaceFoot(AvatarIKGoal goal)
    {
        Vector3 footPos = animator.GetIKPosition(goal);

        if (Physics.Raycast(footPos + Vector3.up, Vector3.down, out RaycastHit hit, raycastDistance + 1f, groundLayers))
        {
            animator.SetIKPositionWeight(goal, 1f);
            animator.SetIKPosition(goal, hit.point + Vector3.up * footGroundOffset);

            Quaternion footRot = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(transform.forward, hit.normal),
                hit.normal);
            animator.SetIKRotationWeight(goal, 1f);
            animator.SetIKRotation(goal, footRot);
        }
        else
        {
            animator.SetIKPositionWeight(goal, 0f);
            animator.SetIKRotationWeight(goal, 0f);
        }
    }
}
