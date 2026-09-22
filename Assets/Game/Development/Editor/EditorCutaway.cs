using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Apartment cutaway using editor-only Scene visibility, never renderer or object activation.</summary>
[InitializeOnLoad]
public sealed class EditorCutaway : EditorWindow
{
    const string Prefix = "LoomRoom.EditorCutaway.";
    const string OwnedKey = Prefix + "OwnedObjects";
    static bool queued;

    [Serializable]
    sealed class HiddenObjects { public List<string> ids = new List<string>(); }

    static EditorCutaway()
    {
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.hierarchyChanged += QueueApply;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        QueueApply();
    }

    static bool Ceilings
    {
        get => SessionState.GetBool(Prefix + "Ceilings", true);
        set => SessionState.SetBool(Prefix + "Ceilings", value);
    }

    static bool Walls
    {
        get => SessionState.GetBool(Prefix + "Walls", false);
        set => SessionState.SetBool(Prefix + "Walls", value);
    }

    [MenuItem("Tools/LoomRoom/Editor Cutaway")]
    public static void Open()
    {
        var window = GetWindow<EditorCutaway>("Editor Cutaway");
        window.minSize = new Vector2(285, 170);
        window.Show();
    }

    void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Apartment Scene View", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        bool ceilings = EditorGUILayout.ToggleLeft("Hide ceilings and roofs", Ceilings);
        bool walls = EditorGUILayout.ToggleLeft("Hide walls and window frames", Walls);
        if (EditorGUI.EndChangeCheck())
        {
            Ceilings = ceilings;
            Walls = walls;
            Apply();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Only affects Scene view. Game view, lighting, colliders and Play mode remain intact.", MessageType.Info);
        if (GUILayout.Button("Show cutaway objects again"))
        {
            Ceilings = Walls = false;
            Apply();
        }
    }

    static void OnSceneOpened(Scene scene, OpenSceneMode mode) => QueueApply();
    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) QueueApply();
    }

    static void QueueApply()
    {
        if (queued) return;
        queued = true;
        EditorApplication.delayCall += () => { queued = false; Apply(); };
    }

    static IEnumerable<GameObject> Targets()
    {
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != "World") continue;
                var apartment = root.transform.Find("Apartment");
                if (!apartment) continue;
                foreach (Transform room in apartment)
                {
                    var geometry = room.Find("Geometry");
                    if (!geometry) continue;
                    foreach (Transform piece in geometry)
                    {
                        string name = piece.name.ToLowerInvariant();
                        bool ceiling = name.Contains("ceiling") || name.Contains("roof");
                        // Room Geometry contains architectural shell pieces. Floors and skirting stay visible.
                        bool wall = !ceiling && !name.Contains("floor") && !name.Contains("skirting");
                        if ((ceiling && Ceilings) || (wall && Walls)) yield return piece.gameObject;
                    }
                }
            }
        }
    }

    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var visibility = SceneVisibilityManager.instance;
        var owned = JsonUtility.FromJson<HiddenObjects>(SessionState.GetString(OwnedKey, "{}")) ?? new HiddenObjects();
        var wanted = new HashSet<GameObject>(Targets());
        var retained = new HiddenObjects();

        foreach (string id in owned.ids ?? new List<string>())
        {
            if (!GlobalObjectId.TryParse(id, out var parsed)) continue;
            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) as GameObject;
            if (!obj) continue;
            if (!wanted.Contains(obj)) visibility.Show(obj, false);
            else retained.ids.Add(id);
        }

        foreach (var obj in wanted)
        {
            // Preserve anything the user already hid with the Hierarchy eye control.
            if (visibility.IsHidden(obj)) continue;
            visibility.Hide(obj, false);
            string id = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
            if (!retained.ids.Contains(id)) retained.ids.Add(id);
        }

        SessionState.SetString(OwnedKey, JsonUtility.ToJson(retained));
        SceneView.RepaintAll();
    }
}
