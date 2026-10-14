using UnityEngine;

// User-facing control options. Gameplay tuning lives on CharacterData.
[CreateAssetMenu(fileName = "PlayerSettings", menuName = "Player/Player Settings")]
public class PlayerSettings : ScriptableObject
{
    [Tooltip("Mouse sensitivity multiplier.")]
    public float mouseSensitivity = 1f;
    [Tooltip("Invert vertical look.")]
    public bool invertLook = false;
    [Tooltip("If disabled, lean input is ignored and the camera stays upright.")]
    public bool enableLean = true;
    [Tooltip("Crouch requires holding the input instead of toggling.")]
    public bool holdToCrouch = false;
    [Tooltip("Sprint requires holding the input instead of toggling.")]
    public bool holdToSprint = false;
    [Tooltip("Automatically stop sprinting when the player stops moving (toggle mode).")]
    public bool autoUnSprint = true;
}
