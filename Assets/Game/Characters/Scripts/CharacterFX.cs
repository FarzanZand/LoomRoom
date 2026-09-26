using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Per-character feel: the knockback this character's hits deal, hurt sounds and the
// hit flash. Global tuning (flash colour, duration) comes from CombatManager.
public class CharacterFX : MonoBehaviour
{
    [Header("On Hit Landed")]
    [Tooltip("Knockback force this character's attacks deal by default (weapons/attacks can override).")]
    [SerializeField] bool  enableKnockback = true;
    [SerializeField] float knockbackForce  = 3f;

    [Header("On Hurt Received")]
    [SerializeField] AudioData onHurtAudio;

    [Header("Hit Flash")]
    [Tooltip("Leave empty to auto-collect all mesh renderers under this object.")]
    [SerializeField] Renderer[] flashRenderers;

    public float KnockbackForce   => enableKnockback ? knockbackForce : 0f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId     = Shader.PropertyToID("_Color");

    Renderer[] renderers;
    MaterialPropertyBlock flashBlock;
    MaterialPropertyBlock[] originals;
    Coroutine flashRoutine;

    protected virtual void Awake()
    {
        flashBlock = new MaterialPropertyBlock();

        if (flashRenderers != null && flashRenderers.Length > 0)
        {
            renderers = flashRenderers;
            return;
        }

        var collected = new List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer))
                collected.Add(r);
        renderers = collected.ToArray();
    }

    public virtual void NotifyHitLanded(DamageInfo info) { }

    public virtual void NotifyHurtReceived(DamageInfo info)
    {
        if (onHurtAudio != null && AudioManager.HasInstance)
            AudioManager.Instance.PlaySFXData(onHurtAudio, transform.position);
        if (!info.Blocked && info.Amount > 0) TryHitFlash();
    }

    // The one hit flash in the game (players, enemies, NPCs). Each renderer's own property
    // block is saved before the flash and put back after, so a flash never leaves a tint.
    void TryHitFlash()
    {
        if (!CombatManager.HasInstance || !CombatManager.Instance.hitFlashEnabled || renderers == null) return;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        else SaveBlocks();
        flashRoutine = StartCoroutine(HitFlashRoutine(CombatManager.Instance.hitFlashColor,
                                                      CombatManager.Instance.hitFlashDuration));
    }

    void SaveBlocks()
    {
        if (originals == null || originals.Length != renderers.Length)
        {
            originals = new MaterialPropertyBlock[renderers.Length];
            for (int i = 0; i < originals.Length; i++) originals[i] = new MaterialPropertyBlock();
        }
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].GetPropertyBlock(originals[i]);
    }

    IEnumerator HitFlashRoutine(Color color, float duration)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(flashBlock);
            flashBlock.SetColor(BaseColorId, color);
            flashBlock.SetColor(ColorId, color);
            r.SetPropertyBlock(flashBlock);
        }
        // Real time: a hit stop must not stretch the flash.
        yield return new WaitForSecondsRealtime(duration);
        RestoreBlocks();
        flashRoutine = null;
    }

    void RestoreBlocks()
    {
        if (originals == null) return;
        for (int i = 0; i < renderers.Length && i < originals.Length; i++)
            if (renderers[i] != null) renderers[i].SetPropertyBlock(originals[i]);
    }

    protected virtual void OnDisable()
    {
        if (flashRoutine == null) return;
        StopCoroutine(flashRoutine);
        flashRoutine = null;
        RestoreBlocks();
    }
}
