using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A floor plate of spikes (trapped treasure, DungeonChestTrap): sprung, it clicks and rattles for a moment (the warning),
// then the spikes thrust up through the plate and hurt everyone standing on it, player and enemies alike, then sink back.
public class DungeonSpikeTrap : MonoBehaviour
{
    [Tooltip("The spike bed that rises through the plate.")]
    public Transform spikes;
    [Min(0f), Tooltip("Metres the spikes sit below their raised position while hidden.")]
    public float hiddenDepth = .66f;
    [Min(0f)] public float warnSeconds = .6f;
    [Min(0f)] public float holdSeconds = 1.1f;
    [Range(0f, 1f), Tooltip("Share of the player's most health a thrust takes.")]
    public float playerDamage = .3f;
    [Min(0f), Tooltip("Damage to an enemy caught on it.")]
    public float enemyDamage = 25f;
    [Tooltip("The area that hurts, centred above the plate (metres).")]
    public Vector3 hurtSize = new(1.7f, 1.2f, 1.7f);
    [Tooltip("Springs when the player steps on it (a trapped hall), not only when a chest opens. Running across keeps you ahead of the spikes.")]
    public bool springOnStep;
    public AudioClip clickAudio, thrustAudio, retractAudio;
    [Range(0, 1)] public float volume = .9f;

    Vector3 raised;
    bool busy;
    static readonly Collider[] hits = new Collider[24];

    void Awake()
    {
        if (spikes == null) return;
        raised = spikes.localPosition;
        spikes.localPosition = raised + Vector3.down * hiddenDepth;
    }

    public void Spring()
    {
        if (busy || !isActiveAndEnabled) return;
        CoopWorld.SpikeSprung(this);   // co-op: it springs for everyone
        StartCoroutine(Run());
    }

    void Update()
    {
        if (!springOnStep || busy || !PlayerManager.HasInstance) return;
        var player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        if (player == null || !player.isActiveAndEnabled || !player.IsAlive) return;
        var local = transform.InverseTransformPoint(player.transform.position);
        if (Mathf.Abs(local.x) < hurtSize.x * .5f && Mathf.Abs(local.z) < hurtSize.z * .5f && local.y > -.3f && local.y < 1f) Spring();
    }

    void Play(AudioClip clip) { if (clip != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(clip, transform.position, volume, .05f); }

    IEnumerator Run()
    {
        busy = true;
        var down = raised + Vector3.down * hiddenDepth;
        // The warning: a click, then the tips rattle just under the plate.
        Play(clickAudio);
        for (float t = 0f; t < warnSeconds; t += Time.deltaTime)
        {
            if (spikes != null) spikes.localPosition = down + Vector3.up * (hiddenDepth * .45f) + Random.insideUnitSphere * .012f;
            yield return null;
        }
        Play(thrustAudio);
        for (float t = 0f; t < .07f; t += Time.deltaTime)
        {
            if (spikes != null) spikes.localPosition = Vector3.Lerp(down, raised, t / .07f);
            yield return null;
        }
        if (spikes != null) spikes.localPosition = raised;
        Hurt();
        yield return new WaitForSeconds(holdSeconds);
        Play(retractAudio);
        for (float t = 0f; t < .5f; t += Time.deltaTime)
        {
            if (spikes != null) spikes.localPosition = Vector3.Lerp(raised, down, t / .5f);
            yield return null;
        }
        if (spikes != null) spikes.localPosition = down;
        busy = false;
    }

    void Hurt()
    {
        var centre = transform.position + Vector3.up * hurtSize.y * .5f;
        int n = Physics.OverlapBoxNonAlloc(centre, hurtSize * .5f, hits, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        var struck = new HashSet<Character>();
        for (int i = 0; i < n; i++)
        {
            var c = hits[i].GetComponentInParent<Character>();
            if (c == null || !c.IsAlive || c.Stats == null || !struck.Add(c)) continue;
            float amount = c is Player p ? Mathf.Max(1f, p.Stats.MaxHealth * playerDamage) : enemyDamage;
            c.Stats.TakeDamage(new DamageInfo
            {
                Amount = amount, SourceName = "the spikes", HitPoint = c.transform.position + Vector3.up * .4f,
                Direction = Vector3.up, SwingDirection = Vector3.up,
            });
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, .3f, .2f, .6f);
        Gizmos.DrawWireCube(Vector3.up * hurtSize.y * .5f, hurtSize);
    }
}
