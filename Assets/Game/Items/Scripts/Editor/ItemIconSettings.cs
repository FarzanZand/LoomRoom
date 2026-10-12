using System.Collections.Generic;
using UnityEngine;

// Editor-only settings for generated item icons (ItemIconRenderer): pixel size, lights, outline, and where worn
// gear comes from. Synty characters wear the armour we have no models for, so body armour, gloves, boots and
// leggings are cut out of an outfit (its restyled skinned mesh) and helms are restyled helmet meshes.
public class ItemIconSettings : ScriptableObject
{
    public const string Path = "Assets/Game/Items/Icons/Item Icon Settings.asset";

    [System.Serializable]
    public class Outfit
    {
        public string name;
        [Tooltip("A Synty character prefab with the renderer below (it supplies the bones).")]
        public GameObject prefab;
        [Tooltip("Name of the SkinnedMeshRenderer in that prefab.")]
        public string renderer;
        [Tooltip("The restyled (Synty Pixel) skinned mesh and its material.")]
        public Mesh mesh;
        public Material material;
    }

    [System.Serializable]
    public class TierLook
    {
        [Range(0, 4)] public int tier;
        [Tooltip("Outfit name for body armour, gloves, boots and leggings of this tier.")]
        public string outfit;
        [Tooltip("Helm of this tier: a prefab, or a mesh and material.")]
        public GameObject helmPrefab;
        public Mesh helmMesh;
        public Material helmMaterial;
        [Tooltip("Multiplies the icon's colours (white keeps them).")]
        public Color tint = Color.white;
    }

    [Header("Pixels")]
    [Tooltip("Icon pixels per bag cell. Bag cells are 88 px at 1080p: 88 draws the icon 1:1, so the painted texels show as they do on the model.")]
    [Min(8)] public int cellPixels = 88;
    [Tooltip("Rendered this many times larger, then averaged down.")]
    [Range(1, 12)] public int supersample = 4;
    [Tooltip("Coverage a pixel needs to be part of the item (hard edges only).")]
    [Range(.1f, .9f)] public float alphaCut = .45f;
    [Tooltip("Free pixels between the item and the icon's edge (the outline takes one).")]
    [Range(1, 4)] public int margin = 2;
    [Tooltip("A one pixel outline around the item.")]
    public bool drawOutline;
    public Color outline = new(.07f, .05f, .04f, 1f);
    [Tooltip("Edge pixels keep their coverage as transparency (smooth edges) instead of being cut hard at Alpha Cut.")]
    public bool softEdges = true;
    public enum Sampling { Dominant, Average }
    [Tooltip("Dominant: each pixel takes the colour most of its block shares, so texels stay crisp. Average: the block's mean, softer.")]
    public Sampling sampling = Sampling.Dominant;
    [Tooltip("Pushes each pixel away from its neighbours' average: crisper edges between colours.")]
    [Range(0, 1.5f)] public float sharpen;

    [Header("Light")]
    public Vector3 keyAngles = new(17, 17, 0);
    public Color keyColor = new(1f, .95f, .85f);
    public float keyIntensity = 1.1f;
    public Vector3 fillAngles = new(-10, -25, 0);
    public Color fillColor = new(.7f, .8f, 1f);
    public float fillIntensity = .35f;

    [Header("Worn gear")]
    public List<Outfit> outfits = new();
    public List<TierLook> tiers = new();

    public Outfit FindOutfit(string name) => string.IsNullOrEmpty(name) ? null : outfits.Find(o => o != null && o.name == name);
    // The look for a tier; tier 0 (hand-placed items) and unknown tiers use the nearest one.
    public TierLook Look(int tier)
    {
        TierLook best = null;
        foreach (var look in tiers)
            if (look != null && (best == null || Mathf.Abs(look.tier - tier) < Mathf.Abs(best.tier - tier) || look.tier == tier)) best = look;
        return best;
    }
}
