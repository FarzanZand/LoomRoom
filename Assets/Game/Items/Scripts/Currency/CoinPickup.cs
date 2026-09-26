using UnityEngine;

// A pile of coins on the floor. Built at runtime by CurrencyManager.SpawnCoins: the model
// comes from the manager's coin visuals, so bigger piles look bigger.
public class CoinPickup : MonoBehaviour, IInteractable
{
    [SerializeField, Min(1)] int amount = 1;
    bool collected;

    public int Amount => amount;
    public string Prompt => $"Pick up {amount} {(CurrencyManager.HasInstance ? CurrencyManager.Instance.currencyName : "gold")}";
    public bool CanInteract(Character who) => !collected && who is Player p && p.GetComponent<Wallet>() != null;

    public void Init(int value)
    {
        amount = Mathf.Max(1, value);
        var manager = CurrencyManager.HasInstance ? CurrencyManager.Instance : null;
        float scale = 1f;
        var prefab = manager != null ? manager.VisualFor(amount, out scale) : null;
        if (prefab != null)
        {
            var visual = Instantiate(prefab, transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
            visual.transform.localScale = prefab.transform.localScale * scale;
            foreach (var c in visual.GetComponentsInChildren<Collider>()) c.enabled = false;
        }
        var trigger = gameObject.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = manager != null ? manager.pickupRadius : .6f;
        trigger.center = Vector3.up * .2f;
        gameObject.AddComponent<InteractableTrigger>();
    }

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        collected = true;
        if (CurrencyManager.HasInstance) CurrencyManager.Instance.Collect((Player)who, amount, transform.position);
        else who.GetComponent<Wallet>().Add(amount);
        Destroy(gameObject);
    }

    void OnTriggerEnter(Collider other)
    {
        if (collected || !CurrencyManager.HasInstance || !CurrencyManager.Instance.collectOnTouch) return;
        var player = other.GetComponentInParent<Player>();
        if (player != null && player.IsActive && player.IsAlive) Interact(player);
    }
}
