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
    [Tooltip("Velocity in the look's frame, metres a second (the one body's legs).")]
    [SerializeField] string moveXParam       = "MoveX", moveZParam = "MoveZ";

    PlayerMotor motor;
    Player player;
    bool jumpFlag;

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        player = GetComponent<Player>();
    }

    void OnEnable()  => motor.Jumped += OnJumped;
    void OnDisable()
    {
        motor.Jumped -= OnJumped;
        jumpFlag = false;
        // The room body remains visible when its controller is switched off.
        if (bodyAnimator != armsAnimator) Apply(bodyAnimator, 0f, 1f, true, false, 0f);
    }

    void OnJumped() => jumpFlag = true;

    void Update()
    {
        bool grounded = motor.IsGrounded;
        Vector3 v = motor.Velocity;
        float horizontal = new Vector3(v.x, 0f, v.z).magnitude;
        float motion = InputManager.HasInstance ? InputManager.Instance.Move.magnitude : (motor.IsMoving ? 1f : 0f);
        bool freeFall = !grounded && v.y < -1f;

        // One body: body and arms are one animator, fed once (twice would step the damped values twice a frame).
        if (bodyAnimator != armsAnimator) Apply(bodyAnimator, horizontal, motion, grounded, freeFall);
        Apply(armsAnimator, horizontal, motion, grounded, freeFall);
        // Which way the legs walk: the velocity turned into the look's frame (only animators that have these parameters).
        var yaw = player != null && player.Look != null ? player.Look.YawTransform : transform;
        var local = Quaternion.Inverse(Quaternion.Euler(0f, yaw.eulerAngles.y, 0f)) * new Vector3(v.x, 0f, v.z);
        AnimatorHelper.SetFloat(armsAnimator, moveXParam, local.x, .1f);
        AnimatorHelper.SetFloat(armsAnimator, moveZParam, local.z, .1f);

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
