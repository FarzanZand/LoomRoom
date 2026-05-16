using System.Collections;
using UnityEngine;
using UnityEngine.Playables;

public class WorldManager : Singleton<WorldManager>
{
    public PlayableDirector WakeUpDirector;
    public Transform startPositionRoom;

    public void Start()
    {
        if(!ProgressionManager.Instance.skipWakeUp)
            WakeUpReset();
    }

    public void WakeUpReset()
    {
        ScreenManager.Instance.FadeOut(2f, 2f);
        PlayerManager.Instance.roomPlayer.transform.SetPositionAndRotation(startPositionRoom.position, startPositionRoom.rotation);

        var pc = PlayerManager.Instance.roomPlayer.GetComponentInChildren<MFPC.PlayerController>(true);
        if (pc != null) pc.enabled = false;

        WakeUpDirector.stopped += OnWakeUpComplete;
        WakeUpDirector.Play();
        StartCoroutine(PlayGroundhogAudio());
    }

    void OnWakeUpComplete(PlayableDirector _)
    {
        WakeUpDirector.stopped -= OnWakeUpComplete;
        if (PlayerManager.Instance == null || PlayerManager.Instance.roomPlayer == null) return;
        var pc = PlayerManager.Instance.roomPlayer.GetComponentInChildren<MFPC.PlayerController>(true);
        if (pc != null) pc.enabled = true;
    }

    IEnumerator PlayGroundhogAudio()
    {
        yield return new WaitForSeconds(1f);
        AudioManager.Instance.PlayMusic("groundhog");
        yield return new WaitForSeconds(12f);
        AudioManager.Instance.StopMusic(1f);
    }
}
