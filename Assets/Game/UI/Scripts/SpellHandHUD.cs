using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Small readout above the hotbar while a spell is in the left hand: its name and cost, and a
// charge bar during the wind-up.
public class SpellHandHUD : MonoBehaviour
{
    public TMP_Text label;
    [Tooltip("Optional bar filled while the spell winds up.")]
    public Image chargeFill;

    PlayerSpellcasting spells;
    SpellDefinition shownSpell;
    float shownMana = -1;

    void Update()
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        if (spells == null || spells.gameObject != (player != null ? player.gameObject : null))
            spells = player != null ? player.GetComponent<PlayerSpellcasting>() : null;
        var spell = spells != null ? spells.Selected : null;
        float mana = player != null && player.Stats != null ? player.Stats.CurrentMana : 0;

        if (chargeFill != null)
        {
            chargeFill.transform.parent.gameObject.SetActive(spells != null && spells.Casting);
            if (spells != null) chargeFill.fillAmount = spells.ChargeProgress;
        }
        // Only rebuild the text when something it shows has changed.
        bool affordable = spell != null && mana >= spells.Cost(spell);
        if (spell == shownSpell && Mathf.Approximately(shownMana, affordable ? 1 : 0)) return;
        shownSpell = spell; shownMana = affordable ? 1 : 0;
        label.text = spell == null ? "" : affordable
            ? $"{spell.displayName.ToUpperInvariant()}  {spells.Cost(spell):0} MP"
            : $"{spell.displayName.ToUpperInvariant()}  <color=#C86A55>{spells.Cost(spell):0} MP</color>";
    }
}
