using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Tools > LoomRoom > Apply Combat Feel
//
// Generates motion for the first-person arms from the poses already authored in the project and
// tunes the FirstPersonTable controller around it. Source clips live in Animations/FirstPersonPlayer,
// generated ones in its Generated subfolder:
//   ChargeTension        additive loop: the hold pose pulled further back with a tremble, played on the
//                        ChargeAdditive layer whose weight follows the charge
//   Attack_Windup_Feel   the windup with an AttackBegin event (press consumed / tap queued)
//   Attack_Swing_A / _B  light swings: hang, whipped strike with torso follow, overshoot, longer settle
//   Attack_Swing_Heavy   heavy swing: coil, larger and faster strike, short hang, weighted recovery
//   Block_Raise          idle -> block pose over 0.22 s with a small overshoot
//   Block_Recoil         block hit knocked back toward the body, springing into the block idle
// Authored clips are never modified. Re-run after changing the source poses.
public static class CombatFeelSetup
{
    const string ControllerPath = "Assets/Game/Players/Shared/Animations/FirstPersonTable.controller";
    const string ClipDir        = "Assets/Game/Players/Shared/Animations/FirstPersonPlayer";
    const string GeneratedDir   = ClipDir + "/Generated";
    const string RightMaskPath  = "Assets/Game/Players/Shared/Animations/Anim masks/RightArmGeneric.mask";

    const string IdleClip      = ClipDir + "/Idle.anim";
    const string HoldClip      = ClipDir + "/Attack_Hold.anim";
    const string ReleaseClip   = ClipDir + "/Attack_Release.anim";
    const string WindupClip    = ClipDir + "/Attack_Windup.anim";
    const string BlockClip     = ClipDir + "/Block.anim";
    const string BlockHitClip  = ClipDir + "/BlockHit.anim";
    const string BlockIdleClip = ClipDir + "/BlockIdle.anim";

    const string ChargeLayer = "ChargeAdditive";
    const string PCharge = "Charge", PHoldSpeed = "HoldSpeed", PSwingIndex = "SwingIndex";
    const string PAttackHeld = "AttackHeld", PHeavy = "HeavyStrike", PReleaseSpeed = "ReleaseSpeed";

    [MenuItem("Tools/LoomRoom/Apply Combat Feel")]
    public static void Apply()
    {
        Directory.CreateDirectory(GeneratedDir);
        var idle      = Load(IdleClip);
        var hold      = Load(HoldClip);
        var release   = Load(ReleaseClip);
        var windupSrc = Load(WindupClip);
        var block     = Load(BlockClip);
        var blockHit  = Load(BlockHitClip);
        var blockIdle = Load(BlockIdleClip);

        var tension = BuildChargeTension(idle, hold);
        var windup  = BuildWindup(windupSrc);
        var swingA  = BuildLightSwing(release, "Attack_Swing_A", 0);
        var swingB  = BuildLightSwing(release, "Attack_Swing_B", 1);
        var heavy   = BuildHeavySwing(release);
        var raise   = BuildBlockRaise(idle, block);
        var recoil  = BuildBlockRecoil(blockHit, blockIdle, idle, block);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        TuneController(controller, tension, windup, swingA, swingB, heavy, raise, recoil);

        AssetDatabase.SaveAssets();
        Debug.Log("[CombatFeelSetup] Generated clips and tuned FirstPersonTable.controller.");
    }

    // ── Clip generation ───────────────────────────────────────────────

