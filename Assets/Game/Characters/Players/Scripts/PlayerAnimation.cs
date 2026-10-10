using UnityEngine;

// Feeds locomotion parameters to the player's animators (third-person body and, for
// the table player, the first-person arms). Combat parameters are PlayerCombat's job.
[RequireComponent(typeof(PlayerMotor))]
public class PlayerAnimation : MonoBehaviour
{
    [SerializeField] Animator bodyAnimator;
    [SerializeField] Animator armsAnimator;

    [Header("Parameter names")]
    [SerializeField] string speedParam       = "Speed";
    [SerializeField] string motionSpeedParam = "MotionSpeed";
    [SerializeField] string groundedParam    = "Grounded";
    [SerializeField] string freeFallParam    = "FreeFall";
    [SerializeField] string jumpParam        = "Jump";

    PlayerMotor motor;
    bool jumpFlag;

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
    }

    void OnEnable()  => motor.Jumped += OnJumped;
    void OnDisable()
    {
        motor.Jumped -= OnJumped;
        jumpFlag = false;
        // The room body remains visible when its controller is switched off.
        Apply(bodyAnimator, 0f, 1f, true, false, 0f);
    }

    void OnJumped() => jumpFlag = true;

    void Update()
    {
        bool grounded = motor.IsGrounded;
        Vector3 v = motor.Velocity;
        float horizontal = new Vector3(v.x, 0f, v.z).magnitude;
        float motion = InputManager.HasInstance ? InputManager.Instance.Move.magnitude : (motor.IsMoving ? 1f : 0f);
        bool freeFall = !grounded && v.y < -1f;

        Apply(bodyAnimator, horizontal, motion, grounded, freeFall);
        Apply(armsAnimator, horizontal, motion, grounded, freeFall);

        if (grounded) jumpFlag = false;
    }

    void Apply(Animator anim, float speed, float motion, bool grounded, bool freeFall, float damping = 0.1f)
    {
        if (!AnimatorHelper.Ready(anim)) return;
        AnimatorHelper.SetFloat(anim, speedParam, speed, damping);
        AnimatorHelper.SetFloat(anim, motionSpeedParam, motion, damping);
        AnimatorHelper.SetBool(anim, groundedParam, grounded);
        AnimatorHelper.SetBool(anim, freeFallParam, freeFall);
        if (jumpFlag)       AnimatorHelper.SetBool(anim, jumpParam, true);
        else if (grounded)  AnimatorHelper.SetBool(anim, jumpParam, false);
    }
}
