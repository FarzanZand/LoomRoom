using UnityEngine;

// The floor's background bed and occasional distant sounds, from its DungeonTheme. Plays
// only while the table player is active; the room player hears the apartment instead.
public class DungeonAmbience : MonoBehaviour
{
    DungeonTheme theme;
    bool playing;
    float nextOneShot;

    public void Initialize(DungeonTheme floorTheme)
    {
        theme = floorTheme;
        Schedule();
    }

    void Schedule()
    {
        if (theme == null) return;
        float min = Mathf.Max(1f, theme.oneShotInterval.x), max = Mathf.Max(min, theme.oneShotInterval.y);
        nextOneShot = Time.time + Random.Range(min, max);
    }

    void Update()
    {
        if (theme == null || !AudioManager.HasInstance) return;
        bool active = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table;
        if (active != playing)
        {
            playing = active;
            if (active) AudioManager.Instance.PlayAmbience(theme.ambience, theme.ambienceVolume);
            else AudioManager.Instance.StopAmbience();
        }
        if (!active || Time.time < nextOneShot) return;
        Schedule();
        var clip = CreatureAudioEntry.Pick(theme.ambientOneShots);
        var player = PlayerManager.Instance.Active;
        if (clip == null || player == null) return;
        // Somewhere off to the side, far enough to feel like another room.
        Vector3 offset = Quaternion.Euler(0, Random.Range(0, 360f), 0) * Vector3.forward * Random.Range(8f, 16f);
        AudioManager.Instance.PlaySFX(clip, player.transform.position + offset + Vector3.up * 2f, theme.oneShotVolume, 1f, .1f, 4f, 30f);
    }

    void OnDisable()
    {
        if (playing && AudioManager.HasInstance) AudioManager.Instance.StopAmbience();
        playing = false;
    }
}
