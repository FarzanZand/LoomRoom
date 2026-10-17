using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// The kneel a stage boss holds while the Dungeon Master pours his beam (BossFight.beamKneel, BossStageEncounter.Kneel):
// writes ThirdPerson/Gestures/Kneel.anim once (the standing idle with the muscles below; edit the .anim by hand freely
// after) and adds the Kneel bool and state to the HumanoidController (from any state while Kneel is on, back to
// locomotion when it goes off). Preview renders the pose on the Crypt Lord in a preview scene to Temp/Kneel preview.png.
public static class BossKneelSetup
{
    const string Controller = "Assets/Game/Characters/Animations/HumanoidController.controller";
    const string Idle = "Assets/Game/Characters/Animations/ThirdPerson/Locomotion/Idle.anim";
    const string ClipPath = "Assets/Game/Characters/Animations/ThirdPerson/Gestures/Kneel.anim";
    const string Boss = "Assets/Game/Characters/Enemies/Crypt/Crypt Lord.prefab";

    // Down on the right knee, left foot planted forward, a forearm on the left knee, head bowed.
    public const string Pose =
        "RootT.y:=0.62;" +
        "Left Upper Leg Front-Back:=0.95;Left Upper Leg In-Out:=0.1;Left Lower Leg Stretch:=-0.15;Left Foot Up-Down:=0.1;" +
        "Right Upper Leg Front-Back:=-0.05;Right Upper Leg In-Out:=0.05;Right Lower Leg Stretch:=-1;Right Foot Up-Down:=-0.6;Right Toes Up-Down:=0.8;" +
        "Spine Front-Back:=0.4;Chest Front-Back:=0.2;UpperChest Front-Back:=0.15;Neck Nod Down-Up:=0.4;Head Nod Down-Up:=0.6;" +
        "Left Arm Down-Up:=-0.8;Left Arm Front-Back:=0.6;Left Forearm Stretch:=0.3;" +
        "Right Arm Down-Up:=-0.85;Right Arm Front-Back:=0.2;Right Forearm Stretch:=0.7";

    public static string SetUp(bool rewriteClip, string pose = null)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        bool wrote = clip == null || rewriteClip;
        if (wrote) clip = MainMenuSetTools.MakePose(Idle, 0f, ClipPath, pose ?? Pose, 4f, .015f);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        if (controller == null) return "no HumanoidController at " + Controller;
        bool hasParam = false;
        foreach (var p in controller.parameters) if (p.name == "Kneel") hasParam = true;
        if (!hasParam) controller.AddParameter("Kneel", AnimatorControllerParameterType.Bool);

        var machine = controller.layers[0].stateMachine;
        AnimatorState kneel = null, locomotion = null;
        foreach (var s in machine.states)
        {
            if (s.state.name == "Kneel") kneel = s.state;
            if (s.state.name == "IdleWalkRunBT") locomotion = s.state;
        }
        bool added = kneel == null;
        if (added)
        {
            kneel = machine.AddState("Kneel", new Vector3(650f, 520f, 0f));
            kneel.motion = clip;
            var into = machine.AddAnyStateTransition(kneel);
            into.AddCondition(AnimatorConditionMode.If, 0f, "Kneel");
            into.hasExitTime = false; into.duration = .35f; into.canTransitionToSelf = false;
            if (locomotion != null)
            {
                var back = kneel.AddTransition(locomotion);
                back.AddCondition(AnimatorConditionMode.IfNot, 0f, "Kneel");
                back.hasExitTime = false; back.duration = .45f;
            }
        }
        else kneel.motion = clip;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return $"{(wrote ? "wrote" : "kept")} {ClipPath}; Kneel {(added ? "state added" : "state kept")} on the HumanoidController";
    }

    // Renders the pose (or a trial pose string, unsaved) on the Crypt Lord, from the side and the front, in a preview scene.
    public static string Preview(string pose = null, string path = "Temp/Kneel preview.png")
    {
        var clip = pose == null ? AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath) : MainMenuSetTools.MakePose(Idle, 0f, null, pose, 4f);
        if (clip == null) return "no kneel clip yet";
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var boss = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Boss), scene);
            boss.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = boss.GetComponentInChildren<Animator>();
            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animator.gameObject, clip, 0f);
            AnimationMode.EndSampling();
            foreach (var s in boss.GetComponentsInChildren<SkinnedMeshRenderer>()) s.forceMatrixRecalculationPerRender = true;
            var bounds = new Bounds(boss.transform.position + Vector3.up, Vector3.one);
            foreach (var r in boss.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            float h = bounds.center.y, dist = Mathf.Max(bounds.size.y, 2f) * 1.9f;
            var lightGo = new GameObject("Light"); SceneManager_Move(lightGo, scene);
            var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.4f; lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            var camGo = new GameObject("Camera"); SceneManager_Move(camGo, scene);
            var cam = camGo.AddComponent<Camera>(); cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.25f, .27f, .3f);
            cam.fieldOfView = 35f;
            string side = Path.ChangeExtension(path, null) + " side.png";   // from the front left, where the planted foot is
            camGo.transform.SetPositionAndRotation(new Vector3(-dist * .75f, h, bounds.center.z + dist * .55f), Quaternion.LookRotation(new Vector3(.75f, 0f, -.55f)));
            MainMenuSetTools.Render(cam, side, 640, 640);
            camGo.transform.SetPositionAndRotation(new Vector3(bounds.center.x, h, dist), Quaternion.LookRotation(Vector3.back));
            MainMenuSetTools.Render(cam, path, 640, 640);
            return $"rendered {path} and {side}; height {bounds.size.y:0.00}, floor {bounds.min.y:0.00}";
        }
        finally { if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode(); EditorSceneManager.ClosePreviewScene(scene); }
    }

    static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene) => UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
}
