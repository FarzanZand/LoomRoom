using UnityEngine;

// Search the shelves: a scrap of lore in the message log, and sometimes a find.
public class DungeonBookshelf : MonoBehaviour, IInteractable
{
    [TextArea(1, 3)]
    public string[] lore =
    {
        "A brittle ledger lists the names of the interred. Many are crossed out twice.",
        "\"Do not wake the lord beneath. He does not sleep; he waits.\"",
        "A child's primer. Every picture of the sun has been scratched out.",
        "Accounts of the sewer works. Page after page of men lost to the dark water.",
        "A miner's journal ends mid-sentence: \"The tunnel sings back when we strike it, and tonight—\"",
        "A prayer to the Weaver, asking that the threads of the dead stay cut.",
        "Recipes for preserving meat. Some of the ingredients are not meat.",
        "A map of these halls, drawn by someone who clearly never found the way out.",
    };
    [Range(0, 1)] public float findChance = .35f;
    [Tooltip("Rolled as a Chest reward on a find. Empty uses the floor's chest table.")]
    public DungeonLootTable loot;
    public AudioClip searchClip;
    [Range(0, 1)] public float searchVolume = .8f;

    [HideInInspector] public int seed;
    [HideInInspector] public int floorNumber = 1;
    [HideInInspector] public DungeonLootTable fallbackLoot;

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
        var items = table.RollDrops(rng, floorNumber, DungeonLootSource.Chest);
        int gold = table.RollGold(rng, floorNumber, DungeonLootSource.Chest);
        if ((items == null || items.Count == 0) && gold <= 0) { MessageLog.Post("You find nothing else of use.", MessageKind.Info); return; }
        MessageLog.Post("Something was tucked behind the books.", MessageKind.Loot);
        DungeonLootDrop.Spill(items, gold, transform.position + transform.forward * .9f, transform.parent);
    }
}
