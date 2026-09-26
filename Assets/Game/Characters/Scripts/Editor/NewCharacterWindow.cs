using System.IO;
using UnityEditor;
using UnityEngine;

// Tools > LoomRoom > New Enemy / New NPC. A character is one prefab file: a variant of a base
// (or of an existing character) with its own data stored inside it — EnemyData for enemies,
// CharacterData for NPCs. Everything is then edited on that prefab's Character component;
// changes shared by a whole family go on the base.
public class NewCharacterWindow : EditorWindow
{
    const string Root = "Assets/Game/Characters/";

    string characterName = "New Character";
    GameObject template;
    string folder;
    bool enemy;

    [MenuItem("Tools/LoomRoom/New Enemy")]
    static void NewEnemy() => Open(true, Selection.activeGameObject);
    [MenuItem("Tools/LoomRoom/New NPC")]
    static void NewNpc() => Open(false, Selection.activeGameObject);

    // Right-click an enemy or NPC prefab: Create > LoomRoom > Character Variant.
    [MenuItem("Assets/Create/LoomRoom/Character Variant", false, 0)]
    static void FromSelection() => Open(Selection.activeGameObject.GetComponent<EnemyBrain>() != null, Selection.activeGameObject);
    [MenuItem("Assets/Create/LoomRoom/Character Variant", true)]
    static bool FromSelectionValid() => IsTemplate(Selection.activeGameObject);

    static void Open(bool enemy, GameObject selected)
    {
        var window = GetWindow<NewCharacterWindow>(true, enemy ? "New Enemy" : "New NPC");
        window.minSize = window.maxSize = new Vector2(440, 230);
        window.enemy = enemy;
        window.characterName = enemy ? "New Enemy" : "New NPC";
        window.folder = Root + (enemy ? "Enemies" : "NPCs");
        window.template = IsTemplate(selected) && (selected.GetComponent<EnemyBrain>() != null) == enemy ? selected
            : AssetDatabase.LoadAssetAtPath<GameObject>(Root + (enemy ? "Enemies/Base/Humanoid enemy base.prefab" : "NPCs/Base/HumanoidNPC.prefab"));
    }

    // Enemies and NPCs; players are set up by hand.
    static bool IsTemplate(GameObject go) => go != null && PrefabUtility.IsPartOfPrefabAsset(go)
        && go.GetComponent<Character>() != null && go.GetComponent<Player>() == null;

    void OnGUI()
    {
        EditorGUILayout.HelpBox("Creates one prefab: a variant of the template with its own copy of the template's data inside it. Edit everything on the new prefab's Character component.", MessageType.None);
        characterName = EditorGUILayout.TextField("Name", characterName);
        template = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Template", "A base (Characters/…/Base) or an existing character. A variant of a character inherits its model, weapon and settings."), template, typeof(GameObject), false);
        using (new EditorGUILayout.HorizontalScope())
        {
            folder = EditorGUILayout.TextField("Folder", folder);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                var picked = EditorUtility.OpenFolderPanel("Folder", folder, "").Replace('\\', '/');
                int at = picked.IndexOf("/Assets/");
                if (at >= 0) folder = picked.Substring(at + 1);
            }
        }
        string path = $"{folder}/{characterName}.prefab";
        bool templateIsEnemy = template != null && template.GetComponent<EnemyBrain>() != null;
        string problem = string.IsNullOrWhiteSpace(characterName) ? "Enter a name."
            : !IsTemplate(template) ? "Pick an enemy or NPC prefab (or a base) as the template."
            : templateIsEnemy != enemy ? (enemy ? "That template is an NPC." : "That template is an enemy.")
            : !AssetDatabase.IsValidFolder(folder) ? "Folder does not exist."
            : File.Exists(path) ? "An asset with that name already exists." : null;
        if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Warning);
        GUILayout.FlexibleSpace();
        using (new EditorGUI.DisabledScope(problem != null))
            if (GUILayout.Button(enemy ? "Create enemy" : "Create NPC", GUILayout.Height(30)))
            {
                var created = Create(characterName.Trim(), template, path);
                Selection.activeObject = created;
                EditorGUIUtility.PingObject(created);
                Close();
            }
    }

    public static GameObject Create(string name, GameObject template, string path)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(template);
        instance.name = name;
        PrefabUtility.SaveAsPrefabAsset(instance, path);
        DestroyImmediate(instance);

        // The new character's own data, stored inside its prefab file. Instantiate keeps the
        // template's data type (EnemyData for enemies, CharacterData for NPCs).
        bool isEnemy = template.GetComponent<EnemyBrain>() != null;
        var source = template.GetComponent<Character>().data;
        CharacterData data = source != null ? Instantiate(source) : isEnemy ? CreateInstance<EnemyData>() : CreateInstance<CharacterData>();
        data.name = name;
        data.characterName = name;
        if (isEnemy) data.faction = Faction.Enemy;
        AssetDatabase.AddObjectToAsset(data, path);
        AssetDatabase.SaveAssets();

        var root = PrefabUtility.LoadPrefabContents(path);
        root.GetComponent<Character>().data = data;
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }
}
