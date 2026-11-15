using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Draws a key cap behind every key a text names, so "[E]  Open" reads as a key on the keyboard and not as a letter in
// brackets. The text marks a key with KeyCapText.Key("E") (a <link="key"> with room either side); this lays one cap per
// marked key, per line it covers, behind the text: the inventory's grey slot, 9-sliced, its alpha following the text's.
// The caps live on a sibling made just before the text (a child would draw over it), which copies the text's rect, scale
// and place every frame, so layout groups, pops and fades carry them along. A text with no marks draws no caps.
[RequireComponent(typeof(TMP_Text))]
public class KeyCapText : MonoBehaviour
{
    public const string LinkId = "key";

    [SerializeField, Tooltip("The cap: a 9-sliced sprite; empty = UI/Resources/Key cap.")] Sprite cap;
    [SerializeField, Tooltip("Room round the key's letters, canvas units (sideways, up and down).")] Vector2 padding = new(6f, 3f);
    [SerializeField, Tooltip("Tint of the cap; its alpha is multiplied by the text's.")] Color capColour = Color.white;
    [SerializeField, Min(.1f), Tooltip("The cap's 9-slice border is drawn this many times its size.")] float borderScale = 1f;

    TMP_Text text;
    RectTransform layer, textRect;
    readonly List<Image> caps = new();
    readonly List<Rect> rects = new();
    bool dirty = true;
    Vector2 measuredSize;

    // A key as a text should write it: thin spaces either side so the cap clears the words beside it, no-break spaces
    // inside (a two-word key is never split over two lines).
    public static string Key(string name)
    {
        name ??= "";
        // A one-letter key gets a square cap (Show), wider than its letter: a little more room either side for it.
        string wide = name.Length == 1 ? Wide : "";
        return Before + wide + "<link=\"" + LinkId + "\">" + name.Replace(' ', '\u00A0') + "</link>" + wide + After;
    }
    const string Before = "<space=.35em>", After = "<space=.35em>", Tight = "<space=.2em>", Wide = "<space=.18em>";

