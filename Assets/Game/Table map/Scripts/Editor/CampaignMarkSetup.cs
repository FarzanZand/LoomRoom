using TMPro;
using UnityEditor;
using UnityEngine;

// The yellow question mark that floats over a new place on the realm map (CampaignManager.Markers): Build() from an eval
// makes Resources/Campaign new place mark.prefab (safe to run again: it is made anew). The mark is the game's own font
// (PixelOperator-Bold) as world text, yellow with a dark edge so it reads over snow and over the dark land alike; it is
// unlit, so it shows as bright in the room's dark as in its light.
public static class CampaignMarkSetup
{
    const string Folder = "Assets/Game/Table map/Resources/";
    const string PrefabPath = Folder + "Campaign new place mark.prefab";
    const string MaterialPath = "Assets/Game/Table map/Map/Place art/New place mark.mat";
    // How tall the question mark stands, in the map's units: tall enough to be found from the room and from the raised
    // map view (at 2.8 it was ten pixels on a 720p screen and its dark edge swallowed the yellow).
    const float GlyphHeight = 6.5f;

    public static string Build()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        if (font == null) return "PixelOperator-Bold.asset is missing";

        // Its own copy of the font's material: a dark edge, drawn over nothing but what is in front of it.
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(MaterialPath));
            material = new Material(font.material) { name = "New place mark" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = font.material.shader;
        material.CopyPropertiesFromMaterial(font.material);
        material.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
        material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(.16f, .09f, .03f, 1f));
        material.SetFloat(ShaderUtilities.ID_OutlineWidth, .16f);
        material.SetFloat(ShaderUtilities.ID_FaceDilate, .12f);
        material.EnableKeyword("OUTLINE_ON");
        EditorUtility.SetDirty(material);

        var root = new GameObject("Campaign new place mark");
        var banner = root.AddComponent<CampaignBanner>();
        banner.bob = .3f; banner.period = 1.8f; banner.faceFully = true;
        var mark = new GameObject("Mark", typeof(RectTransform));
        mark.transform.SetParent(root.transform, false);
        var text = mark.AddComponent<TextMeshPro>();
        text.font = font; text.fontSharedMaterial = material;
        text.text = "?"; text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow;
        // A yellow held under full brightness: the room's tone mapping turns a brighter one cream (and an HDR one
        // white). Measured on screen this comes out as (196, 171, 89); the prompt's own FFD23F is drawn by the UI as it is.
        text.color = new Color(.85f, .72f, 0f, 1f);
        text.fontSize = 36f;
        text.rectTransform.sizeDelta = new Vector2(12f, 12f);
        text.ForceMeshUpdate();
        // Sized by the glyph itself, and stood on the root (its foot at the root's height, where the place's top is).
        float height = Mathf.Max(.01f, text.textBounds.size.y);
        text.fontSize *= GlyphHeight / height;
        text.ForceMeshUpdate();
        var bounds = text.textBounds;
        mark.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, 0f);
        var renderer = mark.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        return $"made {PrefabPath}: glyph {bounds.size.x:0.00} x {bounds.size.y:0.00} units, font size {text.fontSize:0.0}" + (prefab == null ? " (prefab not saved)" : "");
    }
}
