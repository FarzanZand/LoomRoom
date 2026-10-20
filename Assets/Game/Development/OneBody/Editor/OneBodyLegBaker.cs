using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// R&D, branch onebody: legs for the one body, seen from outside.
// The table body plays FirstPersonTable.controller, a generic controller, while the third-person walk, run, sidestep and
// jump clips are humanoid. This samples each of them on the Synty character the table body is made from (Characters.fbx,
// the same skeleton and bone names), keeps the leg bones only and writes them as generic clips in
// Characters/Animations/FirstPersonPlayer/Legs (edit them freely afterwards; baking again overwrites them). Then it adds a
// "Legs" layer at the end of FirstPersonTable.controller, masked to the legs: a 2D blend on MoveX/MoveZ (the player's
// velocity in the look's frame, metres a second, fed by PlayerAnimation) placed by speed as the enemies' locomotion is,
// with faster playback for the player's speeds, and an In Air state while not Grounded.
// First person can't change: the layer only moves leg bones, and the first-person arms are cut from arm triangles only.
// Backwards is the walk played in reverse; sidestep left is sidestep right mirrored.
public static class OneBodyLegBaker
{
    const string Source = "Assets/Synty/PolygonDarkFantasy/Models/Characters.fbx";
    const string Locomotion = "Assets/Game/Characters/Animations/ThirdPerson/Locomotion/";
    const string Folder = "Assets/Game/Characters/Animations/FirstPersonPlayer/Legs";
    const string Controller = "Assets/Game/Characters/Animations/FirstPersonTable.controller";
    const string TablePlayer = "Assets/Game/Characters/Players/Table/TablePlayer.prefab";
    const string LayerName = "Legs";
    const float Rate = 30f;

    static readonly string[] Side = { "UpperLeg_", "LowerLeg_", "Ankle_", "Ball_", "Toes_" };

    // One baked clip: the source, whether it is played backwards, mirrored left for right, and whether it loops.
    struct Take { public string name, source; public bool reverse, mirror, loop; }

    static readonly Take[] Takes =
    {
        new Take { name = "Legs Idle", source = "Idle", loop = true },
        new Take { name = "Legs Walk", source = "Walk", loop = true },
        new Take { name = "Legs Run", source = "Run", loop = true },
        new Take { name = "Legs Walk Back", source = "Walk", reverse = true, loop = true },
        new Take { name = "Legs Strafe Right", source = "Strafe Right", loop = true },
        new Take { name = "Legs Strafe Left", source = "Strafe Right", mirror = true, loop = true },
        new Take { name = "Legs In Air", source = "JumpInAir", loop = true },
    };

