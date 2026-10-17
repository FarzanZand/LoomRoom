using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the run recap's ledger in the open Room scene: a box left of the spoils strip with the big picture of the most
// valuable spoil, the rows RunRecapUI writes in one after another, and the tally sounds (recorded library clips as
// AudioData in Audio/Data) as UI Library keys on the AudioManager. Keeps what exists, so hand edits survive; safe to
// run again.
public static class RunRecapShowcase
{
    const string Slot = "Assets/Game/UI/Sprites/Inventory/Plain slot grey.png";
    const string Library = "Assets/Game/Audio/Library/_Sound Effects Library/";
    const string DataFolder = "Assets/Game/Audio/Data/";
    const string AudioPrefab = "Assets/Game/Core/Prefabs/AudioManager.prefab";
    // One plain recorded sound a row, kept quiet: gold set down, a few things moved, a book shut, a coin pouch.
    static readonly (string key, string asset, string[] clips, float volume, float variance)[] Keys =
    {
        ("tallySpoils", "Tally spoils", new[] { "Items/Itm_Gold_Down_01.wav", "Items/Itm_Gold_Down_02.wav" }, .5f, .03f),
        ("tallyKills", "Tally kills", new[] { "Inventory/Inv_Move_Items_01.wav", "Inventory/Inv_Move_Items_02.wav" }, .35f, .03f),
        ("tallyTotal", "Tally total", new[] { "Items/Itm_Book_Close_01.wav", "Items/Itm_Book_Close_02.wav" }, .6f, .02f),
        ("tallyBank", "Tally bank", new[] { "Items/Itm_Coinpouch_Down_02.wav" }, .55f, 0f),
    };
    // Keys of the earlier, busier tally, taken out of the library.
    static readonly string[] Retired = { "tallyTick", "tallyBigSpoil", "tallySkull" };

    // Each row's pieces, by name under the tally.
    static readonly (string field, string[] names)[] Rows =
    {
        ("spoilsRow", new[] { "Spoils label", "Spoils value", "Spoils showcase", "Spoils strip" }),
        ("killsRow", new[] { "Kills label", "Kills value", "Kills strip" }),
        ("totalRow", new[] { "Rule", "Total label", "Total value" }),
        ("bankRow", new[] { "Bank label", "Bank value" }),
    };

    // Run from an eval: RunRecapShowcase.Build().
    public static string Build()
    {
        var recap = Object.FindAnyObjectByType<RunRecapUI>(FindObjectsInactive.Include);
        if (recap == null) return "No RunRecapUI in the open scene.";
        var so = new SerializedObject(recap);
        var strip = so.FindProperty("spoilsStrip").objectReferenceValue as RectTransform;
        if (strip == null) return "The recap has no spoils strip.";
        var tally = (RectTransform)strip.parent;
        var slot = AssetDatabase.LoadAssetAtPath<Sprite>(Slot);

        // The strip makes room on its left for the showcase.
        var showcase = tally.Find("Spoils showcase") as RectTransform;
        if (showcase == null)
        {
            Undo.RecordObject(strip, "Recap showcase");
            float left = strip.anchoredPosition.x - strip.sizeDelta.x * .5f;
            const float box = 200f, gap = 10f, height = 200f;   // the spoil drawn at twice its icon's 88 px
            strip.sizeDelta = new Vector2(strip.sizeDelta.x - box - gap, height);
            strip.anchoredPosition = new Vector2(left + box + gap + strip.sizeDelta.x * .5f, strip.anchoredPosition.y);

            showcase = New("Spoils showcase", tally);
            showcase.sizeDelta = new Vector2(box, height);
            showcase.anchoredPosition = new Vector2(left + box * .5f, strip.anchoredPosition.y);
            showcase.SetSiblingIndex(strip.GetSiblingIndex());
            var frame = showcase.gameObject.AddComponent<Image>();
            frame.sprite = slot; frame.type = Image.Type.Sliced; frame.raycastTarget = false;

            var icon = New("Spoil", showcase).gameObject.AddComponent<Image>();
            icon.rectTransform.sizeDelta = new Vector2(176, 176);
            icon.preserveAspect = true; icon.raycastTarget = false;
            Undo.RegisterCreatedObjectUndo(showcase.gameObject, "Recap showcase");
        }

        so.FindProperty("showcaseIcon").objectReferenceValue = showcase.Find("Spoil").GetComponent<Image>();
        string missing = "";
        foreach (var (field, names) in Rows)
        {
            var list = so.FindProperty(field);
            list.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                var piece = tally.Find(names[i]);
                if (piece == null) missing += names[i] + ", ";
                list.GetArrayElementAtIndex(i).objectReferenceValue = piece != null ? piece.gameObject : null;
            }
        }
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(recap.gameObject.scene);

        return "Ledger built" + (missing.Length > 0 ? " (missing: " + missing.TrimEnd(',', ' ') + ")" : "") + "; " + AddSounds();
    }

    static RectTransform New(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    // Each tally sound is an AudioData asset in Audio/Data (made once; edit it freely after), keyed in the UI Library on
    // the AudioManager prefab and on the scene's AudioManager when it keeps its own list.
    // Build runs it; on its own from an eval: RunRecapShowcase.AddSounds().
    public static string AddSounds()
    {
        var data = new System.Collections.Generic.Dictionary<string, AudioData>();
        foreach (var (key, asset, clips, volume, variance) in Keys)
        {
            string path = DataFolder + asset + ".asset";
            var d = AssetDatabase.LoadAssetAtPath<AudioData>(path);
            if (d == null)
            {
                d = ScriptableObject.CreateInstance<AudioData>();
                d.clips = System.Array.ConvertAll(clips, c => AssetDatabase.LoadAssetAtPath<AudioClip>(Library + c));
                d.volume = volume; d.pitchVariance = variance;
                AssetDatabase.CreateAsset(d, path);
            }
            data[key] = d;
        }
        AssetDatabase.SaveAssets();
        int set;
        var root = PrefabUtility.LoadPrefabContents(AudioPrefab);
        try
        {
            set = SetKeys(root.GetComponent<AudioManager>(), data);
            PrefabUtility.SaveAsPrefabAsset(root, AudioPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var inScene = Object.FindAnyObjectByType<AudioManager>(FindObjectsInactive.Include);
        int scene = inScene != null ? SetKeys(inScene, data) : 0;
        if (inScene != null) EditorSceneManager.MarkSceneDirty(inScene.gameObject.scene);
        return $"{set} tally sounds set on the AudioManager prefab, {scene} on the scene's";
    }

    static int SetKeys(AudioManager manager, System.Collections.Generic.Dictionary<string, AudioData> data)
    {
        if (manager == null) return 0;
        var so = new SerializedObject(manager);
        var list = so.FindProperty("uiLibrary");
        for (int i = list.arraySize - 1; i >= 0; i--)
            if (System.Array.IndexOf(Retired, list.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue) >= 0) list.DeleteArrayElementAtIndex(i);
        int set = 0;
        foreach (var (key, d) in data)
        {
            SerializedProperty e = null;
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue == key) e = list.GetArrayElementAtIndex(i);
            if (e == null) { list.arraySize++; e = list.GetArrayElementAtIndex(list.arraySize - 1); }
            e.FindPropertyRelative("key").stringValue = key;
            e.FindPropertyRelative("source").enumValueIndex = 1;
            e.FindPropertyRelative("clip").objectReferenceValue = null;
            e.FindPropertyRelative("data").objectReferenceValue = d;
            e.FindPropertyRelative("volume").floatValue = 1f;
            e.FindPropertyRelative("pitch").floatValue = 1f;
            set++;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return set;
    }
}
