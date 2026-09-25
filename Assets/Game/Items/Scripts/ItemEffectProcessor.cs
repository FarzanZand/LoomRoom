using UnityEngine;

// Who used what on whom. Target is null for self-only effects.
public struct EffectContext
{
    public Character  User;
    public Character  Target;
    public ItemData   Item;
    public Vector3    Point;
    public DamageInfo Hit;      // valid when the trigger came from a hit
    public object     ModifierSource;   // stat modifiers are tagged with this; null = a fresh ItemBuffSource per effect

    public static EffectContext For(Character user, ItemData item) =>
        new EffectContext { User = user, Item = item, Point = user != null ? user.transform.position : Vector3.zero };
}

// Source of a stat modifier added by a non-equip effect. One per application, so
// unequipping the item never cancels it; Equipment still recognises shield buffs by Item.
public sealed class ItemBuffSource
{
    public readonly ItemData Item;
    public ItemBuffSource(ItemData item) => Item = item;
}

// Runs EffectEntry lists. Items, equipment and the dispatcher all come through here.
public static class ItemEffectProcessor
{
    public static void Fire(ItemData item, EffectTrigger trigger, EffectContext ctx)
    {
        if (item == null || item.effects == null) return;
        foreach (var e in item.effects)
        {
            if (e == null || e.trigger != trigger) continue;
            if (e.chance < 100f && Random.Range(0f, 100f) > e.chance) continue;
            Apply(e, ctx);
        }
    }

    public static bool HasTrigger(ItemData item, EffectTrigger trigger)
    {
        if (item == null || item.effects == null) return false;
        foreach (var e in item.effects)
            if (e != null && e.trigger == trigger) return true;
        return false;
    }

    public static void Apply(EffectEntry e, EffectContext ctx)
    {
        Character user   = ctx.User;
        Character target = ctx.Target != null ? ctx.Target : user;

        switch (e.type)
        {
            case EffectType.FoodRegen:
                target?.Stats?.EatFood(e.value,e.duration);
                break;

            case EffectType.Heal:
                target?.Stats?.Heal(e.value);
                break;

            case EffectType.RestoreMana:
                target?.Stats?.RestoreMana(e.value);
                break;

            case EffectType.Damage:
                // No explicit target means the effect hurts its own user; anyone else must be hostile.
                if (target?.Stats == null) break;
                if (ctx.Target != null && user != null && target != user && !FactionRules.IsHostile(user.Faction, target.Faction)) break;
                target.Stats.TakeDamage(new DamageInfo
                {
                    Amount = e.value, Source = user, Target = target, FromEffect = true,
                    HitPoint = ctx.Point, Direction = Vector3.zero,
                });
                break;

            case EffectType.TimedStatBuff:
                if (target?.Stats != null)
                {
                    float dur = e.duration > 0f ? e.duration : -1f;
                    object source = ctx.ModifierSource ?? new ItemBuffSource(ctx.Item);
                    target.Stats.AddModifier(new StatModifier(e.stat, e.value, e.modifierType, source, dur));
                }
                break;

            case EffectType.PlayAudio:
                if (e.audio != null && AudioManager.HasInstance)
                {
                    if (e.audio2D) AudioManager.Instance.PlaySFXData2D(e.audio);
                    else           AudioManager.Instance.PlaySFXData(e.audio, ctx.Point);
                }
                break;

            case EffectType.SpawnPrefab:
                if (e.prefab != null)
                {
                    Vector3 pos = ctx.Point;
                    var go = Object.Instantiate(e.prefab, pos, Quaternion.identity);
                    if (e.attachToUser && user != null) go.transform.SetParent(user.transform, true);
                }
                break;

            case EffectType.SetFlag:
                if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(e.flag, e.flagValue);
                break;

            case EffectType.Custom:
                if (e.customEffect != null) e.customEffect.Apply(e, ctx);
                break;
        }
    }
}
