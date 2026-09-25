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
    Player player;

    void Start()
    {
        player = GetComponentInParent<Player>();
        if (player == null) return;
        if (player.Combat != null) player.Combat.ChargeReady += OnChargeReady;
    }

    void OnDestroy()
    {
        if (player != null && player.Combat != null) player.Combat.ChargeReady -= OnChargeReady;
    }

    // Camera charge starts after the tap grace period, so light attacks don't pulse the view.
    void Update()
    {
        if (player == null || player.CameraRig == null || player.Combat == null) return;
        float charge = player.Combat.CameraCharge;
        var tuning = CombatManager.HasInstance ? CombatManager.Instance : null;
        player.CameraRig.FovOffset      = -(tuning != null ? tuning.chargeFovPull : 4f) * player.Combat.CameraZoomCharge;
        player.CameraRig.ExtraAmplitude =  (tuning != null ? tuning.chargeShake   : .45f) * charge;
    }

    void OnChargeReady()
    {
        if (!CombatManager.HasInstance) return;
        var tuning = CombatManager.Instance;
        KickDirectional(tuning.chargeReadyKick, Vector3.up);
        if (tuning.chargeReadyAudio != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData2D(tuning.chargeReadyAudio);
    }

    void KickDirectional(float amount, Vector3 cameraLocalDir)
    {
        if (amount <= 0f || player == null || !player.IsActive || player.CameraRig == null || player.CameraRig.Camera == null || onHitImpulse == null) return;
        onHitImpulse.GenerateImpulseWithVelocity(player.CameraRig.Camera.transform.TransformDirection(cameraLocalDir) * amount);
    }

    public override void NotifyHitLanded(DamageInfo info)
    {
        base.NotifyHitLanded(info);
        Kick(CombatManager.HasInstance ? CombatManager.Instance.landedCameraKick : .65f,info);
        if (player != null && player.Combat != null && player.Combat.HeavySwing && CombatManager.HasInstance)
            player.Combat.Shudder(CombatManager.Instance.heavyImpactShudder);
    }

    public override void NotifyHurtReceived(DamageInfo info)
    {
        base.NotifyHurtReceived(info);
        Kick(CombatManager.HasInstance ? (info.Blocked ? CombatManager.Instance.blockCameraKick : CombatManager.Instance.hurtCameraKick) : 2f,info);

        if ((info.Amount>0 || info.Blocked) && enableHurtFlash && hurtFlashImage != null)
        {
            if (hurtFlashRoutine != null) StopCoroutine(hurtFlashRoutine);
            Color color = hurtFlashColor;
            float peak = hurtFlashPeakAlpha, duration = hurtFlashDuration;
            if(CombatManager.HasInstance)
            {
                var tuning=CombatManager.Instance;
                color=info.Blocked ? tuning.blockScreenColor : tuning.hurtScreenColor;
                peak=tuning.hurtScreenAlpha*(info.Blocked ? .45f : 1f);
                duration=tuning.hurtScreenDuration;
            }
            hurtFlashRoutine = StartCoroutine(HurtFlashRoutine(color, peak, duration));
        }
    }

    void Kick(float amount,DamageInfo info)
    {
        if(player==null || !player.IsActive || player.CameraRig==null || player.CameraRig.Camera==null)return;
        // Camera shake is handled by Cinemachine impulses; character motion stays in clips.
        var impulse=info.Source==player ? onHitImpulse : onHurtImpulse;
        if(impulse!=null) impulse.GenerateImpulseWithForce(amount);
    }
    void OnDisable()
    {
        if(hurtFlashRoutine!=null) StopCoroutine(hurtFlashRoutine);
        hurtFlashRoutine=null;
        if(hurtFlashImage!=null) SetFlashAlpha(hurtFlashColor, 0f);
    }

    IEnumerator HurtFlashRoutine(Color color, float peak, float duration)
    {
        SetFlashAlpha(color, peak);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetFlashAlpha(color, Mathf.Lerp(peak, 0f, elapsed / duration));
            yield return null;
        }
        SetFlashAlpha(color, 0f);
        hurtFlashRoutine = null;
    }

    void SetFlashAlpha(Color color, float alpha)
    {
        color.a = alpha;
        hurtFlashImage.color = color;
        hurtFlashImage.gameObject.SetActive(alpha > 0f);
    }
}