    public static string Bake()
    {
        var log = new System.Text.StringBuilder();
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game/Characters/Animations/FirstPersonPlayer", "Legs");

        // The table body's rest pose: the leg chain's parents in its own frame (where the baked rotations must land).
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TablePlayer);
        Transform rig = null;
        foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) if (t.name == "BodyRig") rig = t;
        if (rig == null) return "No BodyRig in TablePlayer: build the one body first.";
        var target = new Dictionary<string, Transform>();
        foreach (var t in rig.GetComponentsInChildren<Transform>(true)) target.TryAdd(t.name, t);

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        var source = Object.Instantiate(model);
        source.hideFlags = HideFlags.HideAndDontSave;
        var clips = new Dictionary<string, AnimationClip>();
        HumanPoseHandler handlerToDispose = null;
        try
        {
            var animator = source.GetComponent<Animator>();
            animator.applyRootMotion = false;
            var handler = handlerToDispose = new HumanPoseHandler(animator.avatar, source.transform);
            var muscleIndex = new Dictionary<string, int>();
            for (int i = 0; i < HumanTrait.MuscleCount; i++) muscleIndex[HumanTrait.MuscleName[i]] = i;
            var bones = new Dictionary<string, Transform>();
            foreach (var t in source.GetComponentsInChildren<Transform>(true)) bones.TryAdd(t.name, t);
            var legs = new List<string>();
            foreach (var side in new[] { "L", "R" }) foreach (var b in Side) if (bones.ContainsKey(b + side) && target.ContainsKey(b + side)) legs.Add(b + side);

            // Rest rotations in the root's frame (for the mirror): the source's bind pose.
            var restRoot = new Dictionary<string, Quaternion>();
            foreach (var b in legs) restRoot[b] = Quaternion.Inverse(source.transform.rotation) * bones[b].rotation;

            foreach (var take in Takes)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Locomotion + take.source + ".anim");
                if (clip == null) { log.AppendLine("Missing " + take.source); continue; }
                var frames = Mathf.Max(2, Mathf.RoundToInt(clip.length * Rate) + 1);
                var curves = new Dictionary<string, AnimationCurve[]>();
                foreach (var b in legs) curves[b] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };

                // The clip's muscles and body, posed straight onto the model (no animator needs to run in the editor).
                var muscles = new Dictionary<int, AnimationCurve>();
                var body = new Dictionary<string, AnimationCurve>();
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (b.type != typeof(Animator)) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, b);
                    if (muscleIndex.TryGetValue(b.propertyName, out int m)) muscles[m] = curve;
                    else if (b.propertyName.StartsWith("RootT.") || b.propertyName.StartsWith("RootQ.")) body[b.propertyName] = curve;
                }
                float Read(string key, float time, float fallback) => body.TryGetValue(key, out var c) ? c.Evaluate(time) : fallback;
                {
                    for (int f = 0; f < frames; f++)
                    {
                        float t = Mathf.Min(clip.length, f / Rate);
                        float at = take.reverse ? clip.length - t : t;
                        var pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
                        foreach (var m in muscles) pose.muscles[m.Key] = m.Value.Evaluate(at);
                        pose.bodyPosition = new Vector3(Read("RootT.x", at, 0f), Read("RootT.y", at, 1f), Read("RootT.z", at, 0f));
                        pose.bodyRotation = new Quaternion(Read("RootQ.x", at, 0f), Read("RootQ.y", at, 0f), Read("RootQ.z", at, 0f), Read("RootQ.w", at, 1f));
                        handler.SetHumanPose(ref pose);
                        // Each bone in the root's frame, then into the table body's frame: the upper legs relative to its
                        // hips at rest (its hips keep the arms' pose), the rest relative to their animated parents.
                        var rootSpace = new Dictionary<string, Quaternion>();
                        foreach (var b in legs) rootSpace[b] = Quaternion.Inverse(source.transform.rotation) * bones[b].rotation;
                        if (take.mirror)
                        {
                            var mirrored = new Dictionary<string, Quaternion>();
                            foreach (var b in legs)
                            {
                                var other = b.EndsWith("_L") ? b.Substring(0, b.Length - 1) + "R" : b.Substring(0, b.Length - 1) + "L";
                                if (!rootSpace.ContainsKey(other)) continue;
                                // The other side's turn from its rest, reflected across the body's middle, on this side's rest.
                                var d = rootSpace[other] * Quaternion.Inverse(restRoot[other]);
                                var reflected = new Quaternion(d.x, -d.y, -d.z, d.w);
                                mirrored[b] = reflected * restRoot[b];
                            }
                            rootSpace = mirrored;
                        }
                        foreach (var b in legs)
                        {
                            if (!rootSpace.TryGetValue(b, out var r)) continue;
                            var parent = target[b].parent;
                            Quaternion parentRoot = parent.name == "Hips"
                                ? Quaternion.Inverse(rig.rotation) * parent.rotation
                                : rootSpace.TryGetValue(parent.name, out var pr) ? pr : Quaternion.Inverse(rig.rotation) * parent.rotation;
                            var local = Quaternion.Inverse(parentRoot) * r;
                            var c = curves[b];
                            c[0].AddKey(t, local.x); c[1].AddKey(t, local.y); c[2].AddKey(t, local.z); c[3].AddKey(t, local.w);
                        }
                    }
                }

                var path = Folder + "/" + take.name + ".anim";
                var baked = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (baked == null) { baked = new AnimationClip(); AssetDatabase.CreateAsset(baked, path); }
                baked.ClearCurves();
                baked.frameRate = Rate;
                foreach (var b in legs)
                {
                    var p = AnimationUtility.CalculateTransformPath(target[b], rig);
                    string[] axes = { "x", "y", "z", "w" };
                    for (int i = 0; i < 4; i++)
                        AnimationUtility.SetEditorCurve(baked, EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalRotation." + axes[i]), curves[b][i]);
                }
                baked.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(baked);
                settings.loopTime = take.loop;
                AnimationUtility.SetAnimationClipSettings(baked, settings);
                EditorUtility.SetDirty(baked);
                clips[take.name] = baked;
                log.AppendLine($"{take.name}: {frames} frames");
            }
        }
        finally { handlerToDispose?.Dispose(); Object.DestroyImmediate(source); }
        AssetDatabase.SaveAssets();

        log.Append(Wire(rig, clips));
        Debug.Log("[OneBody legs] " + log);
        return log.ToString();
    }

    // The Legs layer: added once, its blend tree and states rebuilt on every bake.
    static string Wire(Transform rig, Dictionary<string, AnimationClip> clips)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        foreach (var p in new[] { "MoveX", "MoveZ" })
            if (System.Array.FindIndex(controller.parameters, x => x.name == p) < 0) controller.AddParameter(p, AnimatorControllerParameterType.Float);

        var maskPath = Folder + "/Legs mask.mask";
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
        if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, maskPath); }
        // Paths relative to the animator (BodyRig), every bone listed, only the leg bones on.
        var all = rig.GetComponentsInChildren<Transform>(true);
        mask.transformCount = all.Length;
        for (int i = 0; i < all.Length; i++)
        {
            mask.SetTransformPath(i, AnimationUtility.CalculateTransformPath(all[i], rig));
            bool leg = false;
            foreach (var s in Side) if (all[i].name.StartsWith(s)) leg = true;
            mask.SetTransformActive(i, leg);
        }
        EditorUtility.SetDirty(mask);

        var layers = controller.layers;
        int index = System.Array.FindIndex(layers, l => l.name == LayerName);
        if (index < 0)
        {
            controller.AddLayer(LayerName);
            layers = controller.layers;
            index = layers.Length - 1;
        }
        var layer = layers[index];
        layer.avatarMask = mask;
        layer.defaultWeight = 1f;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        var machine = layer.stateMachine;
        foreach (var s in machine.states) machine.RemoveState(s.state);
        foreach (var t in machine.anyStateTransitions) machine.RemoveAnyStateTransition(t);

        // The last bake's blend tree is a sub-asset of the controller: removed, not left behind.
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(Controller))
            if (sub is BlendTree old && old.name == "Legs Locomotion") Object.DestroyImmediate(old, true);
        var tree = new BlendTree { name = "Legs Locomotion", blendType = BlendTreeType.FreeformDirectional2D, blendParameter = "MoveX", blendParameterY = "MoveZ", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.hideFlags = HideFlags.HideInHierarchy;
        // The clips walk in place. Placed as the enemies' locomotion places them (walk 0.9 m/s, run 2.6), plus faster
        // playback for the player's own speeds (walk 3, sprint 6, backwards and sideways up to 3).
        var children = new List<ChildMotion>();
        void Add(string name, float x, float z, float timeScale)
        {
            if (clips.TryGetValue(name, out var c)) children.Add(new ChildMotion { motion = c, position = new Vector2(x, z), timeScale = timeScale });
        }
        Add("Legs Idle", 0f, 0f, 1f);
        Add("Legs Walk", 0f, .9f, 1f);
        Add("Legs Run", 0f, 2.6f, 1f);
        Add("Legs Run", 0f, 6f, 2f);
        Add("Legs Walk Back", 0f, -.9f, 1f);
        Add("Legs Walk Back", 0f, -3f, 2.2f);
        Add("Legs Strafe Right", .8f, 0f, 1f);
        Add("Legs Strafe Right", 3f, 0f, 2.1f);
        Add("Legs Strafe Left", -.8f, 0f, 1f);
        Add("Legs Strafe Left", -3f, 0f, 2.1f);
        tree.children = children.ToArray();
        var moving = machine.AddState("Locomotion");
        moving.motion = tree;
        moving.writeDefaultValues = false;
        machine.defaultState = moving;
        if (clips.TryGetValue("Legs In Air", out var air))
        {
            var inAir = machine.AddState("In Air");
            inAir.motion = air;
            inAir.writeDefaultValues = false;
            var up = moving.AddTransition(inAir);
            up.hasExitTime = false; up.duration = .15f;
            up.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            var down = inAir.AddTransition(moving);
            down.hasExitTime = false; down.duration = .1f;
            down.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        }
        layers[index] = layer;
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return $"Layer {LayerName} at index {index} of {layers.Length}, mask {mask.transformCount} paths.";
    }
}
