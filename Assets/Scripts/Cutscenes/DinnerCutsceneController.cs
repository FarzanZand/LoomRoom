using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;

public class DinnerCutsceneController : MonoBehaviour
{
    [SerializeField] PlayableDirector director;
    [SerializeField] Transform roomPlayerDinner;
    [SerializeField] CinemachineCamera dinnerCamera;
    [Tooltip("Priority given to DinnerCamera while the cutscene is playing.")]
    [SerializeField] int cutsceneCameraPriority = 100;

    void Awake()
    {
        if (director == null) director = GetComponent<PlayableDirector>();
    }

    void OnEnable()  => director.stopped += OnCutsceneFinished;
    void OnDisable() => director.stopped -= OnCutsceneFinished;

    public void Play()
    {
        if (dinnerCamera != null)
        {
            var p = dinnerCamera.Priority;
            p.Enabled = true;
            p.Value   = cutsceneCameraPriority;
            dinnerCamera.Priority = p;
        }

        PlayerManager.Instance?.SetControlsFrozen(true);
        director.Play();
    }

    void OnCutsceneFinished(PlayableDirector _)
    {
        if (dinnerCamera != null)
        {
            var p = dinnerCamera.Priority;
            p.Enabled = false;
            dinnerCamera.Priority = p;
        }

        var pm = PlayerManager.Instance;
        if (pm == null || roomPlayerDinner == null) return;

        var roomPlayer = pm.roomPlayer;
        if (roomPlayer != null)
            roomPlayer.transform.SetPositionAndRotation(
                roomPlayerDinner.position,
                roomPlayerDinner.rotation);

        pm.ForceSwapToPlayer(PlayerManager.ActivePlayer.RoomPlayer);
        pm.SetControlsFrozen(false);
    }
}
