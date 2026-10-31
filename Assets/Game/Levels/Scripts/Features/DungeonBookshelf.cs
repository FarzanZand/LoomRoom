using UnityEngine;

// Search the shelves: a scrap of lore in the message log, and sometimes a find.
public class DungeonBookshelf : MonoBehaviour, IInteractable
{
    [TextArea(1, 3)]
    public string[] lore =
    {
        "A ledger of the people buried here.",
        "A note asking for the rent.",
        "A child's reading book, swollen with damp.",
        "Accounts of the sewer works.",
        "A miner's journal, mostly about his feet.",
        "A prayer to the Weaver for the dead.",
        "A cookbook. Most of the recipes are for stew.",
        "An old map of these halls. It's wrong.",
    };
    [Range(0, 1)] public float findChance = .35f;
    [Tooltip("Rolled as a Chest reward on a find. Empty uses the floor's chest table.")]
    public LootSource loot;
    public AudioClip searchClip;
    [Range(0, 1)] public float searchVolume = .8f;

    [HideInInspector] public int seed;
    [HideInInspector] public int floorNumber = 1;
    [HideInInspector] public LootSource fallbackLoot;

    bool searched;

    public string Prompt => searched ? "Dusty shelves" : "Search the bookshelf";
    public bool CanInteract(Character who) => !searched && DungeonFeatureUtility.TablePlayer(who) != null;

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        searched = true;
        var rng = new System.Random(seed);
        if (AudioManager.HasInstance && searchClip != null) AudioManager.Instance.PlaySFX(searchClip, transform.position, searchVolume, .05f);
        if (lore != null && lore.Length > 0) MessageLog.Post(lore[rng.Next(lore.Length)], MessageKind.Lore);
        var table = loot != null ? loot : fallbackLoot;
        if (table == null || rng.NextDouble() >= findChance)
        {
            MessageLog.Post("You find nothing else of use.", MessageKind.Info);
            return;
        }
        var (items, gold) = DungeonLootDrop.Roll(table, rng, floorNumber, DungeonLootSource.Chest);
        if (items.Count == 0 && gold <= 0) { MessageLog.Post("You find nothing else of use.", MessageKind.Info); return; }
        MessageLog.Post("You find something behind the books.", MessageKind.Loot);
        DungeonLootDrop.Spill(items, gold, transform.position + transform.forward * .9f, transform.parent);
    }
}
