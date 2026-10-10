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

    // source: what the player used, named in the log when an effect hurts them ("the fountain").
    public void Apply(Player who, Vector3 at, Transform parent, string source)
    {
        if (who == null) return;
        var ctx = EffectContext.For(who, null);
        ctx.Point = at;
        ctx.SourceName = source;
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

    public static DungeonBoon Choose(DungeonBoon[] boons) =>
        WeightedPick.Choose(boons, b => b != null ? b.weight : 0, UnityEngine.Random.value);
}
