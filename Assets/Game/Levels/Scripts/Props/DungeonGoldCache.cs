using UnityEngine;

// A heap of gold lying on the floor (a hidden chamber's hoard, DungeonGenerator.ShapeTreasure): when the floor starts it
// lays its piles of coins where it stands, then goes away. Seeded from the floor and the spot, so a resumed checkpoint
// lays the same piles.
public class DungeonGoldCache : MonoBehaviour
{
    [Min(1)] public int piles = 3;
    [Min(1), Tooltip("Gold in each pile before the game-wide gold multiplier.")] public int goldPerPile = 6;
    [Min(0), Tooltip("Metres the piles scatter around this spot.")] public float scatter = .6f;

    void Start()
    {
        var generator = GetComponentInParent<DungeonGenerator>();
        var random = new System.Random(generator != null ? generator.PlacedSeed(transform.position) : transform.position.GetHashCode());
        if (CurrencyManager.HasInstance)
            for (int i = 0; i < piles; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f, reach = Mathf.Sqrt((float)random.NextDouble()) * scatter;
                int amount = Mathf.Max(1, Mathf.RoundToInt(goldPerPile * (.75f + .5f * (float)random.NextDouble()) * InventoryManager.GoldScale));
                CurrencyManager.Instance.SpawnCoins(amount, transform.position + new Vector3(Mathf.Cos(angle) * reach, 0f, Mathf.Sin(angle) * reach), transform.parent);
            }
        Destroy(gameObject);
    }
}
