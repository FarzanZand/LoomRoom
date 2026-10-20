using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// The eat and drink clips for the right arm (Characters/Animations/FirstPersonPlayer/Consume), baked with the same IK
// rig as the swings: the hand is moved along a path written in camera space (x right, y up, z forward, metres, before
// the view-model widening) and the arm is solved toward it every frame. The item sits in the hand where Equipment
// puts it (ItemData held position and rotation), so the item's up is the hand socket's up. Equipment plays them
// (UseItem trigger, UseItemType 1 eat / 2 drink, UseItemSpeed fits the clip to ItemData.eatDuration).
// Rebaking overwrites both clips: edit the keys below, or stop rebaking once a clip has been edited by hand.
public static partial class IkSwingBaker
{
    const string ConsumeFolder = "Assets/Game/Characters/Animations/FirstPersonPlayer/Consume";
    static readonly Vector3 ConsumeElbow = new(.55f, -.8f, -.15f);

    // Eat: up to the mouth, three bites (pulled back a little between them), then down again.
    public static Vector3 EatMouth = new(.03f, -.1f, .27f), EatBite = new(.06f, -.14f, .33f);
    public static Vector3 EatUp = new(-.25f, .55f, -.8f);
    // Drink: the bottle to the lips upright, tipped back while drinking, then down again.
    public static Vector3 DrinkLips = new(.03f, -.11f, .28f), DrinkTipped = new(.02f, -.03f, .3f);
    public static Vector3 DrinkUp = new(-.1f, .8f, -.6f), DrinkTippedUp = new(-.05f, -.45f, -.9f);

    public static void BakeConsume()
    {
        var rig = BuildRig();
        try
        {
            var cam = rig.root.transform;
            // Where the hand rests at idle, as a key, so the raise starts exactly from the idle pose.
            foreach (var t in rig.keyed) t.localRotation = rig.idle[t];
            var rest = new Key(0f, cam.InverseTransformPoint(rig.socket.position), cam.InverseTransformDirection(rig.socket.up), cam.InverseTransformDirection(rig.socket.right));
            var idle = new Dictionary<Transform, Quaternion>(rig.idle);

            var mouth = new Key(0f, EatMouth, EatUp, Vector3.right);
            var bite = new Key(0f, EatBite, EatUp, Vector3.right);
            var eat = new (float time, Key key)[]
            {
                (.32f, mouth), (.42f, bite), (.52f, mouth), (.64f, bite), (.74f, mouth), (.86f, bite), (.94f, mouth),
            };
            Bake(rig, "Eat", rest, idle, eat, 1.3f);

            var lips = new Key(0f, DrinkLips, DrinkUp, Vector3.right);
            var tipped = new Key(0f, DrinkTipped, DrinkTippedUp, Vector3.right);
            var drink = new (float time, Key key)[] { (.34f, lips), (.62f, tipped), (.98f, tipped), (1.1f, lips) };
            Bake(rig, "Drink", rest, idle, drink, 1.45f);

            WireConsume();
            AssetDatabase.SaveAssets();
            Debug.Log($"Eat and drink baked to {ConsumeFolder}");
        }
        finally { rig.Destroy(); }
    }

    // From idle into the first key, along the keys, then from the last key back to idle (eased at both ends).
    static void Bake(Rig rig, string name, Key rest, Dictionary<Transform, Quaternion> idle, (float time, Key key)[] keys, float length)
    {
        float first = keys[0].time, last = keys[keys.Length - 1].time;
        var frames = Frames(length, t =>
        {
            if (t <= first) return Solve(rig, Lerp(rest, keys[0].key, Mathf.SmoothStep(0f, 1f, t / first)), ConsumeElbow);
            if (t >= last)
            {
                float back = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(last, length, t));
                // Most of the way down by IK, the last stretch blended into the idle pose so it lands on it exactly.
                var pose = Solve(rig, Lerp(keys[keys.Length - 1].key, rest, back), ConsumeElbow);
                return back > .7f ? Blend(rig, pose, idle, Mathf.InverseLerp(.7f, 1f, back)) : pose;
            }
            for (int i = 1; i < keys.Length; i++)
                if (t <= keys[i].time)
                {
                    float u = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(keys[i - 1].time, keys[i].time, t));
                    return Solve(rig, Lerp(keys[i - 1].key, keys[i].key, u), ConsumeElbow);
                }
            return Solve(rig, keys[keys.Length - 1].key, ConsumeElbow);
        });
        Write(rig, name, frames, false, null, ConsumeFolder);
    }

    // Eat and Drink states on the RightArm layer, entered from any state by UseItem (UseItemType 1 or 2) and back to
    // Idle at the end (Idle hides the arm again when nothing is held). Safe to run again.
    static void WireConsume()
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        Param(ctrl, "UseItem", AnimatorControllerParameterType.Trigger);
        Param(ctrl, "UseItemType", AnimatorControllerParameterType.Int);
        Param(ctrl, "UseItemSpeed", AnimatorControllerParameterType.Float, 1f);
        var sm = System.Array.Find(ctrl.layers, l => l.name == "RightArm").stateMachine;
        var idle = System.Array.Find(sm.states, s => s.state.name == "Idle").state;
        foreach (var t in sm.anyStateTransitions)
            if (t.destinationState != null && (t.destinationState.name == "Eat" || t.destinationState.name == "Drink")) sm.RemoveAnyStateTransition(t);
        foreach (var child in sm.states) if (child.state.name == "Eat" || child.state.name == "Drink") sm.RemoveState(child.state);
        string[] names = { "Eat", "Drink" };
        for (int n = 0; n < names.Length; n++)
        {
            var state = sm.AddState(names[n], new Vector3(260, 420 + n * 70));
            state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ConsumeFolder}/{names[n]}.anim");
            state.tag = "UseItem";
            state.speedParameterActive = true;
            state.speedParameter = "UseItemSpeed";
            state.writeDefaultValues = idle.writeDefaultValues;
            var into = sm.AddAnyStateTransition(state);
            into.AddCondition(AnimatorConditionMode.If, 0f, "UseItem");
            into.AddCondition(AnimatorConditionMode.Equals, n + 1, "UseItemType");
            into.hasExitTime = false; into.hasFixedDuration = true; into.duration = .08f; into.canTransitionToSelf = false;
            Transition(state, idle, .97f, .06f);
        }
        EditorUtility.SetDirty(ctrl);
    }

    public static void PreviewConsume()
    {
        RenderSheets(new[] { "Eat" }, ConsumeFolder, AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Game/Items/Data/Consumables/Apple.asset"), "Temp/Consume", 4);
        RenderSheets(new[] { "Drink" }, ConsumeFolder, AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Game/Items/Data/Consumables/healPotion.asset"), "Temp/Consume", 4);
    }
}
