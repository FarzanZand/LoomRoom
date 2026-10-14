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
    // Set while seated (TableManager): camera height above the feet instead of standing or crouching.
    public float? HeightOverride { get; set; }
    public float Pitch => pitch;

    Player  player;
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

        // The lock check is read only: it also catches the Editor's Esc and a lost window focus,
        // which unlock the cursor without GameManager knowing.
        bool canLook = Cursor.lockState == CursorLockMode.Locked && input != null &&
            (!GameManager.HasInstance || GameManager.Instance.GameplayActive);
        Vector2 raw = canLook ? input.Look : Vector2.zero;
        if (settings != null && settings.invertLook) raw.y = -raw.y;

        // Applied as is: the view turns with the mouse, no smoothing.
        Vector2 look = raw * sensitivity;

        // ── Rotation ──
        float yaw = YawTransform.eulerAngles.y + look.x;
        pitch = Mathf.Clamp(pitch + look.y, minClamp, maxClamp);

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
        float desiredY = HeightOverride ?? (crouching ? crouchCameraHeight : normalCameraHeight);
        float t = 1f - Mathf.Exp(-crouchingCameraSmoothTime * Time.deltaTime);
        cameraHeight.y = Mathf.Lerp(cameraHeight.y, desiredY, t);
        if (lateralTorso != null) lateralTorso.localPosition = cameraHeight;
    }

    // Snap the view to a world yaw (cutscenes, teleports).
    public void SetYaw(float worldYaw)
    {
        if (lateralTorso != null) lateralTorso.rotation = Quaternion.Euler(0f, worldYaw, 0f);
        if (verticalNeck != null) verticalNeck.rotation = Quaternion.Euler(-pitch, worldYaw, 0f);
    }

    // Tilt the view (positive looks up), clamped like mouse look. Applied on the next Update.
    public void SetPitch(float degrees) => pitch = Mathf.Clamp(degrees, minClamp, maxClamp);

    // Turns the view about world up, keeping pitch and lean (seamless teleports).
    public void RotateYaw(float degrees)
    {
        float yaw = YawTransform.eulerAngles.y + degrees;
        if (lateralTorso != null) lateralTorso.rotation = Quaternion.Euler(0f, yaw, CurrentLeanAngle);
        if (verticalNeck != null) verticalNeck.rotation = Quaternion.Euler(-pitch, yaw, -CurrentLeanAngle);
    }

    static float NormalizeAngle(float a) => a > 180f ? a - 360f : a;
}
