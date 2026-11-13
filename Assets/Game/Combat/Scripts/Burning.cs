using UnityEngine;

// A character on fire: a magic hit every second for a few seconds, from whoever lit it (so kills and practice count for
// them), with a flame on its body. Lighting it again starts the time over and keeps the stronger fire. A spell with a
// burn (SpellDefinition.burn: the Fireball, half its hit over 5 seconds) sets what it hits alight (SpellProjectile). Each
// tick lands a whole number and what rounding left over is carried to the next, so the ticks add up to the fire's whole
// (2.1 a second: 2, 2, 2, 2, 3). The flame's BurnCoat chars the body while it burns.
public class Burning : MonoBehaviour
{
    Character target, source;
    float perSecond, nextTick, owed;
    int ticksLeft;

    // Seconds until the last tick (BurnCoat lets the body cool just before).
    public float SecondsLeft => ticksLeft <= 0 ? 0f : nextTick - Time.time + (ticksLeft - 1);
    GameObject flame;

    public static void Ignite(Character target, Character source, float perSecond, float seconds, GameObject flamePrefab)
    {
        if (target == null || !target.IsAlive || perSecond <= 0f || seconds <= 0f || EventProtection.Held(target)) return;
        var fire = target.GetComponent<Burning>();
        if (fire == null) fire = target.gameObject.AddComponent<Burning>();
        fire.Light(target, source, perSecond, seconds, flamePrefab);
    }

    void Light(Character on, Character by, float damage, float seconds, GameObject flamePrefab)
    {
        bool fresh = target == null;
        target = on; source = by;
        perSecond = Mathf.Max(fresh ? 0f : perSecond, damage);
        ticksLeft = Mathf.Max(1, Mathf.RoundToInt(seconds));
        if (fresh) nextTick = Time.time + 1f;
        if (flame == null && flamePrefab != null)
        {
            // Around the chest, in the world (the prefab may be a first-person hand effect on the arms' layer).
            var chest = target.transform.position + Vector3.up * 1.1f;
            flame = Instantiate(flamePrefab, chest, Quaternion.identity, target.transform);
            foreach (var t in flame.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = target.gameObject.layer;
        }
    }

    void Update()
    {
        if (target == null || !target.IsAlive || ticksLeft <= 0) { PutOut(); return; }
        // The world stands still behind a shop, a conversation or a cutscene (time runs on there): so does the fire.
        if (GameManager.HasInstance && !GameManager.Instance.SimulationActive) { nextTick += Time.deltaTime; return; }
        if (Time.time < nextTick) return;
        nextTick += 1f;
        ticksLeft--;
        owed += perSecond;
        float amount = Mathf.Floor(owed + .5f);
        if (amount <= 0f) return;
        owed -= amount;
        target.Stats.TakeDamage(new DamageInfo
        {
            Amount = amount, Source = source, Magic = true, FromEffect = true, SourceName = "fire",
            HitPoint = target.transform.position + Vector3.up * 1.1f,
        });
    }

    void PutOut()
    {
        if (flame != null)
        {
            var visual = flame.GetComponent<SpellVisual>();
            if (visual != null) { visual.Stop(); flame.transform.SetParent(null, true); Destroy(flame, 2f); }
            else Destroy(flame);
        }
        Destroy(this);
    }

    void OnDestroy()
    {
        if (flame != null && flame.transform.parent == transform) Destroy(flame);
    }
}
