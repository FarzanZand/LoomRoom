using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A charge bar above the hotbar while a spell winds up. (The spell's name and cost are in the
// tome's tooltip.)
public class SpellHandHUD : MonoBehaviour
{
    public TMP_Text label;
    [Tooltip("Optional bar filled while the spell winds up.")]
    public Image chargeFill;

    PlayerSpellcasting spells;

    void Update()
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        if (spells == null || spells.gameObject != (player != null ? player.gameObject : null))
            spells = player != null ? player.GetComponent<PlayerSpellcasting>() : null;

        if (chargeFill != null)
        {
            chargeFill.transform.parent.gameObject.SetActive(spells != null && spells.Casting);
            if (spells != null) chargeFill.fillAmount = spells.ChargeProgress;
        }
        // Name and cost live in the tome's tooltip; nothing to read here.
        if (label != null && label.text.Length > 0) label.text = "";
    }
}
