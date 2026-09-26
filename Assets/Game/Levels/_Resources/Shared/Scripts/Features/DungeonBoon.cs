using System;
using Sirenix.OdinInspector;
using UnityEngine;

// One possible result of a fountain, altar or similar: a log line plus ordinary item
// effects (heal, mana, stat buff, damage...). Stat buffs are tagged as run blessings, so a
// permanent (duration 0) buff lasts until the run ends and never leaks into the next one.
[Serializable]
public class DungeonBoon
{
    [HorizontalGroup("Top"), LabelWidth(50)] public string label = "Blessing";
    [HorizontalGroup("Top", Width = 120), LabelWidth(50), Min(0)] public float weight = 1f;
    [TextArea(1, 3)] public string message = "You feel refreshed.";
    public MessageKind kind = MessageKind.Good;
    [ListDrawerSettings(ShowFoldout = false)]
    public EffectEntry[] effects = new EffectEntry[0];
    [AssetsOnly, Tooltip("Enemies that appear next to the object (a curse).")]
    public GameObject[] spawnEnemies = new GameObject[0];
    public AudioClip sound;
    [Range(0, 1)] public float soundVolume = .9f;

    public void Apply(Player who, Vector3 at, Transform parent)
    {
        if (who == null) return;
        var ctx = EffectContext.For(who, null);
        ctx.Point = at;
        ctx.ModifierSource = RunManager.BlessingSource;
        if (effects != null)
            foreach (var e in effects)
            {
                if (e == null) continue;
                if (e.chance < 100f && UnityEngine.Random.Range(0f, 100f) > e.chance) continue;
                ItemEffectProcessor.Apply(e, ctx);
            }
        if (sound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(sound, at, soundVolume, .04f);
        if (!string.IsNullOrWhiteSpace(message)) MessageLog.Post(message.Trim(), kind);
        if (spawnEnemies != null)
            foreach (var prefab in spawnEnemies) DungeonFeatureUtility.SpawnEnemy(prefab, at, parent);
    }

    public static DungeonBoon Choose(DungeonBoon[] boons)
    {
        if (boons == null) return null;
        float total = 0;
        foreach (var b in boons) if (b != null && b.weight > 0) total += b.weight;
        if (total <= 0) return null;
        float roll = UnityEngine.Random.value * total;
        foreach (var b in boons)
        {
            if (b == null || b.weight <= 0) continue;
            roll -= b.weight;
            if (roll < 0) return b;
        }
        return null;
    }
}

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
            generator.LevelData.balance?.Apply(character, generator.FloorNumber);
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
}
