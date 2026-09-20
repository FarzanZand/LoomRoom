using UnityEngine;

// Attach to a bone (e.g. Spine_01 or upper arm) to cancel the forward lean
// that the Hips bone introduces during walk/run animations.
// Runs after the Animator via DefaultExecutionOrder so corrections apply last.
[DefaultExecutionOrder(100)]
public class IgnorePelvisRotation : MonoBehaviour
{
    [SerializeField] private Transform pelvis;
    [SerializeField] private bool cancelPitch = true;  // X — forward/back lean
    [SerializeField] private bool cancelRoll  = false; // Z — side lean

    private Quaternion pelvisRestRotation;

    void Start()
    {
        if (pelvis != null)
            pelvisRestRotation = pelvis.rotation;
    }

    void LateUpdate()
    {
        if (pelvis == null) return;

        // How far has the pelvis rotated from its rest orientation?
        Quaternion delta = pelvis.rotation * Quaternion.Inverse(pelvisRestRotation);

        Vector3 euler = delta.eulerAngles;
        // Remap 0-360 to -180-180 so small angles stay small
        if (euler.x > 180f) euler.x -= 360f;
        if (euler.z > 180f) euler.z -= 360f;

        if (!cancelPitch) euler.x = 0f;
        if (!cancelRoll)  euler.z = 0f;
        euler.y = 0f; // never cancel yaw — the body still needs to turn

        // Counter-rotate this bone by the unwanted pelvis contribution
        transform.rotation = Quaternion.Inverse(Quaternion.Euler(euler)) * transform.rotation;
    }
}
