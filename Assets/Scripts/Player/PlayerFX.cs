using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

// Player-only feel on top of CharacterFX: camera impulses and the screen hurt flash.
// Hit stop is requested by Hitbox through CombatManager, not here, so it fires once.
public class PlayerFX : CharacterFX
{
    [Header("Camera")]
    [SerializeField] CinemachineImpulseSource onHitImpulse;
    [SerializeField] CinemachineImpulseSource onHurtImpulse;

    [Header("Hurt Flash")]
    [SerializeField] bool  enableHurtFlash = true;
    [SerializeField] Image hurtFlashImage;
    [SerializeField] Color hurtFlashColor  = new Color(1f, 0f, 0f, 1f);
    [SerializeField, Range(0f, 1f)] float hurtFlashPeakAlpha = 0.35f;
    [SerializeField] float hurtFlashDuration = 0.3f;

    Coroutine hurtFlashRoutine;

    public override void NotifyHitLanded(DamageInfo info)
    {
        base.NotifyHitLanded(info);
        onHitImpulse?.GenerateImpulse();
    }

    public override void NotifyHurtReceived(DamageInfo info)
    {
        base.NotifyHurtReceived(info);
        onHurtImpulse?.GenerateImpulse();

        if (!info.Blocked && enableHurtFlash && hurtFlashImage != null)
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
