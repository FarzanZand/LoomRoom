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

    void TryHitFlash()
    {
        if (!CombatManager.HasInstance || !CombatManager.Instance.hitFlashEnabled) return;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(HitFlashRoutine(CombatManager.Instance.hitFlashColor,
                                                      CombatManager.Instance.hitFlashDuration));
    }

    IEnumerator HitFlashRoutine(Color color, float duration)
    {
        flashBlock.Clear();
        flashBlock.SetColor(BaseColorId, color);
        flashBlock.SetColor(ColorId, color);
        ApplyFlashBlock();

        yield return new WaitForSecondsRealtime(duration);

        flashBlock.Clear();
        ApplyFlashBlock();
        flashRoutine = null;
    }

    void ApplyFlashBlock()
    {
        foreach (var r in renderers)
            if (r != null) r.SetPropertyBlock(flashBlock);
    }
}