    // A key right after an opening bracket, or right before a full stop, comma or closing bracket, keeps only the room
    // its cap needs on that side ("hold Right mouse, press Left mouse.", not "Right mouse ,").
    public static string Tidy(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf("<link", System.StringComparison.Ordinal) < 0) return s;
        foreach (char c in ".,;:!?)") s = s.Replace("</link>" + After + c, "</link>" + Tight + c);
        foreach (char c in ".,;:!?)-")   // "Shift-click" too
            s = s.Replace("</link>" + After + c, "</link>" + Tight + c).Replace("</link>" + Wide + After + c, "</link>" + Wide + Tight + c);
        return s.Replace("(" + Before + "<link", "(" + Tight + "<link").Replace("(" + Before + Wide + "<link", "(" + Tight + Wide + "<link");
    }

    // Puts caps on a text that has none yet (UI that builds its keys in code calls this once).
    public static KeyCapText On(TMP_Text t)
    {
        if (t == null) return null;
        return t.TryGetComponent(out KeyCapText k) ? k : t.gameObject.AddComponent<KeyCapText>();
    }

    static Sprite defaultCap;
    void Awake()
    {
        text = GetComponent<TMP_Text>(); textRect = text.rectTransform;
        if (cap == null) cap = defaultCap != null ? defaultCap : defaultCap = Resources.Load<Sprite>("Key cap");
    }

    void OnEnable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        dirty = true;
        if (layer != null) layer.gameObject.SetActive(true);
    }

    void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
        if (layer != null) layer.gameObject.SetActive(false);
    }

    void OnDestroy() { if (layer != null) Destroy(layer.gameObject); }

    // Not while Measure lays the text out itself: ForceMeshUpdate raises this same event, so every text with a key in it
    // was marked dirty again by its own measuring and laid out afresh every frame for as long as it was on screen
    // (0.26 ms a frame with one quest line showing, 2026-11-16).
    bool measuring;
    void OnTextChanged(Object changed) { if (changed == text && !measuring) dirty = true; }

    void LateUpdate()
    {
        if (cap == null || text == null) return;
        // The text's rect can settle after its text was set (a layout group sizes a new row a frame later): measure again.
        if (textRect.rect.size != measuredSize) dirty = true;
        if (dirty) { dirty = false; measuredSize = textRect.rect.size; Measure(); }
        if (layer == null) return;
        Follow();
        float alpha = text.color.a * text.alpha;
        for (int i = 0; i < caps.Count; i++)
        {
            var c = capColour; c.a *= alpha;
            if (caps[i].color != c) caps[i].color = c;
        }
    }

    // Where each marked key's letters are, one rect per line it covers, from the text's own layout.
    void Measure()
    {
        rects.Clear();
        if (text.text == null || text.text.IndexOf("<link", System.StringComparison.Ordinal) < 0) { Show(); return; }
        measuring = true;
        try { text.ForceMeshUpdate(); } finally { measuring = false; }
        var info = text.textInfo;
        for (int l = 0; l < info.linkCount; l++)
        {
            var link = info.linkInfo[l];
            if (link.GetLinkID() != LinkId) continue;
            int line = -1; float xMin = 0, xMax = 0, yMin = 0, yMax = 0;
            for (int c = link.linkTextfirstCharacterIndex; c < link.linkTextfirstCharacterIndex + link.linkTextLength && c < info.characterCount; c++)
            {
                var ch = info.characterInfo[c];
                if (!ch.isVisible) continue;
                // From the letters themselves (their ink, the baseline under them): the font's line metrics leave room above and below.
                float top = ch.topRight.y, bottom = Mathf.Min(ch.baseLine, ch.bottomLeft.y);
                if (ch.lineNumber != line)
                {
                    if (line >= 0) rects.Add(Rect.MinMaxRect(xMin - padding.x, yMin - padding.y, xMax + padding.x, yMax + padding.y));
                    line = ch.lineNumber; xMin = ch.bottomLeft.x; xMax = ch.topRight.x; yMin = bottom; yMax = top;
                }
                else { xMin = Mathf.Min(xMin, ch.bottomLeft.x); xMax = Mathf.Max(xMax, ch.topRight.x); yMin = Mathf.Min(yMin, bottom); yMax = Mathf.Max(yMax, top); }
            }
            if (line >= 0) rects.Add(Rect.MinMaxRect(xMin - padding.x, yMin - padding.y, xMax + padding.x, yMax + padding.y));
        }
        Show();
    }

    void Show()
    {
        if (rects.Count > 0 && layer == null) MakeLayer();
        if (layer == null) return;
        while (caps.Count < rects.Count)
        {
            var go = new GameObject("Key cap", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(layer, false);
            var img = go.GetComponent<Image>();
            img.sprite = cap; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1f / borderScale;
            img.raycastTarget = false;
            caps.Add(img);
        }
        for (int i = 0; i < caps.Count; i++)
        {
            bool on = i < rects.Count;
            if (caps[i].gameObject.activeSelf != on) caps[i].gameObject.SetActive(on);
            if (!on) continue;
            var r = (RectTransform)caps[i].transform;
            // Anchored on the layer's pivot, which is the text's: TMP lays its characters out from there.
            r.anchorMin = r.anchorMax = layer.pivot; r.pivot = new Vector2(.5f, .5f);
            // Whole canvas units, so the cap's pixel border stays crisp. Never narrower than it is tall: an "I" or a "1"
            // is a square key with its letter in the middle, not a thin bar (Ciri, 2026-11-16).
            r.anchoredPosition = new Vector2(Mathf.Round(rects[i].center.x), Mathf.Round(rects[i].center.y));
            r.sizeDelta = new Vector2(Mathf.Round(Mathf.Max(rects[i].width, rects[i].height)), Mathf.Round(rects[i].height));
        }
    }

    void MakeLayer()
    {
        var go = new GameObject(name + " key caps", typeof(RectTransform), typeof(LayoutElement));
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        layer = (RectTransform)go.transform;
        layer.SetParent(textRect.parent, false);
        Follow();
    }

    // The layer stands exactly where the text stands, just before it in the hierarchy so the caps draw under it. The text's
    // pivot is the caps' origin: TMP lays its characters out in the rect's local space, centred on the pivot's frame.
    void Follow()
    {
        if (layer.parent != textRect.parent) layer.SetParent(textRect.parent, false);
        int at = textRect.GetSiblingIndex(), me = layer.GetSiblingIndex();
        if (me != at - 1) layer.SetSiblingIndex(me < at ? at - 1 : at);
        layer.anchorMin = textRect.anchorMin; layer.anchorMax = textRect.anchorMax; layer.pivot = textRect.pivot;
        layer.anchoredPosition = textRect.anchoredPosition; layer.sizeDelta = textRect.sizeDelta;
        layer.localRotation = textRect.localRotation; layer.localScale = textRect.localScale;
        if (layer.gameObject.activeSelf != textRect.gameObject.activeInHierarchy) layer.gameObject.SetActive(textRect.gameObject.activeInHierarchy);
    }
}
