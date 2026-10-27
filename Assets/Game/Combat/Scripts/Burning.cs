using UnityEngine;

// A character on fire: a magic hit every second for a few seconds, from whoever lit it (so kills and practice count for
// them), with a flame on its body. Lighting it again starts the time over and keeps the stronger fire. Sorcery's
// Burning milestone sets a Fireball's targets alight (SpellProjectile).
public class Burning : MonoBehaviour
{
    Character target, source;
    float perSecond, nextTick;
    int ticksLeft;
    GameObject flame;

    public static void Ignite(Character target, Character source, float perSecond, float seconds, GameObject flamePrefab)
    {
        if (target == null || !target.IsAlive || perSecond <= 0f || seconds <= 0f) return;
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
        target.Stats.TakeDamage(new DamageInfo
        {
            Amount = perSecond, Source = source, Magic = true, FromEffect = true, SourceName = "fire",
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
