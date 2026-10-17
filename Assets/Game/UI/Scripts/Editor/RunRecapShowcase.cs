using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the run recap's spoils showcase in the open Room scene: a box left of the spoils strip with turning light rays
// (Tools/recap_rays.py) and the big picture of the spoil being counted, the effects layer the flying spoils and sparks
// are drawn on, and the tally sounds (Tools/tally_sounds.py) as UI Library keys on the AudioManager. RunRecapUI plays it
// all. Keeps what exists, so hand edits survive; safe to run again.
public static class RunRecapShowcase
{
    const string Rays = "Assets/Game/UI/Sprites/Meta/Spoils rays.png";
    const string Slot = "Assets/Game/UI/Sprites/Diablo/Plain slot grey.png";
    const string Sounds = "Assets/Game/Audio/Clips/Tally/";
    const string AudioPrefab = "Assets/Game/Core/Prefabs/AudioManager.prefab";
    static readonly (string key, string file, float volume)[] Keys =
    {
        ("tallyTick", "tally tick.wav", .5f), ("tallySkull", "tally skull.wav", .45f),
        ("tallyTotal", "tally total.wav", .8f), ("tallyBank", "tally bank.wav", .6f),
    };

    [MenuItem("Tools/LoomRoom/UI/Build Recap Spoils Showcase")]
    public static string Build()
    {
        var recap = Object.FindAnyObjectByType<RunRecapUI>(FindObjectsInactive.Include);
        if (recap == null) return "No RunRecapUI in the open scene.";
        var so = new SerializedObject(recap);
        var strip = so.FindProperty("spoilsStrip").objectReferenceValue as RectTransform;
        if (strip == null) return "The recap has no spoils strip.";
        var tally = (RectTransform)strip.parent;
        var panel = (RectTransform)tally.parent;
        var rays = SpriteAt(Rays, FilterMode.Point);
        var slot = AssetDatabase.LoadAssetAtPath<Sprite>(Slot);

        // The strip makes room on its left for the showcase.
        var showcase = tally.Find("Spoils showcase") as RectTransform;
        if (showcase == null)
        {
            Undo.RecordObject(strip, "Recap showcase");
            float left = strip.anchoredPosition.x - strip.sizeDelta.x * .5f;
            const float box = 200f, gap = 10f, height = 200f;   // the spoil drawn at twice its icon's 88 px, rays round it
            strip.sizeDelta = new Vector2(strip.sizeDelta.x - box - gap, height);
            strip.anchoredPosition = new Vector2(left + box + gap + strip.sizeDelta.x * .5f, strip.anchoredPosition.y);

            showcase = New("Spoils showcase", tally);
            showcase.sizeDelta = new Vector2(box, height);
            showcase.anchoredPosition = new Vector2(left + box * .5f, strip.anchoredPosition.y);
            showcase.SetSiblingIndex(strip.GetSiblingIndex());
            var frame = showcase.gameObject.AddComponent<Image>();
            frame.sprite = slot; frame.type = Image.Type.Sliced; frame.raycastTarget = false;
            showcase.gameObject.AddComponent<RectMask2D>();

            var rayImage = New("Rays", showcase).gameObject.AddComponent<Image>();
            rayImage.rectTransform.sizeDelta = new Vector2(320, 320);
            rayImage.sprite = rays; rayImage.color = new Color(1f, .78f, .3f, .3f); rayImage.raycastTarget = false;

            var icon = New("Spoil", showcase).gameObject.AddComponent<Image>();
            icon.rectTransform.sizeDelta = new Vector2(176, 176);
            icon.preserveAspect = true; icon.raycastTarget = false;
            Undo.RegisterCreatedObjectUndo(showcase.gameObject, "Recap showcase");
        }

        var effects = panel.Find("Tally effects") as RectTransform;
        if (effects == null)
        {
            effects = New("Tally effects", panel);
            effects.anchorMin = Vector2.zero; effects.anchorMax = Vector2.one; effects.offsetMin = effects.offsetMax = Vector2.zero;
            Undo.RegisterCreatedObjectUndo(effects.gameObject, "Recap showcase");
        }
        effects.SetAsLastSibling();

        so.FindProperty("showcaseIcon").objectReferenceValue = showcase.Find("Spoil").GetComponent<Image>();
        so.FindProperty("showcaseRays").objectReferenceValue = showcase.Find("Rays").GetComponent<Image>();
        so.FindProperty("effects").objectReferenceValue = effects;
        so.FindProperty("shakeTarget").objectReferenceValue = panel;
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(recap.gameObject.scene);

        return "Showcase built; " + AddSounds();
    }

    static RectTransform New(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    static Sprite SpriteAt(string path, FilterMode filter)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer != null && (importer.textureType != TextureImporterType.Sprite || importer.filterMode != filter))
        {
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = filter; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // The tally keys go on the AudioManager prefab, and on the scene's AudioManager when it keeps its own list.
    static string AddSounds()
    {
        int added = 0;
        var root = PrefabUtility.LoadPrefabContents(AudioPrefab);
        try
        {
            added += AddKeys(root.GetComponent<AudioManager>());
            PrefabUtility.SaveAsPrefabAsset(root, AudioPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var inScene = Object.FindAnyObjectByType<AudioManager>(FindObjectsInactive.Include);
        int scene = inScene != null ? AddKeys(inScene) : 0;
        if (scene > 0) EditorSceneManager.MarkSceneDirty(inScene.gameObject.scene);
        return $"{added} tally sounds added to the AudioManager prefab, {scene} to the scene's";
    }

    static int AddKeys(AudioManager manager)
    {
        if (manager == null) return 0;
        var so = new SerializedObject(manager);
        var list = so.FindProperty("uiLibrary");
        int added = 0;
        foreach (var (key, file, volume) in Keys)
        {
            bool has = false;
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue == key) has = true;
            if (has) continue;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Sounds + file);
            if (clip == null) { Debug.LogWarning($"[Recap] {Sounds + file} is missing: run Tools/tally_sounds.py."); continue; }
            list.arraySize++;
            var e = list.GetArrayElementAtIndex(list.arraySize - 1);
            e.FindPropertyRelative("key").stringValue = key;
            e.FindPropertyRelative("source").enumValueIndex = 0;
            e.FindPropertyRelative("clip").objectReferenceValue = clip;
            e.FindPropertyRelative("data").objectReferenceValue = null;
            e.FindPropertyRelative("volume").floatValue = volume;
            e.FindPropertyRelative("pitch").floatValue = 1f;
            added++;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return added;
    }
}
