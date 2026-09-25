using PixelCrushers.DialogueSystem;
using UnityEngine;

// The only place game code talks to Pixel Crushers. Pushes the Dialogue game state
// for the length of a conversation and exposes a few Lua functions so conversations
// can hand out items and set flags:
//   GiveItem("Knife")    HasItem("Knife")    SetFlag("metDM", 1)    GetFlag("metDM")
// Sits on the Dialogue Manager object.
public class DialogueBridge : MonoBehaviour
{
    NpcBrain talkingNpc;

    void OnEnable()
    {
        if (DialogueManager.hasInstance)
        {
            DialogueManager.instance.conversationStarted += OnConversationStarted;
            DialogueManager.instance.conversationEnded   += OnConversationEnded;
        }
        Lua.RegisterFunction("GiveItem", this, SymbolExtensions.GetMethodInfo(() => GiveItem(string.Empty)));
        Lua.RegisterFunction("HasItem",  this, SymbolExtensions.GetMethodInfo(() => HasItem(string.Empty)));
        Lua.RegisterFunction("SetFlag",  this, SymbolExtensions.GetMethodInfo(() => SetFlag(string.Empty, 0d)));
        Lua.RegisterFunction("GetFlag",  this, SymbolExtensions.GetMethodInfo(() => GetFlag(string.Empty)));
    }

    void OnDisable()
    {
        if (DialogueManager.hasInstance)
        {
            DialogueManager.instance.conversationStarted -= OnConversationStarted;
            DialogueManager.instance.conversationEnded   -= OnConversationEnded;
        }
        Lua.UnregisterFunction("GiveItem");
        Lua.UnregisterFunction("HasItem");
        Lua.UnregisterFunction("SetFlag");
        Lua.UnregisterFunction("GetFlag");
    }

    void OnConversationStarted(Transform actor)
    {
        if (GameManager.HasInstance) GameManager.Instance.Push(GameState.Dialogue);
        // Pixel Crushers passes the actor (the player); the NPC is the conversant.
        var conversant = DialogueManager.currentConversant;
        talkingNpc = conversant != null ? conversant.GetComponentInParent<NpcBrain>() : null;
    }

    void OnConversationEnded(Transform actor)
    {
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dialogue);
        var npc = talkingNpc;
        talkingNpc = null;
        if (npc != null) npc.EndInteraction();
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
        return (p.Bag != null && p.Bag.IndexOf(item) >= 0)
            || (p.Hotbar != null && p.Hotbar.IndexOf(item) >= 0)
            || (p.Equipment != null && p.Equipment.IsEquipped(item));
    }

    public void SetFlag(string flag, double value)
    {
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(flag, (int)value);
    }

    public double GetFlag(string flag) =>
        ProgressionManager.HasInstance ? ProgressionManager.Instance.GetFlag(flag) : 0;
}
