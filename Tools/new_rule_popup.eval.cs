// The new-rule notice (NewRuleUI, the user, 2026-11-13: a small silent "New rule added" with the rule typed under it,
// where the Dungeon Master's text is): makes Rules/Resources/New rule.prefab. Its two texts are copies of the Dungeon
// Master's own lines' label (Room open: read from the DungeonMaster on the Dialogue Manager), so font and material are
// his lines': the title in the level up's gold at his lines' size, the rule under it smaller, in white. On its own canvas,
// scaled as the HUD's is; NewRuleUI stands it on his lines at run time. Safe to run again: the prefab is rebuilt,
// NewRuleUI's timings kept.
const string Dir = "Assets/Game/Rules/Resources";
const string Path = Dir + "/New rule.prefab";
var sb = new System.Text.StringBuilder();
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
if (!UnityEditor.AssetDatabase.IsValidFolder(Dir)) UnityEditor.AssetDatabase.CreateFolder("Assets/Game/Rules", "Resources");

var dm = UnityEngine.Object.FindAnyObjectByType<DungeonMaster>(FindObjectsInactive.Include);
if (dm == null) return "no DungeonMaster in the open scene (open Room)";
var dso = new UnityEditor.SerializedObject(dm);
var feed = dso.FindProperty("label").objectReferenceValue as TMPro.TMP_Text;
if (feed == null) return "the DungeonMaster has no label";
float feedSize = dso.FindProperty("fontSize").floatValue;
var hudCanvas = UnityEngine.Object.FindAnyObjectByType<HudCanvas>(FindObjectsInactive.Include).GetComponentInParent<Canvas>().rootCanvas;
var hudScaler = hudCanvas.GetComponent<UnityEngine.UI.CanvasScaler>();
var feedCanvas = feed.GetComponentInParent<Canvas>().rootCanvas;
var levelUp = UnityEngine.Object.FindAnyObjectByType<LevelUpUI>(FindObjectsInactive.Include);
var gold = new Color(.96f, .78f, .36f);
if (levelUp != null) { var t = new UnityEditor.SerializedObject(levelUp).FindProperty("title").objectReferenceValue as TMPro.TMP_Text; if (t != null) gold = t.color; }
sb.Append($"his lines: {feed.font.name} {feedSize}, material {feed.fontSharedMaterial.name}, canvas '{feedCanvas.name}' order {feedCanvas.sortingOrder}; HUD order {hudCanvas.sortingOrder}\n");

TMPro.TMP_Text Copy(string name, Transform parent, string text, float size, Color colour, float width, bool wrap)
{
    var go = UnityEngine.Object.Instantiate(feed.gameObject, parent, false);
    go.name = name; go.SetActive(true);
    foreach (var c in go.GetComponents<Component>()) if (!(c is Transform) && !(c is CanvasRenderer) && !(c is TMPro.TMP_Text)) UnityEngine.Object.DestroyImmediate(c);
    for (int i = go.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
    var t = go.GetComponent<TMPro.TMP_Text>(); var r = t.rectTransform;
    r.anchorMin = r.anchorMax = new Vector2(.5f, 0f); r.pivot = new Vector2(.5f, 0f);   // stands on its foot
    r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(width, size * 1.4f); r.localScale = Vector3.one; r.localRotation = Quaternion.identity;
    t.text = text; t.fontSize = size; t.color = colour; t.alpha = 1f; t.raycastTarget = false; t.enableAutoSizing = false; t.richText = false;
    t.alignment = TMPro.TextAlignmentOptions.Bottom;
    t.textWrappingMode = wrap ? TMPro.TextWrappingModes.Normal : TMPro.TextWrappingModes.NoWrap;
    t.overflowMode = TMPro.TextOverflowModes.Overflow;
    t.maxVisibleCharacters = 99999;
    return t;
}

var keep = new System.Collections.Generic.Dictionary<string, float>();
var old = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Path);
if (old != null && old.GetComponent<NewRuleUI>() != null)
{
    var oso = new UnityEditor.SerializedObject(old.GetComponent<NewRuleUI>());
    foreach (var f in new[] { "gap", "titleGap", "textDelay", "charactersPerSecond", "punctuationPause", "readBase", "readPerWord", "leastHold", "fade" }) { var p = oso.FindProperty(f); if (p != null) keep[f] = p.floatValue; }
}

var root = new GameObject("New rule", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(CanvasGroup), typeof(NewRuleUI));
try
{
    root.layer = feed.gameObject.layer;
    var canvas = root.GetComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = feedCanvas.sortingOrder;   // with his lines
    var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
    scaler.uiScaleMode = hudScaler.uiScaleMode; scaler.referenceResolution = hudScaler.referenceResolution;
    scaler.screenMatchMode = hudScaler.screenMatchMode; scaler.matchWidthOrHeight = hudScaler.matchWidthOrHeight; scaler.referencePixelsPerUnit = hudScaler.referencePixelsPerUnit;
    var group = root.GetComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

    var noticeGo = new GameObject("Notice", typeof(RectTransform)); noticeGo.layer = root.layer;
    var notice = (RectTransform)noticeGo.transform; notice.SetParent(root.transform, false);
    notice.anchorMin = notice.anchorMax = new Vector2(.5f, 0f); notice.pivot = new Vector2(.5f, 0f);
    notice.anchoredPosition = new Vector2(0f, 250f); notice.sizeDelta = new Vector2(0f, 0f);

    // The rule: smaller than his lines (three quarters). The title: his lines' size, in the level up's gold.
    float small = Mathf.Round(feedSize * .75f);
    var rule = Copy("Rule", notice, "The Dungeon Master is not a target. However big he is.", small, Color.white, 1200f, true);
    var title = Copy("Title", notice, "NEW RULE ADDED", feedSize, gold, 1200f, false);
    title.rectTransform.anchoredPosition = new Vector2(0f, small * 1.3f);

    var so = new UnityEditor.SerializedObject(root.GetComponent<NewRuleUI>());
    so.FindProperty("group").objectReferenceValue = group; so.FindProperty("notice").objectReferenceValue = notice;
    so.FindProperty("title").objectReferenceValue = title; so.FindProperty("ruleText").objectReferenceValue = rule;
    foreach (var kv in keep) so.FindProperty(kv.Key).floatValue = kv.Value;
    so.ApplyModifiedPropertiesWithoutUndo();
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, Path);
    sb.Append($"prefab made: title {title.fontSize}, rule {rule.fontSize}, canvas order {canvas.sortingOrder}\n");
}
finally { UnityEngine.Object.DestroyImmediate(root); }
UnityEditor.AssetDatabase.SaveAssets();
return sb.ToString();
