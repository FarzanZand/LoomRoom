using UnityEngine;

// Slowed by frost (a Frost Lance): an enemy walks and swings slower (its motor's speed and its animator), a player walks
// slower. A new chill takes the stronger slow and the later end. Frost clings to the body while it lasts. Added to the
// character when it is first chilled; removes itself when it wears off.
public class Chill : MonoBehaviour
{
    Character character;
    EnemyMotor motor;
    Animator animator;
    SpellVisual frost;
    float share, until, animatorWas = 1f;
    readonly object slowSource = new object();

    public static void Apply(Character target, float share, float seconds, SpellVisual frostEffect)
    {
        if (target == null || !target.IsAlive || share <= 0f || seconds <= 0f) return;
        var chill = target.GetComponent<Chill>();
        if (chill == null) chill = target.gameObject.AddComponent<Chill>();
        chill.Set(target, share, seconds, frostEffect);
    }

    void Set(Character target, float slow, float seconds, SpellVisual frostEffect)
    {
        bool first = character == null;
        character = target;
        if (first)
        {
            motor = GetComponent<EnemyMotor>();
            animator = character.Animator;
            if (animator != null) animatorWas = animator.speed;
            if (frostEffect != null)
            {
                var col = GetComponent<Collider>();
                var at = col != null ? col.bounds.center : transform.position + Vector3.up;
                frost = Instantiate(frostEffect, at, Quaternion.identity, transform);
            }
        }
        share = Mathf.Max(share, Mathf.Clamp(slow, 0f, .9f));
        until = Mathf.Max(until, Time.time + seconds);
        ApplySlow();
    }

    void ApplySlow()
    {
        float scale = 1f - share;
        if (motor != null) motor.SpeedScale = scale;
        if (animator != null) animator.speed = animatorWas * scale;
        if (character is Player p && p.Stats != null)
        {
            p.Stats.RemoveAllFromSource(slowSource);
            p.Stats.AddModifier(new StatModifier(StatType.MoveSpeed, -share, ModifierType.PercentMultiply, slowSource));
        }
    }

    void Update()
    {
        if (character == null || !character.IsAlive || Time.time >= until) Destroy(this);
    }

    void OnDestroy()
    {
        if (motor != null) motor.SpeedScale = 1f;
        // A body that died keeps its animator as death left it (stopped, or thrown).
        if (animator != null && character != null && character.IsAlive) animator.speed = animatorWas;
        if (character is Player p && p.Stats != null) p.Stats.RemoveAllFromSource(slowSource);
        if (frost != null) frost.Stop();
    }
}
