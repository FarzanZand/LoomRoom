using UnityEngine;
using UnityEngine.UI;

// Which UIManager colour an element uses. Serialized by integer: append only.
public enum UISlot
{
    Shadow = 0, Panel = 1, Frame = 2, FrameHighlight = 3, Text = 4, DimText = 5,
    Health = 10, Mana = 11, Stamina = 12, Experience = 13, Gold = 14, BossHealth = 15, RecentDamage = 16, EnemyHealth = 17,
}

// Opt-in: this Graphic (image, text, bar fill) takes its colour from the UIManager slot.
// Remove the component to set the colour by hand again.
[ExecuteAlways, RequireComponent(typeof(Graphic))]
public class UIColor : MonoBehaviour
{
    public UISlot slot = UISlot.Text;
    [Range(0, 1), Tooltip("Opacity of this element; the slot sets only the colour.")] public float alpha = 1f;

    Graphic graphic;
    UIManager manager;

    void OnEnable()
    {
        graphic = GetComponent<Graphic>();
        manager = UIManager.HasInstance ? UIManager.Instance : null;
        if (manager != null) manager.Changed += Apply;
        Apply();
    }

    void OnDisable()
    {
        if (manager != null) manager.Changed -= Apply;
        manager = null;
    }

    void OnValidate() { if (isActiveAndEnabled) Apply(); }

    void Apply()
    {
        if (graphic == null || manager == null) return;
        var c = manager.Get(slot); c.a = alpha;
        graphic.color = c;
    }
}
