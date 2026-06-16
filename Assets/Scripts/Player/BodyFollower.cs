using UnityEngine;

public class BodyFollower : MonoBehaviour
{
    [SerializeField] private Transform positionSource;
    [SerializeField] private Transform rotationSource;

    private void OnFootstep(AnimationEvent animationEvent) { }
    private void OnLand(AnimationEvent animationEvent) { }

    void LateUpdate()
    {
        if (positionSource != null)
            transform.position = positionSource.position;

        if (rotationSource != null)
            transform.rotation = Quaternion.Euler(0f, rotationSource.eulerAngles.y, 0f);
    }

}
