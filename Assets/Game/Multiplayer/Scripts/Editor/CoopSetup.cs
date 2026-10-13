using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > LoomRoom > Multiplayer > Set Up Co-op: everything co-op needs, made once and safe to run again.
// The player prefab (CoopPlayer), the NetworkManager and its prefab list, the class bodies (a Synty character with its
// pixel restyle for each class, set on the class as its Co-op body), the room body, the shield grip, and the
// MultiplayerManager under Systems. Existing bodies are kept (they may have been edited); Rebuild Bodies remakes them.
public static class CoopSetup
{
    const string Root = "Assets/Game/Multiplayer";
    const string PrefabsDir = Root + "/Prefabs", BodiesDir = Root + "/Bodies";
    const string PlayerPrefabPath = PrefabsDir + "/Coop Player.prefab";
    const string ManagerPrefabPath = PrefabsDir + "/Multiplayer Manager.prefab";
    const string PrefabListPath = PrefabsDir + "/Network Prefabs.asset";
    const string ShieldGripPath = "Assets/Game/Characters/Resources/Synty humanoid shield grip.asset";

    // Class id -> the Synty character its body is made from.
    static readonly (string classId, string character)[] ClassBodies =
    {
        ("fighter", "SM_Chr_Soldier_Male_01"),
        ("mage", "SM_Chr_Mage_01"),
        ("cleric", "SM_Chr_Priest_01"),
        ("spellblade", "SM_Chr_Witch_Female_01"),
    };

    [MenuItem("Tools/LoomRoom/Multiplayer/Set Up Co-op")]
    public static void SetUp() => Run(false);

    [MenuItem("Tools/LoomRoom/Multiplayer/Set Up Co-op (Rebuild Bodies)")]
    public static void SetUpRebuild() => Run(true);

    static void Run(bool rebuildBodies)
    {
        Directory.CreateDirectory(PrefabsDir); Directory.CreateDirectory(BodiesDir);
        AssetDatabase.Refresh();

        var shieldGrip = ShieldGrip();
        var classBodies = Bodies(rebuildBodies);
        var roomBody = RoomBody(rebuildBodies);
        var player = PlayerPrefab();
        var list = PrefabList(player);
        var manager = ManagerPrefab(classBodies, roomBody, shieldGrip);
        SceneObjects(player, list, manager);
        AssetDatabase.SaveAssets();
        Debug.Log("[Coop] Set up: player prefab, NetworkManager, bodies and MultiplayerManager are in place.");
    }

    // ── Assets ────────────────────────────────────────────────────────

    static EnemyWeaponGrip ShieldGrip()
    {
        var grip = AssetDatabase.LoadAssetAtPath<EnemyWeaponGrip>(ShieldGripPath);
        if (grip != null) return grip;
        grip = ScriptableObject.CreateInstance<EnemyWeaponGrip>();
        // On the back of the left forearm, face out.
        grip.position = new Vector3(-.05f, .02f, -.08f);
        grip.rotation = new Vector3(0, -90, 90);
        grip.scale = 1;
        AssetDatabase.CreateAsset(grip, ShieldGripPath);
        return grip;
    }

    static Dictionary<string, GameObject> Bodies(bool rebuild)
    {
        var made = new Dictionary<string, GameObject>();
        var classes = AssetDatabase.FindAssets("t:AdventurerClass", new[] { "Assets/Game/Progression" })
            .Select(g => AssetDatabase.LoadAssetAtPath<AdventurerClass>(AssetDatabase.GUIDToAssetPath(g))).Where(c => c != null).ToList();
        foreach (var (classId, character) in ClassBodies)
        {
            var cls = classes.FirstOrDefault(c => c.id == classId);
            string path = $"{BodiesDir}/{(cls != null ? cls.displayName : classId)} body.prefab";
            var body = rebuild ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (body == null) body = MakeBody(character, path);
            if (body == null) continue;
            made[classId] = body;
            if (cls != null && cls.coopBody == null) { cls.coopBody = body; EditorUtility.SetDirty(cls); }
        }
        return made;
    }

