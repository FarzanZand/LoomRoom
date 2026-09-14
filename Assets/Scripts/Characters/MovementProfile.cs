using UnityEngine;

// Movement feel for a player kind. Assign on CharacterData; PlayerMotor reads it.
[CreateAssetMenu(fileName = "NewMovementProfile", menuName = "Characters/Movement Profile")]
public class MovementProfile : ScriptableObject
{
    [Header("Speeds (m/s)")]
    public float walkSpeed   = 3f;
    public float sprintSpeed = 6f;
    public float crouchSpeed = 1.75f;

    [Header("Jump & Gravity")]
    public float jumpForce         = 8f;
    [Tooltip("Multiplier applied to gravity while airborne. Higher values make falling faster.")]
    public float gravityMultiplier = 2.5f;

    [Header("Stamina")]
    [Tooltip("Stamina drained per second while sprint speed is applied. 0 = sprint is free.")]
    public float sprintStaminaPerSecond = 1f;
    [Tooltip("Stamina spent on a jump. 0 = free.")]
    public float jumpStaminaCost = 0.5f;
    [Tooltip("Seconds before stamina starts regenerating after it was spent.")]
    public float staminaRegenDelay = 0.6f;
    [Tooltip("After exhaustion, sprint unlocks again once stamina is back above this fraction of max.")]
    [Range(0f, 1f)] public float sprintRecoveryFraction = 0.25f;

    void OnValidate()
    {
        walkSpeed   = Mathf.Max(0.1f, walkSpeed);
        sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
        crouchSpeed = Mathf.Clamp(crouchSpeed, 0.1f, walkSpeed);
    }
}
