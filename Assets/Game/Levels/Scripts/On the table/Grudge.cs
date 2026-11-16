using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The one that got you. A death used to leave nothing behind in the dungeon: the next run's floors were new and so was
// everyone on them. Now the floor you last died on keeps one of your killer's kind waiting. It is no stronger than the
// others (no stat is touched): it is only pleased with itself. Its name says so on the enemy bar ("Smug Blue Hood
// Skeleton"), the Dungeon Master points it out when you meet and puts a small bounty on it, and killing it pays the
// bounty and settles it ("Even."). Dying to it again makes it the next run's grudge too; walking past it leaves it
// for the next run. It says nothing itself: the enemies' own lines over their heads were taken out (the user,
// 2026-11-16).
// The death is read from the save's memorials (the last one: killer and floor), so it holds across sessions; a death
// taken back by a revive leaves no memorial and no grudge. Only the rank and file: never a boss or anyone with a name
// of their own, never an elite, never in the intro's practice run. Which death is settled is one saved flag.
// It is not in the scene: at Play it is made from Levels/Resources/Grudge (the prefab holds its settings) under the
// RunManager, like the floor's bet.
public class Grudge : MonoBehaviour
{
    [Tooltip("Off: no grudges.")] public bool grudgeOn = true;
    [Tooltip("Goes in front of its name on the enemy bar and in the recap, should it kill you again.")] public string prefix = "Smug";
    [Min(0), Tooltip("Gold paid for killing it.")] public int bounty = 20;
    [Min(1), Tooltip("The player has met it once it is this close with nothing in between (metres), or as soon as it comes for them.")] public float meetWithin = 9f;
    [Min(0), Tooltip("Seconds between meeting it and the Dungeon Master pointing it out.")] public float pointDelay = 1.6f;
    [Tooltip("The Dungeon Master, pointing it out. {killer} is its kind, {gold} the bounty.")]
    public List<string> pointLines = new()
    {
        "That one got you last time. *{gold} gold* if you get even.",
        "Your killer is still here. *{gold} gold* if you return the favour.",
        "See the smug one? It killed you last time. *{gold} gold* for it.",
    };
    [Tooltip("The Dungeon Master as it dies. {gold} is the bounty.")]
    public List<string> evenLines = new() { "Even. *{gold} gold.*", "That settles it. *{gold} gold.*", "It was getting smug. *{gold} gold.*" };
    [Tooltip("The same, when it died before you had met.")]
    public List<string> evenUnmetLines = new() { "That was the one that got you last time. *{gold} gold.*" };

    public const string SettledFlag = "grudge.settled";
    static Grudge current;

