using System;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ProjectStructureValidation
{
    public const string GameScene = "Assets/Game/Scenes/Room.unity";

    [MenuItem("LoomRoom/Open Game Scene")]
    static void OpenScene()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(GameScene);
    }

    [MenuItem("LoomRoom/Validate Scene Setup")]
    public static void Validate()
    {
        var scene = SceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true)).ToArray();
        if (all.Any(c => c == null)) throw new Exception("The scene contains missing scripts.");
        if (all.OfType<Camera>().Count() != 1 || all.OfType<AudioListener>().Count() != 1 || all.OfType<CinemachineBrain>().Count() != 1)
            throw new Exception("Expected exactly one output camera, listener and Cinemachine Brain.");
        var manager = all.OfType<PlayerManager>().Single();
        if (manager.OutputCamera == null) throw new Exception("PlayerManager needs the shared output camera.");
        if (manager.players.Count != 2 || manager.players.Select(p=>p.kind).Distinct().Count()!=2)
            throw new Exception("PlayerManager needs one Room entry and one Table entry.");
        foreach (var entry in manager.players)
        {
            var p = entry.player;
            if (p == null || p.kind != entry.kind || p.data == null || p.settings == null || p.ViewPresentation == null || p.BodyPresentation == null)
                throw new Exception($"Incomplete {entry.kind} player wiring.");
            if (!p.transform.IsChildOf(p.ActivationRoot.transform) || p.ActivationRoot == p.gameObject)
                throw new Exception($"Invalid activation root on {entry.kind}.");
            var rig = p.ActivationRoot.GetComponent<PlayerRig>();
            if (rig == null || rig.controller != p) throw new Exception($"{entry.kind} prefab root needs its PlayerRig entry point.");
            if (p.GetComponent<CharacterController>() == null || p.GetComponent<PlayerMotor>() == null || p.GetComponent<PlayerLook>() == null)
                throw new Exception($"Missing movement components on {entry.kind}.");
            if (p.GetComponent<InteractController>().ViewCamera != manager.OutputCamera)
                throw new Exception($"{entry.kind} interaction must use the shared camera.");
            var look = new SerializedObject(p.GetComponent<PlayerLook>());
            if (look.FindProperty("lateralTorso").objectReferenceValue == null || look.FindProperty("verticalNeck").objectReferenceValue == null)
                throw new Exception($"{entry.kind} look pivots are missing.");
            if (entry.kind == PlayerKind.Table && (p.GetComponent<PlayerCombat>() == null || p.GetComponentInChildren<WeaponAnimationRelay>(true) == null))
                throw new Exception("Table combat rig is incomplete.");
        }
        if (!EditorBuildSettings.scenes.Any(s => s.enabled && s.path == GameScene)) throw new Exception("Game scene is missing from Build Settings.");
        Debug.Log("[LoomRoom] Scene setup valid: two wired players, one output camera, no missing scripts, game scene enabled.");
    }
}
