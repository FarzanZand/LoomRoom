using UnityEngine;
using UnityEngine.UI;

// Sets how full a HUD bar is. A Filled image uses fillAmount; any other image (the pixel HUD bars,
// sliced so their top and bottom pixel rows stay crisp) is resized from the left by its right anchor.
// Such a fill should stretch inside its frame: anchors (0,0)-(1,1) with the frame's border as offsets.
public static class UIBar
{
    public static void Set(Image fill, float amount)
    {
        if (fill == null) return;
        amount = Mathf.Clamp01(amount);
        if (fill.type == Image.Type.Filled) { fill.fillAmount = amount; return; }
        var rt = fill.rectTransform;
        if (!Mathf.Approximately(rt.anchorMax.x, amount)) rt.anchorMax = new Vector2(amount, rt.anchorMax.y);
        fill.enabled = amount > 0f;
    }
}
