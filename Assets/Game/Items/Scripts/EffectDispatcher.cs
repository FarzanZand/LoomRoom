using System.Linq;
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
        // Snapshot: an effect may equip or unequip while we iterate.
        foreach (var item in equipment.EquippedItems.ToArray())
        {
            if (!ItemEffectProcessor.HasTrigger(item, EffectTrigger.OnHitLanded)) continue;
            var ctx = EffectContext.For(character, item);
            ctx.Target = hit.Target;
            ctx.Point  = hit.HitPoint;
            ctx.Hit    = hit;
            ItemEffectProcessor.Fire(item, EffectTrigger.OnHitLanded, ctx);
        }
    }

    void OnHurt(DamageInfo hit)
    {
        // Effect damage never procs OnHurt, so two thorns wearers can't bounce damage forever.
        if (equipment == null || hit.FromEffect) return;
        foreach (var item in equipment.EquippedItems.ToArray())
        {
            if (!ItemEffectProcessor.HasTrigger(item, EffectTrigger.OnHurt)) continue;
            var ctx = EffectContext.For(character, item);
            ctx.Target = hit.Source;
            ctx.Point  = hit.HitPoint;
            ctx.Hit    = hit;
            ItemEffectProcessor.Fire(item, EffectTrigger.OnHurt, ctx);
        }
    }
}
