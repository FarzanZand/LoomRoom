using UnityEngine;

// Scene-owned singleton. Never auto-creates: a missing manager is a wiring mistake
// you want to see, not a blank object silently running with default settings.
// Managers live as prefabs under Assets/Game/Core/Prefabs and are placed in the scene.
//
// HasInstance only reports the cached field — use it for cheap null-safe reads in
// hot paths. Instance does a scene lookup on first access.
public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
#if !UNITY_EDITOR
    private static bool _applicationIsQuitting;
#endif

    public static bool HasInstance => _instance != null;

    public static T Instance
    {
        get
        {
            if (_instance != null)
                return _instance;

#if !UNITY_EDITOR
            if (_applicationIsQuitting)
                return null;
#endif
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying)
                return null;
#endif
            _instance = FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (_instance == null)
                Debug.LogError($"[Singleton] No {typeof(T).Name} in the open scene. Add it (the shared " +
                               "managers live as prefabs under Assets/Game/Core/Prefabs) — this returns null " +
                               "rather than auto-creating a blank one, which would silently run with default " +
                               "settings and no Inspector wiring.");
            return _instance;
        }
    }

    protected virtual void Awake()
    {
#if !UNITY_EDITOR
        _applicationIsQuitting = false;
#endif
        if (_instance == null)
            _instance = this as T;
        else if (_instance != this)
            Destroy(gameObject);
    }

    protected virtual void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    protected virtual void OnApplicationQuit()
    {
#if !UNITY_EDITOR
        _applicationIsQuitting = true;
#endif
    }
}
