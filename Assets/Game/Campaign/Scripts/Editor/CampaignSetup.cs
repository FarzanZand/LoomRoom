using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// One-time scene setup for the campaign, run from an eval: CampaignSetup.Build(). Makes (or updates) the
// CampaignManager prefab with the realm's map, places and rules, puts it under Systems in the open scene, and builds
// the campaign card and hint line on the HUD canvas. Safe to run again.
public static class CampaignSetup
{
    const string ManagerPrefab = "Assets/Game/Campaign/Prefabs/CampaignManager.prefab";
    static readonly Color Gold = new(.93f, .76f, .38f);

    public static string Build()
    {
        System.IO.Directory.CreateDirectory("Assets/Game/Campaign/Prefabs");
        var log = "";
        // The manager prefab.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefab);
        if (prefab == null)
        {
            var go = new GameObject("CampaignManager", typeof(CampaignManager));
            prefab = PrefabUtility.SaveAsPrefabAsset(go, ManagerPrefab);
            Object.DestroyImmediate(go);
            log += "made the manager prefab; ";
        }
        var contents = PrefabUtility.LoadPrefabContents(ManagerPrefab);
        var cm = contents.GetComponent<CampaignManager>();
        cm.mapPrefab = AssetDatabase.LoadAssetAtPath<CampaignMap>(CampaignMapBuilder.PrefabPath);
        var enc = new System.Collections.Generic.List<CampaignEncounter>();
        foreach (var name in new[] { "Home", "The Crypt", "The Mill" })
        {
            var e = FindEncounter(name);
            if (e != null) enc.Add(e);
        }
        if (cm.encounters == null || cm.encounters.Length == 0) cm.encounters = enc.ToArray();
        cm.home = enc.Find(e => e.id == "crypt") ?? enc.Find(e => e.id == "start");
        if (cm.unlocks == null || cm.unlocks.Length == 0)
            cm.unlocks = new[] { new CampaignManager.Unlock { beaten = enc.Find(e => e.id == "crypt"), opensAct = 2 } };
        PrefabUtility.SaveAsPrefabAsset(contents, ManagerPrefab);
        PrefabUtility.UnloadPrefabContents(contents);
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefab);

        // Under Systems in the scene.
        var instance = Object.FindAnyObjectByType<CampaignManager>(FindObjectsInactive.Include);
        if (instance == null)
        {
            var systems = GameObject.Find("Systems");
            var made = (GameObject)PrefabUtility.InstantiatePrefab(prefab, systems != null ? systems.transform : null);
            Undo.RegisterCreatedObjectUndo(made, "Campaign manager");
            instance = made.GetComponent<CampaignManager>();
            log += "placed the manager under Systems; ";
        }

        // The card and hint on the HUD.
        var hud = Object.FindAnyObjectByType<HudCanvas>(FindObjectsInactive.Include);
        if (hud == null) return log + "no HudCanvas in the open scene";
        var old = hud.transform.Find("Campaign card");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset");
        var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator Underlay.mat");
        var panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Inventory/Plain slot grey.png");

        var root = new GameObject("Campaign card", typeof(RectTransform), typeof(CampaignCardUI));
        root.layer = hud.gameObject.layer;
        var rootRect = (RectTransform)root.transform;
        rootRect.SetParent(hud.transform, false);
        rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one; rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;
        var ui = root.GetComponent<CampaignCardUI>();

