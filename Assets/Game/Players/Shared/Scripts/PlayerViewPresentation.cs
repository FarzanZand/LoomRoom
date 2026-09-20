using Unity.Cinemachine;
using UnityEngine;

// Keeps first-person visuals inside their owning prefab while following the one output camera.
public class PlayerViewPresentation : MonoBehaviour
{
    public OutputChannels channels = OutputChannels.Default;
    public LayerMask cullingMask = -1;
    public float nearClip = .01f;
    public float farClip = 1000f;
    Player player;

    void Awake() => player = GetComponentInParent<Player>();
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

    void FollowCamera(CinemachineBrain brain)
    {
        if (player == null || !player.IsActive || brain.OutputCamera == null ||
            !PlayerManager.HasInstance || brain.OutputCamera != PlayerManager.Instance.OutputCamera) return;
        transform.SetPositionAndRotation(brain.OutputCamera.transform.position, brain.OutputCamera.transform.rotation);
    }
}
