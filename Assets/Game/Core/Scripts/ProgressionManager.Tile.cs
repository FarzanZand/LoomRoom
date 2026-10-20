using Sirenix.OdinInspector;
using UnityEngine;

// The room puzzle's missing tile (AdventureRoomPuzzle): every chest the table player opens after the starting room's
// holds it (DungeonContainer, TileDueFromChest) until it is in their bag or hotbar; carried out of the dungeon it comes
// home as a key item (the right edge's icons), the quest log asks the player to put it in the puzzle, and once placed
// the puzzle can be solved. Flag puzzle.tile: 0 not found, 1 carried, 2 placed.
public partial class ProgressionManager
{
    public const string TileFlag = "puzzle.tile", TileQuest = "placeTile";

    [FoldoutGroup("Missing tile"), Tooltip("The tile (a Key item, purple) the dungeon hides. Empty: the puzzle has no missing piece.")]
    public ItemData missingTile;

    public int TileState => GetFlag(TileFlag);

    // Whether a chest opening now holds the tile: any chest but the starting room's, in a real run (not the intro's
    // practice), until the tile is in the table player's bag or hotbar, carried home or placed.
    public bool TileDueFromChest(bool startingChest)
    {
        if (missingTile == null || startingChest || !MetaActive || TileState != 0 || !PlayerManager.HasInstance) return false;
        var player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        if (player == null) return false;
        foreach (var inv in new[] { player.Bag, player.Hotbar })
            if (inv != null) for (int i = 0; i < inv.SlotCount; i++) if (inv.ItemAt(i) == missingTile) return false;
        return true;
    }

    // A tile still lying unpicked (from an earlier chest) goes as the next one comes out, so only one is ever about.
    public void ClearLooseTiles()
    {
        if (missingTile == null) return;
        foreach (var w in FindObjectsByType<WorldItem>()) if (w != null && w.Item == missingTile) Destroy(w.gameObject);
    }

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
