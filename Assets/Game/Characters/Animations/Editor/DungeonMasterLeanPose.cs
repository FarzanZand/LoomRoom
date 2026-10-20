using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// The Dungeon Master leaning over the table on his forearms in a boss fight, face low over the stage (the main menu's
// lean, Menu DM reach, with both forearms down on the tabletop): a held pose clip with a slow breath, and the state
// "Lean On Table" on the HumanoidController's Base Layer that DungeonMasterSeat.Focus cross-fades to (no transitions:
// the seat goes in and out by name). Make it once, then edit the .anim by hand freely; Make overwrites it.
public static class DungeonMasterLeanPose
{
    const string BasePath = "Assets/Game/UI/Main Menu/Menu DM reach.anim";
    public const string ClipPath = "Assets/Game/Characters/Animations/ThirdPerson/Gestures/DM Lean On Table.anim";
    const string ControllerPath = "Assets/Game/Characters/Animations/HumanoidController.controller";
    public const string State = "Lean On Table";

    // Muscle changes on top of the menu lean ("Muscle=value" adds, "Muscle:=value" replaces).
    public static string Overrides =
        "Spine Front-Back:=-.9;Chest Front-Back:=-.7;UpperChest Front-Back:=-.5;Neck Nod Down-Up:=.55;Head Nod Down-Up:=.45;Head Turn Left-Right:=0;Neck Turn Left-Right:=0;Head Tilt Left-Right:=0;Spine Left-Right:=0;" +
        "Left Shoulder Down-Up:=-.2;Left Shoulder Front-Back:=.3;Left Arm Down-Up:=-.35;Left Arm Front-Back:=.55;Left Arm Twist In-Out:=0;Left Forearm Stretch:=-.35;Left Forearm Twist In-Out:=0;Left Hand Down-Up:=0;Left Hand In-Out:=0;" +
        "Right Shoulder Down-Up:=-.2;Right Shoulder Front-Back:=.3;Right Arm Down-Up:=-.35;Right Arm Front-Back:=.55;Right Arm Twist In-Out:=0;Right Forearm Stretch:=-.35;Right Forearm Twist In-Out:=0;Right Hand Down-Up:=0;Right Hand In-Out:=0;" +
        "RightHand.Thumb.1 Stretched:=.2;RightHand.Index.1 Stretched:=.2;RightHand.Index.2 Stretched:=.2;RightHand.Index.3 Stretched:=.2;RightHand.Middle.1 Stretched:=.2;RightHand.Middle.2 Stretched:=.2;RightHand.Middle.3 Stretched:=.2;" +
        "LeftHand.Thumb.1 Stretched:=.2;LeftHand.Index.1 Stretched:=.2;LeftHand.Index.2 Stretched:=.2;LeftHand.Index.3 Stretched:=.2;LeftHand.Middle.1 Stretched:=.2;LeftHand.Middle.2 Stretched:=.2;LeftHand.Middle.3 Stretched:=.2";
    public static float Breathe = .03f;

    public static void MakeMenu() => Debug.Log("[DM lean] " + Make());

    public static string Make()
    {
        var clip = MainMenuSetTools.MakePose(BasePath, 0f, ClipPath, Overrides, 6f, Breathe);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var machine = controller.layers[0].stateMachine;
        AnimatorState state = null;
        foreach (var s in machine.states) if (s.state.name == State) state = s.state;
        if (state == null) state = machine.AddState(State, new Vector3(650, 420, 0));
        state.motion = clip;
        state.writeDefaultValues = true;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return $"{ClipPath} and the {State} state";
    }
}
