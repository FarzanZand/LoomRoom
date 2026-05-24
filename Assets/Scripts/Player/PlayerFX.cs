using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

public class PlayerFX : CharacterFX
{
    [Header("Received Knockback")]
    [SerializeField] float receivedKnockbackForce = 3f;
    public float ReceivedKnockbackForce => receivedKnockbackForce;

    [Header("Hit Stop")]
    [SerializeField] bool  enableHitStop   = true;
    [SerializeField] float hitStopDuration = 0.07f;

    [Header("Camera")]
    [SerializeField] CinemachineImpulseSource onHitImpulse;
    [SerializeField] CinemachineImpulseSource onHurtImpulse;

    [Header("Hurt Flash")]
    [SerializeField] bool  enableHurtFlash       = true;
    [SerializeField] Image hurtFlashImage;
    [SerializeField] Color hurtFlashColor        = new Color(1f, 0f, 0f, 1f);
    [SerializeField, Range(0f, 1f)] float hurtFlashPeakAlpha = 0.35f;
    [SerializeField] float hurtFlashDuration     = 0.3f;

    Coroutine hurtFlashRoutine;

    public override void NotifyHitLanded(Vector3 contactPoint)
    {
        base.NotifyHitLanded(contactPoint);
        if (enableHitStop)
            HitStopManager.Instance?.Trigger(hitStopDuration);
        onHitImpulse?.GenerateImpulse();
    }

    public override void NotifyHurtReceived(float amount, Vector3 direction)
    {
        base.NotifyHurtReceived(amount, direction);
        onHurtImpulse?.GenerateImpulse();

        if (enableHurtFlash && hurtFlashImage != null)
        {
            if (hurtFlashRoutine != null) StopCoroutine(hurtFlashRoutine);
            hurtFlashRoutine = StartCoroutine(HurtFlashRoutine());
        }
    }

    IEnumerator HurtFlashRoutine()
    {
        SetFlashAlpha(hurtFlashPeakAlpha);
        float elapsed = 0f;
        while (elapsed < hurtFlashDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetFlashAlpha(Mathf.Lerp(hurtFlashPeakAlpha, 0f, elapsed / hurtFlashDuration));
            yield return null;
        }
        SetFlashAlpha(0f);
        hurtFlashRoutine = null;
    }

    void SetFlashAlpha(float alpha)
    {
        Color c = hurtFlashColor;
        c.a = alpha;
        hurtFlashImage.color = c;
        hurtFlashImage.gameObject.SetActive(alpha > 0f);
    }
}
