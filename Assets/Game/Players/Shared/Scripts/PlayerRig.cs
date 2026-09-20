using Sirenix.OdinInspector;
using UnityEngine;

// Inspector entry point for the complete prefab; gameplay remains on Controller.
public class PlayerRig : MonoBehaviour
{
    [Required] public Player controller;

    [ShowInInspector, InlineEditor, LabelText("Character Tuning")]
    public CharacterData Definition => controller != null ? controller.data : null;

    [ShowInInspector, InlineEditor, LabelText("Control Preferences")]
    public PlayerSettings Controls => controller != null ? controller.settings : null;

    [Button("Select Gameplay Components")]
    void SelectController()
    {
#if UNITY_EDITOR
        if (controller != null) UnityEditor.Selection.activeGameObject = controller.gameObject;
#endif
    }
}
