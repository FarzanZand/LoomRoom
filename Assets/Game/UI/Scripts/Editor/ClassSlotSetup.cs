using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the class slot (ClassSlotUI) right of the hotbar's last slot in the open scene, and the "Class action" key row
// in the settings screen. Run from an eval: ClassSlotSetup.Build(), ClassSlotSetup.SettingsRow(), and
// ClassSlotSetup.Counter() for only the Crescendo counter on a slot already built (Build makes it too). Safe to run
// again: each replaces what it made before.
//
// The slot is a copy of the hotbar's last slot with the item parts taken off (count, cooldown, rarity floor), a child
// of the hotbar kept out of its layout, so it fades with the hotbar and the six slots stay where they are.
public static class ClassSlotSetup
{
    const string SlotName = "Class slot";
    // The class slot stands a little apart from the six item slots, so it reads as a button of its own.
    const float Gap = 22f;
    const string SettingsPrefab = "Assets/Game/UI/Prefabs/Settings screen.prefab";
    const string RowName = "Bind Class action";
    const string CounterName = "Crescendo count";
    // The HUD's underlay for PixelOperator (the gold counter's, the interact prompt's), so the counter reads over the icon.
    const string UnderlayMaterial = "Assets/Game/UI/Fonts/PixelOperator Underlay.mat";

