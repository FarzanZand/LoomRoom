using System.Collections;
using UnityEngine;
using UnityEngine.Playables;

public class WorldManager : Singleton<WorldManager>
{
    public PlayableDirector WakeUpDirector;
    public Transform startPositionRoom;
    public bool skipWakeUp = false;

    public void Start()
    {
        if(!skipWakeUp)
            WakeUpReset();
    }

    public void WakeUpReset()
    {
        Debug.Log("Wake up reset");
        ScreenManager.Instance.FadeOut(2f, 2f);
        PlayerManager.Instance.roomPlayer.transform.SetPositionAndRotation(startPositionRoom.position, startPositionRoom.rotation);

        MFPC.PlayerController.instance.enabled = false;
        WakeUpDirector.stopped += OnWakeUpComplete;
        WakeUpDirector.Play();
        StartCoroutine(PlayGroundhogAudio());
    }

    void OnWakeUpComplete(PlayableDirector _)
    {
        WakeUpDirector.stopped -= OnWakeUpComplete;
        MFPC.PlayerController.instance.enabled = true;
    }

    IEnumerator PlayGroundhogAudio()
    {
        yield return new WaitForSeconds(1f);
        AudioManager.Instance.PlayMusic("groundhog");
        yield return new WaitForSeconds(12f);
        AudioManager.Instance.StopMusic(1f);
    }
}
