using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

// Reuses short-lived objects (hit particles, debris, coins, projectiles) instead of
// Instantiate/Destroy on every hit. One pool per prefab, created on first use.
//
// Spawn with a lifetime to have the object return itself; otherwise call Release.
// Callers that may run without the manager use the static SpawnOrInstantiate, which
// falls back to plain Instantiate/Destroy so nothing breaks in isolated test scenes.
public class PoolManager : Singleton<PoolManager>
{
    [Tooltip("Objects kept ready per prefab. Extra spawns beyond this are created and destroyed normally.")]
    [SerializeField, Min(1)] int maxPerPrefab = 48;
    [Tooltip("Prefabs to create ahead of time, so the first hit of a fight does not hitch.")]
    [SerializeField] List<Prewarm> prewarm = new();

    [System.Serializable]
    public class Prewarm
    {
        public GameObject prefab;
        [Min(1)] public int count = 8;
    }

    readonly Dictionary<GameObject, ObjectPool<GameObject>> pools = new();
    Transform storage;

    protected override void Awake()
    {
        base.Awake();
        storage = new GameObject("Pooled (inactive)").transform;
        storage.SetParent(transform, false);
        foreach (var p in prewarm)
        {
            if (p?.prefab == null) continue;
            var pool = PoolFor(p.prefab);
            var warm = new List<GameObject>();
            for (int i = 0; i < p.count; i++) warm.Add(pool.Get());
            foreach (var go in warm) pool.Release(go);
        }
    }

    ObjectPool<GameObject> PoolFor(GameObject prefab)
    {
        if (pools.TryGetValue(prefab, out var pool)) return pool;
        pool = new ObjectPool<GameObject>(
            createFunc: () =>
            {
                var go = Instantiate(prefab, storage);
                var tag = go.GetComponent<PooledObject>();
                if (tag == null) tag = go.AddComponent<PooledObject>();
                tag.Source = prefab;
                return go;
            },
            actionOnGet: go => go.SetActive(true),
            actionOnRelease: go =>
            {
                go.SetActive(false);
                go.transform.SetParent(storage, false);
            },
            actionOnDestroy: go => { if (go != null) Destroy(go); },
            collectionCheck: false,
            defaultCapacity: 8,
            maxSize: maxPerPrefab);
        pools[prefab] = pool;
        return pool;
    }

    // lifetime <= 0 keeps the object until Release is called.
    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime = -1f, Transform parent = null)
    {
        if (prefab == null) return null;
        var go = PoolFor(prefab).Get();
        var t = go.transform;
        t.SetParent(parent, true);
        t.SetPositionAndRotation(position, rotation);
        t.localScale = prefab.transform.localScale;
        var tag = go.GetComponent<PooledObject>();
        tag.Generation++;
        foreach (var ps in tag.Particles)
        {
            ps.Clear(true);
            ps.Play(true);
        }
        if (lifetime > 0f) StartCoroutine(ReleaseLater(go, tag.Generation, lifetime));
        return go;
    }

    IEnumerator ReleaseLater(GameObject go, int generation, float delay)
    {
        yield return new WaitForSeconds(delay);
        // Skip if it was already released and handed out again in the meantime.
        if (go != null && go.GetComponent<PooledObject>().Generation == generation) Release(go);
    }

    public void Release(GameObject go)
    {
        if (go == null) return;
        var tag = go.GetComponent<PooledObject>();
        if (tag == null || tag.Source == null || !pools.TryGetValue(tag.Source, out var pool)) { Destroy(go); return; }
        if (!go.activeSelf) return;
        tag.Generation++;
        pool.Release(go);
    }

    // Pooled when the manager exists, plain Instantiate/Destroy otherwise.
    public static GameObject SpawnOrInstantiate(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime, Transform parent = null)
    {
        if (prefab == null) return null;
        if (HasInstance) return Instance.Spawn(prefab, position, rotation, lifetime, parent);
        var go = Object.Instantiate(prefab, position, rotation, parent);
        if (lifetime > 0f) Object.Destroy(go, lifetime);
        return go;
    }

    public static void ReleaseOrDestroy(GameObject go)
    {
        if (go == null) return;
        if (HasInstance && go.GetComponent<PooledObject>() != null) Instance.Release(go);
        else Object.Destroy(go);
    }
}