    public static string Build()
    {
        var hotbar = Object.FindAnyObjectByType<HotbarUI>(FindObjectsInactive.Include);
        if (hotbar == null) return "no HotbarUI in the open scene";
        var old = hotbar.transform.Find(SlotName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        ItemSlotUI last = null;
        foreach (Transform child in hotbar.transform)
            if (child.TryGetComponent<ItemSlotUI>(out var s)) last = s;
        if (last == null) return "no hotbar slot to copy";

        var copy = Object.Instantiate(last.gameObject, hotbar.transform);
        copy.name = SlotName;
        copy.transform.SetAsLastSibling();

        // Where each part is on the copy, read from the slot's own references.
        var source = new SerializedObject(last);
        Component Of(string field) => source.FindProperty(field)?.objectReferenceValue as Component;
        Transform Part(string field)
        {
            var o = Of(field);
            if (o == null) return null;
            var path = AnimationUtility.CalculateTransformPath(o.transform, last.transform);
            return string.IsNullOrEmpty(path) ? copy.transform : copy.transform.Find(path);
        }

        var background = Part("background");
        var icon = Part("iconImage");
        var onFrame = Part("equippedHighlight");
        var keyLabel = Part("keyLabel");
        var dungeonFrame = Part("dungeonFrame");
        // The item parts the class slot has no use for.
        foreach (var field in new[] { "cooldownFill", "cooldownLabel", "rarityFill", "stackLabel" })
        {
            var t = Part(field);
            if (t != null && t != copy.transform) Object.DestroyImmediate(t.gameObject);
        }
        Object.DestroyImmediate(copy.GetComponent<ItemSlotUI>());

        // Out of the hotbar's layout, right of its last slot.
        var layout = hotbar.GetComponent<HorizontalLayoutGroup>();
        var le = copy.GetComponent<LayoutElement>();
        if (le == null) le = copy.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        var rt = (RectTransform)copy.transform;
        var lastRt = (RectTransform)last.transform;
        float w = lastRt.sizeDelta.x;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(.5f, .5f);
        rt.sizeDelta = lastRt.sizeDelta;
        rt.anchoredPosition = new Vector2(-(layout != null ? layout.padding.right : 0) + Gap + w / 2f, lastRt.anchoredPosition.y);
        rt.localScale = Vector3.one;

        // A key name longer than a digit ("Left Shift" after a rebind) runs on to the right instead of wrapping.
        var key = keyLabel != null ? keyLabel.GetComponent<TMP_Text>() : null;
        if (key != null)
        {
            key.text = "X";
            key.textWrappingMode = TextWrappingModes.NoWrap;
            key.overflowMode = TextOverflowModes.Overflow;
            var kr = key.rectTransform; kr.sizeDelta = new Vector2(Mathf.Max(kr.sizeDelta.x, w - 14f), kr.sizeDelta.y);
        }
        if (onFrame != null) onFrame.gameObject.SetActive(false);
        if (icon != null) { var img = icon.GetComponent<Image>(); img.sprite = null; img.enabled = false; }

        var group = copy.GetComponent<CanvasGroup>();
        if (group == null) group = copy.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = group.interactable = false;

        var ui = copy.AddComponent<ClassSlotUI>();
        var so = new SerializedObject(ui);
        so.FindProperty("group").objectReferenceValue = group;
        so.FindProperty("background").objectReferenceValue = background != null ? background.GetComponent<Image>() : null;
        so.FindProperty("icon").objectReferenceValue = icon != null ? icon.GetComponent<Image>() : null;
        so.FindProperty("onFrame").objectReferenceValue = onFrame != null ? onFrame.GetComponent<Image>() : null;
        so.FindProperty("dungeonFrame").objectReferenceValue = dungeonFrame != null ? dungeonFrame.GetComponent<Image>() : null;
        so.FindProperty("keyLabel").objectReferenceValue = key;
        so.FindProperty("matchBackground").objectReferenceValue = Of("background") as Image;
        so.FindProperty("matchKeyLabel").objectReferenceValue = Of("keyLabel") as TMP_Text;
        so.FindProperty("matchDungeonFrame").objectReferenceValue = Of("dungeonFrame") as Image;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(copy);
        PrefabUtility.RecordPrefabInstancePropertyModifications(hotbar);
        EditorSceneManager.MarkSceneDirty(hotbar.gameObject.scene);
        return $"built {SlotName} under {hotbar.name} at {rt.anchoredPosition}, copied from {last.name}; " + Counter();
    }

    // The Crescendo counter in the class slot's lower right corner: a copy of the hotbar's last slot's count label (a
    // stack's size, a scroll's charges: the same corner, font and underlay), shown by ClassSlotUI only for a class whose
    // passive is Crescendo.
    public static string Counter()
    {
        var hotbar = Object.FindAnyObjectByType<HotbarUI>(FindObjectsInactive.Include);
        var slot = hotbar != null ? hotbar.transform.Find(SlotName) : null;
        var ui = slot != null ? slot.GetComponent<ClassSlotUI>() : null;
        if (ui == null) return "no class slot to put a counter on: run ClassSlotSetup.Build()";
        ItemSlotUI last = null;
        foreach (Transform child in hotbar.transform)
            if (child.TryGetComponent<ItemSlotUI>(out var s)) last = s;
        var source = last != null ? new SerializedObject(last).FindProperty("stackLabel").objectReferenceValue as TMP_Text : null;
        if (source == null) return "no count label on the hotbar's last slot to copy";

        var old = slot.Find(CounterName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var parentPath = AnimationUtility.CalculateTransformPath(source.transform.parent, last.transform);
        var parent = string.IsNullOrEmpty(parentPath) ? null : slot.Find(parentPath);
        if (parent == null) parent = slot;
        var copy = Object.Instantiate(source.gameObject, parent);
        copy.name = CounterName;
        copy.transform.SetSiblingIndex(source.transform.GetSiblingIndex());
        var rt = (RectTransform)copy.transform;
        var srt = source.rectTransform;
        rt.anchorMin = srt.anchorMin; rt.anchorMax = srt.anchorMax; rt.pivot = srt.pivot;
        rt.anchoredPosition = srt.anchoredPosition; rt.sizeDelta = srt.sizeDelta; rt.localScale = Vector3.one;
        var label = copy.GetComponent<TMP_Text>();
        label.text = "0/8";
        // "8/8" is wider than a count: it runs on to the left from the corner instead of wrapping.
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        // ClassSlotUI sets the counter's colour (white, gold on the crescendo): the theme colours the count label
        // carries would set it back whenever the theme or the HUD style changes.
        foreach (var themed in copy.GetComponents<UIColor>()) Object.DestroyImmediate(themed);
        copy.SetActive(false);

        var so = new SerializedObject(ui);
        so.FindProperty("counterLabel").objectReferenceValue = label;
        so.FindProperty("matchCountLabel").objectReferenceValue = source;
        var underlay = AssetDatabase.LoadAssetAtPath<Material>(UnderlayMaterial);
        so.FindProperty("counterMaterial").objectReferenceValue = underlay;
        if (underlay != null && underlay.GetTexture(ShaderUtilities.ID_MainTex) == label.font.atlasTexture) label.fontSharedMaterial = underlay;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(slot.gameObject);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ui);
        EditorSceneManager.MarkSceneDirty(slot.gameObject.scene);
        return $"counter {CounterName} under {parent.name}, copied from {last.name}/{source.name} (font {source.font?.name}, size {source.fontSize}, align {source.alignment})";
    }

    // The "Class action" row after Hotbar 6 in the settings screen's key list, and in SettingsUI's list of rows.
    public static string SettingsRow()
    {
        var root = PrefabUtility.LoadPrefabContents(SettingsPrefab);
        try
        {
            var settings = root.GetComponentInChildren<SettingsUI>(true);
            var after = System.Array.Find(root.GetComponentsInChildren<RebindRowUI>(true), r => new SerializedObject(r).FindProperty("actionName").stringValue == "Hotbar6");
            if (settings == null || after == null) return "no SettingsUI or Hotbar 6 row in the settings screen";
            var rows = after.transform.parent;
            var old = rows.Find(RowName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var copy = Object.Instantiate(after.gameObject, rows);
            copy.name = RowName;
            copy.transform.SetSiblingIndex(after.transform.GetSiblingIndex() + 1);
            var row = copy.GetComponent<RebindRowUI>();
            var rso = new SerializedObject(row);
            rso.FindProperty("actionName").stringValue = "ClassAction";
            rso.FindProperty("part").stringValue = "";
            rso.ApplyModifiedPropertiesWithoutUndo();
            if (rso.FindProperty("label").objectReferenceValue is TMP_Text label) label.text = "Class action";

            // SettingsUI's rows in the order they stand (the pad and arrow keys step through them in that order).
            var sso = new SerializedObject(settings);
            var list = sso.FindProperty("bindings");
            var current = new System.Collections.Generic.List<RebindRowUI>();
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is RebindRowUI r && r != null && r.name != RowName) current.Add(r);
            int at = current.IndexOf(after);
            current.Insert(at < 0 ? current.Count : at + 1, row);
            list.arraySize = current.Count;
            for (int i = 0; i < current.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = current[i];
            sso.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, SettingsPrefab);
            return $"added {RowName} after {after.name}; {current.Count} rows";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
