using UnityEngine;

// A room template's own music: on the template's root, it plays while the table player is inside that room and the
// floor's track comes back, from where it was, when they leave (as a merchant's shop music does).
public class RoomMusic : MonoBehaviour
{
    [Tooltip("The room's track. Empty: the floor's music plays on.")]
    public AudioClip music;
    [Range(0f, 1f)] public float volume = 1f;
    [Min(0f)] public float fade = 1.5f;

    DungeonGenerator dungeon;
    int room = -1;
    bool playing;
    AudioClip floorMusic;
    float floorMusicTime, floorMusicVolume;
    bool floorMusicLoops;
    float retakeAt;

    void Start()
    {
        dungeon = GetComponentInParent<DungeonGenerator>();
        if (dungeon != null && dungeon.Layout != null) room = dungeon.Layout.RoomAt(dungeon.CellOf(transform.position));
    }

    void Update()
    {
        if (music == null || room < 0 || dungeon == null || !AudioManager.HasInstance || !PlayerManager.HasInstance) return;
        var player = PlayerManager.Instance.ActiveKind == PlayerKind.Table ? PlayerManager.Instance.Active : null;
        bool inside = player != null && player.IsAlive && dungeon.Layout.RoomAt(dungeon.CellOf(player.transform.position)) == room;
        if (inside && !playing) Begin();
        else if (!inside && playing) End();
        // Something else changed the track while the player is here (a merchant fading back as they left it): take it back.
        else if (inside && AudioManager.Instance.CurrentMusic != music && Time.time >= retakeAt) { retakeAt = Time.time + fade + .5f; AudioManager.Instance.CrossfadeMusic(music, true, fade, volume); }
    }

    void OnDisable() { if (playing) End(); }

    void Begin()
    {
        var audio = AudioManager.Instance;
        floorMusic = audio.CurrentMusic; floorMusicTime = audio.CurrentMusicTime;
        floorMusicVolume = audio.CurrentMusicVolume; floorMusicLoops = audio.CurrentMusicLoops;
        audio.CrossfadeMusic(music, true, fade, volume);
        playing = true;
    }

    void End()
    {
        playing = false;
        if (!AudioManager.HasInstance) return;
        if (floorMusic != null) AudioManager.Instance.CrossfadeMusicAt(floorMusic, floorMusicTime, floorMusicLoops, fade, floorMusicVolume);
        else AudioManager.Instance.StopMusic(fade);
    }
}
