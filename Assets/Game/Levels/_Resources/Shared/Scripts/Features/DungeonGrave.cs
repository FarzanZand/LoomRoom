using System.Collections;
using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;

// Dig for grave goods. Takes a moment, makes noise, and sometimes the occupant objects.
public class DungeonGrave : MonoBehaviour, IInteractable
{
    [Min(0)] public float digSeconds = 1.4f;
    [Range(0, 1), Tooltip("Chance the grave's occupant rises instead of (or as well as) giving up its goods.")]
    public float wakeChance = .3f;
    [Tooltip("Loot still drops when the occupant rises.")]
    public bool lootEvenIfWoken = true;
    [AssetsOnly] public GameObject[] occupants = new GameObject[0];
    [Tooltip("Rolled as a Chest reward. Empty uses the floor's chest table.")]
    public DungeonLootTable loot;
    [Tooltip("Earth mound lowered when dug.")]
    public Transform mound;
    public AudioClip digClip;
    [Range(0, 1)] public float digVolume = .9f;
    public GameObject dirtEffect;
    [Min(0)] public float noiseRadius = 8f;

    [HideInInspector] public int seed;
    [HideInInspector] public int floorNumber = 1;
    [HideInInspector] public DungeonLootTable fallbackLoot;

    bool dug, digging;

    public string Prompt => dug ? "An empty grave" : digging ? "Digging..." : "Dig up the grave";
    public bool CanInteract(Character who) => !dug && !digging && DungeonFeatureUtility.TablePlayer(who) != null;

    public void Interact(Character who)
    {
        var player = DungeonFeatureUtility.TablePlayer(who);
        if (player == null || dug || digging) return;
        StartCoroutine(Dig(player));
    }

    IEnumerator Dig(Player player)
    {
        digging = true;
        MessageLog.Post("You start digging...", MessageKind.Info);
        float end = Time.time + digSeconds;
        while (Time.time < end)
        {
            if (AudioManager.HasInstance && digClip != null) AudioManager.Instance.PlaySFX(digClip, transform.position, digVolume, .1f);
            if (dirtEffect != null) PoolManager.SpawnOrInstantiate(dirtEffect, transform.position + Vector3.up * .3f, Quaternion.identity, 2f);
            yield return new WaitForSeconds(Mathf.Min(.5f, digSeconds));
        }
        digging = false;
        dug = true;
        NoiseEvents.Report(transform.position, noiseRadius);
        if (mound != null) mound.DOLocalMoveY(mound.localPosition.y - .25f, .4f).SetLink(gameObject);

        var rng = new System.Random(seed);
        bool woke = occupants != null && occupants.Length > 0 && rng.NextDouble() < wakeChance;
        if (woke)
        {
            var prefab = occupants[rng.Next(occupants.Length)];
            var risen = DungeonFeatureUtility.SpawnEnemy(prefab, transform.position, transform.parent);
            MessageLog.Post(risen != null ? $"The {risen.DisplayName} rises from the grave!" : "Something stirs below, then falls still.", MessageKind.Bad);
            if (!lootEvenIfWoken) yield break;
        }
        var table = loot != null ? loot : fallbackLoot;
        var items = table != null ? table.RollDrops(rng, floorNumber, DungeonLootSource.Chest) : null;
        int gold = table != null ? table.RollGold(rng, floorNumber, DungeonLootSource.Chest) : 0;
        if ((items == null || items.Count == 0) && gold <= 0)
        {
            if (!woke) MessageLog.Post("You find only bones.", MessageKind.Info);
            yield break;
        }
        MessageLog.Post("You unearth some grave goods.", MessageKind.Loot);
        DungeonLootDrop.Spill(items, gold, transform.position + transform.forward * .8f, transform.parent);
    }
}
