using Sirenix.OdinInspector;
using UnityEngine;

// Offer gold, receive a blessing. The price grows with depth; each altar answers a
// limited number of prayers. Outcomes use DungeonBoon, so blessings last the whole run.
// (Barony's altars also identify items; identification is not in the game yet.)
public class DungeonAltar : MonoBehaviour, IInteractable
{
    [Min(0)] public int baseOffering = 20;
    [Min(0), Tooltip("Added to the offering per floor beyond the first.")]
    public int offeringPerFloor = 10;
    [Min(1)] public int prayers = 1;
    [Tooltip("Lit while the altar can still answer prayers.")]
    public GameObject activeGlow;
    public AudioClip prayClip;
    [Range(0, 1)] public float prayVolume = .9f;
    [ListDrawerSettings(ShowFoldout = true, ListElementLabelName = "label")]
    public DungeonBoon[] outcomes = new DungeonBoon[0];

    [HideInInspector] public int floorNumber = 1;

    int Offering => baseOffering + offeringPerFloor * Mathf.Max(0, floorNumber - 1);
    string Currency => CurrencyManager.HasInstance ? CurrencyManager.Instance.currencyName : "gold";

    public string Prompt => prayers > 0 ? $"Pray at the altar ({Offering} {Currency})" : "The altar is silent";
    public bool CanInteract(Character who) => DungeonFeatureUtility.TablePlayer(who) != null;

    public void Interact(Character who)
    {
        var player = DungeonFeatureUtility.TablePlayer(who);
        if (player == null) return;
        if (prayers <= 0) { MessageLog.Post("Your prayers go unanswered.", MessageKind.Info); return; }
        var wallet = player.Wallet;
        if (wallet == null || !wallet.TrySpend(Offering))
        {
            MessageLog.Post($"The altar demands an offering of {Offering} {Currency}.", MessageKind.Warning);
            return;
        }
        prayers--;
        if (AudioManager.HasInstance && prayClip != null) AudioManager.Instance.PlaySFX(prayClip, transform.position, prayVolume, .03f);
        MessageLog.Post($"You offer {Offering} {Currency} and pray.", MessageKind.Info);
        var outcome = DungeonBoon.Choose(outcomes);
        if (outcome != null) outcome.Apply(player, transform.position + Vector3.up, transform.parent);
        if (prayers <= 0 && activeGlow != null) activeGlow.SetActive(false);
    }
}
