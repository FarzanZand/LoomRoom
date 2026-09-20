using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

// Runtime swaps configure the shared camera through PlayerManager. In edit mode,
// Cinemachine can preview either player without running that initialization.
[InitializeOnLoad]
public static class PlayerCameraPreview
{
    static PlayerCameraPreview()
    {
        CinemachineCore.CameraUpdatedEvent.AddListener(UpdatePreview);
        AssemblyReloadEvents.beforeAssemblyReload += Unsubscribe;
    }

    static void Unsubscribe()
        => CinemachineCore.CameraUpdatedEvent.RemoveListener(UpdatePreview);

    static void UpdatePreview(CinemachineBrain brain)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || brain == null) return;
        var cameraObject = brain.ActiveVirtualCamera as CinemachineVirtualCameraBase;
        var player = cameraObject != null ? cameraObject.GetComponentInParent<Player>() : null;
        var view = player != null ? player.ViewPresentation : null;
        var output = brain.OutputCamera;
        if (view == null || output == null) return;

        // Follow the live camera's owner without changing Cinemachine channel routing.
        output.cullingMask = view.cullingMask;
        output.nearClipPlane = view.nearClip;
        output.farClipPlane = view.farClip;
    }
}
