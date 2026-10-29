using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public class ShadowVeilSettings
{
    [Min(1f), Tooltip("Seconds the player stays unseen.")]
    public float seconds = 8f;
    [Min(0f), Tooltip("Enemies closer than this still find the player (bumping into them).")]
    public float revealDistance = 1.1f;
    [Tooltip("The first blow struck from the veil is a backstab from any side, and ends the veil.")]
    public bool ambush = true;
    [AssetsOnly, Tooltip("Smoke that clings to the player while veiled (seen around the arms in first person).")]
    public SpellVisual wisps;
    [Tooltip("Played as the veil wears off or is broken.")]
    public AudioClip endSound;
    [Range(0, 1)] public float endVolume = .8f;
}

// Shadow Veil on a player: for a while no enemy sees them beyond arm's reach, even one already hunting them (it goes to
// where it last saw them and searches). The view takes the hidden look and sound is muffled, as when sneaking unseen.
// The first blow struck from the veil is a backstab from any side and ends it; casting another spell ends it too.
// Added to the player at the cast; removes itself when the veil is over.
public class ShadowVeil : MonoBehaviour
{
    static readonly List<ShadowVeil> active = new();

    Player player;
    ShadowVeilSettings settings;
    SpellVisual wisps;
    float until;

    public static void Begin(Player player, SpellDefinition spell)
    {
        if (player == null || spell == null) return;
        var veil = player.GetComponent<ShadowVeil>();
        if (veil == null) veil = player.gameObject.AddComponent<ShadowVeil>();
        veil.Set(player, spell.veil);
    }

    void Set(Player who, ShadowVeilSettings with)
    {
        player = who; settings = with;
        until = Time.time + settings.seconds;
        if (!active.Contains(this)) active.Add(this);
        if (wisps == null && settings.wisps != null)
        {
            // On the view, low and ahead, so it curls up round the arms in first person and round the body from outside.
            var view = player.CameraRig != null && player.CameraRig.Camera != null ? player.CameraRig.Camera.transform : player.transform;
            wisps = Instantiate(settings.wisps, view);
            wisps.transform.localPosition = new Vector3(0f, -.55f, .35f);
            wisps.transform.localRotation = Quaternion.identity;
        }
    }

    void OnDisable() => active.Remove(this);

    void Update()
    {
        if (player == null || !player.IsAlive || Time.time >= until) End();
    }

    public void End()
    {
        active.Remove(this);
        if (wisps != null) wisps.Stop();
        wisps = null;
        if (settings != null && settings.endSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(settings.endSound, settings.endVolume);
        Destroy(this);
    }

    // Seconds left, 0 when not veiled.
    public float Remaining => Mathf.Max(0f, until - Time.time);

    public static bool IsVeiled(Character c)
    {
        foreach (var v in active) if (v != null && v.player == c) return true;
        return false;
    }

    // Perception: whether the veil hides this target from eyes this far off.
    public static bool Hides(Transform target, float distance)
    {
        if (active.Count == 0 || target == null) return false;
        foreach (var v in active)
            if (v != null && v.player != null && v.player.transform == target) return distance > v.settings.revealDistance;
        return false;
    }

    // A blow from the veil (CombatManager, as a hit lands): a backstab from any side, once; the veil ends with it.
    public static bool Ambush(Character source)
    {
        foreach (var v in active)
            if (v != null && v.player == source)
            {
                bool ambush = v.settings.ambush;
                v.End();
                return ambush;
            }
        return false;
    }

    // Another spell cast, or anything else that gives the player away.
    public static void Break(Character who)
    {
        foreach (var v in active.ToArray()) if (v != null && v.player == who) v.End();
    }
}
