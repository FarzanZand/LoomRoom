using System;
using System.Collections.Generic;
using UnityEngine;

// Where a carried room item is put down (the user, 2026-11-14: "the player sees the dice, and other future props, get
// placed"). While the item is carried a faint ghost of its props lies exactly where they will lie, in the quest
// outline's colour and wearing that outline (QuestGlow), breathing in step with it, so every place in the room points the
// same way (the user, 2026-11-16); using that ghost puts them down, anywhere else does nothing. Like every room
// outline it shows only while the room player is the one played. Made at run time
// by the item's own script (TableDice): RoomPlaceSpot.Make(owner, props, carried, place, prompt). The props are the
// objects shown once the item is placed, set by hand where they will lie and switched off until then; the ghost is a copy
// of them drawn with their own textures (so its pixels are theirs) in Resources/Place ghost's pale, see-through look.
// (The old quest outline was not used: round small things like dice its shells stood out as big flaps. The outline
// drawn on the screen has no shells.) Nothing to set up by hand.
public class RoomPlaceSpot : MonoBehaviour, IInteractable
{
    Func<bool> carried;
    Action<Character> place;
    string prompt;
    GameObject ghost;
    QuestGlow outline;
    [Min(0f), Tooltip("How strong the ghost's outline is against the usual quest outline (1): the dice are small and lie on the lit table's edge.")]
    public float outlineStrength = 1.6f;
    readonly List<Material> ghostMaterials = new();
    Color ghostColour = Color.white;
    bool shown;

    // How far the use box reaches round the ghost by default, in room units (about 22 to the metre): small things are hard
    // to aim at. The owner can give its own (TableDice.aimMargin).
    public const float Margin = 2.5f;
    float margin = Margin;

    public string Prompt => prompt;
    public bool CanInteract(Character who) => who is Player p && p.kind == PlayerKind.Room && carried != null && carried();
    public void Interact(Character who) { if (CanInteract(who)) place?.Invoke(who); }

    // owner: the object the spot hangs under (it moves with it). props: what gets placed. margin: how far the use box
    // reaches past the props on every side, in room units.
    public static RoomPlaceSpot Make(Transform owner, GameObject[] props, Func<bool> carried, Action<Character> place, string prompt, float margin = Margin)
    {
        var go = new GameObject("Place spot") { layer = owner.gameObject.layer };
        go.transform.SetParent(owner, false);
        var spot = go.AddComponent<RoomPlaceSpot>();
        spot.carried = carried; spot.place = place; spot.prompt = prompt; spot.margin = Mathf.Max(0f, margin);
        spot.Build(props);
        return spot;
    }

    void Build(GameObject[] props)
    {
        var template = Resources.Load<Material>("Place ghost");
        if (template == null) Debug.LogWarning("[RoomPlaceSpot] Resources/Place ghost material is missing: the ghost uses the props' own look.");
        ghost = new GameObject("Ghost") { layer = gameObject.layer };
        ghost.transform.SetParent(transform, false);
        // Off while it is made, so nothing on the copies wakes before it is stripped.
        ghost.SetActive(false);
        var made = new Dictionary<Material, Material>();
        Bounds? world = null;
        foreach (var prop in props)
        {
            if (prop == null) continue;
            // A copy where the prop lies, stripped to its looks.
            var copy = Instantiate(prop, prop.transform.parent, false);
            copy.name = prop.name + " ghost";
            copy.transform.SetParent(ghost.transform, true);
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var b in copy.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(b);
            foreach (var l in copy.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (template != null)
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = Ghost(mats[i], template, made);
                    r.sharedMaterials = mats;
                }
                // Measured from the mesh: a switched-off renderer has no bounds yet.
                if (MeshBounds(r) is Bounds b) world = world == null ? b : Grow(world.Value, b);
            }
            copy.SetActive(true);
        }
        // The use box: round the ghost, in this object's own frame.
        if (world != null)
        {
            var b = world.Value;
            Bounds? local = null;
            for (int i = 0; i < 8; i++)
            {
                var corner = transform.InverseTransformPoint(new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y, i / 4 == 0 ? b.min.z : b.max.z));
                local = local == null ? new Bounds(corner, Vector3.zero) : Grow(local.Value, new Bounds(corner, Vector3.zero));
            }
            var s = transform.lossyScale;
            var box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = local.Value.center;
            box.size = local.Value.size + new Vector3(margin / Mathf.Max(.0001f, Mathf.Abs(s.x)), margin / Mathf.Max(.0001f, Mathf.Abs(s.y)), margin / Mathf.Max(.0001f, Mathf.Abs(s.z))) * 2f;
            gameObject.AddComponent<InteractableTrigger>();
        }
        ghostMaterials.AddRange(made.Values);
        // The quest outline round the ghost, and the ghost in the outline's own colour (as see-through as its template).
        outline = ghost.AddComponent<QuestGlow>();
        outline.seeThroughToo = true; outline.strength = outlineStrength;
        float alpha = template != null ? template.GetColor("_BaseColor").a : .5f;
        ghostColour = new Color(outline.colour.r, outline.colour.g, outline.colour.b, alpha);
    }

    static Bounds? MeshBounds(Renderer r)
    {
        var mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : r.TryGetComponent<MeshFilter>(out var f) ? f.sharedMesh : null;
        if (mesh == null) return null;
        var b = mesh.bounds; var m = r.transform.localToWorldMatrix;
        Bounds? world = null;
        for (int i = 0; i < 8; i++)
        {
            var p = m.MultiplyPoint3x4(new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y, i / 4 == 0 ? b.min.z : b.max.z));
            world = world == null ? new Bounds(p, Vector3.zero) : Grow(world.Value, new Bounds(p, Vector3.zero));
        }
        return world;
    }

    // The prop's own texture (same UVs, so the same pixel size), in the ghost's pale see-through colour.
    static Material Ghost(Material source, Material template, Dictionary<Material, Material> made)
    {
        if (source == null) return template;
        if (made.TryGetValue(source, out var m)) return m;
        m = new Material(template) { name = source.name + " (ghost)" };
        var tex = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
        if (tex != null)
        {
            m.SetTexture("_BaseMap", tex);
            if (source.HasProperty("_BaseMap")) { m.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap")); m.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap")); }
        }
        made[source] = m;
        return m;
    }

    static Bounds Grow(Bounds a, Bounds b) { a.Encapsulate(b); return a; }

    void Update()
    {
        // Only while the room player is the one played: from inside a dungeon the table's edge shows over open walls.
        bool on = carried != null && carried() && (!PlayerManager.HasInstance || PlayerManager.Instance.ActiveKind == PlayerKind.Room);
        if (on != shown)
        {
            shown = on;
            if (ghost != null) ghost.SetActive(on);
            if (outline != null) outline.Show(on);
        }
        if (!on) return;
        // Breathing in step with its outline (QuestGlow.Pulse): toward white and a little less see-through, then back.
        float k = QuestGlow.Pulse;
        var c = Color.Lerp(ghostColour, Color.white, .45f * k);
        c.a = Mathf.Lerp(ghostColour.a, Mathf.Min(1f, ghostColour.a + .25f), k);
        foreach (var m in ghostMaterials) if (m != null) m.SetColor("_BaseColor", c);
    }

    void OnDestroy() { foreach (var m in ghostMaterials) if (m != null) Destroy(m); }
}
