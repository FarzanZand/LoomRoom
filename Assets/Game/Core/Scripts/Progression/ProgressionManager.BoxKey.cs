using Sirenix.OdinInspector;
using UnityEngine;

// The key to the locked box in the bedroom (DiceBox, on the coffee table by the painting): every chest the table player
// opens after the starting room's holds it (DungeonContainer, KeyDueFromChest) until it is in their bag or hotbar;
// carried out of the dungeon it comes home as a key item (the top row of icons), the quest log asks the player to open
// the box, and opening it gives the set of dice (ProgressionManager.Dice). Flag puzzle.tile: 0 not found, 1 carried,
// 2 used; flag puzzle.symbols: the box is open. Both keys are from the symbol puzzle the box replaced (2026-11-11, the
// user), which had a missing tile in the key's place, so a save that carried the tile home carries the key.
public partial class ProgressionManager
{
    public const string BoxKeyFlag = "puzzle.tile", BoxOpenedFlag = "puzzle.symbols", BoxKeyQuest = "placeTile";

    [FoldoutGroup("Bedroom key items"), PropertyOrder(40), LabelText("Key to the locked box (item)"), Tooltip("The key (a Key item) the dungeon hides in its chests; it opens the locked box in the bedroom. Empty: the box needs no key.")]
    [UnityEngine.Serialization.FormerlySerializedAs("missingTile")]
    public ItemData boxKey;

    public int BoxKeyState => GetFlag(BoxKeyFlag);
    public bool BoxOpened => HasFlag(BoxOpenedFlag);
    // The key is at home and the box still shut (a save from the symbol puzzle's time may hold 2, the tile put in but
    // the puzzle never solved: that counts as the key in hand).
    public bool BoxKeyReady => !BoxOpened && (boxKey == null || BoxKeyState >= 1);

    // Whether a chest opening now holds the key: any chest but the starting room's, in a real run (not the intro's
    // practice), until the key is in the table player's bag or hotbar, carried home or used.
    public bool KeyDueFromChest(bool startingChest)
    {
        if (boxKey == null || startingChest || !MetaActive || BoxKeyState != 0 || BoxOpened || !PlayerManager.HasInstance) return false;
        var player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        if (player == null) return false;
        foreach (var inv in new[] { player.Bag, player.Hotbar })
            if (inv != null) for (int i = 0; i < inv.SlotCount; i++) if (inv.ItemAt(i) == boxKey) return false;
        return true;
    }

    // A key still lying unpicked (from an earlier chest) goes as the next one comes out, so only one is ever about.
    public void ClearLooseKeys()
    {
        if (boxKey == null) return;
        foreach (var w in FindObjectsByType<WorldItem>()) if (w != null && w.Item == boxKey) Destroy(w.gameObject);
    }

    // At the run's end: a key in the bag or hotbar comes home (out of the bag, into the key items) with its quest.
    void CarryKeyHome(Player player)
    {
        if (boxKey == null || player == null || BoxKeyState != 0) return;
        bool found = false;
        foreach (var inv in new[] { player.Bag, player.Hotbar })
            if (inv != null) for (int i = 0; i < inv.SlotCount; i++) if (inv.ItemAt(i) == boxKey) { inv.RemoveAt(i); found = true; }
        if (!found) return;
        SetFlag(BoxKeyFlag, 1);
        GiveQuest(BoxKeyQuest);
    }

    // The dev console's addroomitem: the key carried home (and the box shut again if it was open).
    public bool CarryKey()
    {
        if (boxKey == null) return false;
        SetFlag(BoxOpenedFlag, 0);
        SetFlag(BoxKeyFlag, 1);
        if (QuestState(BoxKeyQuest) != 1) SetFlag(QuestFlag(BoxKeyQuest), 1);
        return true;
    }

    // The box is opened with the key: the key is spent, the box stays open. False when it can't be opened yet.
    public bool OpenBox()
    {
        if (!BoxKeyReady) return false;
        SetFlag(BoxKeyFlag, 2);
        SetFlag(BoxOpenedFlag);
        CompleteQuest(BoxKeyQuest);
        return true;
    }
}
