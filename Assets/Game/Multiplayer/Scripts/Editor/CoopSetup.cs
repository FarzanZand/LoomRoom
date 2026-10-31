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
    const string WeaponGripPath = "Assets/Game/Multiplayer/Bodies/Coop weapon grip.asset";

    // Class id -> the Synty character its body is made from.
    static readonly (string classId, string character)[] ClassBodies =
    {
        ("fighter", "SM_Chr_Soldier_Male_01"),
        ("mage", "SM_Chr_Mage_01"),
        ("cleric", "SM_Chr_Priest_01"),
        ("spellblade", "SM_Chr_Witch_Female_01"),
    };

    public static void SetUp() => Run(false);

    public static void SetUpRebuild() => Run(true);

    static void Run(bool rebuildBodies)
    {
        Directory.CreateDirectory(PrefabsDir); Directory.CreateDirectory(BodiesDir);
        AssetDatabase.Refresh();

        var shieldGrip = ShieldGrip();
        var classBodies = Bodies(rebuildBodies);
        var roomBodies = RoomBodies(rebuildBodies);
        var player = PlayerPrefab();
        var list = PrefabList(player);
        var manager = ManagerPrefab(classBodies, roomBodies, shieldGrip);
        SceneObjects(player, list, manager);
        AssetDatabase.SaveAssets();
        Debug.Log("[Coop] Set up: player prefab, NetworkManager, bodies and MultiplayerManager are in place.");
    }

    // ── Assets ────────────────────────────────────────────────────────

    // The grips again from the table player's sockets, on the manager prefab and the scene's manager.
    public static void RefreshGrips()
    {
        var weapon = FirstPersonGrip(WeaponGripPath, "RightHand_Socket");
        var shield = ShieldGrip();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Multiplayer/Prefabs/Multiplayer Manager.prefab");
        foreach (var m in new[] { prefab != null ? prefab.GetComponent<MultiplayerManager>() : null, Object.FindAnyObjectByType<MultiplayerManager>(FindObjectsInactive.Include) })
        {
            if (m == null) continue;
            m.weaponGrip = weapon; m.shieldGrip = shield; m.shieldBlockTurn = Vector3.zero;
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Coop] Grips from the first-person sockets: weapon {weapon.position} {weapon.rotation}, shield {shield.position} {shield.rotation}");
    }

    static EnemyWeaponGrip ShieldGrip() => FirstPersonGrip(ShieldGripPath, "LeftHand_Socket");

    // A teammate's grip is the table player's own hand socket (the arms and the bodies are the same Synty skeleton),
    // so a held item sits on the body as it does in first person (with its held position and rotation on top).
    static EnemyWeaponGrip FirstPersonGrip(string path, string socketName)
    {
        var grip = AssetDatabase.LoadAssetAtPath<EnemyWeaponGrip>(path);
        if (grip == null) { grip = ScriptableObject.CreateInstance<EnemyWeaponGrip>(); AssetDatabase.CreateAsset(grip, path); }
        var table = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Players/Table/TablePlayer.prefab");
        var socket = table != null ? table.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == socketName) : null;
        if (socket == null) { Debug.LogWarning($"[Coop] The table player has no {socketName}; the grip keeps its values."); return grip; }
        grip.position = socket.localPosition;
        grip.rotation = socket.localEulerAngles;
        grip.scale = 1f;
        EditorUtility.SetDirty(grip);
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
            // A part without a restyle (a cape) would stand out against the pixel look: it is left off.
            if (pixelMesh == null || pixelMaterial == null) { smr.gameObject.SetActive(false); Debug.LogWarning($"[Coop] {character}: no pixel restyle for {mesh} in {dir}; that part is switched off (restyle it with the art pipeline to show it)."); }
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
    // The room bodies others see, one per body the room player has (RoomBodyType: the woman, then the man), handed out by
    // player slot (MultiplayerManager.RoomBody). Copied from the room player's BodyVisuals, which holds the Animator and the
    // avatar the bodies share; models switched off there (an old body kept for reference) are left out.
    static GameObject[] RoomBodies(bool rebuild)
    {
        string[] names = { "Room body", "Room body (man)" };
        var paths = names.Select(n => $"{BodiesDir}/{n}.prefab").ToArray();
        if (!rebuild && paths.All(p => AssetDatabase.LoadAssetAtPath<GameObject>(p) != null)) return paths.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p)).ToArray();
        // The scene's room player as it is dressed there (its attachments are scene overrides), else the prefab's.
        var inScene = Object.FindObjectsByType<Player>(FindObjectsInactive.Include).FirstOrDefault(p => p.kind == PlayerKind.Room);
        var roomPlayer = inScene != null && inScene.transform.parent != null ? inScene.transform.parent.gameObject : AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Players/Room/RoomPlayer.prefab");
        var source = roomPlayer != null ? roomPlayer.transform.Find("BodyVisuals") : null;
        if (source == null) { Debug.LogWarning("[Coop] RoomPlayer has no BodyVisuals."); return new GameObject[0]; }
        var bodies = new List<GameObject>();
        for (int i = 0; i < names.Length; i++)
        {
            var copy = Object.Instantiate(source.gameObject);
            copy.name = names[i];
            copy.transform.localScale = source.lossyScale;
            foreach (Transform child in copy.transform.Cast<Transform>().ToArray()) if (!child.gameObject.activeSelf) Object.DestroyImmediate(child.gameObject);
            var type = copy.GetComponentInChildren<RoomBodyType>(true);
            if (type != null)
            {
                bool a = i == 0;
                foreach (var go in new[] { type.bodyA, type.headA }) if (go != null) go.SetActive(a);
                foreach (var go in new[] { type.bodyB, type.headB }) if (go != null) go.SetActive(!a);
            }
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var b in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(b);
            bodies.Add(PrefabUtility.SaveAsPrefabAsset(copy, paths[i]));
            Object.DestroyImmediate(copy);
        }
        return bodies.ToArray();
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
            var font = AssetDatabase.FindAssets("PixelOperator t:TMP_FontAsset").Select(g => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(f => f != null && f.name == "PixelOperator");
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

    static GameObject ManagerPrefab(Dictionary<string, GameObject> bodies, GameObject[] roomBodies, EnemyWeaponGrip shieldGrip)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        var go = existing != null ? PrefabUtility.LoadPrefabContents(ManagerPrefabPath) : new GameObject("Multiplayer Manager");
        var manager = go.GetComponent<MultiplayerManager>();
        if (manager == null) manager = go.AddComponent<MultiplayerManager>();
        // The teammates' own controller (Build Body Animations); the enemies' humanoid one only until that exists.
        manager.bodyController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Game/Multiplayer/Animations/Coop Body.controller")
            ?? AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Game/Characters/Animations/HumanoidController.controller");
        manager.weaponGrip = FirstPersonGrip(WeaponGripPath, "RightHand_Socket");
        manager.shieldGrip = shieldGrip;
        // One room body per body the room player has, handed out by player slot.
        if (roomBodies != null && roomBodies.Length > 0 && roomBodies.All(b => b != null)) manager.roomBodies = roomBodies;
        if (manager.defaultTableBody == null && bodies.TryGetValue("fighter", out var fighter)) manager.defaultTableBody = fighter;
        manager.enemyPrefabs = CoopEnemyList.Find();
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
        CoopMenuBuilder.Build();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
