using System.Collections.Generic;
using UnityEngine;

// A blood altar (rest rooms, the Raised altar, the Shrine of Ashes): give it half your most health and it grants one
// blessing picked at random by weight: a point in an attribute or ranks in a skill for the rest of the run
// (AdventurerProgress.GrantPoint, GrantRanks, kept in the run's checkpoint), a Rare piece of this floor's gear, a heap of
// gold or a spell tome (both thrown out over the altar). It answers once, and refuses while you have no more than half
// your health left, so it can never kill you. Its prompt says the price before you use it, and the feed says what it
// took and what it gave.
public class DungeonAltar : MonoBehaviour, IInteractable
{
    public enum Blessing { Attribute = 0, Skill = 1, Gear = 2, Gold = 3, Tome = 4 }

    [System.Serializable]
    public class Weighted
    {
        public Blessing blessing;
        [Min(0)] public float weight = 1f;
    }

    [Range(.05f, .9f), Tooltip("Share of the player's most health the altar takes.")]
    public float bloodPrice = .5f;
    [Tooltip("What it can give and how often (one is picked at random by weight).")]
    public List<Weighted> blessings = new()
    {
        new() { blessing = Blessing.Attribute, weight = 3f }, new() { blessing = Blessing.Skill, weight = 2f },
        new() { blessing = Blessing.Gear, weight = 2f }, new() { blessing = Blessing.Gold, weight = 2f }, new() { blessing = Blessing.Tome, weight = 1f },
    };
    [Min(1), Tooltip("Skill: ranks given in one skill picked at random.")] public int skillRanks = 5;
    [Min(0), Tooltip("Gold: the heap on the first floor, and what each floor deeper adds.")] public int gold = 30;
    [Min(0)] public int goldPerFloor = 12;
    [Tooltip("Lit while the altar can still answer.")]
    public GameObject activeGlow;
    public AudioClip prayClip;
    [Range(0, 1)] public float prayVolume = .9f;

    [HideInInspector] public int floorNumber = 1;

    bool used;

    static readonly MetaAttribute[] Attributes = { MetaAttribute.Strength, MetaAttribute.Dexterity, MetaAttribute.Constitution, MetaAttribute.Intelligence };

    static Player Table => PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
    static int Price(Player player, float share) => player != null && player.Stats != null ? Mathf.Max(1, Mathf.CeilToInt(player.Stats.MaxHealth * share)) : 0;

    public string Prompt => used ? "The altar is silent" : $"Blood altar: give half your health ({Price(Table, bloodPrice)}) for a random blessing";
    public bool CanInteract(Character who) => DungeonFeatureUtility.TablePlayer(who) != null;

    public void Interact(Character who)
    {
        var player = DungeonFeatureUtility.TablePlayer(who);
        if (player == null) return;
        if (used) { MessageLog.Post("The altar has had its blood.", MessageKind.Info); return; }
        var progress = player.GetComponent<AdventurerProgress>();
        if (progress == null || !progress.InRun) { MessageLog.Post("The altar does not answer.", MessageKind.Info); return; }
        int price = Price(player, bloodPrice);
        int health = Mathf.CeilToInt(player.Stats.CurrentHealth);
        if (health <= price) { MessageLog.Post($"Too weak. The altar wants half your health ({price}) and you have {health}.", MessageKind.Warning); return; }
        used = true;
        player.Stats.TakeDamage(new DamageInfo { Amount = price, SourceName = "the altar", Magic = true, HitPoint = transform.position + Vector3.up });
        if (AudioManager.HasInstance)
        {
            if (prayClip != null) AudioManager.Instance.PlaySFX(prayClip, transform.position, prayVolume, .03f);
            AudioManager.Instance.PlaySkillUp();
        }
        MessageLog.Post($"The altar takes {price} health. {Bless(progress, Pick())}", MessageKind.Good);
        if (activeGlow != null) activeGlow.SetActive(false);
    }

    Blessing Pick()
    {
        float total = 0f;
        foreach (var b in blessings) if (b != null) total += b.weight;
        float roll = Random.value * total;
        foreach (var b in blessings) if (b != null && (roll -= b.weight) < 0f) return b.blessing;
        return Blessing.Attribute;
    }

    // Grants it and says what it gave. Gear or a tome the floor can't give becomes an attribute point.
    string Bless(AdventurerProgress progress, Blessing blessing)
    {
        int floor = Mathf.Max(1, floorNumber);
        var at = transform.position + Vector3.up;
        switch (blessing)
        {
            case Blessing.Skill:
            {
                var skill = AdventureSkills.All[Random.Range(0, AdventureSkills.All.Length)];
                int given = progress.GrantRanks(skill, skillRanks);
                if (given > 0) return $"+{given} {skill} (now {progress.Rank(skill)}).";
                break;
            }
            case Blessing.Gold:
            {
                int heap = Mathf.RoundToInt((gold + goldPerFloor * (floor - 1)) * InventoryManager.GoldScale);
                DungeonLootDrop.Spill(new List<LootSource.Drop>(), heap, transform.position, transform.parent, at);
                return $"It gives you {heap} gold.";
            }
            case Blessing.Gear:
            {
                // A piece of this floor's gear tiers (the level's Gear by floor), Rare.
                var row = LootSource.FloorGear?.Invoke(floor);
                int top = row != null ? row.HighestTier : Mathf.Clamp(1 + (floor - 1) / 2, 1, 12), low = row != null ? row.LowestTier : top;
                var gear = InventoryManager.GivenGear(ItemRarity.Rare, top, floor, new System.Random(Random.Range(0, int.MaxValue)), low);
                if (gear == null) break;
                DungeonLootDrop.Spill(new List<LootSource.Drop> { new(gear, 1) }, 0, transform.position, transform.parent, at);
                return $"It gives you {gear.DisplayName}.";
            }
            case Blessing.Tome:
            {
                var row = LootSource.FloorGear?.Invoke(floor);
                int top = row != null ? row.HighestTier : 1;
                var tomes = InventoryManager.HasInstance && InventoryManager.Instance.itemCatalog != null
                    ? InventoryManager.Instance.itemCatalog.FindAll(i => i != null && i.spell != null && i.tier > 0 && i.tier <= Mathf.Max(1, top) && InventoryManager.Instance.CanDropLoot(i))
                    : new List<ItemData>();
                if (tomes.Count == 0) break;
                var tome = tomes[Random.Range(0, tomes.Count)];
                DungeonLootDrop.Spill(new List<LootSource.Drop> { new(tome, 1) }, 0, transform.position, transform.parent, at);
                return $"It gives you {tome.itemName}.";
            }
        }
        var attribute = Attributes[Random.Range(0, Attributes.Length)];
        progress.GrantPoint(attribute);
        var track = ProgressionManager.HasInstance ? ProgressionManager.Instance.Track(attribute) : null;
        string gives = track != null ? ProgressionManager.AttributeEffects(track, 1) : "";
        return $"+1 {attribute} (now {progress.Score(attribute)}){(string.IsNullOrEmpty(gives) ? "" : ": " + gives)}.";
    }
}