    // A Synty character as a plain prefab of its own (Synty's own prefab is never edited), wearing its pixel restyle.
    static GameObject MakeBody(string character, string path)
    {
        var source = AssetDatabase.FindAssets($"{character} t:Prefab", new[] { "Assets/Synty" })
            .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == character);
        if (source == null) { Debug.LogWarning($"[Coop] No Synty prefab {character}."); return null; }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(source));
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = Path.GetFileNameWithoutExtension(path);
        foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string mesh = smr.sharedMesh != null ? smr.sharedMesh.name : smr.name;
            string material = smr.sharedMaterial != null ? smr.sharedMaterial.name : "";
            string dir = $"Assets/Game/Art/Synty Pixel/Characters/{mesh}";
            var pixelMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{dir}/{mesh} {material}.asset")
                ?? AssetDatabase.FindAssets("t:Mesh", new[] { dir }).Select(g => AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            var pixelMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{mesh} {material}.mat")
                ?? AssetDatabase.FindAssets("t:Material", new[] { dir }).Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            if (pixelMesh != null) smr.sharedMesh = pixelMesh;
            if (pixelMaterial != null) smr.sharedMaterial = pixelMaterial;
            if (pixelMesh == null || pixelMaterial == null) Debug.LogWarning($"[Coop] {character}: no pixel restyle for {mesh} in {dir}; kept Synty's.");
            smr.updateWhenOffscreen = true;
        }
        foreach (var c in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        var animator = instance.GetComponentInChildren<Animator>();
        if (animator != null) { animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
        var saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
        Object.DestroyImmediate(instance);
        return saved;
    }

    // The room player's own body, at room scale, for the others to see walking about the room.
    static GameObject RoomBody(bool rebuild)
    {
        string path = $"{BodiesDir}/Room body.prefab";
        var existing = rebuild ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        // The scene's room player as it is dressed there (its attachments are scene overrides), else the prefab's.
        var inScene = Object.FindObjectsByType<Player>(FindObjectsInactive.Include).FirstOrDefault(p => p.kind == PlayerKind.Room);
        var roomPlayer = inScene != null && inScene.transform.parent != null ? inScene.transform.parent.gameObject : AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Players/Room/RoomPlayer.prefab");
        var source = roomPlayer != null ? roomPlayer.transform.Find("BodyVisuals/RoomBody") : null;
        if (source == null) { Debug.LogWarning("[Coop] RoomPlayer has no BodyVisuals/RoomBody."); return null; }
        var copy = Object.Instantiate(source.gameObject);
        copy.name = "Room body";
        copy.transform.localScale = source.lossyScale;
        foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var b in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(b);
        var saved = PrefabUtility.SaveAsPrefabAsset(copy, path);
        Object.DestroyImmediate(copy);
        return saved;
    }

    static GameObject PlayerPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var go = existing != null ? PrefabUtility.LoadPrefabContents(PlayerPrefabPath) : new GameObject("Coop Player");
        T Need<T>() where T : Component { var c = go.GetComponent<T>(); if (c == null) c = go.AddComponent<T>(); return c; }
        Need<NetworkObject>();
        var transform = Need<NetworkTransform>();
        transform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        transform.SyncRotAngleX = false; transform.SyncRotAngleZ = false;
        transform.SyncScaleX = transform.SyncScaleY = transform.SyncScaleZ = false;
        transform.InLocalSpace = false; transform.Interpolate = true;

        var capsule = Need<CapsuleCollider>();
        capsule.height = 1.8f; capsule.radius = .35f; capsule.center = Vector3.up * .9f; capsule.enabled = false;
        var character = Need<Character>();
        var table = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Players/Table/TablePlayer.prefab");
        var tableCharacter = table != null ? table.GetComponentInChildren<Character>(true) : null;
        if (tableCharacter != null) character.data = tableCharacter.data;
        Need<CharacterStats>();
        var avatar = Need<CoopAvatar>();
        Need<CoopPlayer>();

        var label = go.transform.Find("Name label");
        if (label == null)
        {
            label = new GameObject("Name label").transform;
            label.SetParent(go.transform, false);
            var text = label.gameObject.AddComponent<TextMeshPro>();
            text.text = "Player";
            text.fontSize = 2.4f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, .93f, .78f);
            text.rectTransform.sizeDelta = new Vector2(6, 1);
            var font = AssetDatabase.FindAssets("PixelOperator SDF t:TMP_FontAsset").Select(g => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            if (font != null) text.font = font;
            label = text.transform;   // TextMeshPro swapped the Transform for a RectTransform
        }
        var so = new SerializedObject(avatar);
        so.FindProperty("nameLabel").objectReferenceValue = label.GetComponent<TMP_Text>();
        so.FindProperty("body").objectReferenceValue = capsule;
        so.ApplyModifiedPropertiesWithoutUndo();

        var saved = PrefabUtility.SaveAsPrefabAsset(go, PlayerPrefabPath);
        if (existing != null) PrefabUtility.UnloadPrefabContents(go); else Object.DestroyImmediate(go);
        return saved;
    }

    static NetworkPrefabsList PrefabList(GameObject player)
    {
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
        if (list == null) { list = ScriptableObject.CreateInstance<NetworkPrefabsList>(); AssetDatabase.CreateAsset(list, PrefabListPath); }
        if (!list.PrefabList.Any(p => p.Prefab == player)) list.Add(new NetworkPrefab { Prefab = player });
        EditorUtility.SetDirty(list);
        return list;
    }

    static GameObject ManagerPrefab(Dictionary<string, GameObject> bodies, GameObject roomBody, EnemyWeaponGrip shieldGrip)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        var go = existing != null ? PrefabUtility.LoadPrefabContents(ManagerPrefabPath) : new GameObject("Multiplayer Manager");
        var manager = go.GetComponent<MultiplayerManager>();
        if (manager == null) manager = go.AddComponent<MultiplayerManager>();
        manager.bodyController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Game/Characters/Animations/HumanoidController.controller");
        manager.weaponGrip = Resources.Load<EnemyWeaponGrip>("Synty humanoid weapon grip");
        manager.shieldGrip = shieldGrip;
        if (roomBody != null && (manager.roomBodies == null || manager.roomBodies.All(b => b == null))) manager.roomBodies = new[] { roomBody };
        if (manager.defaultTableBody == null && bodies.TryGetValue("fighter", out var fighter)) manager.defaultTableBody = fighter;
        manager.enemyPrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Characters/Enemies" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !p.Contains("/_Archive/") && !p.Contains("/Base/"))
            .Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p))
            .Where(p => p != null && p.GetComponent<EnemyBrain>() != null)
            .OrderBy(p => p.name).ToArray();
        var saved = PrefabUtility.SaveAsPrefabAsset(go, ManagerPrefabPath);
        if (existing != null) PrefabUtility.UnloadPrefabContents(go); else Object.DestroyImmediate(go);
        return saved;
    }

    // ── The Room scene ────────────────────────────────────────────────

    static void SceneObjects(GameObject player, NetworkPrefabsList list, GameObject managerPrefab)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.path.EndsWith("Room.unity")) { Debug.LogWarning("[Coop] Open Scenes/Room.unity to put the NetworkManager and MultiplayerManager in it."); return; }

        var network = Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
        if (network == null)
        {
            var go = new GameObject("Network Manager");
            network = go.AddComponent<NetworkManager>();
            go.AddComponent<UnityTransport>();
        }
        var transport = network.GetComponent<UnityTransport>();
        if (transport == null) transport = network.gameObject.AddComponent<UnityTransport>();
        network.NetworkConfig ??= new NetworkConfig();
        network.NetworkConfig.NetworkTransport = transport;
        network.NetworkConfig.PlayerPrefab = player;
        network.NetworkConfig.ConnectionApproval = true;
        network.NetworkConfig.EnableSceneManagement = false;
        network.NetworkConfig.TickRate = 30;
        // Netcode's own default list already holds every prefab with a NetworkObject; a second list would register it twice.
        bool listed = network.NetworkConfig.Prefabs.NetworkPrefabsLists.Any(l => l != null && l != list && l.PrefabList.Any(p => p.Prefab == player));
        if (listed) network.NetworkConfig.Prefabs.NetworkPrefabsLists.Remove(list);
        else if (!network.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(list)) network.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
        EditorUtility.SetDirty(network);

        var manager = Object.FindAnyObjectByType<MultiplayerManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            var systems = GameObject.Find("Systems");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(managerPrefab, scene);
            if (systems != null) instance.transform.SetParent(systems.transform, false);
            manager = instance.GetComponent<MultiplayerManager>();
        }
        manager.network = network;
        PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
