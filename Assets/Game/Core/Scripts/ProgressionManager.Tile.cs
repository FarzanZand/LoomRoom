using Sirenix.OdinInspector;
using UnityEngine;

// The room puzzle's missing tile (AdventureRoomPuzzle): from the second run on it lies somewhere on tileFloor until
// found; carried out of the dungeon it comes home as a key item (the right edge's icons), the quest log asks the player
// to put it in the puzzle, and once placed the puzzle can be solved. Flag puzzle.tile: 0 not found, 1 carried, 2 placed.
public partial class ProgressionManager
{
    public const string TileFlag = "puzzle.tile", TileQuest = "placeTile";

    [FoldoutGroup("Missing tile"), Tooltip("The tile (a Key item, purple) the dungeon hides. Empty: the puzzle has no missing piece.")]
    public ItemData missingTile;
    [FoldoutGroup("Missing tile"), Min(1), Tooltip("The floor of a run it lies on.")]
    public int tileFloor = 2;

    public int TileState => GetFlag(TileFlag);

    // Whether this floor hides the tile: from the second run on (the first run's end sets the lesson's flag), until found.
    public bool HidesTile(int floor) => missingTile != null && MetaActive && HasFlag(LedgerIntroFlag) && TileState == 0 && floor == tileFloor;

    // At the run's end: a tile in the bag or hotbar comes home (out of the bag, into the key items) with its quest.
    void CarryTileHome(Player player)
    {
        if (missingTile == null || player == null || TileState != 0) return;
        bool found = false;
        foreach (var inv in new[] { player.Bag, player.Hotbar })
            if (inv != null) for (int i = 0; i < inv.SlotCount; i++) if (inv.ItemAt(i) == missingTile) { inv.RemoveAt(i); found = true; }
        if (!found) return;
        SetFlag(TileFlag, 1);
        GiveQuest(TileQuest);
    }

    // Put into the puzzle.
    public void PlaceTile()
    {
        if (TileState != 1) return;
        SetFlag(TileFlag, 2);
        CompleteQuest(TileQuest);
    }
}
