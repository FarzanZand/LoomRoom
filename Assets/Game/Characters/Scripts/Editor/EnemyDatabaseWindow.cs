using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Tools > Enemy Database: every enemy prefab under Characters/Enemies (the bases left out, retired ones in _Archive on
// request) with its health, damage and armour at a glance; pick one to edit its data (the EnemyData stored inside its
// prefab), open the prefab, or make a new enemy from it. The sibling of the Item Database.
public class EnemyDatabaseWindow : EditorWindow
{
    const string Root = "Assets/Game/Characters/Enemies";
    List<GameObject> enemies = new();
    [SerializeField] GameObject selected;
    Editor inspector;
    string search = "";
    bool showArchived;
    Vector2 listScroll, detailScroll;

    [MenuItem("Tools/Enemy Database", false, 1)]
    public static void ShowWindow()
    {
        var window = GetWindow<EnemyDatabaseWindow>("Enemy Database");
        window.minSize = new Vector2(900, 500);
    }

    void OnEnable() { Rebuild(); EditorApplication.projectChanged += Rebuild; }
    void OnDisable() { EditorApplication.projectChanged -= Rebuild; if (inspector != null) DestroyImmediate(inspector); }

    void Rebuild()
    {
        enemies = AssetDatabase.FindAssets("t:Prefab", new[] { Root })
            .Select(g => AssetDatabase.GUIDToAssetPath(g))
            .Where(p => !p.Contains("/Base/"))
            .Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p))
            .Where(go => go != null && go.GetComponent<EnemyBrain>() != null)
            .OrderBy(go => Folder(go)).ThenBy(go => go.name).ToList();
        Repaint();
    }

    static string Folder(GameObject go)
    {
        var path = AssetDatabase.GetAssetPath(go).Substring(Root.Length + 1);
        int slash = path.IndexOf('/');
        return slash < 0 ? "" : path.Substring(0, slash);
    }
    static bool Archived(GameObject go) => AssetDatabase.GetAssetPath(go).Contains("/_Archive/");
    static CharacterData Data(GameObject go) => go.GetComponent<Character>() is Character c ? c.data : null;
    static float Stat(CharacterData data, StatType stat)
    {
        if (data == null || data.stats == null) return 0f;
        foreach (var s in data.stats) if (s.stat == stat) return s.baseValue;
        return 0f;
    }

    void Select(GameObject go)
    {
        if (selected == go && inspector != null) return;
        selected = go; detailScroll = Vector2.zero;
        if (inspector != null) DestroyImmediate(inspector);
        var data = go != null ? Data(go) : null;
        inspector = data != null ? Editor.CreateEditor(data) : null;
    }

    IEnumerable<GameObject> Visible() => enemies.Where(e => e != null && (showArchived || !Archived(e)) &&
        (string.IsNullOrWhiteSpace(search) || (e.name + " " + Folder(e)).IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0));

    void OnGUI()
    {
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("New enemy", EditorStyles.toolbarButton, GUILayout.Width(85))) NewCharacterWindow.Open(true, selected);
                GUILayout.FlexibleSpace();
                showArchived = GUILayout.Toggle(showArchived, "Show retired", EditorStyles.toolbarButton);
                search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.Width(220));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(360))) List();
                using (new EditorGUILayout.VerticalScope()) Details();
            }
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorGUILayout.HelpBox("Exit Play mode to edit enemies.", MessageType.Info);
    }

    void List()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Enemy", GUILayout.Width(170));
            GUILayout.Label("Health", GUILayout.Width(50));
            GUILayout.Label("Damage", GUILayout.Width(55));
            GUILayout.Label("Armour", GUILayout.Width(50));
        }
        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        string folder = null;
        foreach (var e in Visible())
        {
            if (Folder(e) != folder) { folder = Folder(e); EditorGUILayout.LabelField(folder == "" ? "Enemies" : folder, EditorStyles.boldLabel); }
            var data = Data(e);
            var rect = EditorGUILayout.BeginHorizontal();
            if (e == selected) EditorGUI.DrawRect(rect, new Color(.24f, .48f, .9f, .35f));
            if (GUILayout.Button(e.name, EditorStyles.label, GUILayout.Width(170))) Select(e);
            GUILayout.Label(Stat(data, StatType.MaxHealth).ToString("0"), GUILayout.Width(50));
            GUILayout.Label(Stat(data, StatType.AttackDamage).ToString("0"), GUILayout.Width(55));
            GUILayout.Label(Stat(data, StatType.Armor).ToString("0"), GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    void Details()
    {
        if (selected == null) { EditorGUILayout.HelpBox("Pick an enemy on the left.", MessageType.None); return; }
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(selected.name, EditorStyles.largeLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open prefab", GUILayout.Width(95))) AssetDatabase.OpenAsset(selected);
            if (GUILayout.Button("Show in project", GUILayout.Width(110))) EditorGUIUtility.PingObject(selected);
            if (GUILayout.Button("New enemy from this", GUILayout.Width(140))) NewCharacterWindow.Open(true, selected);
        }
        EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(selected), EditorStyles.miniLabel);
        if (inspector == null) { EditorGUILayout.HelpBox("This enemy has no data of its own (Character > Data).", MessageType.Warning); return; }
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        inspector.OnInspectorGUI();
        EditorGUILayout.EndScrollView();
    }
}
