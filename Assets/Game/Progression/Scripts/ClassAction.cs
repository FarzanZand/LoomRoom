using Sirenix.OdinInspector;
using UnityEngine;

// A class's own button: the slot to the right of the hotbar, used with the Class Action key (X by default). It shows
// only while the table player's class has one (AdventurerClass.action). One asset per action beside its class; each
// kind is a subclass that says what a press does (the Pianist's Mood toggles the dungeon's noir look). A toggle reports
// IsOn so the slot can show which way it stands.
public abstract class ClassAction : ScriptableObject
{
    public string displayName;
    [PreviewField(32), Tooltip("Shown in the class slot.")] public Sprite icon;
    [PreviewField(32), Tooltip("Shown instead while a toggle is off. Empty: the icon, dimmed.")] public Sprite iconOff;
    [TextArea(2, 4), Tooltip("One short, literal line, shown on the slot's tooltip.")] public string description;

    // A toggle (Mood) shows on and off; a one-shot action is always on.
    public virtual bool IsToggle => false;
    public virtual bool IsOn(Player player) => true;
    // Whether it can be used right now (a reason for the feed when not).
    public virtual bool CanUse(Player player, out string reason) { reason = null; return true; }
    // A press of the key or a click on the slot. Raises Changed so the slot redraws.
    public abstract void Use(Player player);

    public static event System.Action<ClassAction> Changed;
    protected void RaiseChanged() => Changed?.Invoke(this);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => Changed = null;
}
