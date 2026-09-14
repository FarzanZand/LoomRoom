using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

// The scripted table reveal, as a list of beat-timed steps instead of a hand-written
// coroutine. Each step waits N beats of the intro music, then does everything it has
// filled in. Add or reorder steps in the Inspector.
public class TableIntroController : MonoBehaviour
{
    [Serializable]
    public class BeatStep
    {
        [Tooltip("Beats to wait before this step runs.")]
        public float beats = 4f;
        [Tooltip("Objects switched on at this step.")]
        public GameObject[] activate;
        [Tooltip("Sound played (2D).")]
        public AudioData sfx;
        [Tooltip("Or a key from the AudioManager SFX library.")]
        public string sfxKey;
        [Tooltip("Animator + trigger fired at this step.")]
        public Animator animator;
        public string animatorTrigger;
        [Tooltip("NPC snapped to face the player at this step.")]
        public NpcBrain npcFacePlayer;
        [Tooltip("ObjectController whose move starts at this step.")]
        public ObjectController startMove;
        [Tooltip("Fade the directional light at this step.")]
        public bool fadeLight;
        [ShowIf("fadeLight")] public float lightIntensity = 1.4f;
        [ShowIf("fadeLight")] public Color lightColor = Color.white;
        [ShowIf("fadeLight")] public float lightFadeDuration = 3f;
        [Tooltip("Hand control back to the player at this step.")]
        public bool unfreezePlayer;
        public UnityEvent onStep;
    }

    [Header("Music")]
    public AudioClip tableIntroAudio;
    public float musicBPM = 120f;

    [Header("Opening")]
    [Tooltip("Screen fade in/hold/out when the intro starts.")]
    public float fadeInDuration = 2f, fadedDuration = 3f, fadeOutDuration = 5f;
    [Tooltip("Beats before the player is swapped to the table and the music starts.")]
    public float beatsBeforeSwap = 8f;

    [Header("Steps (run after the swap, in order)")]
    [ListDrawerSettings(ShowFoldout = true, NumberOfItemsPerPage = 20)]
    public List<BeatStep> steps = new();

    float BeatSeconds => musicBPM > 0f ? 60f / musicBPM : 0.5f;

    public void PlayTableIntro() => StartCoroutine(Routine());

    IEnumerator Routine()
    {
        var pm = PlayerManager.Instance;
        var tm = TableManager.Instance;

        if (ProgressionManager.HasInstance) ProgressionManager.Instance.tableEntered = true;
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeInOut(fadeInDuration, fadedDuration, fadeOutDuration);

        yield return new WaitForSeconds(BeatSeconds * beatsBeforeSwap);

        if (tm != null && tm.DM != null && tm.dmPlacement != null)
            tm.DM.transform.rotation = tm.dmPlacement.rotation;

        pm?.SwapToPlayer(PlayerKind.Table);
        pm?.SetControlsFrozen(true);
        if (tableIntroAudio != null && AudioManager.HasInstance) AudioManager.Instance.PlayMusic(tableIntroAudio);

        foreach (var step in steps)
        {
            yield return new WaitForSeconds(BeatSeconds * step.beats);
            RunStep(step);
        }
    }

    void RunStep(BeatStep step)
    {
        if (step.activate != null)
            foreach (var go in step.activate) if (go != null) go.SetActive(true);

        if (AudioManager.HasInstance)
        {
            if (step.sfx != null) AudioManager.Instance.PlaySFXData2D(step.sfx);
            else if (!string.IsNullOrEmpty(step.sfxKey)) AudioManager.Instance.PlaySFX2D(step.sfxKey);
        }

        if (step.animator != null && !string.IsNullOrEmpty(step.animatorTrigger))
            step.animator.SetTrigger(step.animatorTrigger);

        if (step.npcFacePlayer != null) step.npcFacePlayer.SnapRotationTowardsPlayer();
        if (step.startMove != null) step.startMove.StartMove();

        if (step.fadeLight && WorldManager.HasInstance)
        {
            WorldManager.Instance.FadeDirectionalLight(step.lightIntensity, step.lightFadeDuration);
            WorldManager.Instance.FadeDirectionalLightColor(step.lightColor, step.lightFadeDuration);
        }

        if (step.unfreezePlayer) PlayerManager.Instance?.SetControlsFrozen(false);

        step.onStep?.Invoke();
    }
}
