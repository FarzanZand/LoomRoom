using UnityEngine;

// A shrine bargain (DungeonRoomFeatures.shrineBargains): bleed on it and it raises what you fight with, once. It takes a
// share of your health and lifts the weapon in your hand (or, failing that, your shield, then your body armour) one
// rarity: Common to Uncommon, Uncommon to Rare, Rare to Unique, with new properties rolled for the floor the way loot rolls them
// (ItemPropertyTable). It won't take more blood than you have.
public class DungeonShrine : MonoBehaviour, IInteractable
{
    [Range(.05f, .9f), Tooltip("Share of the player's most health the bargain takes.")]
    public float bloodPrice = .35f;
    [Tooltip("Lit while the shrine can still be used (candle flames, a glow).")]
    public GameObject[] litWhileUnused = new GameObject[0];
    public AudioClip offerClip;
    [Range(0, 1)] public float offerVolume = .9f;

    bool used;

    static readonly EquipmentSlot[] Order = { EquipmentSlot.RightHand, EquipmentSlot.LeftHand, EquipmentSlot.Body };

    static Player Table => PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;

    // The slot the shrine would raise: the first in Order that holds gear below Unique.
    static bool Target(Player player, out EquipmentSlot slot, out ItemData item)
    {
        slot = EquipmentSlot.RightHand; item = null;
        if (player == null || player.Equipment == null) return false;
        foreach (var s in Order)
        {
            var i = player.Equipment.Get(s);
            if (i == null || !i.IsGear || InventoryManager.NextRarity(i.rarity) == ItemRarity.Common) continue;
            slot = s; item = i;
            return true;
        }
        return false;
    }

    // One rarity up, with new properties rolled for this floor (Uncommon keeps none of its old rolls: it is made anew).
    static ItemData Raise(ItemData item)
    {
        var table = ItemPropertyTable.Current;
        if (table == null) return item;
        var to = InventoryManager.NextRarity(item.rarity);
        int floor = TableManager.Loader != null && TableManager.Loader.Dungeon != null ? TableManager.Loader.Dungeon.FloorNumber : 1;
        var properties = table.Roll(item.BaseItem, to, floor, new System.Random());
        return properties.Length > 0 ? item.BaseItem.Rolled(to, properties) : item;
    }

    public string Prompt
    {
        get
        {
            if (used) return "The shrine is dry";
            return Target(Table, out _, out var item) ? $"Bleed on the shrine (raise your {item.BaseItem.itemName})" : "Bleed on the shrine";
        }
    }

    public bool CanInteract(Character who) => DungeonFeatureUtility.TablePlayer(who) != null;

    public void Interact(Character who)
    {
        var player = DungeonFeatureUtility.TablePlayer(who);
        if (player == null) return;
        if (used) { MessageLog.Post("The shrine has had its blood.", MessageKind.Info); return; }
        if (!Target(player, out var slot, out var item)) { MessageLog.Post("You hold nothing the shrine can raise.", MessageKind.Warning); return; }
        float price = Mathf.Max(1f, player.Stats.MaxHealth * bloodPrice);
        if (player.Stats.CurrentHealth <= price) { MessageLog.Post("You are too weak to bleed for it.", MessageKind.Warning); return; }
        used = true;
        player.Stats.TakeDamage(new DamageInfo { Amount = price, SourceName = "the shrine", Magic = true, HitPoint = transform.position + Vector3.up });
        var raised = Raise(item);
        if (raised != item) player.Equipment.Replace(slot, raised);
        if (AudioManager.HasInstance && offerClip != null) AudioManager.Instance.PlaySFX(offerClip, transform.position, offerVolume, .03f);
        MessageLog.Post($"The shrine drinks. Your {item.BaseItem.itemName} is now {raised.DisplayName}.", MessageKind.Good);
        foreach (var go in litWhileUnused) if (go != null) go.SetActive(false);
        CoopWorld.FeatureUsed(this);
    }

    // Co-op: a teammate bled on it. It answers the party once, so it is dry here too.
    public void Silence(string who)
    {
        if (used) return;
        used = true;
        foreach (var go in litWhileUnused) if (go != null) go.SetActive(false);
        if (offerClip != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(offerClip, transform.position, offerVolume, .03f);
        if (!string.IsNullOrEmpty(who)) MessageLog.Post($"{who} bleeds on the shrine.", MessageKind.Info);
    }
}
