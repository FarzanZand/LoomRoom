using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Unity.Cinemachine;

public static class CombatFeelEditor
{
    [MenuItem("Tools/LoomRoom/Apply combat feel")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        System.IO.Directory.CreateDirectory("Assets/Game/Combat/Effects");
        System.IO.Directory.CreateDirectory("Assets/Game/Players/Shared/Animations/Combat");
        AssetDatabase.Refresh();
        var hit=Audio("CombatHit","Wpn_Impact_Blade_Flesh_Short",.65f);
        var swing=Audio("CombatSwing","Wpn_Whoosh",.45f);
        var block=Audio("CombatBlock","Wpn_Impact_Blade_Metal_Short",.6f);
        var impact=Particle("HitBurst",new Color(.9f,.82f,.65f),.035f,10);
        var sparks=Particle("BlockSparks",new Color(.65f,.85f,1),.025f,14);
        var prefab=PrefabUtility.LoadPrefabContents("Assets/Game/Combat/Prefabs/CombatManager.prefab");
        Tune(prefab.GetComponent<CombatManager>(),hit,swing,block,impact,sparks);
        PrefabUtility.SaveAsPrefabAsset(prefab,"Assets/Game/Combat/Prefabs/CombatManager.prefab");PrefabUtility.UnloadPrefabContents(prefab);
        foreach(var manager in Object.FindObjectsByType<CombatManager>(FindObjectsInactive.Include))
        { Undo.RecordObject(manager,"Combat tuning");Tune(manager,hit,swing,block,impact,sparks);PrefabUtility.RecordPrefabInstancePropertyModifications(manager); }
        foreach(var camera in Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include))
        {
            var old=camera.GetComponent("CombatCameraImpulse");
            if(old!=null) Undo.DestroyObjectImmediate(old);
        }
        foreach(var reaction in Object.FindObjectsByType<HitReactionController>(FindObjectsInactive.Include)) reaction.enabled=reaction.GetComponentInParent<EnemyBrain>()!=null;
        foreach(var brain in Object.FindObjectsByType<EnemyBrain>(FindObjectsInactive.Include))
            if(brain.GetComponent<Animator>()!=null) brain.GetComponent<Animator>().applyRootMotion=false;
        var canvas=Object.FindAnyObjectByType<InteractUI>(FindObjectsInactive.Include).GetComponentInParent<Canvas>();
        var existing=canvas.transform.Find("CombatHurtVignette");
        Image image;
        if(existing==null)
        {
            var go=new GameObject("CombatHurtVignette",typeof(RectTransform),typeof(Image));
            go.transform.SetParent(canvas.transform,false);image=go.GetComponent<Image>();
        }else image=existing.GetComponent<Image>();
        var r=image.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
        var texture=new Texture2D(128,128,TextureFormat.RGBA32,false);
        for(int y=0;y<128;y++)for(int x=0;x<128;x++)
        {
            float edge=Mathf.Clamp01((Mathf.Max(Mathf.Abs(x-63.5f),Mathf.Abs(y-63.5f))/63.5f-.5f)*2);
            texture.SetPixel(x,y,new Color(1,1,1,edge*edge));
        }
        texture.Apply();System.IO.File.WriteAllBytes("Assets/Game/Combat/Effects/HurtVignette.png",texture.EncodeToPNG());Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset("Assets/Game/Combat/Effects/HurtVignette.png");
        var importer=(TextureImporter)AssetImporter.GetAtPath("Assets/Game/Combat/Effects/HurtVignette.png");
        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.alphaIsTransparency=true;importer.SaveAndReimport();
        image.sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Combat/Effects/HurtVignette.png");image.raycastTarget=false;image.color=Color.clear;
        foreach(var fx in Object.FindObjectsByType<PlayerFX>(FindObjectsInactive.Include))
        {var so=new SerializedObject(fx);so.FindProperty("hurtFlashImage").objectReferenceValue=image;so.ApplyModifiedProperties();}
        image.gameObject.SetActive(false);
        Animate();
        EnemyRecoil();
        AssetDatabase.SaveAssets();var scene=EditorSceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Debug.Log("[CombatFeel] Saved combat tuning, audio, particles, camera feedback and attack clips.");
    }
    static void Tune(CombatManager m,AudioData hit,AudioData swing,AudioData block,GameObject particle,GameObject sparks)
    {
        m.hitStopDuration=.045f;m.hitStopTimeScale=.12f;m.hitStopOnPlayerHurt=true;
        m.knockbackForceMultiplier=.22f;m.knockbackDuration=.18f;
        m.hitReactionEnabled=true;m.hitReactionAngle=20;m.hitReactionAttackSpeed=60;m.hitReactionDamping=9;m.hitReactionInfluenceDepth=2;m.hitReactionParentFalloff=.35f;
        m.hitFlashColor=new(1,.8f,.65f);m.hitFlashDuration=.065f;
        m.defaultHitAudio=hit;m.defaultSwingAudio=swing;m.blockAudio=block;m.playerHurtAudio=hit;
        m.hitParticlePrefabs=new(){particle};m.blockParticlePrefab=sparks;
        m.blockCancelWindow=.18f;m.blockStaminaCost=.75f;
        EditorUtility.SetDirty(m);
    }
    static AudioData Audio(string name,string prefix,float volume)
    {
        string path="Assets/Game/Audio/Data/"+name+".asset";
        var data=AssetDatabase.LoadAssetAtPath<AudioData>(path);
        if(data==null){data=ScriptableObject.CreateInstance<AudioData>();AssetDatabase.CreateAsset(data,path);}
        data.clips=AssetDatabase.FindAssets(prefix+" t:AudioClip",new[]{"Assets/Game/Audio/Library"}).Select(g=>AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g))).Take(3).ToArray();
        data.volume=volume;data.pitch=1;data.pitchVariance=.07f;EditorUtility.SetDirty(data);return data;
    }
    static GameObject Particle(string name,Color color,float size,int count)
    {
        var go=new GameObject(name);var ps=go.AddComponent<ParticleSystem>();
        var main=ps.main;main.loop=false;main.duration=.3f;main.startLifetime=new ParticleSystem.MinMaxCurve(.12f,.25f);
        main.startSpeed=new ParticleSystem.MinMaxCurve(.5f,1.4f);main.startSize=size;main.startColor=color;main.gravityModifier=.4f;main.simulationSpace=ParticleSystemSimulationSpace.World;
        var emission=ps.emission;emission.rateOverTime=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,(short)count)});
        var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=55;shape.radius=.015f;
        var sizeOver=ps.sizeOverLifetime;sizeOver.enabled=true;sizeOver.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,1,1,0));
        string matPath="Assets/Game/Combat/Effects/Impact.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if(mat==null){mat=new Material(Shader.Find("Particles/Standard Unlit"));AssetDatabase.CreateAsset(mat,matPath);}
        mat.shader=Shader.Find(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline==null ? "Particles/Standard Unlit" : "Universal Render Pipeline/Particles/Unlit");
        mat.SetFloat("_Cull",0);EditorUtility.SetDirty(mat);
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=mat;
        var saved=PrefabUtility.SaveAsPrefabAsset(go,"Assets/Game/Combat/Effects/"+name+".prefab");Object.DestroyImmediate(go);return saved;
    }
    static void EnableEnemyOverlays()
    {
        var prefab=PrefabUtility.LoadPrefabContents("Assets/Game/Characters/Enemies/HumanoidEnemy.prefab");
        var reaction=prefab.GetComponentInChildren<HitReactionController>(true);
        if(reaction==null)reaction=prefab.AddComponent<HitReactionController>();
        reaction.enabled=true;
        PrefabUtility.SaveAsPrefabAsset(prefab,"Assets/Game/Characters/Enemies/HumanoidEnemy.prefab");PrefabUtility.UnloadPrefabContents(prefab);
        foreach(var enemy in Object.FindObjectsByType<EnemyBrain>(FindObjectsInactive.Include))
        {
            reaction=enemy.GetComponentInChildren<HitReactionController>(true);
            if(reaction==null)reaction=Undo.AddComponent<HitReactionController>(enemy.gameObject);
            Undo.RecordObject(reaction,"Enable directional hit reactions");reaction.enabled=true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(reaction);
        }
        prefab=PrefabUtility.LoadPrefabContents("Assets/Game/Combat/Prefabs/CombatManager.prefab");
        TuneOverlay(prefab.GetComponent<CombatManager>());
        PrefabUtility.SaveAsPrefabAsset(prefab,"Assets/Game/Combat/Prefabs/CombatManager.prefab");PrefabUtility.UnloadPrefabContents(prefab);
        foreach(var manager in Object.FindObjectsByType<CombatManager>(FindObjectsInactive.Include))
        {
            Undo.RecordObject(manager,"Tune directional hit reactions");TuneOverlay(manager);PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
        }
        var scene=EditorSceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }
    static void TuneOverlay(CombatManager manager)
    {
        manager.hitReactionEnabled=true;manager.hitReactionAngle=20;manager.hitReactionAttackSpeed=60;
        manager.hitReactionDamping=9;manager.hitReactionInfluenceDepth=2;manager.hitReactionParentFalloff=.35f;
        EditorUtility.SetDirty(manager);
    }

    [MenuItem("Tools/LoomRoom/Update enemy reactions")]
    public static void EnemyRecoil()
    {
        EnableEnemyOverlays();
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Game/Players/Shared/Animations/HumanoidController.controller");
        var machine=controller.layers.Select(l=>l.stateMachine).FirstOrDefault(m=>m.states.Any(s=>s.state.name=="IdleWalkRunBT"));
        if(machine==null)return;
        var idle=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="IdleWalkRunBT");
        if(idle==null || !(idle.motion is BlendTree tree) || !(tree.children[0].motion is AnimationClip source))return;
        const string path="Assets/Game/Players/Shared/Animations/Combat/Enemy_Hurt.anim";
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null){clip=Object.Instantiate(source);AssetDatabase.CreateAsset(clip,path);}else EditorUtility.CopySerialized(source,clip);
        foreach(var binding in AnimationUtility.GetCurveBindings(source))
        {
            float value=AnimationUtility.GetEditorCurve(source,binding).Evaluate(0);
            float offset=binding.propertyName=="Spine Front-Back" ? -.55f : binding.propertyName=="Chest Front-Back" ? -.45f :
                binding.propertyName=="Head Nod Down-Up" ? -.3f : binding.propertyName=="Chest Left-Right" ? .2f :
                binding.propertyName.EndsWith("Arm Down-Up") ? .35f : binding.propertyName.EndsWith("Forearm Stretch") ? -.3f : 0;
            AnimationUtility.SetEditorCurve(clip,binding,new AnimationCurve(new Keyframe(0,value),new Keyframe(.085f,value+offset),new Keyframe(.42f,value)));
        }
        clip.name="Enemy_Hurt";
        AnimationUtility.SetAnimationEvents(clip,new AnimationEvent[0]);
        var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;settings.startTime=0;settings.stopTime=.42f;
        AnimationUtility.SetAnimationClipSettings(clip,settings);
        if(!controller.parameters.Any(p=>p.name=="Hurt"))controller.AddParameter("Hurt",AnimatorControllerParameterType.Trigger);
        if(!controller.parameters.Any(p=>p.name=="HurtSpeed"))controller.AddParameter("HurtSpeed",AnimatorControllerParameterType.Float);
        var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Hurt");
        if(state==null)
        {
            state=machine.AddState("Hurt");
            var enter=machine.AddAnyStateTransition(state);enter.AddCondition(AnimatorConditionMode.If,0,"Hurt");enter.duration=.025f;enter.hasFixedDuration=true;enter.canTransitionToSelf=false;
            var exit=state.AddTransition(idle);exit.hasExitTime=true;exit.exitTime=1;exit.duration=.075f;exit.hasFixedDuration=true;
        }
        state.motion=clip;state.tag="Hurt";state.speedParameter="HurtSpeed";state.speedParameterActive=true;
        var parameters=controller.parameters;foreach(var parameter in parameters)if(parameter.name=="HurtSpeed")parameter.defaultFloat=1.3f;controller.parameters=parameters;
        if(!controller.parameters.Any(p=>p.name=="Dead"))controller.AddParameter("Dead",AnimatorControllerParameterType.Bool);

        var death=machine.states.Select(s=>s.state).First(s=>s.name=="Death1");
        death.tag="Death";
        foreach(var transition in machine.anyStateTransitions)
        {
            if(transition.destinationState==death)
            {
                transition.duration=.06f;transition.hasExitTime=false;transition.canTransitionToSelf=false;
            }
            else if(!transition.conditions.Any(c=>c.parameter=="Dead"))
                transition.AddCondition(AnimatorConditionMode.IfNot,0,"Dead");
        }
        // Death is terminal and takes priority over any pending reaction.
        machine.anyStateTransitions=machine.anyStateTransitions.OrderBy(t=>t.destinationState==death ? 0 : 1).ToArray();
        // Never let locomotion or a pending attack exit the terminal death state.
        foreach(var transition in death.transitions.ToArray())death.RemoveTransition(transition);
        EditorUtility.SetDirty(clip);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
    }

    static void Animate()
    {
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Game/Players/Shared/Animations/FirstPersonTable.controller");
        foreach(string parameter in new[]{"WindupSpeed","ReleaseSpeed","HeavyReleaseSpeed"})
            if(!controller.parameters.Any(p=>p.name==parameter)) controller.AddParameter(parameter,AnimatorControllerParameterType.Float);
        if(!controller.parameters.Any(p=>p.name=="HeavyStrike")) controller.AddParameter("HeavyStrike",AnimatorControllerParameterType.Bool);
        if(!controller.parameters.Any(p=>p.name=="Hurt")) controller.AddParameter("Hurt",AnimatorControllerParameterType.Trigger);
        foreach(var layer in controller.layers)
        {
            Visit(layer.stateMachine);
            if(layer.name=="RightArm") Recoil(layer.stateMachine);
        }
        var parameters=controller.parameters;
        foreach(var parameter in parameters)
            if(parameter.name=="WindupSpeed" || parameter.name=="ReleaseSpeed" || parameter.name=="HeavyReleaseSpeed") parameter.defaultFloat=1;
        controller.parameters=parameters;
        EditorUtility.SetDirty(controller);
    }
    static void Recoil(AnimatorStateMachine machine)
    {
        var idle=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Idle");
        if(idle==null || !(idle.motion is AnimationClip source))return;
        const string path="Assets/Game/Players/Shared/Animations/Combat/Player_Hurt.anim";
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,path);}
        clip.ClearCurves();clip.frameRate=60;
        var bindings=AnimationUtility.GetCurveBindings(source);
        foreach(var binding in bindings)
        {
            float value=AnimationUtility.GetEditorCurve(source,binding).Evaluate(0);
            AnimationUtility.SetEditorCurve(clip,binding,AnimationCurve.Constant(0,.26f,value));
        }
        foreach(var binding in bindings.Where(b=>b.path.EndsWith("Shoulder_R") && b.propertyName.StartsWith("localEulerAnglesRaw.")))
        {
            float baseline=AnimationUtility.GetEditorCurve(source,binding).Evaluate(0);
            float offset=binding.propertyName.EndsWith("x") ? -14 : binding.propertyName.EndsWith("y") ? 8 : -8;
            AnimationUtility.SetEditorCurve(clip,binding,new AnimationCurve(new Keyframe(0,baseline),new Keyframe(.055f,baseline+offset),new Keyframe(.26f,baseline)));
        }
        AnimationUtility.SetAnimationEvents(clip,new[]{new AnimationEvent{time=0,functionName="DisableHitbox"}});
        var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Hurt");
        if(state==null)
        {
            state=machine.AddState("Hurt");
            var enter=machine.AddAnyStateTransition(state);enter.AddCondition(AnimatorConditionMode.If,0,"Hurt");enter.duration=.035f;enter.hasFixedDuration=true;enter.canTransitionToSelf=false;
            var exit=state.AddTransition(idle);exit.hasExitTime=true;exit.exitTime=1;exit.duration=.06f;exit.hasFixedDuration=true;
        }
        state.motion=clip;state.tag="Hurt";EditorUtility.SetDirty(clip);
    }

    // Editor-only authoring: all sampled motion is saved as ordinary editable clip curves.
    static void BakeAttack(AnimationClip clip, string name)
    {
        var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Players/Shared/Animations/Farzan anims/Idle.anim");
        var hold=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Players/Shared/Animations/Farzan anims/Attack_Hold.anim");
        var cut=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Players/Shared/Animations/Farzan anims/Attack_Release.anim");
        bool windup=name=="Attack_Windup", holding=name=="Attack_Hold";
        bool heavy=name=="Attack_HeavyRelease";
        float duration=windup ? .28f : holding ? 1.2f : heavy ? .58f : .48f;
        clip.ClearCurves();clip.frameRate=60;
        foreach(var binding in AnimationUtility.GetCurveBindings(idle))
        {
            var idleCurve=AnimationUtility.GetEditorCurve(idle,binding);
            float start=idleCurve.Evaluate(0);
            var holdCurve=AnimationUtility.GetEditorCurve(hold,binding);
            var cutCurve=AnimationUtility.GetEditorCurve(cut,binding);
            bool arm=binding.path.Contains("Clavicle_R");
            bool rotation=binding.propertyName.StartsWith("localEulerAnglesRaw.");
            float raised=arm && holdCurve!=null ? holdCurve.Evaluate(0) : start;
            float follow=arm && cutCurve!=null ? cutCurve.Evaluate(cut.length) : start;
            if(rotation)
            {
                raised=start+Mathf.DeltaAngle(start,raised);
                follow=start+Mathf.DeltaAngle(start,follow)*(heavy ? 1.12f : 1f);
                // Camera-checked raised pose; explicit keys remain editable in the Animation window.
                if(binding.path.EndsWith("Shoulder_R"))
                    raised=binding.propertyName.EndsWith("x") ? -51.2f : binding.propertyName.EndsWith("y") ? 77f : 16.8f;
                if(binding.path.EndsWith("Elbow_R"))
                    raised=binding.propertyName.EndsWith("x") ? -33.8f : binding.propertyName.EndsWith("y") ? 82.7f : -6.14f;
            }
            var curve=new AnimationCurve();
            for(int frame=0;frame<=72;frame++)
            {
                float t=frame/72f, value=start;
                if(windup) value=Mathf.LerpUnclamped(start,raised,Mathf.SmoothStep(0,1,t));
                else if(holding)
                    value=raised+(rotation && binding.path.EndsWith("Shoulder_R") ? Mathf.Sin(t*Mathf.PI*2)*.65f : 0);
                else
                {
                    // Accelerate through the cut, then decelerate into the follow-through.
                    float strike=Mathf.SmoothStep(0,1,t/.4f);
                    value=t<=.4f ? Mathf.LerpUnclamped(raised,follow,strike) :
                        Mathf.LerpUnclamped(follow,start,Mathf.SmoothStep(0,1,(t-.4f)/.6f));
                }
                curve.AddKey(t*duration,value);
            }
            for(int key=0;key<curve.length;key++)curve.SmoothTangents(key,0);
            AnimationUtility.SetEditorCurve(clip,binding,curve);
        }
        var settings=AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime=holding;settings.startTime=0;settings.stopTime=duration;
        AnimationUtility.SetAnimationClipSettings(clip,settings);
        AnimationUtility.SetAnimationEvents(clip,windup || holding ? new AnimationEvent[0] : new[]{
            new AnimationEvent{time=.025f,functionName="PlaySwingAudio"},
            new AnimationEvent{time=.045f,functionName="EnableHitbox"},
            new AnimationEvent{time=heavy ? .25f : .21f,functionName="DisableHitbox"}});
    }

    static void ConstrainWindup(AnimationClip clip)
    {
        // Solve only while authoring. The resulting joint rotations are ordinary clip keys.
        var rig=PrefabUtility.LoadPrefabContents("Assets/Game/Players/Table/Prefabs/TablePlayer.prefab");
        try
        {
            var relay=rig.GetComponentInChildren<WeaponAnimationRelay>(true);
            var bones=relay.GetComponentsInChildren<Transform>(true);
            var hand=bones.First(t=>t.name=="Hand_R");
            var elbow=bones.First(t=>t.name=="Elbow_R");
            var shoulder=bones.First(t=>t.name=="Shoulder_R");
            clip.SampleAnimation(relay.gameObject,0);var start=hand.position;
            clip.SampleAnimation(relay.gameObject,clip.length);var end=hand.position;
            var bindings=AnimationUtility.GetCurveBindings(clip).Where(b=>b.propertyName.StartsWith("localEulerAnglesRaw.") &&
                (b.path.EndsWith("Shoulder_R") || b.path.EndsWith("Elbow_R"))).ToArray();
            var curves=bindings.Select(b=>new AnimationCurve()).ToArray();
            for(int frame=0;frame<=72;frame++)
            {
                float time=clip.length*frame/72f;
                clip.SampleAnimation(relay.gameObject,time);
                var target=Vector3.Lerp(start,end,Mathf.SmoothStep(0,1,frame/72f));
                for(int iteration=0;iteration<16;iteration++)
                    foreach(var joint in new[]{elbow,shoulder})
                        joint.rotation=Quaternion.Slerp(Quaternion.identity,Quaternion.FromToRotation(hand.position-joint.position,target-joint.position),.6f)*joint.rotation;
                for(int index=0;index<bindings.Length;index++)
                {
                    var binding=bindings[index];var angles=(binding.path.EndsWith("Shoulder_R") ? shoulder : elbow).localEulerAngles;
                    float angle=binding.propertyName.EndsWith("x") ? angles.x : binding.propertyName.EndsWith("y") ? angles.y : angles.z;
                    float baseline=AnimationUtility.GetEditorCurve(clip,binding).Evaluate(time);
                    curves[index].AddKey(time,baseline+Mathf.DeltaAngle(baseline,angle));
                }
            }
            for(int index=0;index<bindings.Length;index++)
            {
                for(int key=0;key<curves[index].length;key++)curves[index].SmoothTangents(key,0);
                AnimationUtility.SetEditorCurve(clip,bindings[index],curves[index]);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(rig); }
    }

    static void Visit(AnimatorStateMachine machine)
    {
        foreach(var child in machine.states)
        {
            var state=child.state;
            if(state.name!="Attack_Windup" && state.name!="Attack_Hold" && state.name!="Attack_Release")continue;
            var source=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Players/Shared/Animations/Farzan anims/"+state.name+".anim");
            if(source==null)continue;
            string path="Assets/Game/Players/Shared/Animations/Combat/"+state.name+".anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(clip==null){clip=Object.Instantiate(source);AssetDatabase.CreateAsset(clip,path);}else EditorUtility.CopySerialized(source,clip);
            bool release=state.name=="Attack_Release", hold=state.name=="Attack_Hold";
            BakeAttack(clip, state.name);
            if(state.name=="Attack_Windup") ConstrainWindup(clip);
            state.motion=clip;
            if(!hold){state.speedParameter=release ? "ReleaseSpeed" : "WindupSpeed";state.speedParameterActive=true;}
            foreach(var transition in state.transitions){transition.duration=.025f;transition.hasFixedDuration=true;}
            EditorUtility.SetDirty(clip);EditorUtility.SetDirty(state);
        }
        var releaseState=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Attack_Release");
        if(releaseState!=null)
        {
            const string path="Assets/Game/Players/Shared/Animations/Combat/Attack_HeavyRelease.anim";
            var heavyClip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(heavyClip==null){heavyClip=Object.Instantiate((AnimationClip)releaseState.motion);AssetDatabase.CreateAsset(heavyClip,path);}
            else EditorUtility.CopySerialized(releaseState.motion,heavyClip);
            var heavy=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Attack_HeavyRelease");
            if(heavy==null)
            {
                heavy=machine.AddState("Attack_HeavyRelease");
                foreach(var original in releaseState.transitions)
                {
                    var transition=original.isExit ? heavy.AddExitTransition() : heavy.AddTransition(original.destinationState);
                    transition.hasExitTime=original.hasExitTime;transition.exitTime=original.exitTime;
                    transition.duration=.09f;transition.hasFixedDuration=true;transition.conditions=original.conditions;
                }
                foreach(var childState in machine.states)
                    foreach(var original in childState.state.transitions.ToArray())
                        if(original.destinationState==releaseState)
                        {
                            var transition=childState.state.AddTransition(heavy);
                            transition.conditions=original.conditions;transition.hasExitTime=original.hasExitTime;
                            transition.exitTime=original.exitTime;transition.duration=original.duration;transition.hasFixedDuration=true;
                            transition.AddCondition(AnimatorConditionMode.If,0,"HeavyStrike");
                            original.AddCondition(AnimatorConditionMode.IfNot,0,"HeavyStrike");
                        }
            }
            BakeAttack(heavyClip,"Attack_HeavyRelease");
            heavy.motion=heavyClip;heavy.tag="Attack";heavy.speedParameter="HeavyReleaseSpeed";heavy.speedParameterActive=true;
            EditorUtility.SetDirty(heavyClip);
        }
        foreach(var child in machine.stateMachines)Visit(child.stateMachine);
    }
}
