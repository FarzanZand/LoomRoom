using System;
using System.Collections;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

// The timing and sounds of a boss's name being typed out before the battle, kept on the boss fight
// (BossFight > Intro > Boss title).
[Serializable]
public class BossTitleSettings
{
    [FoldoutGroup("Boss title"), Min(1), Tooltip("Letters a second as the name is typed out.")] public float lettersPerSecond = 11f;
    [FoldoutGroup("Boss title"), Min(0), Tooltip("Seconds the subtitle (the boss's title) takes to fade in once the name is typed.")] public float subtitleFade = .4f;
    [FoldoutGroup("Boss title"), Min(0), Tooltip("Seconds the whole of it stays up once typed.")] public float hold = 1.4f;
    [FoldoutGroup("Boss title"), Min(.05f), Tooltip("Seconds it takes to fade away as the battle begins.")] public float fadeOut = .5f;
    [FoldoutGroup("Boss title"), Tooltip("Played on each letter (one picked each time).")] public AudioClip[] letterSound = new AudioClip[0];
    [FoldoutGroup("Boss title"), Range(0, 1)] public float letterVolume = .8f;
    [FoldoutGroup("Boss title"), Tooltip("The letters' pitch (lower: heavier).")] public float letterPitch = .7f;
    [FoldoutGroup("Boss title"), Range(0, .3f)] public float letterPitchVariance = .05f;
}

// The boss's name before the battle (BossFight > Intro): large, centred in the upper half of the screen, typed out
// letter by letter with a heavy key sound while the fight's music starts; its title fades in beneath, it holds, and it
// fades away as the battle begins. On real time. Its timing and sounds come from the fight (BossTitleSettings); its look
// is the Boss dialogue prefab's (Dialogue/UI, under the Dialogue Manager).
public class BossTitleUI : MonoBehaviour
{
    public static BossTitleUI Current { get; private set; }

    [SerializeField] CanvasGroup group;
    [SerializeField] TMP_Text nameText, subtitle;
    [Tooltip("Canvas units each letter drops in from as it is typed.")] public float letterDrop = 18f;

    bool playing;
    public static bool Covers => Current != null && Current.playing;

    void Awake() { Current = this; group.alpha = 0f; group.blocksRaycasts = false; }
    void OnDestroy() { if (Current == this) Current = null; }

    // Started runs as the first letter is typed (BossStageEncounter starts the fight's music there).
    public IEnumerator Play(string name, string title, BossTitleSettings s, Action started = null)
    {
        s ??= new BossTitleSettings();
        playing = true;
        nameText.text = (name ?? "").ToUpperInvariant();
        subtitle.text = title ?? "";
        nameText.maxVisibleCharacters = 0;
        subtitle.alpha = 0f;
        group.alpha = 1f;
        nameText.ForceMeshUpdate();
        int total = nameText.textInfo.characterCount;
        started?.Invoke();
        float t0 = Time.unscaledTime;
        int shown = 0;
        while (shown < total)
        {
            int due = Mathf.Min(total, 1 + Mathf.FloorToInt((Time.unscaledTime - t0) * s.lettersPerSecond));
            while (shown < due)
            {
                char c = nameText.textInfo.characterInfo[shown].character;
                shown++;
                if (!char.IsWhiteSpace(c)) Letter(s);
            }
            nameText.maxVisibleCharacters = shown;
            Drop(shown, (Time.unscaledTime - t0) * s.lettersPerSecond);
            yield return null;
        }
        // The last letters settle while the title fades in.
        for (float t = 0f; t < Mathf.Max(s.subtitleFade, .2f); t += Time.unscaledDeltaTime)
        {
            Drop(total, (Time.unscaledTime - t0) * s.lettersPerSecond);
            subtitle.alpha = s.subtitleFade > 0f ? Mathf.Clamp01(t / s.subtitleFade) : 1f;
            yield return null;
        }
        Drop(total, float.MaxValue);
        subtitle.alpha = 1f;
        yield return Wait(s.hold);
        for (float t = 0f; t < s.fadeOut; t += Time.unscaledDeltaTime) { group.alpha = 1f - t / s.fadeOut; yield return null; }
        group.alpha = 0f;
        playing = false;
    }

    static IEnumerator Wait(float seconds) { for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null; }

    // Each letter drops into place over its first moment on screen (age: letters typed so far, as a running count).
    void Drop(int shown, float age)
    {
        var info = nameText.textInfo;
        nameText.ForceMeshUpdate();
        for (int i = 0; i < shown && i < info.characterCount; i++)
        {
            var ch = info.characterInfo[i];
            if (!ch.isVisible) continue;
            float k = Mathf.Clamp01((age - i) / 1.5f);   // a letter and a half's time to land
            float lift = letterDrop * (1f - k) * (1f - k);
            var verts = info.meshInfo[ch.materialReferenceIndex].vertices;
            for (int v = 0; v < 4; v++) verts[ch.vertexIndex + v] += Vector3.up * lift;
        }
        nameText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }

    void Letter(BossTitleSettings s)
    {
        if (s.letterSound == null || s.letterSound.Length == 0 || !AudioManager.HasInstance) return;
        var clip = s.letterSound[UnityEngine.Random.Range(0, s.letterSound.Length)];
        if (clip != null) AudioManager.Instance.PlayUI(clip, s.letterVolume, s.letterPitch * (1f + UnityEngine.Random.Range(-s.letterPitchVariance, s.letterPitchVariance)));
    }
}
