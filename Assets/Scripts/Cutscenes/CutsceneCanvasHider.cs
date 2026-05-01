using UnityEngine;
using UnityEngine.Playables;

public class CutsceneCanvasHider : MonoBehaviour
{
    [SerializeField] private Canvas canvas;
    [SerializeField] private PlayableDirector[] directors;

    void OnEnable()
    {
        foreach (var d in directors)
        {
            if (d == null) continue;
            d.played  += OnCutsceneStarted;
            d.stopped += OnCutsceneStopped;
        }
    }

    void OnDisable()
    {
        foreach (var d in directors)
        {
            if (d == null) continue;
            d.played  -= OnCutsceneStarted;
            d.stopped -= OnCutsceneStopped;
        }
    }

    private void OnCutsceneStarted(PlayableDirector _)
    {
        canvas.enabled = false;
        if (MFPC.PlayerController.instance != null)
            MFPC.PlayerController.instance.enabled = false;
    }

    private void OnCutsceneStopped(PlayableDirector _)
    {
        canvas.enabled = true;
        if (MFPC.PlayerController.instance != null)
            MFPC.PlayerController.instance.enabled = true;
    }
}
