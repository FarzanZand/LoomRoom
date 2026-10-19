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
    bool silenced;   // Silence(): no music here; the floor's track comes back once the player is out (or the pocket goes)

    void Start()
    {
        dungeon = GetComponentInParent<DungeonGenerator>();
        if (dungeon != null && dungeon.Layout != null) room = dungeon.Layout.RoomAt(dungeon.CellOf(transform.position));
    }

    void Update()
    {
        // A pocket finishes laying out after this starts: find the room once its layout exists.
        if (room < 0 && dungeon != null && dungeon.Layout != null) room = dungeon.Layout.RoomAt(dungeon.CellOf(transform.position));
        if (music == null || room < 0 || dungeon == null || !AudioManager.HasInstance || !PlayerManager.HasInstance) return;
        var player = PlayerManager.Instance.ActiveKind == PlayerKind.Table ? PlayerManager.Instance.Active : null;
        bool inside = player != null && player.IsAlive && dungeon.Layout.RoomAt(dungeon.CellOf(player.transform.position)) == room;
        if (silenced)
        {
            // In a pocket the silence lasts until the player goes back through the way back (the pocket is then taken down).
            bool pocket = dungeon.LevelData != null && dungeon.LevelData.pocket;
            if (!inside && !pocket) { silenced = false; End(); }
            return;
        }
        if (inside && !playing) Begin();
        else if (!inside && playing) End();
        // Something else changed the track while the player is here (a merchant fading back as they left it): take it back.
        else if (inside && AudioManager.Instance.CurrentMusic != music && Time.time >= retakeAt) { retakeAt = Time.time + fade + .5f; AudioManager.Instance.CrossfadeMusic(music, true, fade, volume); }
    }

    // Gone with its floor: the floor's track only comes back while the player is still on the table (after a death or
    // the end of a run the room's own music is already playing, and this must not start the dungeon's again over it).
    void OnDisable()
    {
        if (!playing && !silenced) return;
        silenced = false;
        if (OnTable) End(); else playing = false;
    }
    static bool OnTable => PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table;

    // The music stops at once (and a sting plays over the silence); the floor's track returns as the player leaves.
    public void Silence(AudioClip sting, float stingVolume = 1f)
    {
        if (!AudioManager.HasInstance) return;
        var audio = AudioManager.Instance;
        if (!playing) { floorMusic = audio.CurrentMusic; floorMusicTime = audio.CurrentMusicTime; floorMusicVolume = audio.CurrentMusicVolume; floorMusicLoops = audio.CurrentMusicLoops; }
        playing = false; silenced = true;
        audio.StopMusic(0f);
        if (sting != null) audio.PlaySFX2D(sting, stingVolume);
    }

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
