using UnityEngine;
using UnityEngine.Playables;

public class WorldManager : Singleton<WorldManager>
{
    public PlayableDirector WakeUpDirector;

    public void Start()
    {
        WakeUpReset();
    }
    
    public void WakeUpReset()
    {
        WakeUpDirector.Play();
    }
}
