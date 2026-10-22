using UnityEngine;

// The house rules sheet taped to the inside of the bedroom's east door (a child of the door's leaf, so it swings with
// it). Its picture shows as many written rules as are pinned (Sheets: one picture per count, painted by
// Tools/house_rules_sheet.py); using it opens the read screen with the rules in full.
public class HouseRulesSheet : MonoBehaviour, IInteractable
{
    [Tooltip("The sheet's picture with 1, 2, 3 ... rules written on it (index 0 = one rule). The last one is kept for more.")]
    public Texture2D[] sheets = new Texture2D[0];
    [Tooltip("The paper's renderer (its material is copied at run time to swap the picture).")]
    public Renderer paper;

    public string Prompt => "Read the house rules";
    public bool CanInteract(Character who) => who is Player p && p.kind == PlayerKind.Room && RuleManager.HasInstance;
    public void Interact(Character who) { if (RuleManager.HasInstance) RuleManager.Instance.ShowRules(); }

    Material own;
    void Start()
    {
        if (RuleManager.HasInstance) RuleManager.Instance.Changed += Redraw;
        Redraw();
    }
    void OnDestroy()
    {
        if (RuleManager.HasInstance) RuleManager.Instance.Changed -= Redraw;
        if (own != null) Destroy(own);
    }

    void Redraw()
    {
        if (paper == null || sheets == null || sheets.Length == 0 || !RuleManager.HasInstance) return;
        int count = RuleManager.Instance.PinnedRules().Count;
        paper.enabled = count > 0;
        if (count == 0) return;
        var picture = sheets[Mathf.Clamp(count - 1, 0, sheets.Length - 1)];
        if (picture == null) return;
        if (own == null) { own = new Material(paper.sharedMaterial) { name = paper.sharedMaterial.name + " (rules)" }; paper.sharedMaterial = own; }
        if (own.HasProperty("_BaseMap")) own.SetTexture("_BaseMap", picture);
    }
}
