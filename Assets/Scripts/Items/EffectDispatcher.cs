using UnityEngine;

// Fans a character's combat events out to the items it has equipped, so gear with
// OnHitLanded / OnHurt effects works with no per-item code. Equipment adds this
// automatically; nothing needs to carry it in a prefab.
[RequireComponent(typeof(Character))]
public class EffectDispatcher : MonoBehaviour
{
    Character character;
    Equipment equipment;

    public static EffectDispatcher Ensure(GameObject go)
    {
        var d = go.GetComponent<EffectDispatcher>();
        return d != null ? d : go.AddComponent<EffectDispatcher>();
    }

    void Awake()
    {
        character = GetComponent<Character>();
        equipment = GetComponent<Equipment>();
    }

    void OnEnable()
    {
        character.HitLanded += OnHitLanded;
        character.Damaged   += OnHurt;
    }

    void OnDisable()
    {
        character.HitLanded -= OnHitLanded;
        character.Damaged   -= OnHurt;
    }

    void OnHitLanded(DamageInfo hit)
    {
        if (equipment == null) return;
        foreach (var item in equipment.EquippedItems)
        {
            if (!ItemEffectProcessor.HasTrigger(item, EffectTrigger.OnHitLanded)) continue;
            var ctx = EffectContext.For(character, item);
            ctx.Target = hit.Source == character ? null : FindVictim(hit);
            ctx.Point  = hit.HitPoint;
            ctx.Hit    = hit;
            ItemEffectProcessor.Fire(item, EffectTrigger.OnHitLanded, ctx);
        }
    }

    void OnHurt(DamageInfo hit)
    {
        if (equipment == null) return;
        foreach (var item in equipment.EquippedItems)
        {
            if (!ItemEffectProcessor.HasTrigger(item, EffectTrigger.OnHurt)) continue;
            var ctx = EffectContext.For(character, item);
            ctx.Target = hit.Source;
            ctx.Point  = hit.HitPoint;
            ctx.Hit    = hit;
            ItemEffectProcessor.Fire(item, EffectTrigger.OnHurt, ctx);
        }
    }

    // The DamageInfo carries the attacker, not the victim; for OnHitLanded the victim is
    // whoever is at the hit point. Cheap lookup that is good enough for effects.
    static Character FindVictim(DamageInfo hit)
    {
        var cols = Physics.OverlapSphere(hit.HitPoint, 0.5f);
        foreach (var c in cols)
        {
            var ch = c.GetComponentInParent<Character>();
            if (ch != null && ch != hit.Source) return ch;
        }
        return null;
    }
}
