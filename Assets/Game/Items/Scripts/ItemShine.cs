using UnityEngine;

// Authored on the model prefab; uses per-renderer properties without material instances.
public class ItemShine : MonoBehaviour
{
    public Renderer[] surfaces;
    public Transform glint;
    Vector3 glintScale;
    [ColorUsage(false, true)] public Color shineColor = new Color(.22f, .17f, .08f);
    [Min(.1f)] public float cycleSeconds = 3;
    [Range(0, 1)] public float strength = .3f;
    MaterialPropertyBlock properties;
    void Awake(){properties = new MaterialPropertyBlock();if(glint!=null)glintScale=glint.localScale;}
    void Update()
    {
        float pulse = Mathf.Pow(Mathf.Max(0, Mathf.Sin(Time.time * Mathf.PI * 2 / cycleSeconds)), 8);
        if(glint!=null)glint.localScale=glintScale*(.15f+pulse);
        if(surfaces==null)return;
        foreach (var surface in surfaces)
        {
            if (surface == null) continue;
            surface.GetPropertyBlock(properties);
            properties.SetColor("_EmissionColor", shineColor * (strength * pulse));
            surface.SetPropertyBlock(properties);
        }
    }
}
