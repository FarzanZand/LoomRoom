using UnityEngine;

// A guest's copy of an enemy the host runs: it glides to where the host's snapshots put it and plays what the host's
// animator plays. Hits on it go to the host (CoopWorld); its blood, bar and death come back as the host's result.
public class CoopEnemyPuppet : MonoBehaviour
{
    Character character;
    EnemyBrain brain;
    FlyingEnemy flyer;
    Vector3 targetPosition;
    float targetYaw;
    bool hasTarget;
    // The player the host's enemy has in sight (Coop.NoSlot: nobody).
    public byte Sees { get; private set; } = Coop.NoSlot;

    void Awake()
    {
        character = GetComponent<Character>();
        brain = GetComponent<EnemyBrain>();
        flyer = GetComponent<FlyingEnemy>();
        targetPosition = transform.position;
        targetYaw = transform.eulerAngles.y;
        if (character != null && character.Animator != null) character.Animator.applyRootMotion = false;
    }

    public void Apply(EnemySnapshot s)
    {
        targetPosition = s.position;
        Sees = s.sees;
        targetYaw = s.yaw / 50f;
        if (!hasTarget || (transform.position - targetPosition).sqrMagnitude > 16f) { transform.position = targetPosition; transform.rotation = Quaternion.Euler(0, targetYaw, 0); }
        hasTarget = true;
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
        if (!hasTarget || character == null || !character.IsAlive) return;
        float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPosition, k);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, targetYaw, 0), k);
    }
}
