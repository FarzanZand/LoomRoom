using System.Collections;
using UnityEngine;

// The opening: screen fades from black while the wake-up timeline plays and a short
// music sting runs underneath. The room player is placed at the start marker first.
// In the Room scene, this rig and its start marker are children of the bed so
// designers can move or rotate the bed without retiming the local Timeline animation.
public class WakeUpCutsceneController : CutsceneController
{
    [Header("Wake Up")]
    [Tooltip("Bed-relative player start marker. Keep this marker and the wake-up rig under the bed when editing its placement.")]
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
            var room = PlayerManager.Instance.GetPlayer(PlayerKind.Room);
            if (room != null)
            {
                room.Warp(startPositionRoom.position);
                room.transform.rotation = startPositionRoom.rotation;
                room.Look?.SetYaw(startPositionRoom.eulerAngles.y);
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
