using UnityEngine;

// The floor's background bed and occasional distant sounds, from its DungeonBiome. Plays
// only while the table player is active; the room player hears the apartment instead.
public class DungeonAmbience : MonoBehaviour
{
    DungeonBiome biome;
    bool playing;
    float nextOneShot;

    public void Initialize(DungeonBiome floorBiome)
    {
        biome = floorBiome;
        Schedule();
    }

    void Schedule()
    {
        if (biome == null) return;
        float min = Mathf.Max(1f, biome.oneShotInterval.x), max = Mathf.Max(min, biome.oneShotInterval.y);
        nextOneShot = Time.time + Random.Range(min, max);
    }

    void Update()
    {
        if (biome == null || !AudioManager.HasInstance) return;
        bool active = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table;
        if (active != playing)
        {
            playing = active;
            if (active) AudioManager.Instance.PlayAmbience(biome.ambience, biome.ambienceVolume);
            else AudioManager.Instance.StopAmbience();
        }
        if (!active || Time.time < nextOneShot) return;
        Schedule();
        var clip = CreatureAudio.Pick(biome.ambientOneShots);
        var player = PlayerManager.Instance.Active;
        if (clip == null || player == null) return;
        // Somewhere off to the side, far enough to feel like another room.
        Vector3 offset = Quaternion.Euler(0, Random.Range(0, 360f), 0) * Vector3.forward * Random.Range(8f, 16f);
        AudioManager.Instance.PlaySFX(clip, player.transform.position + offset + Vector3.up * 2f, biome.oneShotVolume, 1f, .1f, 4f, 30f);
    }

    void OnDisable()
    {
        if (playing && AudioManager.HasInstance) AudioManager.Instance.StopAmbience();
        playing = false;
    }
}
