using System.Collections;
using UnityEngine;

// The opening: screen fades from black while the wake-up timeline plays and a short
// music sting runs underneath. The room player is placed at the start marker first.
// After a death the run recap plays it again, brief: the player wakes in bed for the next run.
// In the Room scene, this rig and its start marker are children of the bed so
// designers can move or rotate the bed without retiming the local Timeline animation.
public class WakeUpCutsceneController : CutsceneController
{
    [Header("Wake Up")]
    [Tooltip("Bed-relative player start marker. Keep this marker and the wake-up rig under the bed when editing its placement.")]
    [SerializeField] Transform startPositionRoom;
    [SerializeField] float screenFadeDuration = 2f;
    [SerializeField] float screenFadeHold     = 2f;
    [Tooltip("The eyes opening on a full wake-up: (alpha, seconds) steps from black, eased; a negative alpha holds. Empty uses the plain fade above.")]
    [SerializeField] Vector2[] eyesOpening =
    {
        new(-1f, 1.2f), new(.55f, .9f), new(.95f, .22f), new(.3f, .7f), new(.85f, .14f), new(0f, .65f),
    };
    [Tooltip("Music library key played after a short delay.")]
    [SerializeField] string musicKey = "groundhog";
    [SerializeField] float musicDelay    = 1f;
    [SerializeField] float musicDuration = 12f;

    [Header("Brief (waking again after a death)")]
    [Tooltip("Seconds of the timeline's end that a brief wake-up plays.")]
    [SerializeField, Min(0)] float briefSeconds = 3f;
    [SerializeField, Min(0)] float briefFadeDuration = .8f;
    [SerializeField, Min(0)] float briefFadeHold = .4f;
    [SerializeField, Min(0)] float briefMusicDuration = 4f;

    bool brief;

    // When the player can move again; true after a brief (death) wake-up.
    public event System.Action<bool> Woke;

    // The full opening, or the short version for every morning after a death.
    public void Play(bool brief)
    {
        if (IsPlaying) return;
        this.brief = brief;
        Play();
        if (brief && director != null && IsPlaying)
        {
            director.time = Mathf.Max(0f, (float)director.duration - briefSeconds);
            director.Evaluate();
        }
    }

    protected override void OnFinished()
    {
        bool wasBrief = brief;
        brief = false;
        Woke?.Invoke(wasBrief);
    }

    protected override void OnPlay()
    {
        if (ScreenManager.HasInstance)
        {
            if (!brief && eyesOpening != null && eyesOpening.Length > 0) { ScreenManager.Instance.FadeToBlack(0f); ScreenManager.Instance.FadeSteps(eyesOpening); }
            else ScreenManager.Instance.FadeFromBlack(brief ? briefFadeDuration : screenFadeDuration, brief ? briefFadeHold : screenFadeHold);
        }

        if (PlayerManager.HasInstance && startPositionRoom != null)
        {
            var room = PlayerManager.Instance.GetPlayer(PlayerKind.Room);
            if (room != null) room.Warp(startPositionRoom.position, startPositionRoom.rotation);
        }

        if (!string.IsNullOrEmpty(musicKey))
        {
            if (music != null) StopCoroutine(music);
            music = StartCoroutine(MusicRoutine(brief ? 0f : musicDelay, brief ? briefMusicDuration : musicDuration));
        }
    }

    Coroutine music;

    IEnumerator MusicRoutine(float delay, float duration)
    {
        yield return new WaitForSeconds(delay);
        if (!AudioManager.HasInstance) yield break;
        AudioManager.Instance.PlayMusic(musicKey);
        var clip = AudioManager.Instance.CurrentMusic;
        yield return new WaitForSeconds(duration);
        // Leave it alone if the player has already sat down and the table's music took over.
        if (AudioManager.HasInstance && AudioManager.Instance.CurrentMusic == clip) AudioManager.Instance.StopMusic(1f);
    }
}
