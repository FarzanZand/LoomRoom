using System;
using UnityEngine;
using UnityEngine.UI;

// One HUD image in both HUD looks (UIManager.hudStyle): its sprite, slicing, pixel scale, material and, when set, its
// colour for Pixel and for Rugged. Applied when it is enabled and whenever the UIManager changes, also outside Play mode.
// Set up by Tools > LoomRoom > UI > Set Up HUD Styles (HudStyleSetup); edit either look here by hand.
[ExecuteAlways, RequireComponent(typeof(Image))]
public class HudStyleImage : MonoBehaviour
{
    [Serializable]
    public class Look
    {
        public Sprite sprite;
        public Image.Type type = Image.Type.Sliced;
        [Min(.01f)] public float pixelsPerUnitMultiplier = 1f;
        [Tooltip("Empty: the default UI material.")] public Material material;
        [Tooltip("Off: the colour is left to the code or component that sets it (fills, slot backgrounds).")] public bool setColor;
        public Color color = Color.white;
    }

    public Look pixel = new();
    public Look rugged = new();

    Image image;
    UIManager listening;

    void OnEnable()
    {
        image = GetComponent<Image>();
        if (UIManager.HasInstance) { listening = UIManager.Instance; listening.Changed += Apply; }
        Apply();
    }

    void OnDisable()
    {
        if (listening != null) listening.Changed -= Apply;
        listening = null;
    }

    public void Apply()
    {
        if (image == null) image = GetComponent<Image>();
        if (image == null) return;
        var look = UIManager.CurrentHudStyle == HudStyle.Rugged ? rugged : pixel;
        image.sprite = look.sprite;
        image.type = look.type;
        image.pixelsPerUnitMultiplier = look.pixelsPerUnitMultiplier;
        image.material = look.material;
        if (look.setColor) image.color = look.color;
    }
}
