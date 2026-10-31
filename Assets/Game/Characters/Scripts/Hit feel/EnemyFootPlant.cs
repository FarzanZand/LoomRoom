using UnityEngine;

// While a hit slides a humanoid enemy (EnemyMotor.IsKnockedBack), its feet stay where they stood instead of skating
// along with the body: foot IK holds them at the spot they had when the slide began. A foot lets go when the slide ends,
// or sooner when the animation's own foot gets more than CombatManager.footPlantReach from it (a long slide doesn't
// stretch the leg), and then eases back under the body over footPlantRelease, which reads as the step that catches the
// weight. On the Animator object (Unity calls OnAnimatorIK there, in every layer with an IK pass); EnemyBrain adds it to
// humanoids at start.
[RequireComponent(typeof(Animator))]
public class EnemyFootPlant : MonoBehaviour
{
    Animator anim;
    EnemyMotor motor;
    Character character;
    bool planted, wasSliding;
    readonly Foot left = new Foot { goal = AvatarIKGoal.LeftFoot }, right = new Foot { goal = AvatarIKGoal.RightFoot };

    class Foot
    {
        public AvatarIKGoal goal;
        public Vector3 at;
        public Quaternion turn;
        public float weight;
        public bool letGo;
    }

    static CombatManager Tuning => CombatManager.HasInstance ? CombatManager.Instance : null;

    void Awake()
    {
        anim = GetComponent<Animator>();
        motor = GetComponentInParent<EnemyMotor>();
        character = GetComponentInParent<Character>();
    }

    void Update()
    {
        var tuning = Tuning;
        bool sliding = motor != null && motor.IsKnockedBack && tuning != null && tuning.plantFeetInKnockback
            && (character == null || character.IsAlive);
        float release = Time.deltaTime / Mathf.Max(.01f, tuning != null ? tuning.footPlantRelease : .2f);
        // A new slide plants the feet again where they stand now (a second hit during the step back too).
        if (sliding && !wasSliding) { left.weight = right.weight = 1f; left.letGo = right.letGo = false; planted = false; }
        wasSliding = sliding;
        if (!sliding || left.letGo) left.weight = Mathf.MoveTowards(left.weight, 0f, release);
        if (!sliding || right.letGo) right.weight = Mathf.MoveTowards(right.weight, 0f, release);
        if (planted && left.weight <= 0f && right.weight <= 0f) planted = false;
    }

    void OnAnimatorIK(int layer)
    {
        if (anim == null || !anim.isHuman || left.weight <= 0f && right.weight <= 0f) return;
        // The spot each foot stands on when the slide begins, read from the pose the animation gives it.
        if (!planted)
        {
            left.at = anim.GetIKPosition(left.goal); left.turn = anim.GetIKRotation(left.goal);
            right.at = anim.GetIKPosition(right.goal); right.turn = anim.GetIKRotation(right.goal);
            planted = true;
        }
        float reach = Tuning != null ? Tuning.footPlantReach : .4f;
        Plant(left, reach);
        Plant(right, reach);
    }

    void Plant(Foot foot, float reach)
    {
        // A foot left too far behind starts its step back, rather than the leg stretching after it.
        if (Vector3.Distance(anim.GetIKPosition(foot.goal), foot.at) > reach) foot.letGo = true;
        float w = Mathf.SmoothStep(0f, 1f, foot.weight);
        anim.SetIKPosition(foot.goal, foot.at); anim.SetIKPositionWeight(foot.goal, w);
        anim.SetIKRotation(foot.goal, foot.turn); anim.SetIKRotationWeight(foot.goal, w);
    }
}
