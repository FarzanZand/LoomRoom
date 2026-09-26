using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;

// Drink and hope. Each sip rolls one weighted outcome: healing, a run-long blessing,
// a curse, or something crawling out of the basin. Dries up after a few sips.
public class DungeonFountain : MonoBehaviour, IInteractable
{
    [Min(1)] public int sips = 2;
    [Tooltip("Water surface hidden or lowered when the fountain runs dry.")]
    public Transform water;
    public AudioClip drinkClip;
    [Range(0, 1)] public float drinkVolume = .9f;
    [ListDrawerSettings(ShowFoldout = true, ListElementLabelName = "label")]
    public DungeonBoon[] outcomes = new DungeonBoon[0];

    public string Prompt => sips > 0 ? "Drink from the fountain" : "The fountain is dry";
    public bool CanInteract(Character who) => DungeonFeatureUtility.TablePlayer(who) != null;

    public void Interact(Character who)
    {
        var player = DungeonFeatureUtility.TablePlayer(who);
        if (player == null) return;
        if (sips <= 0) { MessageLog.Post("The basin is dry. Only grit remains.", MessageKind.Info); return; }
        sips--;
        if (AudioManager.HasInstance && drinkClip != null) AudioManager.Instance.PlaySFX(drinkClip, transform.position, drinkVolume, .05f);
        MessageLog.Post("You drink from the fountain.", MessageKind.Info);
        var outcome = DungeonBoon.Choose(outcomes);
        if (outcome != null) outcome.Apply(player, transform.position + Vector3.up, transform.parent);
        else MessageLog.Post("The water tastes of stone. Nothing happens.", MessageKind.Info);
        if (sips <= 0)
        {
            MessageLog.Post("The fountain runs dry.", MessageKind.Info);
            if (water != null) water.DOScaleY(.01f, 1.2f).SetLink(gameObject).OnComplete(() => water.gameObject.SetActive(false));
        }
    }
}