    const string PrefabName = "Grudge";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Register()
    {
        current = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnScene;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnScene;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void AfterFirstScene() => OnScene(default, default);
    static void OnScene(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (current != null || !RunManager.HasInstance) return;
        var prefab = Resources.Load<GameObject>(PrefabName);
        if (prefab != null) Instantiate(prefab, RunManager.Instance.transform).name = prefab.name;
    }

    Player player;
    DungeonGenerator floorOf;
    int floorNumber;
    bool looked, met;
    EnemyBrain target;
    Character targetCharacter;
    string kind;      // its name without the prefix
    int deathKey;     // which death it stands for (the settled flag's value once it is dead)

    void OnEnable() { current = this; Character.AnyDied += OnAnyDied; }

    void OnDisable()
    {
        if (current == this) current = null;
        Character.AnyDied -= OnAnyDied;
        player = null;
    }

    void Update()
    {
        var run = RunManager.HasInstance ? RunManager.Instance : null;
        var dungeon = TableManager.Loader != null ? TableManager.Loader.Dungeon : null;
        if (!grudgeOn || run == null || !run.Running || dungeon == null || IntroController.PracticeRunning) { floorOf = null; Forget(); return; }
        if (run.Ended) return;   // the player is down: it stands as it is (a revive goes on with it)
        if (dungeon != floorOf || run.Floor != floorNumber) { floorOf = dungeon; floorNumber = run.Floor; looked = false; Forget(); return; }
        if (!Free()) return;
        if (!looked) { looked = true; Pick(run, dungeon); return; }
        if (target == null || met || Time.frameCount % 6 != 0) return;
        if (Meets()) Meet();
    }

    bool Free()
    {
        if (!GameManager.HasInstance || GameManager.Instance.State != GameState.Explore) return false;
        if (!PlayerManager.HasInstance || PlayerManager.Instance.ActiveKind != PlayerKind.Table) return false;
        if (player == null) player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        return player != null && player.IsAlive;
    }

    void Forget()
    {
        // Its name goes back: a floor's enemies can outlive the run for a moment (the flight out).
        if (targetCharacter != null && targetCharacter.namePrefix == prefix) targetCharacter.namePrefix = null;
        target = null; targetCharacter = null; met = false;
    }

    // ── Who ───────────────────────────────────────────────────────────

    // The last death, when it was on this floor and is not settled: the nearest living enemy of the killer's kind.
    void Pick(RunManager run, DungeonGenerator dungeon)
    {
        var save = player.GetComponent<AdventureSave>();
        var deaths = save != null && save.Saved != null ? save.Saved.memorials : null;
        AdventureSave.Memorial last = null;
        if (deaths != null) for (int i = deaths.Count - 1; i >= 0; i--) if (deaths[i] != null && !deaths[i].forgotten) { last = deaths[i]; break; }
        if (last == null || string.IsNullOrWhiteSpace(last.killer) || last.floor != run.Floor) return;
        int key = Key(last.id);
        if (ProgressionManager.HasInstance && ProgressionManager.Instance.GetFlag(SettledFlag) == key) return;
        Take(Nearest(dungeon, last.killer), key);
    }

    EnemyBrain Nearest(DungeonGenerator dungeon, string killer)
    {
        EnemyBrain pick = null; float best = float.MaxValue;
        foreach (var b in dungeon.GetComponentsInChildren<EnemyBrain>())
        {
            var c = b.Character;
            if (c == null || !c.IsAlive || !string.IsNullOrEmpty(c.namePrefix) || b.GetComponent<Summon>() != null) continue;
            string name = c.DisplayName;
            if (c.TheName == name) continue;   // a name of its own: a boss, an event's character
            // The killer's name may carry a word in front (an elite's trait, or this script's own, from the death before).
            if (killer != null && killer != name && !killer.EndsWith(" " + name)) continue;
            float d = (b.transform.position - player.transform.position).sqrMagnitude;
            if (d < best) { best = d; pick = b; }
        }
        return pick;
    }

    void Take(EnemyBrain b, int key)
    {
        if (b == null) return;
        target = b; targetCharacter = b.Character; kind = targetCharacter.DisplayName; deathKey = key; met = false;
        if (!string.IsNullOrEmpty(prefix)) targetCharacter.namePrefix = prefix;
    }

    // A stable number for a death's id (string hashes differ between sessions), never 0.
    static int Key(string id)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char ch in id ?? "") h = (h ^ ch) * 16777619;
            return (int)(h & 0x7fffffff) | 1;
        }
    }

    // ── Meeting it ────────────────────────────────────────────────────

    bool Meets()
    {
        if (BossStageEncounter.Current != null) return false;
        var to = target.transform.position - player.transform.position;
        if (to.sqrMagnitude > 16f * 16f) return false;
        if (target.State == EnemyState.Chase || target.State == EnemyState.Attack) return true;
        if (to.sqrMagnitude > meetWithin * meetWithin) return false;
        var eye = player.Look != null ? player.Look.PitchTransform.position : player.transform.position + Vector3.up * 1.6f;
        return LineOfSight.Clear(eye, target.transform.position + Vector3.up, ~0, player.transform, target.transform, charactersBlock: false);
    }

    void Meet()
    {
        met = true;
        StartCoroutine(Point(targetCharacter));
    }

    IEnumerator Point(Character who)
    {
        yield return new WaitForSeconds(pointDelay);
        if (who != null && who == targetCharacter && who.IsAlive) DungeonMaster.Say(Fill(Any(pointLines)));
    }

    // ── The end of it ─────────────────────────────────────────────────

    void OnAnyDied(Character dead)
    {
        if (dead == null) return;
        // The player's own death changes nothing here: if it got them again, the next run's grudge is this death's.
        if (dead != targetCharacter) return;
        bool wasMet = met;
        var at = dead.transform.position;
        target = null; targetCharacter = null; met = false;
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(SettledFlag, deathKey);
        if (bounty > 0 && player != null && CurrencyManager.HasInstance) CurrencyManager.Instance.Collect(player, bounty, at);
        DungeonMaster.Say(Fill(Any(wasMet ? evenLines : evenUnmetLines)));
    }

    static string Any(List<string> lines) => lines != null && lines.Count > 0 ? lines[Random.Range(0, lines.Count)] : "";
    string Fill(string text) => string.IsNullOrEmpty(text) ? "" : text.Replace("{gold}", bounty.ToString()).Replace("{killer}", kind ?? "");

#if UNITY_EDITOR || DEBUG
    // For tests, from an eval in Play on a floor: the nearest enemy (of that kind, or any with "") becomes the grudge
    // now, whatever the save says; and what it is doing.
    public static string TestTake(string killer)
    {
        if (current == null) return "no Grudge";
        var dungeon = TableManager.Loader != null ? TableManager.Loader.Dungeon : null;
        if (dungeon == null || !current.Free()) return "no floor, or the player is not free";
        current.Forget();
        current.floorOf = dungeon; current.floorNumber = RunManager.Instance.Floor; current.looked = true;
        current.Take(current.Nearest(dungeon, string.IsNullOrEmpty(killer) ? null : killer), 12345);
        return TestState();
    }
    public static string TestState()
    {
        if (current == null) return "no Grudge";
        if (current.target == null) return "no grudge on this floor (looked " + current.looked + ")";
        return current.targetCharacter.DisplayName + " (" + current.kind + "), " + (current.target.transform.position - current.player.transform.position).magnitude.ToString("0.0") + " m, met " + current.met + ", state " + current.target.State + ", key " + current.deathKey;
    }
#endif
}
