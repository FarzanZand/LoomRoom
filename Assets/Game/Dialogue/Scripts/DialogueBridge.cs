using PixelCrushers.DialogueSystem;
using UnityEngine;

// The only place game code talks to Pixel Crushers. Pushes the Dialogue game state
// for the length of a conversation and exposes a few Lua functions so conversations
// can hand out items and set flags:
//   GiveItem("Knife")    HasItem("Knife")    SetFlag("story.metDM", 1)    GetFlag("story.metDM")
// Sits on the Dialogue Manager object.
public class DialogueBridge : MonoBehaviour
{
    NpcBrain talkingNpc;
    // Subscribed to the Dialogue Manager's conversation events; pushedDialogue: this bridge holds a Dialogue push.
    bool subscribed, pushedDialogue;

    void OnEnable()
    {
        Subscribe();
        Lua.RegisterFunction("GiveItem", this, SymbolExtensions.GetMethodInfo(() => GiveItem(string.Empty)));
        Lua.RegisterFunction("HasItem",  this, SymbolExtensions.GetMethodInfo(() => HasItem(string.Empty)));
        Lua.RegisterFunction("SetFlag",  this, SymbolExtensions.GetMethodInfo(() => SetFlag(string.Empty, 0d)));
        Lua.RegisterFunction("GetFlag",  this, SymbolExtensions.GetMethodInfo(() => GetFlag(string.Empty)));
        Lua.RegisterFunction("DMSay",    this, SymbolExtensions.GetMethodInfo(() => DMSay(string.Empty, false)));
    }

    // The Dialogue Manager may not be initialised at OnEnable (component order); try again here.
    void Start() => Subscribe();

    // A conversation can still start before Start if another component's Awake/OnEnable begins one.
    void Update() { if (!subscribed) Subscribe(); }

    void Subscribe()
    {
        if (subscribed || !DialogueManager.hasInstance) return;
        DialogueManager.instance.conversationStarted += OnConversationStarted;
        DialogueManager.instance.conversationEnded   += OnConversationEnded;
        subscribed = true;
        // Already talking (it started before we could listen): own the state for it now.
        if (DialogueManager.isConversationActive) OnConversationStarted(null);
    }

    void OnDisable()
    {
        if (subscribed && DialogueManager.hasInstance)
        {
            DialogueManager.instance.conversationStarted -= OnConversationStarted;
            DialogueManager.instance.conversationEnded   -= OnConversationEnded;
        }
        subscribed = false;
        // Disabled mid-conversation: give the Dialogue state back.
        PopDialogue();
        Lua.UnregisterFunction("GiveItem");
        Lua.UnregisterFunction("HasItem");
        Lua.UnregisterFunction("SetFlag");
        Lua.UnregisterFunction("GetFlag");
        Lua.UnregisterFunction("DMSay");
    }

    // Lua: DMSay("Mind the stairs.", true) shows a Dungeon Master line, mumbling if true.
    void DMSay(string text, bool mumble) => DungeonMaster.Say(text, (AudioClip)null, mumble);

    void OnConversationStarted(Transform actor)
    {
        if (!pushedDialogue && GameManager.HasInstance) { GameManager.Instance.Push(GameState.Dialogue); pushedDialogue = true; }
        // Pixel Crushers passes the actor (the player); the NPC is the conversant.
        var conversant = DialogueManager.currentConversant;
        talkingNpc = conversant != null ? conversant.GetComponentInParent<NpcBrain>() : null;
    }

    // Sent by Pixel Crushers before each line is shown: NPC lines are quoted, like the DM's feed.
    void OnConversationLine(Subtitle subtitle)
    {
        if (subtitle == null || subtitle.speakerInfo == null || subtitle.speakerInfo.isPlayer) return;
        var text = subtitle.formattedText.text;
        if (!string.IsNullOrWhiteSpace(text) && !text.StartsWith("\"")) subtitle.formattedText.text = "\"" + text + "\"";
    }

    void OnConversationEnded(Transform actor)
    {
        PopDialogue();
        var npc = talkingNpc;
        talkingNpc = null;
        if (npc != null) npc.EndInteraction();
    }

    void PopDialogue()
    {
        if (!pushedDialogue) return;
        pushedDialogue = false;
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dialogue);
    }

    public static bool StartConversation(string title, Transform actor, Transform conversant)
    {
        if (string.IsNullOrEmpty(title) || !DialogueManager.hasInstance) return false;
        if (DialogueManager.isConversationActive) return false;
        DialogueManager.StartConversation(title, actor, conversant);
        return true;
    }

    // ── Lua ───────────────────────────────────────────────────────────

    public bool GiveItem(string itemName)
    {
        if (!InventoryManager.HasInstance || !PlayerManager.HasInstance) return false;
        var item = InventoryManager.Instance.FindItem(itemName);
        if (item == null) { Debug.LogWarning($"[DialogueBridge] GiveItem: no item named '{itemName}' in the catalog."); return false; }
        return InventoryManager.Instance.Pickup(item, PlayerManager.Instance.Active);
    }

    public bool HasItem(string itemName)
    {
        if (!InventoryManager.HasInstance || !PlayerManager.HasInstance) return false;
        var item = InventoryManager.Instance.FindItem(itemName);
        var p = PlayerManager.Instance.Active;
        if (item == null || p == null) return false;
        // Honed copies count as the item they were made from ("Iron Sword" matches "Honed Iron Sword +1").
        var root = item.BaseItem;
        if (Holds(p.Bag, root) || Holds(p.Hotbar, root)) return true;
        if (p.Equipment != null)
            foreach (var worn in p.Equipment.EquippedItems)
                if (worn != null && worn.BaseItem == root) return true;
        return false;
    }

    static bool Holds(Inventory container, ItemData root)
    {
        if (container == null) return false;
        for (int i = 0; i < container.SlotCount; i++)
            if (container.ItemAt(i) is ItemData held && held.BaseItem == root) return true;
        return false;
    }

    public void SetFlag(string flag, double value)
    {
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(flag, (int)value);
    }

    public double GetFlag(string flag) =>
        ProgressionManager.HasInstance ? ProgressionManager.Instance.GetFlag(flag) : 0;
}
