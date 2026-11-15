using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// The skeletons talk. The dungeon's rank and file fought in silence: only the Dungeon Master, the bosses and the
// events' characters had anything to say. Now a skeleton says a short line over its head now and then: as it spots
// the player, when one beside it falls, when its blow lands or is blocked, when it is nearly done for, when it is the
// last one standing, and, unaware of a player sneaking close by, to itself. One line at a time on the whole floor,
// never two close together, never the same skeleton twice in a row; nothing during a boss's fight, from a summon, a
// sleeper, a dancer still dancing, or anyone with a name of their own (they have their own words).
// The line is world text in the game's font, drawn over the speaker's head and hidden by walls like anything else,
// kept about the same size on screen. It listens to Character.Spawned, each brain's StateChanged, the enemies' and the
// table player's Damaged and Character.AnyDied; no enemy code knows about it.
public class EnemyBarks : MonoBehaviour
{
    public enum Moment { Spot = 0, FriendFell = 1, LandedHit = 2, Blocked = 3, Hurt = 4, LastOne = 5, Idle = 6 }

    [Serializable]
    public class Set
    {
        public Moment moment;
        [Range(0, 1), Tooltip("Chance a line is said when this happens (and nothing else was said just before).")] public float chance = .4f;
        public List<string> lines = new();
    }

    [Tooltip("Off: nobody says anything.")] public bool barksOn = true;
    [Tooltip("Who talks: enemies of this kind (Character data > Creature Kind).")] public CreatureKind kind = CreatureKind.Skeleton;
    public List<Set> sets = new()
    {
        new Set { moment = Moment.Spot, chance = .4f, lines = { "Oi!", "You there!", "Intruder. Finally.", "Not on my shift.", "Another one.", "Hey! No visitors!", "Got one!" } },
        new Set { moment = Moment.FriendFell, chance = .5f, lines = { "He owed me gold.", "That was my cousin.", "We just fixed him.", "He was nearly retired.", "I liked him.", "Who's cleaning that up?" } },
        new Set { moment = Moment.LandedHit, chance = .2f, lines = { "Ha!", "Felt that?", "That's one.", "Got you." } },
        new Set { moment = Moment.Blocked, chance = .25f, lines = { "Cheat.", "Drop the shield.", "Rude.", "That's not fair." } },
        new Set { moment = Moment.Hurt, chance = .5f, lines = { "I'm fine.", "Wait, wait.", "Time out?", "I need this job.", "Mind the ribs." } },
        new Set { moment = Moment.LastOne, chance = .7f, lines = { "Just me, then.", "Lads?", "We can talk about this.", "I'm new here." } },
        new Set { moment = Moment.Idle, chance = .6f, lines = { "Quiet tonight.", "My knee clicks.", "Is it lunch yet?", "I hate this floor.", "Did you hear that? No.", "Forty years. No raise." } },
    };

    [Header("When")]
    [Min(0), Tooltip("Seconds between any two lines on the floor.")] public float gap = 7f;
    [Min(0), Tooltip("Seconds before the same enemy speaks again.")] public float gapPerEnemy = 20f;
    [Min(0), Tooltip("Only enemies within this of the table player speak (metres).")] public float hearing = 11f;
    [Min(0), Tooltip("One beside it counts as a friend within this (metres).")] public float friendReach = 8f;
    [Range(0, 1), Tooltip("Nearly done for: its health at or under this share, once each.")] public float hurtAt = .3f;
    [Tooltip("Seconds between tries at a line from one that hasn't noticed the player (a random time in between).")] public Vector2 idleEvery = new(14f, 26f);

    [Header("Look")]
    public TMP_FontAsset font;
    [Tooltip("The font's material for it (an outlined one reads on any wall).")] public Material material;
    public Color colour = new(.93f, .9f, .8f, 1f);
    [Min(.1f), Tooltip("Letter size at Size Distance, in the font's own units (2 is about 20 cm).")] public float size = 3.2f;
    [Min(.5f), Tooltip("The line is this size at this distance and grows or shrinks with it, so it reads about the same on screen.")] public float sizeDistance = 4f;
    public Vector2 sizeRange = new(.55f, 2f);
    [Min(0), Tooltip("How far over the head it stands (metres).")] public float lift = .12f;
    [Min(0)] public float hold = 1.5f;
    [Min(0), Tooltip("More seconds on screen per letter.")] public float perLetter = .05f;
    [Min(.01f)] public float fade = .15f;

