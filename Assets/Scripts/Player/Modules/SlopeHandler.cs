namespace MFPC
{
    using UnityEngine;

    public class SlopeHandler : MonoBehaviour
    {
        [Tooltip("Downward force applied when the player is grounded. It's used to make the player stick to the ground on slopes.")]
        [SerializeField] float stickForce = -2f;

        public Vector3 AdjustMovement(Vector3 movement, CharacterController cc)
        {
            if (cc.isGrounded && movement.y < 0f)
                movement.y = stickForce;

            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, cc.height / 2f + 0.5f))
            {
                movement = Vector3.ProjectOnPlane(movement, hit.normal);
            }

            return movement;
        }

        public bool OnSlope(CharacterController characterController, out RaycastHit hit)
        {
            Vector3 origin = transform.position + Vector3.up * 0.1f;

            if (Physics.Raycast(origin, Vector3.down, out hit,
                characterController.height / 2f + 0.5f))
            {
                return hit.normal != Vector3.up;
            }

            return false;
        }
    }
}