        var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        card.layer = root.layer;
        var cardRect = (RectTransform)card.transform;
        cardRect.SetParent(rootRect, false);
        cardRect.anchorMin = cardRect.anchorMax = new Vector2(.5f, .5f); cardRect.pivot = new Vector2(0f, 0f);
        cardRect.sizeDelta = new Vector2(420f, 120f);
        var img = card.GetComponent<Image>();
        img.sprite = panelSprite; img.type = Image.Type.Sliced; img.color = new Color(.06f, .055f, .07f, .9f); img.raycastTarget = false;
        var layout = card.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 12, 14); layout.spacing = 4f;
        layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        card.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var group = card.GetComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

        ui.card = cardRect; ui.group = group;
        ui.title = Text("Title", cardRect, title, 32f, Gold, null, true);
        ui.kind = Text("Kind", cardRect, body, 22f, new Color(.66f, .64f, .6f), bodyMaterial, false);
        ui.description = Text("Description", cardRect, body, 24f, new Color(.93f, .9f, .84f), bodyMaterial, false);
        ui.status = Text("Status", cardRect, body, 24f, Color.white, bodyMaterial, false);

        var hint = Text("Campaign hint", rootRect, body, 26f, new Color(.93f, .9f, .84f), bodyMaterial, false);
        var hr = hint.rectTransform;
        hr.anchorMin = new Vector2(0f, 0f); hr.anchorMax = new Vector2(1f, 0f); hr.pivot = new Vector2(.5f, 0f);
        hr.anchoredPosition = new Vector2(0f, 26f); hr.sizeDelta = new Vector2(0f, 40f);
        hint.alignment = TextAlignmentOptions.Center;
        ui.hint = hint;

        instance.card = ui;
        instance.heroSelect = BuildHeroSelect(hud.transform, title, body, bodyMaterial);
        instance.eventScreen = BuildEventScreen(hud.transform, title, body, bodyMaterial);
        EditorUtility.SetDirty(instance);
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
        EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
        return log + "built the campaign card";
    }

    // Only the hero select, in its old place among the HUD's children (from an eval: CampaignSetup.RebuildHeroSelect()).
    public static string RebuildHeroSelect()
    {
        var hud = Object.FindAnyObjectByType<HudCanvas>(FindObjectsInactive.Include);
        var instance = Object.FindAnyObjectByType<CampaignManager>(FindObjectsInactive.Include);
        if (hud == null || instance == null) return "no HudCanvas or CampaignManager in the open scene";
        var old = hud.transform.Find("Campaign hero select");
        int at = old != null ? old.GetSiblingIndex() : -1;
        var title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset");
        var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator Underlay.mat");
        var ui = BuildHeroSelect(hud.transform, title, body, bodyMaterial);
        if (at >= 0) ui.transform.SetSiblingIndex(at);
        instance.heroSelect = ui;
        EditorUtility.SetDirty(instance);
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
        EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
        return "rebuilt the hero select";
    }

    // The hero select: a shade over the map, a title, a row of hero cards at the bottom between two arrows, and the
    // chosen hero's details in a panel at the right (the class card's content). Replaces any older one.
    static CampaignHeroSelectUI BuildHeroSelect(Transform hud, TMP_FontAsset title, TMP_FontAsset body, Material bodyMaterial)
    {
        var old = hud.Find("Campaign hero select");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Inventory/Plain frame grey.png");
        var slotSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Inventory/Plain slot grey.png");
        var dim = new Color(.66f, .64f, .6f); var light = new Color(.93f, .9f, .84f);

        var root = MakeRect("Campaign hero select", hud, typeof(CanvasGroup), typeof(CampaignHeroSelectUI));
        Stretch(root);
        var ui = root.GetComponent<CampaignHeroSelectUI>();
        ui.group = root.GetComponent<CanvasGroup>();
        ui.group.alpha = 0f; ui.group.blocksRaycasts = false; ui.group.interactable = false;

        // A shade over the map; it also keeps clicks off the map.
        var shade = MakeRect("Shade", root, typeof(Image));
        Stretch(shade);
        shade.GetComponent<Image>().color = new Color(0f, 0f, 0f, .45f);

        var heading = Text("Title", root, title, 44f, Gold, null, true);
        Place(heading.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -56f), new Vector2(900f, 60f));
        heading.alignment = TextAlignmentOptions.Center; heading.text = "Choose your hero";
        var keys = Text("Keys", root, body, 22f, dim, bodyMaterial, false);
        Place(keys.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -108f), new Vector2(1200f, 32f));
        keys.alignment = TextAlignmentOptions.Center;
        keys.text = "Up and down to choose.  Enter or click again to take.  Esc to go back.";

        // The details panel at the right.
        var panel = MakeRect("Details", root, typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(AdventureClassSelectionUI));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, .5f);
        panel.anchoredPosition = new Vector2(-64f, 40f); panel.sizeDelta = new Vector2(724f, 0f);   // the info column 286 wide: its widest line (an attribute row) fills it, so both sides keep the 40 px margin
        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var panelImage = panel.GetComponent<Image>();
        panelImage.sprite = frameSprite; panelImage.type = Image.Type.Tiled; panelImage.pixelsPerUnitMultiplier = 1f; panelImage.color = new Color(.85f, .85f, .85f, .97f);
        var v = panel.GetComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(40, 40, 36, 34); v.spacing = 26f;
        v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        var details = panel.GetComponent<AdventureClassSelectionUI>();
        ui.details = details;

        // Two columns: a big portrait at the left with no frame (the class paintings are 3:4, so it fills its 330 x 440),
        // and beside it everything about the class: name, pools, attributes, description and starting skills at the top,
        // the kit at the bottom, level with the portrait's foot. The panel fits its height to them; the buttons run along
        // the bottom.
        var top = Row("Top", panel, 28f, 0f);
        top.GetComponent<LayoutElement>().preferredHeight = -1f;
        var topLayout = top.GetComponent<HorizontalLayoutGroup>();
        topLayout.childAlignment = TextAnchor.UpperLeft; topLayout.childForceExpandHeight = true;
        var portraitSlot = Slot("Portrait slot", top, slotSprite, 330f, 440f);
        var slotLayout = portraitSlot.GetComponent<LayoutElement>(); slotLayout.minWidth = 330f; slotLayout.minHeight = 440f;
        portraitSlot.GetComponent<Image>().enabled = false;
        details.portrait = Icon("Portrait", portraitSlot, 0f);
        var nameBlock = MakeRect("Name block", top, typeof(VerticalLayoutGroup), typeof(LayoutElement));
        nameBlock.GetComponent<LayoutElement>().flexibleWidth = 1f;
        var nv = nameBlock.GetComponent<VerticalLayoutGroup>();
        nv.childControlWidth = nv.childControlHeight = true; nv.childForceExpandHeight = false; nv.spacing = 6f; nv.childAlignment = TextAnchor.UpperLeft; nv.padding = new RectOffset(0, 0, 0, 0);
        details.className = Text("Name", nameBlock, title, 40f, Gold, null, true);
        details.resources = Text("Pools", nameBlock, body, 24f, light, bodyMaterial, false);
        details.attributes = Text("Attributes", nameBlock, body, 24f, light, bodyMaterial, false);
        details.attributes.textWrappingMode = TextWrappingModes.NoWrap;
        details.description = Text("Description", nameBlock, body, 24f, light, bodyMaterial, false);
        details.description.margin = new Vector4(0f, 8f, 0f, 0f);
        // The two gaps (before the skills and before the kit) share the column's free height equally, so the kit ends
        // level with the portrait's foot.
        Gap("Gap before skills", nameBlock);
        Text("Skills heading", nameBlock, title, 22f, dim, null, true).text = "STARTING SKILLS";
        details.skillIcons = new Image[3]; details.skillLabels = new TMP_Text[3];
        for (int i = 0; i < 3; i++)
        {
            var row = Row($"Skill {i + 1}", nameBlock, 10f, 30f);
            var icon = MakeRect("Icon", row, typeof(Image), typeof(LayoutElement));
            var le = icon.GetComponent<LayoutElement>(); le.preferredWidth = le.preferredHeight = 32f; le.flexibleWidth = 0f;
            details.skillIcons[i] = icon.GetComponent<Image>(); details.skillIcons[i].preserveAspect = true;
            details.skillLabels[i] = Text("Label", row, body, 24f, light, bodyMaterial, false);
            details.skillLabels[i].gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }
        Gap("Gap before kit", nameBlock);
        var kitHeading = Text("Kit heading", nameBlock, title, 22f, dim, null, true);
        kitHeading.text = "STARTS WITH";
        var kit = Row("Kit", nameBlock, 8f, 50f);
        details.kitIcons = new Image[5];
        for (int i = 0; i < 5; i++) details.kitIcons[i] = Icon("Icon", Slot($"Kit {i + 1}", kit, slotSprite, 50f, 50f), 6f);
        var buttons = Row("Buttons", panel, 16f, 58f);
        ui.back = MakeButton("Back", buttons, slotSprite, title, "Back");
        ui.choose = MakeButton("Choose", buttons, slotSprite, title, "Choose");
        details.pips = new Image[0];

        // The heroes in a column at the left (over the floor beside the table, so the map's middle and the figure on
        // it stay in view), between Previous and Next.
        var strip = MakeRect("Hero column", root, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        strip.anchorMin = strip.anchorMax = strip.pivot = new Vector2(0f, .5f);
        strip.anchoredPosition = new Vector2(64f, 20f); strip.sizeDelta = new Vector2(320f, 0f);
        var sv = strip.GetComponent<VerticalLayoutGroup>();
        sv.spacing = 12f; sv.childAlignment = TextAnchor.MiddleLeft; sv.childControlWidth = sv.childControlHeight = true; sv.childForceExpandWidth = true; sv.childForceExpandHeight = false;
        strip.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        details.previous = MakeButton("Previous", strip, slotSprite, title, "Previous", 0f, 46f);
        var cards = MakeRect("Cards", strip, typeof(VerticalLayoutGroup), typeof(LayoutElement));
        var cv = cards.GetComponent<VerticalLayoutGroup>();
        cv.spacing = 12f; cv.childAlignment = TextAnchor.UpperLeft; cv.childControlWidth = cv.childControlHeight = true; cv.childForceExpandWidth = true; cv.childForceExpandHeight = false;
        details.next = MakeButton("Next", strip, slotSprite, title, "Next", 0f, 46f);
        ui.cardRow = cards;

        // The card everything else is copied from: portrait at the left, name beside it; the chosen one slides right.
        var card = MakeRect("Hero card", cards, typeof(LayoutElement), typeof(Image), typeof(CampaignHeroCard));
        var cle = card.GetComponent<LayoutElement>(); cle.preferredWidth = 320f; cle.preferredHeight = 124f;
        card.GetComponent<Image>().color = new Color(0, 0, 0, 0);   // catches the mouse for the whole card
        var hc = card.GetComponent<CampaignHeroCard>();
        var lift = MakeRect("Lift", card); Stretch(lift);
        hc.lift = lift; hc.liftDirection = Vector2.right;
        var frame = MakeRect("Frame", lift, typeof(Image)); Stretch(frame);
        hc.frame = frame.GetComponent<Image>();
        hc.frame.sprite = slotSprite; hc.frame.type = Image.Type.Sliced; hc.frame.pixelsPerUnitMultiplier = 1f; hc.frame.raycastTarget = false;
        var pic = MakeRect("Portrait", lift, typeof(Image));
        pic.anchorMin = pic.anchorMax = pic.pivot = new Vector2(0f, .5f);
        pic.anchoredPosition = new Vector2(12f, 0f); pic.sizeDelta = new Vector2(100f, 104f);
        hc.portrait = pic.GetComponent<Image>(); hc.portrait.preserveAspect = true; hc.portrait.raycastTarget = false;
        hc.title = Text("Name", lift, title, 32f, light, null, true);
        Place(hc.title.rectTransform, new Vector2(0f, .5f), new Vector2(128f, 0f), new Vector2(180f, 40f));
        hc.title.alignment = TextAlignmentOptions.Left;
        ui.cardTemplate = hc;
        card.gameObject.SetActive(false);
        return ui;
    }

    // The text event screen: the picture over the whole screen, a darker band along the bottom for the choices, and a
    // panel at the right with the title, the text and the spoils in the bank. Replaces any older one.
    static CampaignEventUI BuildEventScreen(Transform hud, TMP_FontAsset title, TMP_FontAsset body, Material bodyMaterial)
    {
        var old = hud.Find("Campaign event");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Inventory/Plain frame grey.png");
        var slotSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Inventory/Plain slot grey.png");
        var light = new Color(.93f, .9f, .84f); var dim = new Color(.66f, .64f, .6f);

        var root = MakeRect("Campaign event", hud, typeof(CanvasGroup), typeof(CampaignEventUI));
        Stretch(root);
        var ui = root.GetComponent<CampaignEventUI>();
        ui.group = root.GetComponent<CanvasGroup>();
        ui.group.alpha = 0f; ui.group.blocksRaycasts = false; ui.group.interactable = false;

        var art = MakeRect("Art", root, typeof(Image), typeof(AspectRatioFitter)); Stretch(art);
        ui.art = art.GetComponent<Image>(); ui.art.raycastTarget = true;   // keeps clicks off the map
        var fit = art.GetComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent; fit.aspectRatio = 16f / 9f;
        // A plain shade under it fills whatever the picture leaves (and still keeps clicks off the map).
        var under = MakeRect("Backdrop", root, typeof(Image)); Stretch(under); under.SetAsFirstSibling();
        under.GetComponent<Image>().color = Color.black;
        var band = MakeRect("Choice band", root, typeof(Image));
        band.anchorMin = new Vector2(0f, 0f); band.anchorMax = new Vector2(1f, 0f); band.pivot = new Vector2(.5f, 0f);
        band.sizeDelta = new Vector2(0f, 190f);
        band.GetComponent<Image>().color = new Color(0f, 0f, 0f, .55f);

        // The panel at the right.
        var panel = MakeRect("Panel", root, typeof(Image), typeof(VerticalLayoutGroup));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, .5f);
        panel.anchoredPosition = new Vector2(-70f, 110f); panel.sizeDelta = new Vector2(640f, 600f);
        var pi = panel.GetComponent<Image>();
        pi.sprite = frameSprite; pi.type = Image.Type.Tiled; pi.pixelsPerUnitMultiplier = 1f; pi.color = new Color(.85f, .85f, .85f, .97f);
        var v = panel.GetComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(34, 34, 28, 28); v.spacing = 18f;
        v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        var banner = MakeRect("Title plate", panel, typeof(Image), typeof(LayoutElement));
        var bi = banner.GetComponent<Image>(); bi.sprite = slotSprite; bi.type = Image.Type.Sliced; bi.pixelsPerUnitMultiplier = 1f; bi.color = new Color(.35f, .3f, .24f);
        banner.GetComponent<LayoutElement>().preferredHeight = 76f;
        ui.title = Text("Title", banner, title, 40f, Gold, null, true);
        Stretch(ui.title.rectTransform); ui.title.alignment = TextAlignmentOptions.Center;
        ui.body = Text("Text", panel, body, 27f, light, bodyMaterial, false);
        ui.body.lineSpacing = 8f;
        ui.body.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        ui.body.alignment = TextAlignmentOptions.TopLeft;
        ui.bank = Text("Bank", panel, body, 22f, dim, bodyMaterial, false);
        ui.bank.alignment = TextAlignmentOptions.BottomRight;

        // The choices along the bottom left.
        var row = MakeRect("Choices", root, typeof(HorizontalLayoutGroup));
        row.anchorMin = row.anchorMax = row.pivot = new Vector2(0f, 0f);
        row.anchoredPosition = new Vector2(70f, 62f); row.sizeDelta = new Vector2(1720f, 76f);
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 24f; h.childAlignment = TextAnchor.MiddleLeft; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
        ui.choiceRow = row;
        var template = MakeRect("Choice", row, typeof(Image), typeof(Button), typeof(LayoutElement));
        var ti = template.GetComponent<Image>(); ti.sprite = slotSprite; ti.type = Image.Type.Sliced; ti.pixelsPerUnitMultiplier = 1f;
        var le = template.GetComponent<LayoutElement>(); le.preferredWidth = 440f; le.preferredHeight = 76f;
        var b = template.GetComponent<Button>();
        var colours = b.colors; colours.highlightedColor = new Color(1f, .9f, .65f); colours.selectedColor = new Color(1f, .93f, .75f);
        colours.pressedColor = new Color(.8f, .7f, .5f); colours.disabledColor = new Color(.45f, .45f, .45f, .8f); b.colors = colours;
        var label = Text("Label", template, title, 30f, Gold, null, true);
        label.rectTransform.anchorMin = new Vector2(0f, .3f); label.rectTransform.anchorMax = new Vector2(1f, 1f);
        label.rectTransform.offsetMin = new Vector2(14f, 0f); label.rectTransform.offsetMax = new Vector2(-14f, -4f);
        label.alignment = TextAlignmentOptions.Center;
        var cost = Text("Cost", template, body, 20f, new Color(.9f, .45f, .4f), bodyMaterial, false);
        cost.rectTransform.anchorMin = new Vector2(0f, 0f); cost.rectTransform.anchorMax = new Vector2(1f, .36f);
        cost.rectTransform.offsetMin = new Vector2(14f, 4f); cost.rectTransform.offsetMax = new Vector2(-14f, 0f);
        cost.alignment = TextAlignmentOptions.Center;
        ui.choiceTemplate = b;
        template.gameObject.SetActive(false);
        root.SetAsLastSibling();
        return ui;
    }

    static RectTransform MakeRect(string name, Transform parent, params System.Type[] components)
    {
        var all = new System.Type[components.Length + 1];
        all[0] = typeof(RectTransform); components.CopyTo(all, 1);
        var go = new GameObject(name, all);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    static void Place(RectTransform r, Vector2 anchor, Vector2 at, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor; r.pivot = anchor; r.anchoredPosition = at; r.sizeDelta = size;
    }

    static RectTransform Row(string name, Transform parent, float spacing, float height)
    {
        var r = MakeRect(name, parent, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        var h = r.GetComponent<HorizontalLayoutGroup>();
        h.spacing = spacing; h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
        r.GetComponent<LayoutElement>().preferredHeight = height;
        return r;
    }

    static void Gap(string name, Transform parent)
    {
        var le = MakeRect(name, parent, typeof(LayoutElement)).GetComponent<LayoutElement>();
        le.minHeight = 8f; le.flexibleHeight = 1f;
    }

    static RectTransform Slot(string name, Transform parent, Sprite sprite, float w, float h)
    {
        var r = MakeRect(name, parent, typeof(Image), typeof(LayoutElement));
        var img = r.GetComponent<Image>(); img.sprite = sprite; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1f; img.raycastTarget = false;
        var le = r.GetComponent<LayoutElement>(); le.preferredWidth = w; le.preferredHeight = h; le.flexibleWidth = 0f;
        return r;
    }

    static Image Icon(string name, Transform parent, float inset)
    {
        var r = MakeRect(name, parent, typeof(Image));
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.one * inset; r.offsetMax = -Vector2.one * inset;
        var img = r.GetComponent<Image>(); img.preserveAspect = true; img.raycastTarget = false;
        return img;
    }

    static Button MakeButton(string name, Transform parent, Sprite sprite, TMP_FontAsset font, string label, float w = 0f, float h = 58f)
    {
        var r = MakeRect(name, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
        var img = r.GetComponent<Image>(); img.sprite = sprite; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1f;
        var le = r.GetComponent<LayoutElement>(); le.preferredHeight = h;
        if (w > 0f) le.preferredWidth = w; else le.flexibleWidth = 1f;
        var b = r.GetComponent<Button>();
        var colours = b.colors; colours.highlightedColor = new Color(1f, .9f, .65f); colours.pressedColor = new Color(.8f, .7f, .5f); b.colors = colours;
        var t = Text("Label", r, font, 28f, Gold, null, true);
        Stretch(t.rectTransform); t.alignment = TextAlignmentOptions.Center; t.text = label;
        return b;
    }

    // An encounter by its asset name, in any act's folder under Campaign/Encounters.
    public static CampaignEncounter FindEncounter(string name)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:CampaignEncounter", new[] { "Assets/Game/Campaign/Encounters" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<CampaignEncounter>(path);
        }
        return null;
    }

    static TextMeshProUGUI Text(string name, RectTransform parent, TMP_FontAsset font, float size, Color colour, Material material, bool bold)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.font = font; if (material != null) t.fontSharedMaterial = material;
        t.fontSize = size; t.color = colour; t.raycastTarget = false; t.richText = true;
        t.textWrappingMode = TextWrappingModes.Normal;
        if (bold) t.characterSpacing = 2f;
        return t;
    }
}