    static EnemyBarks current;

    // It is not in the scene: at Play it is made from Characters/Resources/Enemy barks (the prefab holds its settings) under the
    // CombatManager, in any scene that has one.
    const string PrefabName = "Enemy barks";
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
        if (current != null || !CombatManager.HasInstance) return;
        var prefab = Resources.Load<GameObject>(PrefabName);
        if (prefab != null) Instantiate(prefab, CombatManager.Instance.transform).name = prefab.name;
    }

    readonly List<EnemyBrain> speakers = new();
    readonly Dictionary<EnemyBrain, float> spokeAt = new();
    readonly HashSet<EnemyBrain> saidHurt = new();
    Player player;
    TextMeshPro label;
    Transform over, overHead;
    Vector3 lastSpot;
    float shownAt = -100f, showFor, lastAt = -100f, nextIdle;
    string lastLine;

    void OnEnable()
    {
        current = this;
        Character.Spawned += OnSpawned;
        Character.AnyDied += OnAnyDied;
        nextIdle = Time.time + UnityEngine.Random.Range(idleEvery.x, idleEvery.y);
    }

    void OnDisable()
    {
        if (current == this) current = null;
        Character.Spawned -= OnSpawned;
        Character.AnyDied -= OnAnyDied;
        if (player != null) player.Damaged -= OnPlayerDamaged;
        player = null;
        if (label != null) label.gameObject.SetActive(false);
    }

    // ── Who talks ─────────────────────────────────────────────────────

    void OnSpawned(Character c)
    {
        if (c == null || c is Player) return;
        var brain = c.GetComponent<EnemyBrain>();
        if (brain == null || brain.Data == null || brain.Data.Kind != kind || brain.Data.properName) return;
        speakers.Add(brain);
        brain.StateChanged += (from, to) => OnState(brain, from, to);
        c.Damaged += info => OnEnemyDamaged(brain, c, info);
    }

    // Free to speak now: alive, awake, not a summon or a dancer, and near enough the table player to be heard.
    bool Can(EnemyBrain b)
    {
        if (b == null || !b.isActiveAndEnabled || b.Asleep || b.Entranced || b.NoExperience) return false;
        var c = b.GetComponent<Character>();
        if (c == null || c.Stats == null || !c.Stats.IsAlive || b.GetComponent<Summon>() != null) return false;
        return player != null && (b.transform.position - player.transform.position).sqrMagnitude <= hearing * hearing;
    }

    bool Listening()
    {
        if (!barksOn || BossStageEncounter.Current != null) return false;
        if (!GameManager.HasInstance || GameManager.Instance.State != GameState.Explore) return false;
        if (!PlayerManager.HasInstance || PlayerManager.Instance.ActiveKind != PlayerKind.Table) return false;
        if (player == null)
        {
            player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
            if (player != null) player.Damaged += OnPlayerDamaged;
        }
        return player != null;
    }

    // ── The moments ───────────────────────────────────────────────────

    void OnState(EnemyBrain b, EnemyState from, EnemyState to)
    {
        if (to == EnemyState.Chase && from != EnemyState.Attack && from != EnemyState.Chase) Try(b, Moment.Spot);
    }

    void OnEnemyDamaged(EnemyBrain b, Character c, DamageInfo info)
    {
        if (b == null || c == null || c.Stats == null || !c.Stats.IsAlive || saidHurt.Contains(b)) return;
        if (c.Stats.MaxHealth <= 0f || c.Stats.CurrentHealth / c.Stats.MaxHealth > hurtAt) return;
        saidHurt.Add(b);
        Try(b, Moment.Hurt);
    }

    void OnPlayerDamaged(DamageInfo info)
    {
        var b = info.Source != null ? info.Source.GetComponent<EnemyBrain>() : null;
        if (b == null || !speakers.Contains(b)) return;
        if (info.Blocked) Try(b, Moment.Blocked);
        else if (info.Amount > 0f && !info.Spared) Try(b, Moment.LandedHit);
    }

    void OnAnyDied(Character dead)
    {
        if (dead == null || dead is Player || !Listening()) return;
        var fallen = dead.GetComponent<EnemyBrain>();
        if (fallen == null || fallen.GetComponent<Summon>() != null) return;
        // The nearest one beside it that can speak; the last one standing says so instead.
        EnemyBrain nearest = null; float best = friendReach * friendReach; int left = 0;
        foreach (var b in speakers)
        {
            if (b == fallen || !Can(b)) continue;
            float d = (b.transform.position - dead.transform.position).sqrMagnitude;
            if (d > friendReach * friendReach) continue;
            left++;
            if (d <= best) { best = d; nearest = b; }
        }
        if (nearest == null) return;
        Try(nearest, left == 1 && speakers.Contains(fallen) ? Moment.LastOne : Moment.FriendFell);
    }

    void Update()
    {
        if (speakers.Count > 0 && Time.frameCount % 120 == 0)
        {
            speakers.RemoveAll(b => b == null);
            if (speakers.Count == 0) { spokeAt.Clear(); saidHurt.Clear(); }
        }
        if (Time.time >= nextIdle)
        {
            nextIdle = Time.time + UnityEngine.Random.Range(idleEvery.x, idleEvery.y);
            if (Listening())
            {
                // One that hasn't noticed anything, the nearest to the player.
                EnemyBrain pick = null; float best = float.MaxValue;
                foreach (var b in speakers)
                {
                    if (!Can(b) || b.State != EnemyState.Idle && b.State != EnemyState.Wander && b.State != EnemyState.Patrol) continue;
                    float d = (b.transform.position - player.transform.position).sqrMagnitude;
                    if (d < best) { best = d; pick = b; }
                }
                if (pick != null) Try(pick, Moment.Idle);
            }
        }
    }

    void Try(EnemyBrain b, Moment moment)
    {
        if (!Listening() || !Can(b) || Time.time - lastAt < gap) return;
        if (spokeAt.TryGetValue(b, out float at) && Time.time - at < gapPerEnemy) return;
        var set = sets.Find(s => s != null && s.moment == moment);
        if (set == null || set.lines.Count == 0 || UnityEngine.Random.value > set.chance) return;
        string line = set.lines[UnityEngine.Random.Range(0, set.lines.Count)];
        if (line == lastLine && set.lines.Count > 1) line = set.lines[(set.lines.IndexOf(line) + 1) % set.lines.Count];
        if (string.IsNullOrWhiteSpace(line)) return;
        lastLine = line; lastAt = Time.time; spokeAt[b] = Time.time;
        Show(b, line);
    }

    // ── The line over the head ────────────────────────────────────────

    void Show(EnemyBrain b, string line)
    {
        if (label == null)
        {
            var go = new GameObject("Enemy bark");
            go.transform.SetParent(transform, false);
            label = go.AddComponent<TextMeshPro>();
            if (font != null) label.font = font;
            if (material != null) label.fontSharedMaterial = material;
            label.alignment = TextAlignmentOptions.Bottom;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.rectTransform.sizeDelta = new Vector2(6f, 1f);
            label.rectTransform.pivot = new Vector2(.5f, 0f);
            var r = label.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        label.fontSize = size;
        label.text = line;
        label.color = new Color(colour.r, colour.g, colour.b, 0f);
        label.gameObject.SetActive(true);
        over = b.transform; overHead = HeadOf(over);
        shownAt = Time.time; showFor = hold + line.Length * perLetter;
        Place();
    }

    void LateUpdate()
    {
        if (label == null || !label.gameObject.activeSelf) return;
        float t = Time.time - shownAt;
        if (t > showFor + fade) { label.gameObject.SetActive(false); over = null; return; }
        float a = Mathf.Clamp01(t / fade) * Mathf.Clamp01((showFor + fade - t) / fade);
        label.color = new Color(colour.r, colour.g, colour.b, colour.a * a);
        Place();
    }


    // The top of a body's head. Its head bone when it has one, else the top of its own solid collider: the first
    // collider on an enemy's root can be its wide perception trigger, which put the text metres over it (Celes's find).
    static Transform HeadOf(Transform body)
    {
        var animator = body != null ? body.GetComponentInChildren<Animator>() : null;
        return animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
    }
    static Vector3 HeadTop(Transform body, Transform head)
    {
        if (head != null) return head.position + Vector3.up * .25f;
        foreach (var c in body.GetComponents<Collider>()) if (c.enabled && !c.isTrigger) return new Vector3(c.bounds.center.x, c.bounds.max.y, c.bounds.center.z);
        return body.position + Vector3.up * 1.9f;
    }

    // Over the speaker's head (where it last was, once it is gone), facing the eye, sized by its distance.
    void Place()
    {
        var cam = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : null;
        if (cam == null) cam = Camera.main;
        if (over != null) lastSpot = HeadTop(over, overHead) + Vector3.up * lift;
        label.transform.position = lastSpot;
        if (cam == null) return;
        label.transform.rotation = cam.transform.rotation;
        float d = Vector3.Distance(cam.transform.position, lastSpot);
        label.transform.localScale = Vector3.one * Mathf.Clamp(d / Mathf.Max(.5f, sizeDistance), sizeRange.x, sizeRange.y);
        // The whole line stays in view, while the head itself is in view. Sideways: over a head at the edge of the screen
        // half of it ran off ("Remember th", Zelda's find). Downwards: at sword range the head is near the top of the view
        // and the line stood half off it, across the enemy's bar ("Rude.", Celes's find); it stays under Ceiling.
        var head = cam.WorldToViewportPoint(lastSpot);
        if (head.z > .1f && head.x > 0f && head.x < 1f)
        {
            float tall = 2f * head.z * Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad);   // the view's height at that depth
            float across = tall * cam.aspect;
            float scale = label.transform.localScale.x;
            float half = label.preferredWidth * scale * .5f / across;    // half the line, as a share of the view's width
            float height = label.preferredHeight * scale / tall;         // the line's height, as a share of the view's height
            float shift = 0f;
            if (half * 2f < 1f - Margin - MarginRight)
            {
                if (head.x - half < Margin) shift = Margin - (head.x - half);
                else if (head.x + half > 1f - MarginRight) shift = 1f - MarginRight - (head.x + half);
            }
            float drop = Mathf.Max(0f, head.y + height - Ceiling);
            if (shift != 0f || drop > 0f) label.transform.position = lastSpot + cam.transform.right * (shift * across) - cam.transform.up * (drop * tall);
        }
    }
    // Shares of the view: the least room left at its left side and at its right, and the highest a line's top may stand
    // (under the top bars). The right side keeps clear of the held weapon, which is drawn over the world and so over a
    // line there (Celes: the blade covered the last letters of "Hey! No visitors!").
    const float Margin = .015f, MarginRight = .2f, Ceiling = .84f;

#if UNITY_EDITOR || DEBUG
    // For tests, from an eval in Play on a floor: the nearest enemy that can speak says a line of that moment now.
    public string TestSay(int moment)
    {
        if (!Listening()) return "not listening (no table player exploring, or a boss fight)";
        EnemyBrain pick = null; float best = float.MaxValue;
        foreach (var b in speakers)
        {
            if (!Can(b)) continue;
            float d = (b.transform.position - player.transform.position).sqrMagnitude;
            if (d < best) { best = d; pick = b; }
        }
        if (pick == null) return $"nobody within {hearing} m can speak ({speakers.Count} known)";
        var set = sets.Find(s => (int)s.moment == moment);
        if (set == null || set.lines.Count == 0) return "no lines";
        string line = set.lines[UnityEngine.Random.Range(0, set.lines.Count)];
        lastAt = Time.time; spokeAt[pick] = Time.time;
        Show(pick, line);
        return $"{pick.name}: {line}";
    }
#endif
}
