using DG.Tweening;
using UnityEngine;

// Sneaking (the table player crouched): an eye on the HUD, open and orange while an enemy sees them, shut while none
// does. Crouched, the ghost's look in grey comes over the view (look, Stealth look: Ghost look with its tints made
// neutral grey) at no more than half strength: a little of it while crouched, the half while hidden (seen by no one);
// it eases back as they are seen or stand up. Sound is left as it is (the low-pass muffle was removed on 2026-10-30,
// the user's call). The look's settings are on ScreenManager (Stealth look).
public class StealthIndicatorUI : MonoBehaviour
{
    [SerializeField] CanvasGroup visibility;
    [SerializeField] StealthEyeGraphic eye;
    [SerializeField] Color hiddenColor = new(.85f, .82f, .72f), seenColor = new(.95f, .58f, .35f);
    [SerializeField, Min(0)] float transitionDuration = .18f;
    Tween transition;
    bool? lastSeen;
    float hiddenAmount, crouchAmount;
    UnityEngine.Rendering.Volume volume;

    void OnEnable() { if (visibility != null) visibility.alpha = 0; lastSeen = null; }
    void OnDisable()
    {
        transition?.Kill(); if (visibility != null) visibility.alpha = 0; lastSeen = null;
        hiddenAmount = crouchAmount = 0f; Apply();
    }

    void OnDestroy() { if (volume != null) Destroy(volume.gameObject); }

    void Update()
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        bool sneaking = player != null && player.kind == PlayerKind.Table && player.IsActive && player.IsAlive && player.Motor != null && player.Motor.IsCrouching;
        // A Shadow Veil counts as sneaking unseen (the eye shows, shut), standing or not.
        bool veiled = player != null && player.IsAlive && ShadowVeil.IsVeiled(player);
        bool seen = false;
        if (sneaking)
            foreach (var brain in EnemyBrain.Active)
                if (brain != null && brain.Character != null && brain.Character.IsAlive && brain.Perception != null && brain.Perception.Target == player && brain.Perception.TargetVisible) { seen = true; break; }
        // Co-op: a guest's enemies are the host's; whom they see comes with their snapshots.
        if (sneaking && !seen && Coop.IsGuest) seen = CoopWorld.LocalSeen();
        bool exploring = !(GameManager.HasInstance && GameManager.Instance.State != GameState.Explore);
        bool hidden = (sneaking && !seen || veiled) && exploring;
        var screen = ScreenManager.HasInstance ? ScreenManager.Instance : null;
        float hiddenFade = screen != null ? screen.stealthFade : .5f;
        hiddenAmount = Mathf.MoveTowards(hiddenAmount, hidden ? 1f : 0f, Time.unscaledDeltaTime / hiddenFade);
        crouchAmount = Mathf.MoveTowards(crouchAmount, sneaking && exploring ? 1f : 0f, Time.unscaledDeltaTime / hiddenFade);
        Apply();

        if (eye == null || visibility == null) return;
        // Only while exploring: behind the shop, the bag or a recap the shut eye was a stray white line under the middle.
        visibility.alpha = (sneaking || veiled) && exploring ? 1 : 0;
        if (!sneaking && !veiled) { transition?.Kill(); lastSeen = null; return; }
        if (lastSeen == seen) return;
        transition?.Kill();
        eye.color = seen ? seenColor : hiddenColor;
        float target = seen ? 1 : 0;
        if (!lastSeen.HasValue) eye.Openness = target;
        else transition = DOTween.To(() => eye.Openness, value => eye.Openness = value, target, transitionDuration).SetEase(Ease.OutCubic).SetUpdate(true);
        lastSeen = seen;
    }

    void Apply()
    {
        var screen = ScreenManager.HasInstance ? ScreenManager.Instance : null;
        var look = screen != null ? screen.stealthLook : null;
        float hiddenLook = screen != null ? screen.stealthHiddenLook : 0f, crouchedLook = screen != null ? screen.stealthCrouchedLook : 0f;
        float k = hiddenAmount * hiddenAmount * (3f - 2f * hiddenAmount);
        float c = crouchAmount * crouchAmount * (3f - 2f * crouchAmount);
        // The look: a little while crouched, more while hidden; steady, no pulsing.
        float weight = Mathf.Lerp(c * crouchedLook, hiddenLook, k);
        if (look != null && (weight > 0f || volume != null))
        {
            if (volume == null)
            {
                // Its own object on the default layer (cameras' volume masks take it; the HUD's UI layer they may not).
                volume = new GameObject("Stealth look").AddComponent<UnityEngine.Rendering.Volume>();
                volume.gameObject.layer = 0;
                volume.isGlobal = true; volume.priority = 40f; volume.sharedProfile = look;
            }
            volume.weight = weight;
            volume.enabled = weight > 0f;
        }
    }
}
