using UnityEngine;

public enum DestructiblePlacement { Floor = 0, Corner = 1 }

// Barrels, crates, pots and cobwebs: take a few hits, burst into pooled debris and maybe
// drop supplies and coins. Loot and floor come from the generator (Barrel source).
public class DungeonDestructible : MonoBehaviour, IDamageable
{
    [Min(.1f)] public float health = 8f;
    [Tooltip("Floor objects fill supply spots; Corner objects (cobwebs) hang in room corners.")]
    public DestructiblePlacement placement = DestructiblePlacement.Floor;
    [Tooltip("The visible model, hidden when broken. Empty hides every renderer.")]
    public GameObject model;
    public AudioClip breakClip;
    [Range(0, 1)] public float breakVolume = .9f;
    [Tooltip("Pooled pieces thrown outward when broken (small rigid bodies).")]
    public GameObject debrisPrefab;
    [Range(0, 16)] public int debrisCount = 6;
    [Min(0)] public float debrisForce = 2.2f;
    [Min(.1f)] public float debrisLifetime = 3f;
    [Tooltip("Pooled one-shot effect (dust, splinters, web wisps).")]
    public GameObject breakEffect;
    [Tooltip("Rolls rewards when broken.")]
    public bool dropsLoot = true;
    [Tooltip("Noise radius when broken; enemies within it investigate.")]
    [Min(0)] public float noiseRadius = 6f;

    [HideInInspector] public DungeonLootTable loot;
    [HideInInspector] public int seed;
    [HideInInspector] public int floorNumber = 1;
    [HideInInspector] public ItemData guaranteedItem;

    bool broken;
    public bool Broken => broken;

    public void TakeDamage(DamageInfo info)
    {
        if (broken || info.Amount <= 0f) return;
        health -= info.Amount;
        if (health <= 0f) Break(info.Direction);
    }

    public void Break(Vector3 direction = default)
    {
        if (broken) return;
        broken = true;
        Vector3 center = transform.position + Vector3.up * .4f;
        var renderers = model != null ? model.GetComponentsInChildren<Renderer>() : GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            center = bounds.center;
        }
        if (AudioManager.HasInstance && breakClip != null) AudioManager.Instance.PlaySFX(breakClip, center, breakVolume, .08f);
        if (noiseRadius > 0f) NoiseEvents.Report(center, noiseRadius);
        if (breakEffect != null) PoolManager.SpawnOrInstantiate(breakEffect, center, Quaternion.identity, 3f);
        if (debrisPrefab != null)
            for (int i = 0; i < debrisCount; i++)
            {
                var piece = PoolManager.SpawnOrInstantiate(debrisPrefab, center + Random.insideUnitSphere * .2f, Random.rotation, debrisLifetime);
                var rb = piece != null ? piece.GetComponent<Rigidbody>() : null;
                if (rb == null) continue;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                Vector3 push = (Random.insideUnitSphere + Vector3.up * .9f + direction * .6f).normalized * debrisForce;
                rb.AddForce(push, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * debrisForce, ForceMode.Impulse);
            }
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (model != null) model.SetActive(false);
        else foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;

        if (dropsLoot)
        {
            var rng = new System.Random(seed);
            var drop = transform.position;
            if (guaranteedItem != null) DungeonPickup.Spawn(guaranteedItem, drop, transform.parent);
            if (loot != null)
            {
                var items = loot.RollDrops(rng, floorNumber, DungeonLootSource.Barrel);
                int gold = loot.RollGold(rng, floorNumber, DungeonLootSource.Barrel);
                DungeonLootDrop.Spill(items, gold, drop, transform.parent);
            }
        }
        Destroy(gameObject, .1f);
    }
}
