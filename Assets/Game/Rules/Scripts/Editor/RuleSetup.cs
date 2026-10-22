using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Sets up the house rules in the open Room scene, from an eval: RuleSetup.Build(). Safe to run again (what exists is
// kept and refreshed). Makes rule 1 (Rules/Data), the RuleManager prefab under Systems, the sheet taped to the
// bedroom's east door (its picture from Tools/house_rules_sheet.py, at the bedroom's 6.4 texels a unit), the read
// screen on the HUD canvas, and gives the intro rule 1 to say.
public static class RuleSetup
{
    const string Dir = "Assets/Game/Rules";
    const string SheetArt = Dir + "/Art/House rules sheet.png";
    static readonly Color Ink = new(.17f, .15f, .14f), Faint = new(.47f, .45f, .42f);

    public static string Build()
    {
        var log = new System.Text.StringBuilder();
        var rules = Rules(log);
        var rule = rules[0];
        var manager = Manager(rules, log);
        Sheet(log);
        manager.readScreen = ReadScreen(log);
        EditorUtility.SetDirty(manager);
        Intro(rule, log);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        EditorSceneManager.SaveScene(manager.gameObject.scene);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // The rules, in order: id (saved, never change), text, the line he gives it with, the quote, on the sheet from the start.
    static readonly (string id, string text, string given, string quote, bool pinned)[] Table =
    {
        ("rule1", "Nobody leaves the table until I say so.", "The first rule. Nobody leaves the table until I say so.", "Rule one.", true),
        ("rule2", "Hitting the merchant is allowed, but not recommended.", "The second rule. Hitting the merchant is allowed, but not recommended.", "Rule two.", true),
    };

    // Each rule's asset (Rules/Data/Rule N), made when missing; made ones are kept as they are (edit them freely).
    static HouseRule[] Rules(System.Text.StringBuilder log)
    {
        var list = new HouseRule[Table.Length];
        for (int i = 0; i < Table.Length; i++) list[i] = Rule(i + 1, Table[i], log);
        return list;
    }

    static HouseRule Rule(int number, (string id, string text, string given, string quote, bool pinned) row, System.Text.StringBuilder log)
    {
        string path = $"{Dir}/Data/Rule {number}.asset";
        System.IO.Directory.CreateDirectory(Dir + "/Data");
        var rule = AssetDatabase.LoadAssetAtPath<HouseRule>(path);
        if (rule == null)
        {
            rule = ScriptableObject.CreateInstance<HouseRule>();
            rule.id = row.id;
            rule.text = row.text;
            rule.given = new DungeonMaster.Line { text = row.given, inPerson = true, mumble = true };
            rule.quote = new DungeonMaster.Line { text = row.quote, mumble = true };
            rule.pinnedFromStart = row.pinned;
            AssetDatabase.CreateAsset(rule, path);
            log.Append($"rule {number} made; ");
        }
        // Said like the intro's other lines (its voice).
        var intro = Object.FindAnyObjectByType<IntroController>(FindObjectsInactive.Include);
        if (intro != null && intro.afterLamp.Length > 0 && intro.afterLamp[0] != null && rule.given.voice == null)
        {
            rule.given.voice = intro.afterLamp[0].voice; rule.given.mumble = intro.afterLamp[0].mumble; rule.given.inPerson = intro.afterLamp[0].inPerson;
            rule.quote.voice = intro.afterLamp[0].voice;
            EditorUtility.SetDirty(rule);
        }
        return rule;
    }

    static RuleManager Manager(HouseRule[] rules, System.Text.StringBuilder log)
    {
        string path = Dir + "/Prefabs/RuleManager.prefab";
        System.IO.Directory.CreateDirectory(Dir + "/Prefabs");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            var go = new GameObject("RuleManager");
            var m = go.AddComponent<RuleManager>();
            prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            log.Append("manager prefab made; ");
        }
        // The prefab lists every rule in order.
        var contents = PrefabUtility.LoadPrefabContents(path);
        try { contents.GetComponent<RuleManager>().rules = rules; PrefabUtility.SaveAsPrefabAsset(contents, path); }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        var existing = Object.FindAnyObjectByType<RuleManager>(FindObjectsInactive.Include);
        if (existing != null)
        {
            // The scene's copy follows the prefab's list.
            var so = new SerializedObject(existing);
            var p = so.FindProperty("rules");
            if (p != null && p.prefabOverride) PrefabUtility.RevertPropertyOverride(p, InteractionMode.AutomatedAction);
            return existing;
        }
        var systems = GameObject.Find("Systems");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, systems != null ? systems.transform : null);
        instance.name = "RuleManager";
        log.Append("manager under Systems; ");
        return instance.GetComponent<RuleManager>();
    }

    // The sheet on the room side of the east door's leaf, at eye height, swinging with the leaf.
    static void Sheet(System.Text.StringBuilder log)
    {
        var door = WorldManager.Instance != null ? WorldManager.Instance.otherDoor : null;
        if (door == null) { log.Append("NO east door (WorldManager.otherDoor); "); return; }
        Renderer leaf = null;
        foreach (var r in door.GetComponentsInChildren<Renderer>(true)) if (r.name == "Synty door leaf") leaf = r;
        if (leaf == null) { log.Append("no door leaf; "); return; }
        var old = leaf.transform.Find("House rules sheet");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        // The art (one picture per number of rules, Tools/house_rules_sheet.py): point filtered, every texel kept.
        var pictures = new System.Collections.Generic.List<Texture2D>();
        for (int n = 1; n <= Table.Length; n++)
        {
            string file = n == 1 ? SheetArt : $"{Dir}/Art/House rules sheet {n}.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(file);
            if (importer == null) { log.Append($"no sheet picture for {n} rules ({file}); "); continue; }
            importer.textureType = TextureImporterType.Default; importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.wrapMode = TextureWrapMode.Clamp; importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            pictures.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(file));
        }
        var art = pictures[0];
        string matPath = Dir + "/Art/House rules sheet.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(leaf.sharedMaterial.shader); AssetDatabase.CreateAsset(mat, matPath); }
        mat.SetTexture("_BaseMap", art); mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", .1f);
        EditorUtility.SetDirty(mat);

        // The room side is away from the door's forward (the room lies west of the east wall).
        var b = leaf.bounds;
        var roomSide = -door.transform.forward;
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "House rules sheet";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(leaf.transform, true);
        const float width = 7f, height = 9.5f, eye = 31f;
        var centre = b.center + roomSide * (Mathf.Abs(Vector3.Dot(b.extents, roomSide)) + .04f);
        centre.y = b.min.y + eye;
        go.transform.position = centre;
        go.transform.rotation = Quaternion.LookRotation(-roomSide, Vector3.up);   // the quad's face looks along -forward: into the room
        go.transform.localScale = Vector3.one;
        var s = go.transform.lossyScale; var want = new Vector3(width, height, 1f);
        go.transform.localScale = new Vector3(want.x / s.x, want.y / s.y, 1f / s.z);
        go.layer = leaf.gameObject.layer;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat; mr.renderingLayerMask = leaf.renderingLayerMask; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // To read it: a trigger a little in front of the paper.
        var box = go.AddComponent<BoxCollider>(); box.size = new Vector3(1f, 1f, 1.2f / Mathf.Max(.001f, go.transform.lossyScale.z)); box.center = new Vector3(0, 0, -box.size.z * .5f);
        go.AddComponent<InteractableTrigger>();
        var sheet = go.AddComponent<HouseRulesSheet>();
        sheet.paper = mr; sheet.sheets = pictures.ToArray();
        log.Append($"sheet on {door.name} at {centre}; ");
    }

    static HouseRulesUI ReadScreen(System.Text.StringBuilder log)
    {
        var canvas = GameObject.Find("UI/Canvas")?.transform;
        if (canvas == null) { log.Append("no UI/Canvas; "); return null; }
        var old = canvas.Find("House rules");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var holder = Rect("House rules", canvas, Vector2.zero, Vector2.zero);
        holder.anchorMin = Vector2.zero; holder.anchorMax = Vector2.one; holder.sizeDelta = Vector2.zero;
        var ui = holder.gameObject.AddComponent<HouseRulesUI>();
        var root = Rect("Sheet", holder, Vector2.zero, Vector2.zero);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.sizeDelta = Vector2.zero;
        var dim = root.gameObject.AddComponent<Image>(); dim.color = new Color(0, 0, 0, .55f); dim.raycastTarget = true;
        var paper = Img("Paper", root, Vector2.zero, new Vector2(640, 820), AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Meta/Paper.png"), Color.white);
        var tape = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Meta/Paper.png");
        Img("Tape left", paper.transform, new Vector2(-250, 404), new Vector2(110, 34), tape, new Color(.93f, .89f, .66f, .85f)).rectTransform.localRotation = Quaternion.Euler(0, 0, 6);
        Img("Tape right", paper.transform, new Vector2(250, 404), new Vector2(110, 34), tape, new Color(.93f, .89f, .66f, .85f)).rectTransform.localRotation = Quaternion.Euler(0, 0, -5);
        var bold = Font("PixelOperator-Bold"); var body = Font("PixelOperator");
        Text("Title", paper.transform, new Vector2(0, 320), new Vector2(560, 70), "HOUSE RULES", bold, 52, Ink, TextAlignmentOptions.Center);
        var line = Img("Underline", paper.transform, new Vector2(0, 278), new Vector2(330, 4), null, Ink);
        line.rectTransform.localRotation = Quaternion.Euler(0, 0, -1);
        var list = Text("Rules", paper.transform, new Vector2(0, -40), new Vector2(520, 560), "1.  Nobody leaves the table until I say so.", body, 32, Ink, TextAlignmentOptions.TopLeft);
        list.textWrappingMode = TextWrappingModes.Normal;
        Text("Hint", paper.transform, new Vector2(0, -372), new Vector2(560, 36), "Esc or click to put it down", body, 22, Faint, TextAlignmentOptions.Center);
        ui.root = root.gameObject; ui.list = list;
        root.gameObject.SetActive(false);
        log.Append("read screen built; ");
        return ui;
    }

    static void Intro(HouseRule rule, System.Text.StringBuilder log)
    {
        var intro = Object.FindAnyObjectByType<IntroController>(FindObjectsInactive.Include);
        if (intro == null) { log.Append("no IntroController; "); return; }
        Undo.RecordObject(intro, "Opening rule");
        intro.openingRule = rule;
        // After "We're going to play a game." (the third line after the lamp).
        int at = System.Array.FindIndex(intro.afterLamp, l => l != null && l.text != null && l.text.Contains("play a game"));
        intro.openingRuleAfter = at >= 0 ? at + 1 : 3;
        EditorUtility.SetDirty(intro);
        log.Append($"intro gives rule 1 after line {intro.openingRuleAfter}; ");
    }

    static TMP_FontAsset Font(string name) => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"Assets/Game/UI/Fonts/{name}.asset");
    static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }
    static Image Img(string name, Transform parent, Vector2 pos, Vector2 size, Sprite sprite, Color color)
    {
        var img = Rect(name, parent, pos, size).gameObject.AddComponent<Image>();
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        return img;
    }
    static TMP_Text Text(string name, Transform parent, Vector2 pos, Vector2 size, string text, TMP_FontAsset font, float fontSize, Color color, TextAlignmentOptions align)
    {
        var t = Rect(name, parent, pos, size).gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.font = font; t.fontSize = fontSize; t.color = color; t.alignment = align; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }
}
