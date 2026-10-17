using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A volley of a projectile spell (Fireball): the balls appear one by one at the caster's hand, drift up and apart and hang
// there, turning to follow the target; then they leave one after another, each speeding up from a standstill toward
// where the target is at that moment. Cast by the Dungeon Master in a boss fight (BossEvent What.Volley) and by any
// enemy whose EnemySpell has Volley on (EnemyAttackRunner). Made at run time; nothing to place.
public class FireballVolley : MonoBehaviour
{
    [System.Serializable]
    public class Settings
    {
        [Min(1)] public int count = 3;
        [Min(0), Tooltip("Seconds between one ball appearing and the next.")]
        public float spawnInterval = 1f;
        [Min(0), Tooltip("Metres each ball drifts up after it appears.")]
        public float rise = 1.2f;
        [Min(0), Tooltip("Metres between the hanging balls, side by side.")]
        public float spread = 1.3f;
        [Min(.05f), Tooltip("Seconds a ball takes to drift up into place.")]
        public float riseSeconds = .5f;
        [Min(0), Tooltip("Seconds after the last ball is in place before the first one leaves.")]
        public float holdSeconds = 1f;
        [Min(0), Tooltip("Seconds between one ball leaving and the next.")]
        public float launchInterval = 1f;
        [Min(0), Tooltip("Seconds a ball takes to reach full speed (the spell's Speed) from a standstill.")]
        public float rampSeconds = .7f;
        [Min(.1f), Tooltip("Size of each ball against the spell's own projectile.")]
        public float scale = 1.3f;
        [Tooltip("The balls home in on the target once they leave (also on when the spell itself is Homing). Off: each flies at where the target stood when it started to speed up.")]
        public bool homing;
    }

    // origin: where the balls appear (the caster's hand), read again for each ball. only: the one character the
    // balls may hurt (the Dungeon Master's, which aren't anyone's side); null hurts whoever the owner is hostile to.
    // hangAt: the middle of the row the balls hang in (default: Rise above where the first appeared). The Dungeon Master
    // sends his out over the stage in front of the player, where they can be seen.
    public static FireballVolley Cast(SpellDefinition spell, Character owner, System.Func<Vector3> origin, Character target, float damage,
        Settings settings, Character only = null, string sourceName = null, System.Action done = null, Vector3? hangAt = null)
    {
        if (spell == null || spell.projectile == null || target == null || settings == null) return null;
        var go = new GameObject(spell.name + " (volley)");
        var volley = go.AddComponent<FireballVolley>();
        volley.StartCoroutine(volley.Run(spell, owner, origin, target, damage, settings, only, sourceName, done, hangAt));
        return volley;
    }

    readonly List<SpellProjectile> balls = new();

    // Every volley running, so going back to the room can clear them with their hanging balls (PlayerManager).
    static readonly HashSet<FireballVolley> live = new();
    void OnEnable() => live.Add(this);
    void OnDisable() => live.Remove(this);
    public static void ClearAll()
    {
        foreach (var v in new List<FireballVolley>(live)) if (v != null) Destroy(v.gameObject);
        live.Clear();
    }

    IEnumerator Run(SpellDefinition spell, Character owner, System.Func<Vector3> origin, Character target, float damage, Settings s,
        Character only, string sourceName, System.Action done, Vector3? hangAt)
    {
        // Appear, drift up and apart, hang.
        var start = origin();
        var row = hangAt ?? start;
        var side = Vector3.Cross(Vector3.up, Flat(Aim(target) - row)).normalized;
        // Rooms are low: the balls hang at least half a metre under whatever is above them.
        float rise = s.rise;
        if (Physics.Raycast(row, Vector3.up, out var roof, s.rise + 2f, ~0, QueryTriggerInteraction.Ignore) && roof.collider.GetComponentInParent<Character>() == null)
            rise = Mathf.Clamp(roof.distance - .55f, 0f, s.rise);
        for (int i = 0; i < s.count && target != null && target.IsAlive; i++)
        {
            var from = origin();
            var to = row + Vector3.up * rise + side * ((i - (s.count - 1) * .5f) * s.spread);
            var ball = Instantiate(spell.projectile, from, Quaternion.LookRotation(Aim(target) - from));
            ball.transform.localScale *= s.scale;
            ball.Held = true; ball.RampSeconds = s.rampSeconds; ball.Only = only; ball.SourceName = sourceName;
            ball.Launch(owner, spell, damage, null);
            balls.Add(ball);
            StartCoroutine(Drift(ball, from, to, s.riseSeconds, target));
            if (spell.chargeSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(spell.chargeSound, from, spell.chargeVolume * .7f);
            if (i < s.count - 1) yield return Wait(s.spawnInterval, target);
        }
        yield return Wait(s.riseSeconds + s.holdSeconds, target);
        // Away, one by one, each toward where the target stands now.
        for (int i = 0; i < balls.Count; i++)
        {
            var ball = balls[i];
            if (ball == null) continue;
            if (target != null && target.IsAlive) ball.transform.rotation = Quaternion.LookRotation(Aim(target) - ball.transform.position);
            if (s.homing || spell.homing) { ball.HomingTarget = target; ball.HomingTurn = spell.homingTurn; }
            ball.Held = false;
            if (spell.releaseSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(spell.releaseSound, ball.transform.position, spell.releaseVolume);
            if (i < balls.Count - 1) yield return Wait(s.launchInterval, null);
        }
        done?.Invoke();
        Destroy(gameObject, 1f);
    }

    // Eases a hanging ball into place and keeps it turned toward the target while it waits.
    IEnumerator Drift(SpellProjectile ball, Vector3 from, Vector3 to, float seconds, Character target)
    {
        for (float t = 0; ball != null && ball.Held; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / seconds));
            ball.transform.position = Vector3.Lerp(from, to, u) + Vector3.up * Mathf.Sin(t * 3f) * .05f * u;
            if (target != null) ball.transform.rotation = Quaternion.LookRotation(Aim(target) - ball.transform.position);
            yield return null;
        }
    }

    // A dead or vanished target ends the wait early (the balls then fly on their last heading).
    static IEnumerator Wait(float seconds, Character target)
    {
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            if (target != null && !target.IsAlive) yield break;
            yield return null;
        }
    }

    // Cut short (the fight ended): the hanging balls fizzle.
    public void Stop()
    {
        StopAllCoroutines();
        foreach (var b in balls) if (b != null && b.Held) b.Fizzle();
        Destroy(gameObject);
    }

    static Vector3 Aim(Character target)
    {
        var col = target != null ? target.GetComponent<Collider>() : null;
        return col != null ? col.bounds.center + Vector3.up * col.bounds.extents.y * .35f : target != null ? target.transform.position + Vector3.up * 1.2f : Vector3.zero;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > .0001f ? v.normalized : Vector3.forward; }
}
