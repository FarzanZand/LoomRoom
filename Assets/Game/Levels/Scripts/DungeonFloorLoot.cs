using UnityEngine;

// Cheap loot lying about (Heathen's goblets and candlesticks): placed like any furnishing, it lays one or two of its
// items on the floor where it stands when the floor starts (floor loot with beams and labels), then goes away. Worth
// a few coins at a merchant.
public class DungeonFloorLoot : MonoBehaviour
{
    [System.Serializable]
    public class Choice { public ItemData item; [Min(0)] public float weight = 1; }

    public Choice[] items = new Choice[0];
    [Range(0, 1), Tooltip("Chance anything is here at all.")] public float chance = .6f;
    [Tooltip("How many items, at least and at most.")] public Vector2Int count = new(1, 2);
    [Min(0), Tooltip("Metres the items scatter around this spot.")] public float scatter = .45f;

    void Start()
    {
        // Seeded from the floor and the spot, so a floor played again (a resumed checkpoint) lays the same loot.
        var generator = GetComponentInParent<DungeonGenerator>();
        var random = new System.Random(generator != null ? generator.PlacedSeed(transform.position) : transform.position.GetHashCode());
        if (random.NextDouble() < chance && items != null && items.Length > 0)
        {
            int n = random.Next(count.x, Mathf.Max(count.x, count.y) + 1);
            for (int i = 0; i < n; i++)
            {
                var choice = WeightedPick.Choose(items, c => c != null && c.item != null ? c.weight : 0f, random);
                if (choice == null) continue;
                float angle = (float)random.NextDouble() * Mathf.PI * 2f, reach = Mathf.Sqrt((float)random.NextDouble()) * scatter;
                DungeonPickup.Spawn(choice.item, transform.position + new Vector3(Mathf.Cos(angle) * reach, 0f, Mathf.Sin(angle) * reach), transform.parent);
            }
        }
        Destroy(gameObject);
    }
}
