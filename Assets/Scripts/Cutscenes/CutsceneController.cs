using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;

// Base for Timeline-driven cutscenes: pushes the Cutscene game state (which freezes
// input and hides the HUD), raises a camera's priority, plays the director and
// cleans up when it stops. Subclass for anything that needs to happen at the end.
public class CutsceneController : MonoBehaviour
{
    [Header("Playback")]
    [SerializeField] protected PlayableDirector director;
    [Tooltip("Camera raised to Cutscene Priority while playing. Optional.")]
    [SerializeField] protected CinemachineCamera cutsceneCamera;
    [SerializeField] protected int cutsceneCameraPriority = 100;
    [Tooltip("Push the Cutscene game state (no input, HUD hidden) while playing.")]
    [SerializeField] protected bool freezePlayer = true;

    public UnityEvent onStarted;
    public UnityEvent onFinished;

    public bool IsPlaying { get; private set; }

    protected virtual void Awake()
    {
        if (director == null) director = GetComponent<PlayableDirector>();
    }

    protected virtual void OnEnable()
    {
        if (director != null) director.stopped += OnDirectorStopped;
    }

    protected virtual void OnDisable()
    {
        if (director != null) director.stopped -= OnDirectorStopped;
    }

    public virtual void Play()
    {
        if (IsPlaying) return;
        IsPlaying = true;

        SetCameraPriority(true);
        if (freezePlayer && GameManager.HasInstance) GameManager.Instance.Push(GameState.Cutscene);

        onStarted?.Invoke();
        OnPlay();

        if (director != null) director.Play();
        else Finish();
    }

    public virtual void Stop()
    {
        if (director != null && IsPlaying) director.Stop();
    }

    protected virtual void OnPlay() { }
    protected virtual void OnFinished() { }

    void OnDirectorStopped(PlayableDirector _) => Finish();

    void Finish()
    {
        if (!IsPlaying) return;
        IsPlaying = false;
        SetCameraPriority(false);
        OnFinished();
        if (freezePlayer && GameManager.HasInstance) GameManager.Instance.Pop(GameState.Cutscene);
        onFinished?.Invoke();
    }

    void SetCameraPriority(bool active)
    {
        if (cutsceneCamera == null) return;
        var p = cutsceneCamera.Priority;
        p.Enabled = active;
        if (active) p.Value = cutsceneCameraPriority;
        cutsceneCamera.Priority = p;
    }
}
