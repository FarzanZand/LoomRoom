using UnityEngine;

// A milestone fight. Added by the generator to the floor root: the boss waits in the exit
// room; when the player enters that room (or the boss notices them) the sting plays, the
// boss bar appears, the music changes and the boss attacks. The stairs stay sealed until
// the boss falls.
public class DungeonBossEncounter : MonoBehaviour
{
    DungeonGenerator dungeon;
    DungeonMilestone milestone;
    Character boss;
    EnemyBrain brain;
    RectInt room;
    DungeonExit exit;
    bool started, finished;

    public Character Boss => boss;
    public bool Started => started;

    public void Initialize(DungeonGenerator generator, DungeonMilestone data, Character bossCharacter, RectInt arena, DungeonExit sealedExit)
    {
        dungeon = generator; milestone = data; boss = bossCharacter; room = arena; exit = sealedExit;
        brain = boss != null ? boss.GetComponent<EnemyBrain>() : null;
        if (boss != null) boss.Died += OnBossDied;
        if (brain != null) brain.StateChanged += OnBossState;
        if (exit != null && milestone.sealExit) exit.Sealed = true;
    }

    void OnDestroy()
    {
        if (boss != null) boss.Died -= OnBossDied;
        if (brain != null) brain.StateChanged -= OnBossState;
        if (started && !finished)
        {
            if (BossBarUI.HasInstance) BossBarUI.Instance.Hide();
        }
    }

    void Update()
    {
        if (started || finished || dungeon == null || dungeon.Layout == null || boss == null) return;
        if (!PlayerManager.HasInstance || PlayerManager.Instance.ActiveKind != PlayerKind.Table) return;
        var player = PlayerManager.Instance.Active;
        if (player == null || !player.IsAlive) return;
        // Inside the arena's rectangle, one cell in from the doorway so the sting lands as you step in.
        var cell = dungeon.CellOf(player.transform.position);
        var inner = new RectInt(room.x + 1, room.y + 1, Mathf.Max(1, room.width - 2), Mathf.Max(1, room.height - 2));
        if (inner.Contains(cell)) Begin();
    }

    void OnBossState(EnemyState previous, EnemyState next)
    {
        if (!started && (next == EnemyState.Chase || next == EnemyState.Attack)) Begin();
    }

    void Begin()
    {
        if (started || boss == null || !boss.IsAlive) return;
        started = true;
        if (AudioManager.HasInstance)
        {
            if (milestone.introSting != null) AudioManager.Instance.PlaySFX2D(milestone.introSting, milestone.stingVolume);
            if (milestone.bossMusic != null) AudioManager.Instance.CrossfadeMusic(milestone.bossMusic, true, 1.2f, milestone.bossMusicVolume);
        }
        if (BossBarUI.HasInstance) BossBarUI.Instance.Show(boss, milestone.title);
        if (!string.IsNullOrWhiteSpace(milestone.introMessage)) MessageLog.Post(milestone.introMessage.Trim(), MessageKind.Warning);
        MessageLog.Post($"The {boss.DisplayName} awakens!", MessageKind.Bad);
        if (NotificationUI.HasInstance) NotificationUI.Show(boss.DisplayName.ToUpperInvariant());
        brain?.Alert();
    }

    void OnBossDied()
    {
        if (finished) return;
        finished = true;
        if (BossBarUI.HasInstance) BossBarUI.Instance.Hide();
        if (exit != null) exit.Sealed = false;
        if (RunManager.HasInstance) RunManager.Instance.NoteBossSlain();
        MessageLog.Post($"The {boss.DisplayName} falls! The way down is open.", MessageKind.Good);
        if (AudioManager.HasInstance && dungeon != null)
        {
            var music = dungeon.LevelData.MusicFor(dungeon.FloorNumber, out float volume);
            if (music != null) AudioManager.Instance.CrossfadeMusic(music, dungeon.LevelData.loopMusic, 3f, volume);
            else AudioManager.Instance.StopMusic(3f);
        }
    }
}
