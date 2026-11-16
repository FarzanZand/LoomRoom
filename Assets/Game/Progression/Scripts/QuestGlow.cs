using System.Collections.Generic;
using UnityEngine;

// Marks a place while a quest points at it (the character sheet's table, the class desk, the locked box's table, a
// carried trophy's plaque, the dice cup, the place to start with on the map): a soft glow round its outline that
// breathes slowly, the way games mark what to go to. Added and switched by the place's own script; nothing to set up.
// The glow is drawn on the screen round the silhouette of the place's pieces (QuestOutlinePass, Quest Outline.shader):
// one soft edge of the same width at any distance, with nothing drawn inside the shape. Its look (width, strength,
// falloff) is the material Resources/Quest outline. History: until 2026-11-16 it was a hard bright line, a shell pushed
// out round each mesh (the user: "real ugly and sticks out too much in the room ... Especially ugly when the table or
// desk is highlighted"); a cone of light from the ceiling was tried in its place and thrown out the same day (the
// user: "You can do an outline, just don't do it as sharp and ugly").
public class QuestGlow : MonoBehaviour
{
    public Color colour = new(.45f, .8f, 1f, 1f);
    [Range(0f, .6f), Tooltip("How much it breathes: the share of its brightness that comes and goes.")]
    public float breath = .25f;
    [Min(0f), Tooltip("How strong this place's glow is against the usual (1). A small flat thing in a pool of lamp light (the character sheet) needs more to show.")]
    public float strength = 1f;

    const float FadeSeconds = .35f, BreathSeconds = 2.6f;

    readonly List<(Renderer renderer, int parts)> pieces = new();
    Material material;
    bool shown;
    float level, nextLook;

    public void Show(bool on)
    {
        shown = on;
        if (on && isActiveAndEnabled) QuestOutlinePass.Add(this);
    }

    void Update()
    {
        level = Mathf.MoveTowards(level, shown ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
        if (!shown && level <= 0f) QuestOutlinePass.Remove(this);
    }

    void OnEnable() { if (shown) QuestOutlinePass.Add(this); }
    void OnDisable() { QuestOutlinePass.Remove(this); level = 0f; }
    void OnDestroy() { QuestOutlinePass.Remove(this); if (material != null) Destroy(material); }

    // Called by the pass each frame it draws: this place's pieces, with the material that draws them into the mask in
    // its colour as bright as it is just now.
    public void Collect(List<(Renderer renderer, Material material, int parts)> draws)
    {
        if (level <= 0f) return;
        if (material == null)
        {
            var template = QuestOutlinePass.Template;
            if (template == null) return;
            material = new Material(template);
        }
        // Looked for again now and then: switched on while its object wasn't drawn yet (the table's build hides every
        // piece until it lands) it finds nothing at first, and a place's pieces can change (a trophy is hung).
        if (pieces.Count == 0 || Time.unscaledTime >= nextLook) { Look(); nextLook = Time.unscaledTime + .5f; }
        float k = .5f + .5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / BreathSeconds);
        var tint = colour * (strength * level * (1f - breath + breath * k));
        tint.a = 1f;
        material.SetColor("_Tint", tint);
        foreach (var (renderer, parts) in pieces)
            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy && !renderer.forceRenderingOff) draws.Add((renderer, material, parts));
    }

    // The glow goes round everything the place is made of, as one shape: a table with what stands on it, the desk with
    // its posters. (The old shells went only round its biggest pieces, since a shell round every brush and figure was
    // a tangle of lines; a glow round the whole silhouette has no lines inside it.) Only its solid pieces: a see-through
    // one (the map's floating question mark, an empty trophy's faint outline) is a flat card, and the glow went round
    // the card's square.
    void Look()
    {
        pieces.Clear();
        foreach (var r in GetComponentsInChildren<MeshRenderer>(false))
        {
            if (!r.enabled || !r.TryGetComponent<MeshFilter>(out var f) || f.sharedMesh == null) continue;
            var m = r.sharedMaterial;
            if (m == null || m.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent) continue;
            pieces.Add((r, f.sharedMesh.subMeshCount));
        }
    }
}
