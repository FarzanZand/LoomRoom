using System.Collections.Generic;
using UnityEngine;

// One of a sleeping pack (DungeonRoomFeatures.sleepingPacks): put to sleep when the room is built and woken by noise it
// hears at a fraction of the usual range (a walking step close by, a running one further off, a fight, a chest; a
// crouching step never), by a hit, or when one of its pack wakes. Sleepers are unaware, so every first blow on one is a
// backstab. Bats roost under the ceiling (FlyingEnemy.RoostHeight).
[RequireComponent(typeof(EnemyBrain))]
public class EnemySleep : MonoBehaviour
{
    [Tooltip("Share of a sound's reach a sleeper hears it at.")]
    public float hearing = .5f;
    [Tooltip("Seconds it takes to get up once woken.")]
    public float riseSeconds = 1.1f;
    [Tooltip("Seconds between packmates waking, one after another.")]
    public Vector2 packDelay = new(.25f, .9f);

    EnemyBrain brain;
    List<EnemySleep> pack;
    AudioClip snore;
    float wakeAt = -1f, snoreAt;

    // Puts every enemy in the list to sleep as one pack. roostHeight: under the room's ceiling, for flyers.
    public static void SleepPack(List<Character> enemies, float roostHeight, AudioClip snore = null)
    {
        var pack = new List<EnemySleep>();
        foreach (var c in enemies)
        {
            if (c == null || !c.TryGetComponent<EnemyBrain>(out _)) continue;
            var s = c.gameObject.AddComponent<EnemySleep>();
            s.pack = pack; s.snore = snore;
            pack.Add(s);
            if (c.TryGetComponent<FlyingEnemy>(out var flyer)) flyer.RoostHeight = roostHeight;
        }
    }

    void Awake() => brain = GetComponent<EnemyBrain>();

    void Start()
    {
        brain.Sleep();
        snoreAt = Time.time + Random.Range(1f, 6f);
    }

    void OnEnable()
    {
        NoiseEvents.Noise += OnNoise;
        if (brain != null && brain.Character != null) brain.Character.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        NoiseEvents.Noise -= OnNoise;
        if (brain != null && brain.Character != null) brain.Character.Damaged -= OnDamaged;
    }

    void OnNoise(Vector3 position, float radius, Character source)
    {
        if (!brain.Asleep) return;
        if (source != null && source == brain.Character) return;
        float reach = radius * hearing;
        if ((position - transform.position).sqrMagnitude <= reach * reach) WakeNow();
    }

    // The brain wakes itself on the hit; the pack follows.
    void OnDamaged(DamageInfo info) => RousePack();

    void WakeNow()
    {
        if (!brain.Asleep) return;
        brain.Wake(riseSeconds);
        RousePack();
    }

    void RousePack()
    {
        if (pack == null) return;
        float t = Time.time;
        foreach (var other in pack)
        {
            if (other == null || other == this || !other.brain.Asleep || other.wakeAt >= 0f) continue;
            t += Random.Range(packDelay.x, packDelay.y);
            other.wakeAt = t;
        }
    }

    void Update()
    {
        if (wakeAt >= 0f && Time.time >= wakeAt) { wakeAt = -1f; WakeNow(); }
        // A low snore now and then (one creature of the pack at a time is enough to hear).
        if (brain.Asleep && snore != null && Time.time >= snoreAt && AudioManager.HasInstance)
        {
            snoreAt = Time.time + Random.Range(5f, 11f);
            AudioManager.Instance.PlaySFX(snore, transform.position + Vector3.up * .6f, .35f, .12f);
        }
        if (!brain.Asleep && wakeAt < 0f) enabled = false;
    }
}
