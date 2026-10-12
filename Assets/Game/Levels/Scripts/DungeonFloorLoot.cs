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
        if (Random.value < chance && items != null && items.Length > 0)
        {
            int n = Random.Range(count.x, Mathf.Max(count.x, count.y) + 1);
            for (int i = 0; i < n; i++)
            {
                float total = 0; foreach (var c in items) if (c != null && c.item != null) total += c.weight;
                float pick = Random.value * total; ItemData item = null;
                foreach (var c in items) { if (c == null || c.item == null) continue; pick -= c.weight; if (pick <= 0) { item = c.item; break; } }
                if (item == null) continue;
                var offset = Random.insideUnitCircle * scatter;
                DungeonPickup.Spawn(item, transform.position + new Vector3(offset.x, 0f, offset.y), transform.parent);
            }
        }
        Destroy(gameObject);
    }
}
