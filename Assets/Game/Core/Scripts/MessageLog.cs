using System;
using System.Collections.Generic;
using UnityEngine;

public enum MessageKind
{
    Info    = 0,
    Combat  = 1,
    Loot    = 2,
    Good    = 3,
    Bad     = 4,
    Warning = 5,
    Lore    = 6,
}

// The Barony-style text feed. Owns the history and the colours; MessageLogUI only
// draws it. Most lines come from here too: the log listens to the table player's
// hits, deaths and pickups and narrates them, so combat code never formats text.
// Anything else posts with MessageLog.Post("...").
public class MessageLog : Singleton<MessageLog>
{
    [Serializable]
    public class KindColor
    {
        public MessageKind kind;
        public Color color = Color.white;
    }

    [SerializeField, Min(8)] int historySize = 60;
    [Tooltip("Narrate the table player's hits, blocks, kills and pickups.")]
    [SerializeField] bool narrateCombat = true;
    [Tooltip("Collapse repeats of the same line posted within this many seconds into \"line (x2)\".")]
    [SerializeField, Min(0)] float repeatWindow = 1.5f;
    [SerializeField] List<KindColor> colors = new()
    {
        new KindColor { kind = MessageKind.Info,    color = new Color(.86f, .82f, .72f) },
        new KindColor { kind = MessageKind.Combat,  color = new Color(.95f, .93f, .88f) },
        new KindColor { kind = MessageKind.Loot,    color = new Color(1f, .85f, .4f) },
        new KindColor { kind = MessageKind.Good,    color = new Color(.55f, .9f, .6f) },
        new KindColor { kind = MessageKind.Bad,     color = new Color(1f, .45f, .38f) },
        new KindColor { kind = MessageKind.Warning, color = new Color(1f, .68f, .3f) },
        new KindColor { kind = MessageKind.Lore,    color = new Color(.65f, .78f, .95f) },
    };

    public struct Entry
    {
        public string text;
        public MessageKind kind;
        public float time;   // unscaled
        public int repeats;
    }

    readonly List<Entry> history = new();
    public IReadOnlyList<Entry> History => history;
    public event Action Changed;

    Player tablePlayer;

    public static void Post(string text, MessageKind kind = MessageKind.Info)
    {
        if (HasInstance) Instance.Add(text, kind);
    }

    public void Add(string text, MessageKind kind = MessageKind.Info)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        float now = Time.unscaledTime;
        if (history.Count > 0)
        {
            var last = history[^1];
            if (last.text == text && now - last.time <= repeatWindow)
            {
                last.repeats++;
                last.time = now;
                history[^1] = last;
                Changed?.Invoke();
                return;
            }
        }
        history.Add(new Entry { text = text, kind = kind, time = now, repeats = 1 });
        if (history.Count > historySize) history.RemoveAt(0);
        Changed?.Invoke();
    }

    public void Clear()
    {
        history.Clear();
        Changed?.Invoke();
    }

    public Color ColorOf(MessageKind kind)
    {
        foreach (var c in colors) if (c.kind == kind) return c.color;
        return Color.white;
    }

    public static string Format(Entry e) => e.repeats > 1 ? $"{e.text} (x{e.repeats})" : e.text;

    // ── Narration ─────────────────────────────────────────────────────

    void Start()
    {
        if (!narrateCombat) return;
        if (PlayerManager.HasInstance) tablePlayer = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        if (tablePlayer != null)
        {
            tablePlayer.HitLanded += OnPlayerHitLanded;
            tablePlayer.Damaged   += OnPlayerDamaged;
            tablePlayer.Died      += OnPlayerDied;
        }
        Character.AnyDied += OnAnyDied;
        if (InventoryManager.HasInstance) InventoryManager.Instance.ItemPickedUp += OnPickedUp;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (tablePlayer != null)
        {
            tablePlayer.HitLanded -= OnPlayerHitLanded;
            tablePlayer.Damaged   -= OnPlayerDamaged;
            tablePlayer.Died      -= OnPlayerDied;
        }
        Character.AnyDied -= OnAnyDied;
        if (InventoryManager.HasInstance) InventoryManager.Instance.ItemPickedUp -= OnPickedUp;
    }

    static string The(Character c) => c == null ? "something" : "the " + c.DisplayName;
    static string TheCap(Character c) => c == null ? "Something" : "The " + c.DisplayName;
    static int Round(float amount) => Mathf.Max(0, Mathf.CeilToInt(amount));

    void OnPlayerHitLanded(DamageInfo info)
    {
        if (info.Target == null || info.Target is Player) return;
        if (info.Blocked) { Add($"{TheCap(info.Target)} blocks your attack.", MessageKind.Combat); return; }
        if (info.Amount <= 0f) { Add($"Your attack glances off {The(info.Target)}.", MessageKind.Combat); return; }
        string verb = info.Backstab ? "You backstab" : info.Critical ? "Critical hit! You strike" : info.Heavy ? "You smash" : "You hit";
        Add($"{verb} {The(info.Target)} for {Round(info.Amount)}.", info.Backstab || info.Critical ? MessageKind.Warning : MessageKind.Combat);
    }

    void OnPlayerDamaged(DamageInfo info)
    {
        if (info.Blocked) { Add($"You block {The(info.Source)}'s attack.", MessageKind.Combat); return; }
        if (info.Amount <= 0f) return;
        string source = info.Source != null ? TheCap(info.Source) + " hits you" : "You take";
        Add($"{source} for {Round(info.Amount)}.", MessageKind.Bad);
    }

    void OnPlayerDied() => Add("You die...", MessageKind.Bad);

    void OnAnyDied(Character c)
    {
        if (c == null || c is Player || tablePlayer == null || !tablePlayer.IsActive) return;
        Add($"You destroy {The(c)}.", MessageKind.Good);
    }

    void OnPickedUp(ItemData item, Player who, int count)
    {
        if (item == null || who == null || who.kind != PlayerKind.Table) return;
        Add(count > 1 ? $"You pick up {count} {item.itemName}." : $"You pick up {item.itemName}.", MessageKind.Loot);
    }
}
