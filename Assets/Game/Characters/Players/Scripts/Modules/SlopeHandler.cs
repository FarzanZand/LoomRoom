using UnityEngine;

public class SlopeHandler : MonoBehaviour
{
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
