using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

// Player-only feel on top of CharacterFX: camera impulses and the screen hurt flash.
// Resolved damage requests hit stop through CombatManager once per impact.
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
        Kick(CombatManager.HasInstance ? CombatManager.Instance.landedCameraKick : .65f,info);
    }

    public override void NotifyHurtReceived(DamageInfo info)
    {
        base.NotifyHurtReceived(info);
        Kick(CombatManager.HasInstance ? (info.Blocked ? CombatManager.Instance.blockCameraKick : CombatManager.Instance.hurtCameraKick) : 2f,info);

        if ((info.Amount>0 || info.Blocked) && enableHurtFlash && hurtFlashImage != null)
        {
            if (hurtFlashRoutine != null) StopCoroutine(hurtFlashRoutine);
            if(CombatManager.HasInstance)
            {
                var tuning=CombatManager.Instance;
                hurtFlashColor=info.Blocked ? tuning.blockScreenColor : tuning.hurtScreenColor;
                hurtFlashPeakAlpha=tuning.hurtScreenAlpha*(info.Blocked ? .45f : 1f);
                hurtFlashDuration=tuning.hurtScreenDuration;
            }
            hurtFlashRoutine = StartCoroutine(HurtFlashRoutine());
        }
    }

    void Kick(float amount,DamageInfo info)
    {
        var player=GetComponentInParent<Player>();
        if(player==null || !player.IsActive || player.CameraRig==null || player.CameraRig.Camera==null)return;
        // Camera shake is handled by Cinemachine impulses; character motion stays in clips.
        var impulse=info.Source==player ? onHitImpulse : onHurtImpulse;
        if(impulse!=null) impulse.GenerateImpulseWithForce(amount);
    }
    void OnDisable()
    {
        if(hurtFlashRoutine!=null) StopCoroutine(hurtFlashRoutine);
        hurtFlashRoutine=null;
        if(hurtFlashImage!=null) SetFlashAlpha(0);
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
