using System;
using DG.Tweening;
using UnityEngine;
using Random = UnityEngine.Random;

public enum DestructiblePlacement { Floor = 0, Corner = 1 }

// Barrels, crates, pots and cobwebs: burst into pooled debris and maybe drop supplies and coins.
// Most break in one hit; sturdier ones shake, chip and show their health on each hit.
// Loot and floor come from the generator (Barrel source).
public class DungeonDestructible : MonoBehaviour, IDamageable
{
    [Min(.1f), Tooltip("1 breaks in one hit. Ignored when Hits To Break is set.")] public float health = 1f;
    [Tooltip("Breaks after a number of hits picked at random in this range (per object, from its seed), whatever the damage. 0 uses Health.")]
    public Vector2Int hitsToBreak = Vector2Int.zero;
    [Tooltip("Shown over its health bar. Empty uses the object name.")] public string displayName;
    [Tooltip("Floor objects fill supply spots; Corner objects (cobwebs) hang in room corners.")]
    public DestructiblePlacement placement = DestructiblePlacement.Floor;
    [Tooltip("The visible model, hidden when broken. Empty hides every renderer.")]
    public GameObject model;
    public AudioClip breakClip;
    [Range(0, 1)] public float breakVolume = .9f;
    [Tooltip("A hit that doesn't break it, picked from several clips. Empty uses Hit Clip.")] public AudioData hitSound;
    [Tooltip("A hit that doesn't break it. Empty uses Break Clip, quieter.")] public AudioClip hitClip;
    [Range(0, 2), Tooltip("Hit stop when the player strikes it, against an enemy hit (light or heavy). 0 is none.")]
    public float hitStopScale = .7f;
    [Range(0, 1)] public float hitVolume = .6f;
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

    [HideInInspector] public LootSource loot;
    [HideInInspector] public int seed;
    [HideInInspector] public int floorNumber = 1;
    [HideInInspector] public ItemData guaranteedItem;

    // A hit that didn't break it (for health bars and numbers).
    public static event Action<DungeonDestructible, DamageInfo> Hit;

    bool broken;
    float maxHealth;
    public bool Broken => broken;
    public float HealthFraction => maxHealth > 0 ? Mathf.Clamp01(health / maxHealth) : 0;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name.Replace("(Clone)", "").Trim() : displayName;
    public Vector3 Top { get { var b = Bounds(); return new Vector3(b.center.x, b.max.y, b.center.z); } }

    bool HitCounted => hitsToBreak.y > 0;
    bool started;

    void Awake() => maxHealth = health;

    // Hits To Break is rolled on the first hit: the generator sets the seed after Awake.
    void Begin()
    {
        if (started) return;
        started = true;
        if (!HitCounted) return;
        int min = Mathf.Max(1, hitsToBreak.x), max = Mathf.Max(min, hitsToBreak.y);
        health = maxHealth = new System.Random(unchecked(seed * 31 + 977)).Next(min, max + 1);
    }

    // True when the damage was counted: a hit-counted object takes one point per hit.
    public bool CountsHits => HitCounted;

    public void TakeDamage(DamageInfo info)
    {
        if (broken || info.Amount <= 0f) return;
        Begin();
        health -= HitCounted ? 1f : info.Amount;
        if (hitStopScale > 0f && CombatManager.HasInstance && info.Source is Player)
            CombatManager.Instance.RequestHitStop(info.Heavy, hitStopScale);
        if (health <= 0f) { Break(info.Direction); return; }
        var center = Bounds().center;
        if (AudioManager.HasInstance)
        {
            if (hitSound != null) AudioManager.Instance.PlaySFXData(hitSound, center);
            else if (hitClip != null) AudioManager.Instance.PlaySFX(hitClip, center, hitVolume, .08f);
            else if (breakClip != null) AudioManager.Instance.PlaySFX(breakClip, center, breakVolume * .5f, 1.25f, .08f, 1, 20);
        }
        Throw(center, info.Direction, Mathf.Min(2, debrisCount), .6f);
        var shake = model != null ? model.transform : transform;
        shake.DOKill(true);
        shake.DOPunchRotation(new Vector3(6, 0, 6), .25f, 12);
        Hit?.Invoke(this, info);
    }

    Bounds Bounds()
    {
        var renderers = model != null ? model.GetComponentsInChildren<Renderer>() : GetComponentsInChildren<Renderer>();
        return DungeonFeatureUtility.RendererBounds(renderers, out var bounds) ? bounds : new Bounds(transform.position + Vector3.up * .4f, Vector3.one * .8f);
    }

    void Throw(Vector3 center, Vector3 direction, int count, float force)
    {
        if (debrisPrefab == null) return;
        for (int i = 0; i < count; i++)
        {
            var piece = PoolManager.SpawnOrInstantiate(debrisPrefab, center + Random.insideUnitSphere * .2f, Random.rotation, debrisLifetime);
            var rb = piece != null ? piece.GetComponent<Rigidbody>() : null;
            if (rb == null) continue;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            Vector3 push = (Random.insideUnitSphere + Vector3.up * .9f + direction * .6f).normalized * debrisForce * force;
            rb.AddForce(push, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * debrisForce * force, ForceMode.Impulse);
        }
    }

    public void Break(Vector3 direction = default)
    {
        if (broken) return;
        broken = true;
        Vector3 center = Bounds().center;
        if (AudioManager.HasInstance && breakClip != null) AudioManager.Instance.PlaySFX(breakClip, center, breakVolume, .08f);
        if (noiseRadius > 0f) NoiseEvents.Report(center, noiseRadius);
        if (breakEffect != null) PoolManager.SpawnOrInstantiate(breakEffect, center, Quaternion.identity, 3f);
        Throw(center, direction, debrisCount, 1);
        (model != null ? model.transform : transform).DOKill();
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
                var (items, gold) = DungeonLootDrop.Roll(loot, rng, floorNumber, DungeonLootSource.Barrel);
                DungeonLootDrop.Spill(items, gold, drop, transform.parent);
            }
        }
        Destroy(gameObject, .1f);
    }
}
