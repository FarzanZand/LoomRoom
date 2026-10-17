using Unity.Cinemachine;
using UnityEngine;

// Keeps first-person visuals inside their owning prefab while following the one output camera.
public class PlayerViewPresentation : MonoBehaviour
{
    public OutputChannels channels = OutputChannels.Default;
    public LayerMask cullingMask = -1;
    public float nearClip = .01f;
    public float farClip = 1000f;
    [Tooltip("The arms and held items are drawn as if through this field of view, whatever the world camera uses (the usual first-person view model). 0 = the camera's own.")]
    [Range(0, 120)] public float viewModelFieldOfView = 70f;
    Player player;
    OneBodyRig body;

    void Awake()
    {
        player = GetComponentInParent<Player>();
        body = player != null ? player.GetComponentInChildren<OneBodyRig>(true) : null;
    }
    void OnEnable() => CinemachineCore.CameraUpdatedEvent.AddListener(FollowCamera);
    void OnDisable() => CinemachineCore.CameraUpdatedEvent.RemoveListener(FollowCamera);

    public void Configure(Camera output)
    {
        if (output == null) return;
        output.cullingMask = cullingMask;
        output.nearClipPlane = nearClip;
        output.farClipPlane = farClip;
        var brain = output.GetComponent<CinemachineBrain>();
        if (brain != null) brain.ChannelMask = channels;
    }

    public void FollowTransitionCamera(Camera output, float lowerBy)
    {
        if (output == null) return;
        transform.SetPositionAndRotation(output.transform.TransformPoint(0, -lowerBy, 0), output.transform.rotation);
        FitViewModel(output);
    }

    // This transform sits on the camera: scaling it sideways and up (not in depth) by the ratio of the two
    // half-angle tangents projects everything under it exactly as through viewModelFieldOfView.
    void FitViewModel(Camera output)
    {
        float k = 1f;
        // A clip can widen or narrow it (OneBodyRig.ViewFovOffset, the converted Unreal combo).
        float viewModel = viewModelFieldOfView > 0f && body != null ? viewModelFieldOfView + body.ViewFovOffset : viewModelFieldOfView;
        if (viewModel > 0f && output.fieldOfView > 0f && !output.orthographic)
            k = Mathf.Tan(output.fieldOfView * .5f * Mathf.Deg2Rad) / Mathf.Tan(viewModel * .5f * Mathf.Deg2Rad);
        transform.localScale = new Vector3(k, k, 1f);
    }

    void FollowCamera(CinemachineBrain brain)
    {
        if (player == null || !player.IsActive || brain.OutputCamera == null ||
            !PlayerManager.HasInstance || brain.OutputCamera != PlayerManager.Instance.OutputCamera) return;
        transform.SetPositionAndRotation(brain.OutputCamera.transform.position, brain.OutputCamera.transform.rotation);
        FitViewModel(brain.OutputCamera);
    }
}
