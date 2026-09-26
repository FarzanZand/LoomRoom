using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// A controllable player (Room or Table): movement, stamina and the kit they start with.
[CreateAssetMenu(fileName = "NewPlayerData", menuName = "Characters/Player Data")]
public class PlayerData : CharacterData
{
    protected override bool HasCreatureAudio => false;

    [FoldoutGroup("Movement", Expanded = true, Order = 1), Title("Speeds (m/s)", HorizontalLine = false)]
    public float walkSpeed   = 3f;
    [FoldoutGroup("Movement")]
    public float sprintSpeed = 6f;
    [FoldoutGroup("Movement")]
    public float crouchSpeed = 1.75f;
    [FoldoutGroup("Movement"), Title("Jump & gravity", HorizontalLine = false)]
    public float jumpForce = 8f;
    [FoldoutGroup("Movement"), Tooltip("Multiplier applied to gravity while airborne. Higher values make falling faster.")]
    public float gravityMultiplier = 2.5f;

    [FoldoutGroup("Stamina", Order = 2), Min(0)]
    public float sprintStaminaPerSecond = 6f;
    [HideInInspector] public float jumpStaminaCost;
    [FoldoutGroup("Stamina"), Min(0)]
    public float staminaRegenDelay = .6f;
    [FoldoutGroup("Stamina"), Range(.01f, 1f), Tooltip("Exhaustion ends once stamina refills to this fraction.")]
    public float sprintRecoveryFraction = .25f;

    [FoldoutGroup("Starting items", Order = 3), Tooltip("Added to the bag or hotbar on first spawn.")]
    public List<ItemData> startingItems = new();
    [FoldoutGroup("Starting items"), Tooltip("Equipped directly on first spawn.")]
    public List<ItemData> startingEquipment = new();

    void OnValidate()
    {
        walkSpeed   = Mathf.Max(0.1f, walkSpeed);
        sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
        crouchSpeed = Mathf.Clamp(crouchSpeed, 0.1f, walkSpeed);
    }
}
