using System.Collections;
using UnityEngine;

// The opening: screen fades from black while the wake-up timeline plays and a short
// music sting runs underneath. The room player is placed at the start marker first.
public class WakeUpCutsceneController : CutsceneController
{
    [Header("Wake Up")]
    [SerializeField] Transform startPositionRoom;
    [SerializeField] float screenFadeDuration = 2f;
    [SerializeField] float screenFadeHold     = 2f;
    [Tooltip("Music library key played after a short delay.")]
    [SerializeField] string musicKey = "groundhog";
    [SerializeField] float musicDelay    = 1f;
    [SerializeField] float musicDuration = 12f;

    protected override void OnPlay()
    {
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeOut(screenFadeDuration, screenFadeHold);

        if (PlayerManager.HasInstance && startPositionRoom != null)
        {
            var ctx = PlayerManager.Instance.Get(PlayerKind.Room);
            var target = ctx != null && ctx.player != null ? ctx.player.transform : PlayerManager.Instance.roomPlayer?.transform;
            if (target != null)
            {
                target.SetPositionAndRotation(startPositionRoom.position, startPositionRoom.rotation);
                ctx?.player?.Look?.SetYaw(startPositionRoom.eulerAngles.y);
            }
        }

        if (!string.IsNullOrEmpty(musicKey)) StartCoroutine(MusicRoutine());
    }

    IEnumerator MusicRoutine()
    {
        yield return new WaitForSeconds(musicDelay);
        if (AudioManager.HasInstance) AudioManager.Instance.PlayMusic(musicKey);
        yield return new WaitForSeconds(musicDuration);
        if (AudioManager.HasInstance) AudioManager.Instance.StopMusic(1f);
    }
}
