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
    void OnDisable() => motor.Jumped -= OnJumped;

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

        if (grounded && !jumpFlag) { }
        if (grounded) jumpFlag = false;
    }

    void Apply(Animator anim, float speed, float motion, bool grounded, bool freeFall)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return;
        SetFloat(anim, speedParam, speed);
        SetFloat(anim, motionSpeedParam, motion);
        SetBool(anim, groundedParam, grounded);
        SetBool(anim, freeFallParam, freeFall);
        if (jumpFlag)       SetBool(anim, jumpParam, true);
        else if (grounded)  SetBool(anim, jumpParam, false);
    }

    static void SetFloat(Animator anim, string name, float value)
    {
        if (Character.HasParameter(anim, name, AnimatorControllerParameterType.Float)) anim.SetFloat(name, value, 0.1f, Time.deltaTime);
    }

    static void SetBool(Animator anim, string name, bool value)
    {
        if (Character.HasParameter(anim, name, AnimatorControllerParameterType.Bool)) anim.SetBool(name, value);
    }
}
