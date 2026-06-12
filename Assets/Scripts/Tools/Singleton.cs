using UnityEngine;

// Bumps a session id on every play-mode start so Singleton<T> statics can detect
// stale state when domain reload is disabled (Enter Play Mode Options).
internal static class SingletonSession
{
    internal static int Id;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnSessionStart() => Id++;
}

public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    private static int _quitSessionId = -1;

    static bool ApplicationIsQuitting => _quitSessionId == SingletonSession.Id;

    public static T Instance
    {
        get
        {
            if (_instance != null)
                return _instance;

            // Don't create new objects during teardown (editor or build).
            // In the editor, also guard against creation after play mode ends.
            if (ApplicationIsQuitting || !Application.isPlaying)
                return null;

            _instance = FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (_instance == null)
            {
                var go = new GameObject(typeof(T).Name);
                _instance = go.AddComponent<T>();
            }

            return _instance;
        }
    }

    protected virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = this as T;
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    protected virtual void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    protected virtual void OnApplicationQuit()
    {
        _quitSessionId = SingletonSession.Id;
    }
}
