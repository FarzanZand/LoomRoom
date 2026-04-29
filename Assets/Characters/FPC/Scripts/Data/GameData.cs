namespace MFPC
{
    using UnityEngine;

    [CreateAssetMenu(fileName = "GameData", menuName = "GameData")]
    public class GameData : ScriptableObject
    {
        [Tooltip("Mouse sensitivity multiplier.")]
        public float mouseSensitivity = 1f;

        [Tooltip("Multiplier applied to look sensitivity when the Look action is driven by a gamepad.")]
        public float gamepadLookMultiplier = 10f;

        [Tooltip("Invert vertical mouse movement.")]
        public bool invertLook = false;

        [Tooltip("If disabled, camera lean input is ignored and the camera stays upright.")]
        public bool enableLean = true;

        [Tooltip("If enabled, crouch requires holding the input instead of toggling.")]
        public bool holdToCrouch = true;

        [Tooltip("If enabled, sprint requires holding the input instead of toggling.")]
        public bool holdToSprint = true;

        [Tooltip("Automatically stop sprinting when the player stops moving.")]
        public bool autoUnSprint = true;

        [Tooltip("Enable noise and headbob on Start.")]
        public bool EnableNoiseAndHeadbob = true;
    }
}