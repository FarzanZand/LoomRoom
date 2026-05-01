using UnityEngine;

public class BodyFollower : MonoBehaviour
{
    [SerializeField] private Transform positionSource;
    [SerializeField] private Transform rotationSource;

    void LateUpdate()
    {
        if (positionSource != null)
            transform.position = positionSource.position;

        if (rotationSource != null)
            transform.rotation = Quaternion.Euler(0f, rotationSource.eulerAngles.y, 0f);
    }

    // Footstep audio is handled by PlayerController — silences the animation event warning.
    private void OnFootstep(AnimationEvent animationEvent) { }
}
