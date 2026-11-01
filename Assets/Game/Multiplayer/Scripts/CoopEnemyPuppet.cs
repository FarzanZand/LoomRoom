using System.Collections.Generic;
using UnityEngine;

// A guest's copy of an enemy the host runs: it plays the host's snapshots a moment behind (CoopWorld.HostTimeShown),
// moving at an even pace from one to the next, and plays what the host's animator plays. Hits on it go to the host
// (CoopWorld); its blood, bar and death come back as the host's result.
public class CoopEnemyPuppet : MonoBehaviour
{
    // Past the newest snapshot it carries on at its last pace for at most this long, then waits.
    const float MaxAhead = .15f;
    // Metres between two snapshots taken as a jump (a warp, a pull out of a wall): no gliding across it.
    const float JumpMetres = 4f;

    Character character;
    EnemyBrain brain;
    EnemyMotor motor;
    FlyingEnemy flyer;
    readonly List<(float time, Vector3 position, float yaw)> snapshots = new(8);
    // The player the host's enemy has in sight (Coop.NoSlot: nobody).
    public byte Sees { get; private set; } = Coop.NoSlot;

    void Awake()
    {
        character = GetComponent<Character>();
        brain = GetComponent<EnemyBrain>();
        motor = GetComponent<EnemyMotor>();
        flyer = GetComponent<FlyingEnemy>();
        if (character != null && character.Animator != null) character.Animator.applyRootMotion = false;
    }

    // hostTime: when the host took it, on the host's clock.
    public void Apply(EnemySnapshot s, float hostTime)
    {
        Sees = s.sees;
        float yaw = s.yaw / 50f;
        if (snapshots.Count > 0 && hostTime <= snapshots[snapshots.Count - 1].time) return;   // late or repeated
        if (snapshots.Count == 0 || (s.position - snapshots[snapshots.Count - 1].position).sqrMagnitude > JumpMetres * JumpMetres)
        {
            // The first, or a jump: straight there.
            snapshots.Clear();
            transform.SetPositionAndRotation(s.position, Quaternion.Euler(0, yaw, 0));
        }
        snapshots.Add((hostTime, s.position, yaw));
        if (snapshots.Count > 8) snapshots.RemoveAt(0);
        if (brain != null) brain.ApplyNetworkState((EnemyState)s.state, s.asleep);
        if (flyer != null) flyer.NetworkHeight = s.modelHeight;

        var anim = character != null ? character.Animator : null;
        if (anim == null || anim.runtimeAnimatorController == null || !anim.isInitialized) return;
        var (floats, bools) = CoopWorld.Parameters(anim);
        for (int i = 0; i < floats.Length && i < s.floats.Length; i++) anim.SetFloat(floats[i], s.floats[i]);
        for (int i = 0; i < bools.Length && i < 32; i++) anim.SetBool(bools[i], (s.bools & (1u << i)) != 0);
        int layers = Mathf.Min(anim.layerCount, s.states.Length);
        for (int l = 0; l < layers; l++)
        {
            int wanted = s.states[l];
            if (wanted == 0) continue;
            var current = anim.GetCurrentAnimatorStateInfo(l);
            bool transitioning = anim.IsInTransition(l);
            if (current.fullPathHash == wanted && !transitioning) continue;
            if (transitioning && anim.GetNextAnimatorStateInfo(l).fullPathHash == wanted) continue;
            if (!anim.HasState(l, wanted)) continue;
            anim.CrossFade(wanted, .12f, l, s.times[l] / 255f);
        }
    }

    void Update()
    {
        if (snapshots.Count == 0 || character == null || !character.IsAlive) return;
        float shown = CoopWorld.HostTimeShown;
        Vector3 position, velocity = Vector3.zero;
        float yaw;
        int newest = snapshots.Count - 1;
        if (shown <= snapshots[0].time) { position = snapshots[0].position; yaw = snapshots[0].yaw; }
        else if (shown >= snapshots[newest].time)
        {
            // Past the newest: on at the last pace for a moment (a late snapshot), then it waits.
            var last = snapshots[newest];
            if (newest > 0)
            {
                var before = snapshots[newest - 1];
                velocity = (last.position - before.position) / Mathf.Max(.01f, last.time - before.time);
            }
            position = last.position + velocity * Mathf.Min(shown - last.time, MaxAhead);
            if (shown - last.time > MaxAhead) velocity = Vector3.zero;
            yaw = last.yaw;
        }
        else
        {
            int i = 1;
            while (snapshots[i].time < shown) i++;
            var a = snapshots[i - 1];
            var b = snapshots[i];
            float u = (shown - a.time) / Mathf.Max(.001f, b.time - a.time);
            position = Vector3.Lerp(a.position, b.position, u);
            yaw = Mathf.LerpAngle(a.yaw, b.yaw, u);
            velocity = (b.position - a.position) / Mathf.Max(.01f, b.time - a.time);
        }
        transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        // Its pace for its legs (EnemyFootPlant pins a foot the animation has down while it walks).
        if (motor != null) motor.SetNetworkVelocity(velocity);
    }
}
