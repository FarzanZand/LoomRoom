using System.Collections.Generic;
using System.Text;
using UnityEngine;

// A fallen enemy the player can search. Holds the loot rolled at death and spills it
// where the body actually lies (the ragdoll's hips), then stays as scenery.
public class DungeonCorpse : MonoBehaviour, IInteractable
{
    readonly List<DungeonLootTable.Drop> drops = new();
    int gold;
    bool searched, filled;
    Character character;
    InteractableTrigger trigger;

    public string Prompt => "Search the " + (character != null ? character.DisplayName : "body");
    public bool CanInteract(Character who) => filled && !searched && who is Player;

    public void Fill(IReadOnlyList<DungeonLootTable.Drop> loot, int coins)
    {
        character = GetComponent<Character>();
        drops.Clear();
        if (loot != null) drops.AddRange(loot);
        gold = coins;
        filled = true;
        StartCoroutine(AttachTrigger());
    }

    // Wait a frame: the ragdoll is built by another Died listener that may run after this one.
    System.Collections.IEnumerator AttachTrigger()
    {
        yield return null;
        // The interaction volume follows the body as it falls.
        var ragdoll = GetComponent<EnemyRagdoll>();
        var anchor = ragdoll != null && ragdoll.BodyCenter != null ? ragdoll.BodyCenter : transform;
        var go = new GameObject("Search corpse");
        go.transform.SetParent(anchor, false);
        if (anchor == transform) go.transform.localPosition = Vector3.up * .4f;
        var sphere = go.AddComponent<SphereCollider>();
        sphere.radius = .7f / Mathf.Max(.0001f, anchor.lossyScale.x);
        trigger = go.AddComponent<InteractableTrigger>();
    }

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        searched = true;
        if (trigger != null) trigger.gameObject.SetActive(false);
        var ragdoll = GetComponent<EnemyRagdoll>();
        Vector3 at = ragdoll != null && ragdoll.BodyCenter != null ? ragdoll.BodyCenter.position : transform.position;
        at.y = transform.position.y;
        string name = character != null ? character.DisplayName : "body";
        if (drops.Count == 0 && gold <= 0)
        {
            MessageLog.Post($"You search the {name} and find nothing.", MessageKind.Info);
            return;
        }
        var found = new StringBuilder();
        foreach (var d in drops)
        {
            if (d.item == null) continue;
            if (found.Length > 0) found.Append(", ");
            found.Append(d.quantity > 1 ? $"{d.quantity} {d.item.itemName}" : d.item.itemName);
        }
        if (gold > 0)
        {
            if (found.Length > 0) found.Append(" and ");
            found.Append(gold).Append(' ').Append(CurrencyManager.HasInstance ? CurrencyManager.Instance.currencyName : "gold");
        }
        MessageLog.Post($"You search the {name}: {found}.", MessageKind.Loot);
        DungeonLootDrop.Spill(drops, gold, at, transform.parent);
    }
}
