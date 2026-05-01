using UnityEngine;

public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    private static bool _applicationIsQuitting;


    public static T Instance
    {
        get
        {
            if (_instance != null)
                return _instance;

            // In builds, respect the quit flag to avoid creating objects during shutdown.
            // In the editor, skip it — the flag is a static and persists as stale `true`
            // between play sessions, causing false nulls on the next play.
#if !UNITY_EDITOR
            if (_applicationIsQuitting)
                return null;
#endif
            _applicationIsQuitting = false;

            _instance = FindFirstObjectByType<T>(FindObjectsInactive.Include);
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
        _applicationIsQuitting = false;
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
        _applicationIsQuitting = true;
    }
}