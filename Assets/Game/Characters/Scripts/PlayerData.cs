using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// A controllable player (Room or Table): movement, stamina and the kit they start with.
[CreateAssetMenu(fileName = "NewPlayerData", menuName = "Characters/Player Data")]
public class PlayerData : CharacterData
{
    protected override bool HasCreatureAudio => false;

    [TabGroup(InspectorTabs, "Movement", Order = 10), Title("Speeds (m/s)", HorizontalLine = false)]
    public float walkSpeed   = 3f;
    [TabGroup(InspectorTabs, "Movement")]
    public float sprintSpeed = 6f;
    [TabGroup(InspectorTabs, "Movement")]
    public float crouchSpeed = 1.75f;
    [TabGroup(InspectorTabs, "Movement"), Range(.1f, 1f), Tooltip("Share of the speed kept while moving backwards. Barony backs off at half speed, so attacks can't simply be outwalked.")]
    public float backwardSpeed = 1f;
    [TabGroup(InspectorTabs, "Movement"), Title("Jump & gravity", HorizontalLine = false)]
    public float jumpForce = 8f;
    [TabGroup(InspectorTabs, "Movement"), Tooltip("Multiplier applied to gravity while airborne. Higher values make falling faster.")]
    public float gravityMultiplier = 2.5f;

    [TabGroup(InspectorTabs, "Movement"), Title("Stamina", HorizontalLine = false), Min(0)]
    public float sprintStaminaPerSecond = 6f;
    [HideInInspector] public float jumpStaminaCost;
    [TabGroup(InspectorTabs, "Movement"), Min(0)]
    public float staminaRegenDelay = .6f;
    [TabGroup(InspectorTabs, "Movement"), Range(.01f, 1f), Tooltip("Exhaustion ends once stamina refills to this fraction.")]
    public float sprintRecoveryFraction = .25f;

    [TabGroup(InspectorTabs, "Inventory", Order = 20), Tooltip("Added to the bag or hotbar on first spawn.")]
    [ListDrawerSettings(ShowFoldout = false, ShowPaging = true, NumberOfItemsPerPage = 6)]
    public List<ItemData> startingItems = new();
    [TabGroup(InspectorTabs, "Inventory"), Tooltip("Equipped directly on first spawn.")]
    [ListDrawerSettings(ShowFoldout = false, ShowPaging = true, NumberOfItemsPerPage = 6)]
    public List<ItemData> startingEquipment = new();

    void OnValidate()
    {
        walkSpeed   = Mathf.Max(0.1f, walkSpeed);
        sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
        crouchSpeed = Mathf.Clamp(crouchSpeed, 0.1f, walkSpeed);
    }
}
