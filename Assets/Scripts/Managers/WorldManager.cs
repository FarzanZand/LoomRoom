using System.Collections;
using UnityEngine;
using UnityEngine.Playables;

public class WorldManager : Singleton<WorldManager>
{
    public PlayableDirector WakeUpDirector;
    public Transform startPositionRoom;

    public void Start()
    {
        WakeUpReset();
    }

    public void WakeUpReset()
    {
        Debug.Log("Wake up reset");
        ScreenManager.Instance.FadeOut(1.5f, 1.5f);
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
        var src = AudioManager.Instance.PlaySFX2D("groundhog");
        yield return new WaitForSeconds(10f);
        if (src != null) src.Stop();
    }
}