    static AnimationClip Load(string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) throw new FileNotFoundException(path);
        return clip;
    }

    // Every curve of a clip sampled at one time, keyed by binding.
    static Dictionary<EditorCurveBinding, float> Sample(AnimationClip clip, float time)
    {
        var pose = new Dictionary<EditorCurveBinding, float>();
        foreach (var b in AnimationUtility.GetCurveBindings(clip))
            pose[b] = AnimationUtility.GetEditorCurve(clip, b).Evaluate(time);
        return pose;
    }

    static bool IsEuler(EditorCurveBinding b) => b.propertyName.StartsWith("localEulerAnglesRaw");
    static float Wrap(float deg) => Mathf.Repeat(deg + 180f, 360f) - 180f;

    // Delta from a to b per binding; angles wrapped so the shortest turn is used.
    static Dictionary<EditorCurveBinding, float> Delta(Dictionary<EditorCurveBinding, float> a, Dictionary<EditorCurveBinding, float> b)
    {
        var d = new Dictionary<EditorCurveBinding, float>();
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out float to)) continue;
            float diff = to - kv.Value;
            d[kv.Key] = IsEuler(kv.Key) ? Wrap(diff) : diff;
        }
        return d;
    }

    // Writes a clip where each binding's value over time comes from a function of (binding, normalized time).
    static AnimationClip Write(string name, float length, bool loop, IEnumerable<EditorCurveBinding> bindings, Func<EditorCurveBinding, float, float> value, AnimationEvent[] events = null)
    {
        string path = $"{GeneratedDir}/{name}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        bool fresh = clip == null;
        if (fresh) clip = new AnimationClip();
        clip.ClearCurves();
        clip.frameRate = 60f;
        int frames = Mathf.Max(2, Mathf.RoundToInt(length * 60f) + 1);
        foreach (var b in bindings)
        {
            var keys = new Keyframe[frames];
            for (int i = 0; i < frames; i++)
            {
                float t = i / 60f;
                keys[i] = new Keyframe(t, value(b, Mathf.Clamp01(t / length)));
            }
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < frames; i++) curve.SmoothTangents(i, 0f);
            AnimationUtility.SetEditorCurve(clip, b, curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        settings.stopTime = length;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AnimationUtility.SetAnimationEvents(clip, events ?? new AnimationEvent[0]);
        if (fresh) AssetDatabase.CreateAsset(clip, path);
        else EditorUtility.SetDirty(clip);
        return clip;
    }

    // Additive: the hold pose pushed 45% further from idle, breathing slowly with a fine tremble on the arm chain.
    // Reference pose is the hold clip's first frame, so the additive delta is exactly the extra pull plus tremble.
    static AnimationClip BuildChargeTension(AnimationClip idle, AnimationClip hold)
    {
        var idle0 = Sample(idle, 0f);
        var hold0 = Sample(hold, 0f);
        var pull  = Delta(idle0, hold0);
        const float length = 1f;   // integer-Hz components below keep the loop seamless
        var clip = Write("ChargeTension", length, true, hold0.Keys, (b, u) =>
        {
            if (!IsEuler(b) || !b.path.Contains("Clavicle_R")) return hold0[b];
            bool arm = b.path.EndsWith("Shoulder_R") || b.path.EndsWith("Elbow_R") || b.path.EndsWith("Hand_R");
            float phase = (b.propertyName.EndsWith(".x") ? 0f : b.propertyName.EndsWith(".y") ? 2.1f : 4.2f) + b.path.Length * 0.37f;
            float w = u * Mathf.PI * 2f;
            float breathe = 1.6f * Mathf.Sin(w + phase);
            float tremble = arm ? 1.1f * Mathf.Sin(9f * w + phase) + 0.7f * Mathf.Sin(14f * w + phase * 1.7f) : 0f;
            return hold0[b] + pull[b] * 0.35f + breathe + tremble;
        });
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.hasAdditiveReferencePose = true;
        settings.additiveReferencePoseClip = hold;
        settings.additiveReferencePoseTime = 0f;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    // Windup copy with an AttackBegin event on its first frame so PlayerCombat knows a queued press has been consumed.
    static AnimationClip BuildWindup(AnimationClip src)
    {
        var start = Sample(src, 0f);
        var curves = new Dictionary<EditorCurveBinding, AnimationCurve>();
        foreach (var b in start.Keys) curves[b] = AnimationUtility.GetEditorCurve(src, b);
        var events = new List<AnimationEvent> { new AnimationEvent { functionName = "AttackBegin", time = 0f } };
        foreach (var e in src.events) events.Add(new AnimationEvent { functionName = e.functionName, time = e.time, stringParameter = e.stringParameter, floatParameter = e.floatParameter, intParameter = e.intParameter, objectReferenceParameter = e.objectReferenceParameter });
        return Write("Attack_Windup_Feel", src.length, false, start.Keys, (b, u) => curves[b].Evaluate(u * src.length), events.ToArray());
    }

    // Light swing rebuilt from the authored release: a short anticipation hang, a whipped strike with the arm
    // motion amplified and the torso following, an overshoot past the end pose and a longer settle.
    // Variant 1 rolls the wrist and lifts the arc a little so alternating swings don't repeat exactly.
    static AnimationClip BuildLightSwing(AnimationClip src, string name, int variant)
    {
        float srcLen = src.length;
        float length = srcLen * 1.2f;
        var start = Sample(src, 0f);
        var end   = Sample(src, srcLen);
        var bindings = new List<EditorCurveBinding>(start.Keys);
        var curves = new Dictionary<EditorCurveBinding, AnimationCurve>();
        foreach (var b in bindings) curves[b] = AnimationUtility.GetEditorCurve(src, b);

        // Time warp new (0..1) -> source (0..1): hang, whip, ease out.
        float Warp(float u)
        {
            if (u < 0.12f) return Mathf.Lerp(0f, 0.06f, Mathf.SmoothStep(0f, 1f, u / 0.12f));
            if (u < 0.42f) return Mathf.Lerp(0.06f, 0.55f, (u - 0.12f) / 0.30f);
            float r = (u - 0.42f) / 0.58f;
            return Mathf.Lerp(0.55f, 1f, 1f - Mathf.Pow(1f - r, 2.2f));
        }
        float Unwarp(float s)   // source (0..1) -> new (0..1), by search
        {
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 30; i++) { float mid = (lo + hi) * 0.5f; if (Warp(mid) < s) lo = mid; else hi = mid; }
            return (lo + hi) * 0.5f;
        }
        EditorCurveBinding Find(string bone, string axis) { foreach (var b in bindings) if (b.path.EndsWith("/" + bone) && b.propertyName.EndsWith(axis)) return b; return default; }
        bool RightArm(EditorCurveBinding b) => b.path.EndsWith("Shoulder_R") || b.path.EndsWith("Elbow_R") || b.path.EndsWith("Hand_R");

        var events = new List<AnimationEvent>();
        foreach (var e in src.events)
            events.Add(new AnimationEvent { functionName = e.functionName, time = Unwarp(e.time / srcLen) * length, stringParameter = e.stringParameter, floatParameter = e.floatParameter, intParameter = e.intParameter, objectReferenceParameter = e.objectReferenceParameter });

        return Write(name, length, false, bindings, (b, u) =>
        {
            float s = Warp(u);
            float v = curves[b].Evaluate(s * srcLen);
            if (!IsEuler(b)) return v;
            string axis = b.propertyName.Substring(b.propertyName.Length - 2);
            float d = Wrap(v - start[b]);
            float result = start[b] + d;
            if (RightArm(b))
            {
                float amp = 0.22f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u / 0.6f));            // more reach through the strike
                float over = Wrap(end[b] - start[b]) * 0.10f * Mathf.Exp(-6f * Mathf.Max(0f, u - 0.42f)) * Mathf.Sin(Mathf.PI * Mathf.Clamp01((u - 0.42f) / 0.4f)); // overshoot past the end pose, then settle
                result = start[b] + d * (1f + amp) + over;
                if (variant == 1)
                {
                    float bump = Mathf.Sin(Mathf.PI * Mathf.Clamp01(u / 0.8f));
                    if (b.path.EndsWith("Hand_R") && axis == ".z")     result += 16f * bump;   // wrist roll
                    if (b.path.EndsWith("Shoulder_R") && axis == ".x") result += 9f * bump;    // higher arc
                }
            }
            else if (b.path.EndsWith("Spine_03") || b.path.EndsWith("Clavicle_R"))
            {
                // Torso and clavicle follow a fraction of the upper arm's travel on the same axis.
                var sb = Find("Shoulder_R", axis);
                if (sb.path != null) { float sd = Wrap(curves[sb].Evaluate(s * srcLen) - start[sb]); result += sd * (b.path.EndsWith("Spine_03") ? 0.18f : 0.28f); }
            }
            return result;
        }, events.ToArray());
    }

    // Heavy swing from the authored release. The source spends 0-40% striking (peak at 23%) and 40-100% returning,
    // so the phases are laid out around that: a coil backwards from the hold pose, a strike faster and 30% larger
    // than the light one, a hang at the end of the arc, then a slow, weighted recovery.
    static AnimationClip BuildHeavySwing(AnimationClip src)
    {
        float srcLen = src.length;
        const float length = 0.75f;
        var start = Sample(src, 0f);
        var end   = Sample(src, srcLen);
        var bindings = new List<EditorCurveBinding>(start.Keys);
        var curves = new Dictionary<EditorCurveBinding, AnimationCurve>();
        foreach (var b in bindings) curves[b] = AnimationUtility.GetEditorCurve(src, b);

        // Phases of the new clip (normalized): coil 0-0.18, strike 0.18-0.40, hang 0.40-0.47, recovery 0.47-1.
        // The arc's end is at the edge of the view, so the hang is short and the recovery starts moving at once.
        const float coilEnd = 0.18f, strikeEnd = 0.40f, hangEnd = 0.47f;
        float Warp(float u)   // new (0..1) -> source (0..1)
        {
            if (u < coilEnd)   return 0f;
            if (u < strikeEnd) return Mathf.Lerp(0f, 0.42f, Mathf.SmoothStep(0f, 1f, (u - coilEnd) / (strikeEnd - coilEnd)) * 0.3f + (u - coilEnd) / (strikeEnd - coilEnd) * 0.7f);
            if (u < hangEnd)   return Mathf.Lerp(0.42f, 0.48f, (u - strikeEnd) / (hangEnd - strikeEnd));
            float r = (u - hangEnd) / (1f - hangEnd);
            return Mathf.Lerp(0.48f, 1f, 1f - Mathf.Pow(1f - r, 1.6f));
        }
        EditorCurveBinding Find(string bone, string axis) { foreach (var b in bindings) if (b.path.EndsWith("/" + bone) && b.propertyName.EndsWith(axis)) return b; return default; }
        bool RightArm(EditorCurveBinding b) => b.path.EndsWith("Shoulder_R") || b.path.EndsWith("Elbow_R") || b.path.EndsWith("Hand_R");

        var events = new[]
        {
            new AnimationEvent { functionName = "PlaySwingAudio", time = coilEnd * length },
            new AnimationEvent { functionName = "EnableHitbox",   time = (coilEnd + 0.03f) * length },
            new AnimationEvent { functionName = "DisableHitbox",  time = (strikeEnd + 0.05f) * length },
        };

        return Write("Attack_Swing_Heavy", length, false, bindings, (b, u) =>
        {
            float s = Warp(u);
            float v = curves[b].Evaluate(s * srcLen);
            if (!IsEuler(b)) return v;
            string axis = b.propertyName.Substring(b.propertyName.Length - 2);
            float d = Wrap(v - start[b]);
            float result = start[b] + d;
            if (RightArm(b))
            {
                // Coil: pull backwards along the strike direction, using the early strike delta mirrored.
                float early = Wrap(curves[b].Evaluate(0.10f * srcLen) - start[b]);
                float coil = u < coilEnd ? -0.45f * early * Mathf.Sin(Mathf.PI * u / coilEnd * 0.5f) : 0f;
                float amp = u < hangEnd ? 0.15f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((u - coilEnd) / (hangEnd - coilEnd))) : 0f;
                float endDelta = Wrap(curves[b].Evaluate(0.42f * srcLen) - start[b]);
                float over = u >= strikeEnd ? endDelta * 0.05f * Mathf.Exp(-6f * (u - strikeEnd)) * Mathf.Sin(Mathf.PI * Mathf.Clamp01((u - strikeEnd) / 0.25f)) : 0f;
                result = start[b] + d * (1f + amp) + coil + over;
            }
            else if (b.path.EndsWith("Spine_03") || b.path.EndsWith("Spine_02") || b.path.EndsWith("Clavicle_R"))
            {
                var sb = Find("Shoulder_R", axis);
                if (sb.path != null) { float sd = Wrap(curves[sb].Evaluate(s * srcLen) - start[sb]); result += sd * (b.path.EndsWith("Clavicle_R") ? 0.32f : b.path.EndsWith("Spine_03") ? 0.22f : 0.12f); }
            }
            return result;
        }, events);
    }

    // Idle -> block pose with an eased arrival and a small overshoot so the shield reads as thrown up, not placed.
    static AnimationClip BuildBlockRaise(AnimationClip idle, AnimationClip block)
    {
        var from = Sample(idle, 0f);
        var to   = Sample(block, block.length);
        var d    = Delta(from, to);
        return Write("Block_Raise", 0.22f, false, from.Keys, (b, u) =>
        {
            if (!d.ContainsKey(b)) return from[b];
            float s = 1f - Mathf.Pow(1f - u, 3f) + 0.07f * Mathf.Sin(Mathf.PI * u) * u;
            return from[b] + d[b] * s;
        });
    }

    // Block hit: the authored hit jolt (1.5x) plus the shield knocked a third of the way back toward the idle
    // pose, springing back into the block idle with one damped bounce.
    static AnimationClip BuildBlockRecoil(AnimationClip blockHit, AnimationClip blockIdle, AnimationClip idle, AnimationClip block)
    {
        var rest  = Sample(blockIdle, 0f);
        var hit   = Sample(blockHit, blockHit.length);
        var jolt  = Delta(rest, hit);
        var raise = Delta(Sample(idle, 0f), Sample(block, block.length));   // idle -> block; negative = pushed back
        var bindings = new List<EditorCurveBinding>(hit.Keys);
        var events = new List<AnimationEvent>();
        foreach (var e in blockHit.events) events.Add(new AnimationEvent { functionName = e.functionName, time = 0f, stringParameter = e.stringParameter, floatParameter = e.floatParameter, intParameter = e.intParameter, objectReferenceParameter = e.objectReferenceParameter });
        return Write("Block_Recoil", 0.4f, false, bindings, (b, u) =>
        {
            float baseValue = rest.TryGetValue(b, out float r) ? r : hit[b];
            float amount = 0f;
            if (jolt.TryGetValue(b, out float j)) amount += j * 2.5f;
            if (IsEuler(b) && raise.TryGetValue(b, out float k) && (b.path.EndsWith("Shoulder_L") || b.path.EndsWith("Elbow_L") || b.path.EndsWith("Clavicle_L"))) amount -= k * 0.5f;
            float spring = Mathf.Exp(-5.5f * u) * Mathf.Cos(2f * Mathf.PI * 1.4f * u);
            return baseValue + amount * spring;
        }, events.ToArray());
    }

    // ── Controller tuning ─────────────────────────────────────────────

    static void TuneController(AnimatorController c, AnimationClip tension, AnimationClip windupClip, AnimationClip swingA, AnimationClip swingB, AnimationClip heavySwing, AnimationClip raise, AnimationClip recoil)
    {
        if (!HasParam(c, PCharge))     c.AddParameter(PCharge, AnimatorControllerParameterType.Float);
        if (!HasParam(c, PHoldSpeed))  c.AddParameter(new AnimatorControllerParameter { name = PHoldSpeed, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        if (!HasParam(c, PSwingIndex)) c.AddParameter(PSwingIndex, AnimatorControllerParameterType.Int);

        // Additive charge layer, right arm only, weight driven by PlayerCombat.
        int layerIndex = Array.FindIndex(c.layers, l => l.name == ChargeLayer);
        if (layerIndex < 0)
        {
            var sm = new AnimatorStateMachine { name = ChargeLayer, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, c);
            c.AddLayer(new AnimatorControllerLayer
            {
                name = ChargeLayer, stateMachine = sm, defaultWeight = 0f,
                blendingMode = AnimatorLayerBlendingMode.Additive,
                avatarMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(RightMaskPath),
            });
            layerIndex = c.layers.Length - 1;
        }
        var chargeSm = c.layers[layerIndex].stateMachine;
        var tensionState = FindState(chargeSm, "Tension") ?? chargeSm.AddState("Tension");
        tensionState.motion = tension;
        chargeSm.defaultState = tensionState;

        // Right arm: attack pacing, two alternating light swings, and chaining out of the recovery.
        var right = Layer(c, "RightArm").stateMachine;
        var hold = FindState(right, "Attack_Hold");
        hold.speedParameter = PHoldSpeed; hold.speedParameterActive = true;
        var windup   = FindState(right, "Attack_Windup");
        var releaseA = FindState(right, "Attack_Release");
        var heavy    = FindState(right, "Attack_HeavyRelease");
        var idle     = FindState(right, "Idle");
        windup.motion = windupClip;
        releaseA.motion = swingA;
        heavy.motion = heavySwing;
        var releaseB = FindState(right, "Attack_Release_B");
        if (releaseB == null)
        {
            releaseB = right.AddState("Attack_Release_B", FindPosition(right, "Attack_Release") + new Vector3(0, 60, 0));
            releaseB.tag = releaseA.tag;
            releaseB.speedParameter = PReleaseSpeed; releaseB.speedParameterActive = true;
        }
        releaseB.motion = swingB;
        // Which light swing plays is picked by SwingIndex (PlayerCombat alternates it per press).
        foreach (var t in windup.transitions) if (t.destinationState == releaseA) RequireSwing(t, 0);
        foreach (var t in hold.transitions)   if (t.destinationState == releaseA) RequireSwing(t, 0);
        EnsureTransition(windup, releaseB, 1f, 0.03f, (PAttackHeld, AnimatorConditionMode.IfNot, 0), (PHeavy, AnimatorConditionMode.IfNot, 0), (PSwingIndex, AnimatorConditionMode.Equals, 1));
        EnsureTransition(hold,   releaseB, -1f, 0.04f, (PAttackHeld, AnimatorConditionMode.IfNot, 0), (PHeavy, AnimatorConditionMode.IfNot, 0), (PSwingIndex, AnimatorConditionMode.Equals, 1));
        EnsureTransition(releaseB, idle, 0.8f, 0.25f);
        // Spamming the button starts the next windup while the previous swing is still settling.
        EnsureTransition(releaseA, windup, 0.55f, 0.06f, (PAttackHeld, AnimatorConditionMode.If, 0));
        EnsureTransition(releaseB, windup, 0.55f, 0.06f, (PAttackHeld, AnimatorConditionMode.If, 0));

        // The windup always settles into Hold; Hold releases on its first frame when the button is up. Deciding
        // hold-vs-release at the windup's exit time is a race when a press lands right at its end.
        foreach (var t in windup.transitions) if (t.destinationState == hold) t.conditions = new AnimatorCondition[0];
        Blend(right, "Idle", "Attack_Windup", 0.08f);
        Blend(right, "Attack_Windup", "Attack_Hold", 0.05f, exitTime: 0.95f);
        Blend(right, "Attack_Windup", "Attack_Release", 0.03f);
        Blend(right, "Attack_Windup", "Attack_HeavyRelease", 0.05f);
        Blend(right, "Attack_Hold", "Attack_Release", 0.04f);
        Blend(right, "Attack_Hold", "Attack_HeavyRelease", 0.07f);
        Blend(right, "Attack_Release", "Idle", 0.25f, exitTime: 0.8f);
        Blend(right, "Attack_HeavyRelease", "Idle", 0.32f, exitTime: 0.85f);

        // Left arm: real block motion and reactions that start immediately.
        var left = Layer(c, "LeftArm").stateMachine;
        var blockState = FindState(left, "Block");
        blockState.motion = raise;
        var hitState = FindState(left, "BlockHit");
        hitState.motion = recoil; hitState.speed = 1f;
        Blend(left, "Idle", "Block", 0.05f);
        Blend(left, "Block", "BlockIdle", 0.08f, exitTime: 0.95f);
        Blend(left, "Block", "Idle", 0.12f);
        Blend(left, "BlockIdle", "BlockHit", 0.02f, hasExitTime: false);
        Blend(left, "BlockHit", "BlockIdle", 0.15f, exitTime: 0.9f);
        Blend(left, "BlockHit", "Idle", 0.15f, hasExitTime: false);
        Blend(left, "BlockIdle", "Idle", 0.15f);
        LayoutGraphs(c);
        EditorUtility.SetDirty(c);
    }

    // Lays the arms layers out on a grid so the graphs read top to bottom: idle row, windup row, release row.
    // Empty sub-state machines nothing points at are removed.
    static void LayoutGraphs(AnimatorController c)
    {
        Vector3 P(int col, int row) => new Vector3(col * 260, row * 110, 0);
        var right = Layer(c, "RightArm").stateMachine;
        Place(right, new Dictionary<string, Vector3>
        {
            ["Hidden"] = P(0, 0), ["Idle"] = P(1, 0), ["Hurt"] = P(3, 0),
            ["Attack_Windup"] = P(0, 1), ["Attack_Hold"] = P(1, 1),
            ["Attack_Release"] = P(0, 2), ["Attack_Release_B"] = P(1, 2), ["Attack_HeavyRelease"] = P(2, 2),
        });
        var left = Layer(c, "LeftArm").stateMachine;
        Place(left, new Dictionary<string, Vector3>
        {
            ["Hidden"] = P(0, 0), ["Idle"] = P(1, 0),
            ["Block"] = P(0, 1), ["BlockIdle"] = P(1, 1), ["BlockHit"] = P(2, 1),
        });
        foreach (var layer in c.layers)
        {
            var sm = layer.stateMachine;
            sm.entryPosition = new Vector3(-300, 0, 0);
            sm.anyStatePosition = new Vector3(-300, 110, 0);
            sm.exitPosition = new Vector3(-300, 220, 0);
            foreach (var child in sm.stateMachines)
            {
                bool referenced = false;
                foreach (var s in sm.states) foreach (var t in s.state.transitions) if (t.destinationStateMachine == child.stateMachine) referenced = true;
                if (child.stateMachine.states.Length == 0 && child.stateMachine.stateMachines.Length == 0 && !referenced)
                    sm.RemoveStateMachine(child.stateMachine);
            }
        }
    }

    static void Place(AnimatorStateMachine sm, Dictionary<string, Vector3> positions)
    {
        var states = sm.states;
        for (int i = 0; i < states.Length; i++)
            if (positions.TryGetValue(states[i].state.name, out var pos)) states[i].position = pos;
        sm.states = states;
    }

    static bool HasParam(AnimatorController c, string name)
    {
        foreach (var p in c.parameters) if (p.name == name) return true;
        return false;
    }

    static AnimatorControllerLayer Layer(AnimatorController c, string name)
    {
        foreach (var l in c.layers) if (l.name == name) return l;
        throw new Exception($"Layer {name} not found");
    }

    static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        foreach (var s in sm.states) if (s.state.name == name) return s.state;
        return null;
    }

    static Vector3 FindPosition(AnimatorStateMachine sm, string name)
    {
        foreach (var s in sm.states) if (s.state.name == name) return s.position;
        return Vector3.zero;
    }

    // Adds a SwingIndex == value condition to a transition if it doesn't have one.
    static void RequireSwing(AnimatorStateTransition t, int value)
    {
        foreach (var cond in t.conditions) if (cond.parameter == PSwingIndex) return;
        t.AddCondition(AnimatorConditionMode.Equals, value, PSwingIndex);
    }

    // Creates from -> to with the given conditions unless an identical one exists; exitTime < 0 means no exit time.
    static void EnsureTransition(AnimatorState from, AnimatorState to, float exitTime, float duration, params (string param, AnimatorConditionMode mode, int value)[] conds)
    {
        foreach (var t in from.transitions)
        {
            if (t.destinationState != to || t.conditions.Length != conds.Length) continue;
            bool same = true;
            for (int i = 0; i < conds.Length && same; i++) same = t.conditions[i].parameter == conds[i].param && t.conditions[i].mode == conds[i].mode;
            if (same) { t.hasFixedDuration = true; t.duration = duration; t.hasExitTime = exitTime >= 0f; if (exitTime >= 0f) t.exitTime = exitTime; return; }
        }
        var n = from.AddTransition(to);
        n.hasFixedDuration = true; n.duration = duration; n.hasExitTime = exitTime >= 0f; if (exitTime >= 0f) n.exitTime = exitTime;
        n.interruptionSource = TransitionInterruptionSource.None;
        foreach (var c in conds) n.AddCondition(c.mode, c.value, c.param);
    }


    // Retunes every transition from -> to. exitTime sets hasExitTime=true with that time; hasExitTime=false clears it.
    static void Blend(AnimatorStateMachine sm, string from, string to, float duration, float exitTime = -1f, bool? hasExitTime = null)
    {
        var state = FindState(sm, from);
        if (state == null) { Debug.LogWarning($"[CombatFeelSetup] state {from} missing"); return; }
        int n = 0;
        foreach (var t in state.transitions)
        {
            if (t.destinationState == null || t.destinationState.name != to) continue;
            t.hasFixedDuration = true;
            t.duration = duration;
            if (exitTime >= 0f) { t.hasExitTime = true; t.exitTime = exitTime; }
            if (hasExitTime.HasValue) t.hasExitTime = hasExitTime.Value;
            n++;
        }
        if (n == 0) Debug.LogWarning($"[CombatFeelSetup] no transition {from} -> {to}");
    }
}
