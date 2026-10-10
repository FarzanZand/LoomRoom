using TMPro;
using UnityEngine;

// A bedroom clock (the wall clock's hands, the bedside radio's digits). It shows the minute the
// player always wakes at and moves on one minute for every floor of the deepest descent, so the
// best run can be read off the wall (floor 20 is 7:32).
public class RoomClock : MonoBehaviour, IInteractable
{
    [Tooltip("Hand pivots. At their authored rotation both point at twelve.")]
    public Transform hourHand, minuteHand;
    [Tooltip("Local axis the hands turn around. Flip its sign if they run backwards.")]
    public Vector3 handAxis = Vector3.forward;
    [Tooltip("Digital display, shown as 07:12.")]
    public TMP_Text display;
    [Range(0, 23)] public int startHour = 7;
    [Range(0, 59)] public int startMinute = 12;
    [Tooltip("Story flag that moves the clock, one minute per unit.")]
    public string progressFlag = "adventure.deepestFloor";
    [Min(0)] public int maxMinutes = 20;

    Quaternion hourBase, minuteBase;

    // Shown instead while set (the intro's night), in minutes after midnight; -1 for the usual morning.
    static int nightMinutes = -1;
    // Enter Play Mode without a domain reload keeps statics: back to the morning each session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetNight() => nightMinutes = -1;
    public static void SetNight(int hour, int minute)
    {
        nightMinutes = hour < 0 ? -1 : hour * 60 + minute;
        foreach (var clock in FindObjectsByType<RoomClock>()) clock.Refresh();
    }

    int TotalMinutes => nightMinutes >= 0 ? nightMinutes : startHour * 60 + startMinute
        + Mathf.Clamp(ProgressionManager.HasInstance ? ProgressionManager.Instance.GetFlag(progressFlag) : 0, 0, maxMinutes);
    public string TimeText { get { int t = TotalMinutes; int h = t / 60 % 12; return $"{(h == 0 ? 12 : h)}:{t % 60:00}"; } }
    public string Prompt => TimeText;

    void Awake()
    {
        if (hourHand != null) hourBase = hourHand.localRotation;
        if (minuteHand != null) minuteBase = minuteHand.localRotation;
    }

    void Start()
    {
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged += OnFlag;
        Refresh();
    }

    void OnDestroy() { if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged -= OnFlag; }

    void OnFlag(string key, int value) { if (key == progressFlag) Refresh(); }

    void Refresh()
    {
        int t = TotalMinutes;
        if (minuteHand != null) minuteHand.localRotation = minuteBase * Quaternion.AngleAxis(t % 60 * 6f, handAxis);
        if (hourHand != null) hourHand.localRotation = hourBase * Quaternion.AngleAxis(t % 720 * .5f, handAxis);
        if (display != null) display.text = $"{t / 60 % 24:00}:{t % 60:00}";
    }

    public bool CanInteract(Character who) => who is Player player && player.kind == PlayerKind.Room;

    public void Interact(Character who)
    {
        if (CanInteract(who)) MessageLog.Post($"It's {TimeText}.", MessageKind.Lore);
    }
}
