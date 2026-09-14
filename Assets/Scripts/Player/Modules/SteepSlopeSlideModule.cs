namespace MFPC
{
    using UnityEngine;
    using Unity.Cinemachine;
    using UnityEngine.Serialization;

    public class SteepSlopeSlideModule : MonoBehaviour
    {
        [Header("Slope Limit")]

        [Tooltip("Maximum slope angle the player can stand on before forced sliding starts.")]
        [SerializeField] private float maxWalkableSlopeAngle = 45f;
        [Tooltip("Extra ground-check distance used to sample the slope under the player.")]
        [SerializeField] private float groundProbeDistance = 0.5f;


        [Header("Sliding")]

        [Tooltip("Horizontal speed applied downhill when steep-slope sliding first begins.")]
        [FormerlySerializedAs("slideSpeed")]
        [SerializeField] private float initialSlideSpeed = 6f;
        [Tooltip("Maximum horizontal speed reached after sliding for a while.")]
        [SerializeField] private float maxSlideSpeed = 30f;
        [Tooltip("Time, in seconds, required to ramp from the initial slide speed and tilt to their maximum values.")]
        [SerializeField] private float timeToReachMaxSlide = 1.5f;
        [Tooltip("Minimum downward force kept while sliding so the controller stays attached to the slope.")]
        [SerializeField] private float slideStickForce = 2f;


        [Header("Tilt Impulse")]

        [Tooltip("Enable the camera tilt impulse while sliding down a steep slope.")]
        [SerializeField] private bool enableCameraTilt = true;
        [Tooltip("Camera tilt impulse played while sliding down a slope.")]
        [SerializeField] private CinemachineImpulseSource slopeTiltImpulse;
        [Tooltip("Impulse velocity used when sliding first begins.")]
        [SerializeField] private Vector3 initialTiltImpulseVelocity = new Vector3(0.009f, 0f, 0f);
        [Tooltip("Maximum impulse velocity reached after sliding for a while.")]
        [SerializeField] private Vector3 maxTiltImpulseVelocity = new Vector3(0.025f, 0f, 0f);


        public bool IsSliding { get; private set; }
        public float CurrentSlopeAngle { get; private set; }
        public Vector3 CurrentSlideDirection { get; private set; }
        public float CurrentSlideSpeed { get; private set; }
        public Vector3 CurrentTiltImpulseVelocity { get; private set; }

        private float slideRampTimer;


        private void OnValidate()
        {
            maxWalkableSlopeAngle = Mathf.Clamp(maxWalkableSlopeAngle, 0f, 89f);
            groundProbeDistance = Mathf.Max(0.05f, groundProbeDistance);
            initialSlideSpeed = Mathf.Max(0f, initialSlideSpeed);
            maxSlideSpeed = Mathf.Max(initialSlideSpeed, maxSlideSpeed);
            timeToReachMaxSlide = Mathf.Max(0f, timeToReachMaxSlide);
            slideStickForce = Mathf.Max(0f, slideStickForce);
        }

        public bool TryApplySteepSlopeSlide(CharacterController characterController, ref Vector3 movement)
        {
            if (!enabled || !characterController || !characterController.isGrounded)
            {
                ResetState();
                return false;
            }

            if (!TryGetGroundHit(characterController, out RaycastHit hit))
            {
                ResetState();
                return false;
            }

            CurrentSlopeAngle = Vector3.Angle(hit.normal, Vector3.up);
            if (CurrentSlopeAngle <= maxWalkableSlopeAngle)
            {
                ResetSlideMotionState();
                return false;
            }

            Vector3 slideDirection = Vector3.ProjectOnPlane(Vector3.down, hit.normal).normalized;
            if (slideDirection.sqrMagnitude <= 0.0001f)
            {
                ResetState();
                return false;
            }

            bool wasSliding = IsSliding;
            IsSliding = true;
            CurrentSlideDirection = slideDirection;

            if (!wasSliding)
            {
                slideRampTimer = 0f;
            }

            float rampT = timeToReachMaxSlide <= 0f
                ? 1f
                : Mathf.Clamp01(slideRampTimer / timeToReachMaxSlide);

            CurrentSlideSpeed = Mathf.Lerp(initialSlideSpeed, maxSlideSpeed, rampT);
            CurrentTiltImpulseVelocity = Vector3.Lerp(
                initialTiltImpulseVelocity,
                maxTiltImpulseVelocity,
                rampT);

            Vector3 slideVelocity = slideDirection * CurrentSlideSpeed;
            movement.x = slideVelocity.x;
            movement.z = slideVelocity.z;
            if (movement.y < 0f)
            {
                movement.y = Mathf.Min(movement.y, -slideStickForce);
            }

            if (enableCameraTilt && slopeTiltImpulse)
            {
                slopeTiltImpulse.GenerateImpulseWithVelocity(CurrentTiltImpulseVelocity);
            }

            if (timeToReachMaxSlide > 0f)
            {
                slideRampTimer = Mathf.Min(timeToReachMaxSlide, slideRampTimer + Time.deltaTime);
            }

            return true;
        }

        private bool TryGetGroundHit(CharacterController characterController, out RaycastHit hit)
        {
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            float rayDistance = characterController.height * 0.5f + groundProbeDistance;

            return Physics.Raycast(
                origin,
                Vector3.down,
                out hit,
                rayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
        }

        private void ResetState()
        {
            CurrentSlopeAngle = 0f;
            ResetSlideMotionState();
        }

        private void ResetSlideMotionState()
        {
            IsSliding = false;
            CurrentSlideDirection = Vector3.zero;
            CurrentSlideSpeed = 0f;
            CurrentTiltImpulseVelocity = Vector3.zero;
            slideRampTimer = 0f;
        }
    }
}
