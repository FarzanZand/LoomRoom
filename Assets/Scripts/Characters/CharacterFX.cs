using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterFX : MonoBehaviour
{
    [Header("On Hit Landed")]
    [SerializeField] AudioClip[] onHitSounds;
    [SerializeField, Range(0f, 1f)] float onHitVolume = 1f;
    [SerializeField, Range(0f, 0.5f)] float onHitPitchVariance = 0.05f;
    [SerializeField] bool  enableKnockback   = true;
    [SerializeField] float knockbackForce    = 3f;
    [SerializeField] float knockbackDuration = 0.25f;

    [Header("On Hurt Received")]
    [SerializeField] AudioClip[] onHurtSounds;
    [SerializeField, Range(0f, 1f)] float onHurtVolume = 1f;
    [SerializeField, Range(0f, 0.5f)] float onHurtPitchVariance = 0.05f;

    [Header("Hit Flash")]
    [Tooltip("Leave empty to auto-collect all mesh renderers under this object.")]
    [SerializeField] Renderer[] flashRenderers;

    public bool  KnockbackEnabled   => enableKnockback;
    public float KnockbackForce    => knockbackForce;
    public float KnockbackDuration => knockbackDuration;

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

    public virtual void NotifyHitLanded(Vector3 contactPoint)
    {
        AudioManager.Instance?.PlaySFXRandom(onHitSounds, contactPoint, onHitVolume, onHitPitchVariance);
    }

    public virtual void NotifyHurtReceived(float amount, Vector3 direction)
    {
        AudioManager.Instance?.PlaySFXRandom(onHurtSounds, transform.position, onHurtVolume, onHurtPitchVariance);
        TryHitFlash();
    }

    void TryHitFlash()
    {
        var combat = CombatManager.Instance;
        if (combat == null || !combat.hitFlashEnabled) return;

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(HitFlashRoutine(combat.hitFlashColor, combat.hitFlashDuration));
    }

    IEnumerator HitFlashRoutine(Color color, float duration)
    {
        flashBlock.Clear();
        flashBlock.SetColor(BaseColorId, color);
        flashBlock.SetColor(ColorId, color);
        ApplyFlashBlock();

        yield return new WaitForSeconds(duration);

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
