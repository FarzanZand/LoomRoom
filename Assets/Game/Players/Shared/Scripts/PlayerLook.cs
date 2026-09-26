using UnityEngine;

// Yaw, pitch and lean. Rotates the torso (yaw + roll) and neck (pitch) pivots and
// raises/lowers the camera pivot when crouching. Sensitivity and inversion come from
// PlayerSettings; the actual camera is a child of the neck.
[DefaultExecutionOrder(10)]
public class PlayerLook : MonoBehaviour
{
    [Header("Pivots")]
    [Tooltip("Horizontal rotation and lean pivot (yaw & roll).")]
    [SerializeField] Transform lateralTorso;
    [Tooltip("Vertical rotation pivot (pitch).")]
    [SerializeField] Transform verticalNeck;

    [Header("Rotation")]
    [SerializeField] float minClamp = -70f;
    [SerializeField] float maxClamp =  70f;

    [Header("Lean")]
    [SerializeField] float leanAmount        = 30f;
    [SerializeField] float lateralSmoothTime = 5f;
    [SerializeField] float lateralReturnTime = 7f;

    [Header("Crouch camera height")]
    [SerializeField] float normalCameraHeight        = 1f;
    [SerializeField] float crouchCameraHeight        = 0.1f;
    [SerializeField] float crouchingCameraSmoothTime = 5f;

    public Transform YawTransform   => lateralTorso != null ? lateralTorso : transform;
    public Transform PitchTransform => verticalNeck != null ? verticalNeck : YawTransform;
    public float CurrentLeanAngle { get; private set; }

    Player  player;
    Vector2 smoothedLook;
    float   pitch;
    Vector3 cameraHeight;

    void Awake()
    {
        player = GetComponent<Player>();
        cameraHeight = new Vector3(0f, normalCameraHeight, 0f);
        if (verticalNeck != null) pitch = -NormalizeAngle(verticalNeck.localEulerAngles.x);
    }

    void Update()
    {
        var settings = player != null ? player.settings : null;
        var input    = InputManager.HasInstance ? InputManager.Instance : null;

        // ── Look input ──
        float sensitivity = (settings != null ? settings.mouseSensitivity : 1f) * UserSettings.MouseSensitivity / 5f;
        float smoothing   = settings != null ? settings.mouseSmoothing : 20f;

        bool canLook = Cursor.lockState == CursorLockMode.Locked && input != null &&
            (!GameManager.HasInstance || GameManager.Instance.GameplayActive);
        Vector2 raw = canLook ? input.Look : Vector2.zero;
        if (!canLook) ResetInput();
        if (settings != null && settings.invertLook) raw.y = -raw.y;

        Vector2 target = raw * sensitivity;
        float lookT = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        smoothedLook = Vector2.Lerp(smoothedLook, target, lookT);

        // ── Rotation ──
        float yaw = YawTransform.eulerAngles.y + smoothedLook.x;
        pitch = Mathf.Clamp(pitch + smoothedLook.y, minClamp, maxClamp);

        // ── Lean ──
        bool leanEnabled = settings == null || settings.enableLean;
        float targetLean = 0f;
        if (leanEnabled && input != null)
        {
            if (input.LeanLeftHeld)       targetLean = leanAmount;
            else if (input.LeanRightHeld) targetLean = -leanAmount;
        }
        float leanT = 1f - Mathf.Exp(-(targetLean == 0f ? lateralReturnTime : lateralSmoothTime) * Time.deltaTime);
        CurrentLeanAngle = Mathf.Lerp(CurrentLeanAngle, targetLean, leanT);

        if (lateralTorso != null) lateralTorso.rotation = Quaternion.Euler(0f, yaw, CurrentLeanAngle);
        if (verticalNeck != null) verticalNeck.rotation = Quaternion.Euler(-pitch, yaw, -CurrentLeanAngle);

        // ── Crouch height ──
        bool crouching = player != null && player.Motor != null && player.Motor.IsCrouching;
        float desiredY = crouching ? crouchCameraHeight : normalCameraHeight;
        float t = 1f - Mathf.Exp(-crouchingCameraSmoothTime * Time.deltaTime);
        cameraHeight.y = Mathf.Lerp(cameraHeight.y, desiredY, t);
        if (lateralTorso != null) lateralTorso.localPosition = cameraHeight;
    }

    // Snap the view to a world yaw (cutscenes, teleports).
    public void ResetInput() => smoothedLook = Vector2.zero;

    public void SetYaw(float worldYaw)
    {
        ResetInput();
        if (lateralTorso != null) lateralTorso.rotation = Quaternion.Euler(0f, worldYaw, 0f);
        if (verticalNeck != null) verticalNeck.rotation = Quaternion.Euler(-pitch, worldYaw, 0f);
    }

    // Turns the view about world up, keeping pitch, lean and look smoothing (seamless teleports).
    public void RotateYaw(float degrees)
    {
        float yaw = YawTransform.eulerAngles.y + degrees;
        if (lateralTorso != null) lateralTorso.rotation = Quaternion.Euler(0f, yaw, CurrentLeanAngle);
        if (verticalNeck != null) verticalNeck.rotation = Quaternion.Euler(-pitch, yaw, -CurrentLeanAngle);
    }

    static float NormalizeAngle(float a) => a > 180f ? a - 360f : a;
}
