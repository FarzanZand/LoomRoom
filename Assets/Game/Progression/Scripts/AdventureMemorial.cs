using UnityEngine;

// One spot on the memorial table in the room. When an adventurer dies, the most recent run takes
// spot 0, the one before it spot 1, and so on. The spot shows that class's miniature on its base;
// looking at it tells you who it was and how far they got.
public class AdventureMemorial : MonoBehaviour, IInteractable
{
    [Tooltip("Zero shows the most recent run.")]
    public int recentIndex;
    [Tooltip("Where the miniature stands (top of the base).")]
    public Transform figureAnchor;
    [Tooltip("Applied to every part of the miniature so it reads as an unpainted tabletop figure.")]
    public Material figureMaterial;
    public float figureScale = 3f;

    // A miniature was looked at closely (DungeonMasterRemarks notices).
    public static event System.Action<AdventureSave.Memorial> Inspected;

    AdventureSave save;
    AdventureSave.Memorial record;
    GameObject figure;
    string shownId;

    public string Prompt => record == null ? "" : Describe(record);

    void Start()
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        save = player != null ? player.GetComponent<AdventureSave>() : null;
        if (save != null) { save.Initialize(); save.Changed += Refresh; }
        Refresh();
    }

    void OnDestroy() { if (save != null) save.Changed -= Refresh; }

    void Refresh()
    {
        int index = save != null ? save.Saved.memorials.Count - 1 - recentIndex : -1;
        record = index >= 0 ? save.Saved.memorials[index] : null;
        if (record?.id == shownId) return;
        shownId = record?.id;
        if (figure != null) Destroy(figure);
        if (record == null) return;
        var cls = FindClass(record);
        if (cls == null || cls.miniature == null) return;
        var anchor = figureAnchor != null ? figureAnchor : transform;
        figure = Instantiate(cls.miniature, anchor);
        figure.transform.localPosition = Vector3.zero;
        figure.transform.localRotation = Quaternion.identity;
        figure.transform.localScale = Vector3.one * figureScale;
        foreach (var anim in figure.GetComponentsInChildren<Animator>()) anim.enabled = false;
        if (figureMaterial != null)
            foreach (var r in figure.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = figureMaterial;
                r.sharedMaterials = mats;
            }
    }

    static AdventurerClass FindClass(AdventureSave.Memorial record)
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        var rules = player != null ? player.GetComponent<AdventurerProgress>()?.rules : null;
        if (rules == null) return null;
        // Older records only kept the class name.
        return System.Array.Find(rules.classes, c => c != null && (c.id == record.classId || (string.IsNullOrEmpty(record.classId) && c.displayName == record.className)));
    }

    // A forgotten figure (from a new save) is just its class.
    static string Describe(AdventureSave.Memorial r) => r.forgotten ? r.className : $"{r.className}, level {r.level}, reached floor {r.floor}";

    public bool CanInteract(Character who) => record != null && who is Player player && player.kind == PlayerKind.Room;

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        string killer = record.forgotten || string.IsNullOrEmpty(record.killer) ? "" : $" Killed by {record.killer}.";
        MessageLog.Post($"{Describe(record)}.{killer}", MessageKind.Lore);
        Inspected?.Invoke(record);
    }
}
