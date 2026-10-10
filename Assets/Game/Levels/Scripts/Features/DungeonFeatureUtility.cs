using UnityEngine;

// Small helpers shared by the dungeon features (fountains, altars, graves, bookshelves), the merchant
// and the loot code.
public static class DungeonFeatureUtility
{
    // Spawns an enemy on navigation near a point, scaled to the current floor, already hunting.
    public static Character SpawnEnemy(GameObject prefab, Vector3 near, Transform parent)
    {
        if (prefab == null) return null;
        Vector3 pos = near + UnityEngine.Random.insideUnitSphere.WithY(0).normalized * 1.2f;
        if (!UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 2.5f, UnityEngine.AI.NavMesh.AllAreas)) return null;
        var go = UnityEngine.Object.Instantiate(prefab, hit.position, Quaternion.Euler(0, UnityEngine.Random.Range(0, 360f), 0), parent);
        var character = go.GetComponent<Character>();
        var generator = parent != null ? parent.GetComponentInParent<DungeonGenerator>() : null;
        if (generator != null && character != null)
        {
            var balance = generator.LevelData.balance;
            if (balance != null) balance.Apply(character, generator.FloorNumber);
            var drop = go.GetComponent<DungeonLootDrop>();
            if (drop != null && !drop.overrideLevelTable) { drop.table = generator.EnemyLootTable; drop.seed = UnityEngine.Random.Range(1, int.MaxValue); drop.floorNumber = generator.FloorNumber; }
        }
        var brain = go.GetComponent<EnemyBrain>();
        if (brain != null) brain.StartCoroutine(AlertNextFrame(brain));
        return character;
    }

    static System.Collections.IEnumerator AlertNextFrame(EnemyBrain brain)
    {
        yield return null;
        if (brain != null) brain.Alert();
    }

    public static Vector3 WithY(this Vector3 v, float y) => new Vector3(v.x, y, v.z);

    public static Player TablePlayer(Character who) => who is Player p && p.kind == PlayerKind.Table ? p : null;

    // The world bounds around every renderer given; false when there are none.
    public static bool RendererBounds(Renderer[] renderers, out Bounds bounds)
    {
        bounds = default;
        if (renderers == null || renderers.Length == 0) return false;
        bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return true;
    }
}
