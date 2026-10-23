#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

// Editor test driver for combat: plays a script of inputs on the table player, logs swing and hit
// timings, and can capture frames at a fixed 30 fps for frame sheets. Started from an eval with
// CombatTestBot.Run("place Soldier 1.6; face; capture 3 swing; attack; wait .6; attack; wait 2").
// Commands (separated by ';'):
//   place <name> <metres>   move the first living enemy whose name contains <name> in front of the player
//   face                    turn the player toward that enemy (chest height); track [off] keeps facing it
//   pitch <degrees>         set the camera pitch (positive looks up)
//   attack                  tap attack       hold <s>   hold attack for s seconds
//   block <s>               hold block       blockon / blockoff
//   spam <n> <gap>          n taps, gap seconds apart
//   counter                 attack as soon as the enemy starts a swing
//   wait <s>                capture <s> <name>  frames to Temp/CombatTest/<name> (runs alongside)
//   chest <metres> [wood|iron|gold]   place a chest in front of the player; open  opens it
//   god [off]               keep the player at full health
//   fight <s> <metres> [block 0-1]  fight like a plain player (block: share of enemy swings it reacts to; 1 = every one) for up to s seconds: walk at the nearest enemy within <metres>,
//                           block when one swings close by, swing otherwise; logs kills, hits taken and health
//   cm <field> <value>      set a CombatManager field
//   freeze / unfreeze       the enemy's brain off/on      heal  full health for player and enemy
public class CombatTestBot : MonoBehaviour
{
    public static string Report => log.ToString();
    public static bool Running { get; private set; }
    static readonly StringBuilder log = new();
    static CombatTestBot instance;

    Player player;
    Character enemy;
    bool tracking;
    static bool god;   // the player is healed every frame (stays on between scripts)
    float startedAt;

    // Statics outlive Play mode (domain reload is off): god mode must not carry over to the next session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { god = false; Running = false; instance = null; log.Clear(); }

    public static void Run(string script)
    {
        if (instance == null) instance = new GameObject("Combat test bot").AddComponent<CombatTestBot>();
        instance.StopAllCoroutines();
        log.Clear();
        Running = true;
        instance.StartCoroutine(instance.Play(script));
    }

    void Log(string line) => log.AppendLine($"{Time.time - startedAt:0.000} {line}");

    IEnumerator Play(string script)
    {
        player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        startedAt = Time.time;
        var relay = player.GetComponentInChildren<WeaponAnimationRelay>(true);
        System.Action begin = () => Log("swing begin"), strike = () => Log("swing audio");
        if (relay != null) { relay.AttackStarted += begin; relay.SwingStarted += strike; }
        System.Action<DamageInfo> hurt = i => Log($"player hit by {(i.Source != null ? i.Source.name : "?")} {i.Amount:0.0}{(i.Blocked ? " BLOCKED" : "")}");
        player.Damaged += hurt;
        try
        {
            foreach (var raw in script.Split(';'))
            {
                var parts = raw.Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                float F(int i, float d) => parts.Length > i && float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d;
                switch (parts[0])
                {
                    case "place": Place(parts.Length > 1 ? parts[1] : "", F(2, 1.6f)); break;
                    case "face": Face(); break;
                    case "goto": GoTo(parts.Length > 1 ? parts[1] : "enemy", F(2, 3f)); break;
                    case "open": OpenChest(); break;
#if UNITY_EDITOR
                    case "chest": SpawnChest(F(1, 2.2f), parts.Length > 2 ? parts[2] : "wood"); break;
#endif
                    case "god": god = parts.Length < 2 || parts[1] != "off"; break;
                    case "track": tracking = parts.Length < 2 || parts[1] != "off"; break;
                    case "pitch": if (player.Look != null) player.Look.SetPitch(F(1, 0f)); break;
                    case "attack": Press(true); yield return new WaitForSeconds(.05f); Press(false); break;
                    case "hold": Press(true); yield return new WaitForSeconds(F(1, .8f)); Press(false); break;
                    case "spam":
                        for (int n = 0, count = (int)F(1, 3); n < count; n++)
                        { Press(true); yield return new WaitForSeconds(.05f); Press(false); yield return new WaitForSeconds(Mathf.Max(0f, F(2, .1f) - .05f)); }
                        break;
                    case "block": Block(true); yield return new WaitForSeconds(F(1, 1f)); Block(false); break;
                    case "blockon": Block(true); break;
                    case "blockoff": Block(false); break;
                    case "wait": yield return new WaitForSeconds(F(1, .5f)); break;
                    case "counter":
                        // Wait until the enemy starts a swing (up to 6 s), then attack at once.
                        var brain = enemy != null ? enemy.GetComponent<EnemyBrain>() : null;
                        for (float t = 0f; brain != null && !brain.IsSwinging && t < 6f; t += Time.deltaTime) yield return null;
                        Log("enemy swing seen");
                        Press(true); yield return new WaitForSeconds(.05f); Press(false);
                        break;
                    case "capture": StartCoroutine(Capture(F(1, 2f), parts.Length > 2 ? parts[2] : "take")); break;
                    case "freeze": SetBrain(false); break;
                    case "unfreeze": SetBrain(true); break;
                    case "heal": Heal(player); Heal(enemy); break;
                    case "cm": SetTuning(parts[1], parts[2]); break;
                    case "fight": yield return Fight(F(1, 40f), F(2, 14f), F(3, 1f)); break;
                    // strafe <seconds> [speed]: run sideways (to the right) as a player dodging would.
                    case "strafe":
                        for (float t = 0, limit = F(1, 3f), speed = F(2, 4f); t < limit; t += Time.deltaTime)
                        {
                            var side = player.Look != null ? player.Look.YawTransform.right : player.transform.right;
                            player.Motor.Controller.Move(side * speed * Time.deltaTime + Vector3.down * .5f * Time.deltaTime);
                            yield return null;
                        }
                        break;
                    // walkstage: walk (as the player would) to the middle of the boss stage.
                    case "walkstage":
                    {
                        var g = TableManager.Loader != null ? TableManager.Loader.Dungeon : null;
                        for (float t = 0; g != null && g.Stage != null && t < 10f; t += Time.deltaTime)
                        {
                            var d = g.StageCentre - player.transform.position; d.y = 0;
                            if (d.magnitude < .8f) break;
                            player.Motor.Controller.Move(d.normalized * 3f * Time.deltaTime + Vector3.down * .5f * Time.deltaTime);
                            yield return null;
                        }
                        break;
                    }
                    // waitboss [s]: until the boss on the stage is down and the fight has begun (BossStageEncounter).
                    case "waitboss":
                        for (float t = 0, limit = F(1, 20f); t < limit && !(BossStageEncounter.Current != null && BossStageEncounter.Current.Fighting); t += Time.deltaTime)
                        {
                            // Watch his hand bring the boss down, as a player would.
                            var dmHand = FindAnyObjectByType<DungeonMasterHand>();
                            var boss = BossStageEncounter.Current != null ? BossStageEncounter.Current.Boss : null;
                            if (dmHand != null && dmHand.Busy && dmHand.RightHand != null) FaceAt(Vector3.Lerp(dmHand.RightHand.position, boss != null ? boss.transform.position + Vector3.up : dmHand.RightHand.position, .6f), 3f);
                            else if (boss != null) FaceAt(boss.transform.position + Vector3.up * 1.2f, 3f);
                            yield return null;
                        }
                        Log(BossStageEncounter.Current != null && BossStageEncounter.Current.Fighting ? "boss fight begun" : "no boss fight");
                        break;
                }
            }
        }
        finally
        {
            if (relay != null) { relay.AttackStarted -= begin; relay.SwingStarted -= strike; }
            player.Damaged -= hurt;
            Press(false); Block(false);
            tracking = false;
            Running = false;
            Log("done");
        }
    }

    // A plain fighter: the nearest living enemy in reach is the target; the bot walks at it, keeps it in view, raises the
    // shield while an enemy close by is swinging, and swings whenever it can.
    IEnumerator Fight(float seconds, float reach, float blockSkill)
    {
        var reacted = new System.Collections.Generic.Dictionary<EnemyBrain, bool>();
        float hp0 = player.Stats.CurrentHealth, until = Time.time + seconds;
        int kills = 0, hits = 0;
        System.Action<DamageInfo> taken = i => { if (!i.Blocked && i.Amount > 0f) hits++; };
        player.Damaged += taken;
        var known = new System.Collections.Generic.HashSet<Character>();
        float nextSwing = 0f;
        var dmBeam = FindAnyObjectByType<DungeonMasterBeam>();
        int beams = 0; bool wasBeam = false, seeBeam = false; string lastBeamState = "";
        while (Time.time < until && player.IsAlive)
        {
            // The Dungeon Master's beam: turn to him and hold the shield up until it ends (as often as blockSkill says).
            if (dmBeam != null && dmBeam.Firing)
            {
                if (!wasBeam) { beams++; wasBeam = true; seeBeam = Random.value < blockSkill; }
                var palm = FindAnyObjectByType<DungeonMasterHand>()?.LeftHand;
                if (seeBeam) { FaceAt(palm != null ? palm.position : dmBeam.transform.position + Vector3.up * 25f, 6f); Block(true); }
                else FaceAt(palm != null ? palm.position : dmBeam.transform.position + Vector3.up * 25f, 2f);
                if (dmBeam.State != lastBeamState) { lastBeamState = dmBeam.State; Log("beam: " + lastBeamState); }
                yield return null;
                continue;
            }
            if (wasBeam) { wasBeam = false; Block(false); }
            Character nearest = null; float best = reach;
            foreach (var b in EnemyBrain.Active)
            {
                if (b == null || !b.Character.IsAlive) continue;
                float d = Vector3.Distance(b.transform.position, player.transform.position);
                if (d < best) { best = d; nearest = b.Character; }
            }
            foreach (var k in new System.Collections.Generic.List<Character>(known)) if (k == null || !k.IsAlive) { known.Remove(k); kills++; }
            if (nearest == null) break;
            known.Add(nearest);
            enemy = nearest; Face();
            bool threat = false;
            foreach (var b in EnemyBrain.Active)
            {
                if (b == null || !b.Character.IsAlive) continue;
                if (!b.IsSwinging) { reacted.Remove(b); continue; }
                if (Vector3.Distance(b.transform.position, player.transform.position) >= 2.8f) continue;
                // Each swing is either seen in time or not.
                if (!reacted.TryGetValue(b, out bool seen)) reacted[b] = seen = Random.value < blockSkill;
                if (seen) threat = true;
            }
            Block(threat);
            if (best > 1.9f && player.Motor != null && player.Motor.Controller != null)
            {
                var to = nearest.transform.position - player.transform.position; to.y = 0f;
                player.Motor.Controller.Move(to.normalized * 3.6f * Time.deltaTime + Vector3.down * .5f * Time.deltaTime);
            }
            else if (!threat && Time.time >= nextSwing) { Press(true); yield return new WaitForSeconds(.05f); Press(false); nextSwing = Time.time + .55f; }
            yield return null;
        }
        Block(false);
        player.Damaged -= taken;
        foreach (var k in known) if (k == null || !k.IsAlive) kills++;
        if (beams > 0) Log($"fight: {beams} beams");
        Log($"fight: {kills} killed, {hits} hits taken, health {hp0:0} -> {player.Stats.CurrentHealth:0} of {player.Stats.MaxHealth:0}, {(player.IsAlive ? "alive" : "DIED")}, {seconds - Mathf.Max(0f, until - Time.time):0.0} s");
    }

    // ── Enemy ─────────────────────────────────────────────────────────

    void Place(string name, float distance)
    {
        if (enemy != null) { enemy.Damaged -= OnEnemyDamaged; enemy.Died -= OnEnemyDied; }
        enemy = null;
        foreach (var brain in EnemyBrain.Active)
            if (brain != null && brain.Character.IsAlive && brain.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0) { enemy = brain.Character; break; }
#if UNITY_EDITOR
        // None alive: spawn the enemy prefab with that name.
        if (enemy == null)
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets($"t:Prefab {name}", new[] { "Assets/Game/Characters/Enemies" }))
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null || prefab.GetComponent<EnemyBrain>() == null || UnityEditor.AssetDatabase.GUIDToAssetPath(guid).Contains("_Archive")) continue;
                var go = Instantiate(prefab, player.transform.position + player.transform.forward * distance, Quaternion.identity);
                enemy = go.GetComponent<Character>();
                break;
            }
#endif
        if (enemy == null) { Log($"no enemy named {name}"); return; }
        Vector3 forward = player.Look != null ? player.Look.YawTransform.forward : player.transform.forward;
        forward.y = 0f; forward.Normalize();
        Vector3 pos = player.transform.position + forward * distance;
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
        var agent = enemy.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null && agent.enabled) agent.Warp(pos); else enemy.transform.position = pos;
        enemy.transform.rotation = Quaternion.LookRotation(-forward);
        enemy.Damaged += OnEnemyDamaged;
        enemy.Died += OnEnemyDied;
        Log($"placed {enemy.name} at {distance} m, hp {enemy.Stats.CurrentHealth:0}");
    }

    void OnEnemyDamaged(DamageInfo info)
    {
        Vector3 at = enemy != null ? enemy.transform.position : Vector3.zero;
        Log($"enemy hit {info.Amount:0.0}{(info.Critical ? " CRIT" : "")}{(info.Heavy ? " HEAVY" : "")} hp {(enemy != null ? enemy.Stats.CurrentHealth : 0):0.0} point {info.HitPoint} dir {info.Direction}");
        if (enemy != null) StartCoroutine(MeasurePush(enemy.transform, at));
    }

    void OnEnemyDied() => Log("enemy died");

    IEnumerator MeasurePush(Transform target, Vector3 from)
    {
        yield return new WaitForSeconds(.4f);
        if (target != null) Log($"  push after .4 s: {Vector3.Distance(Flat(from), Flat(target.position)):0.00} m");
    }
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // Turns the view toward aim; speed: how quickly (a player's turn, not a snap), 0 snaps.
    void FaceAt(Vector3 aim, float speed = 0f)
    {
        if (player.Look == null) return;
        Vector3 to = aim - player.Look.PitchTransform.position;
        var flat = new Vector3(to.x, 0f, to.z);
        float yaw = Quaternion.LookRotation(flat).eulerAngles.y, pitch = Mathf.Clamp(Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg, -60f, 60f);
        if (speed > 0f)
        {
            float k = 1f - Mathf.Exp(-speed * Time.deltaTime);
            yaw = Mathf.LerpAngle(player.Look.YawTransform.eulerAngles.y, yaw, k);
            pitch = Mathf.Lerp(player.Look.Pitch, pitch, k);
        }
        player.Look.SetYaw(yaw);
        player.Look.SetPitch(pitch);
    }

    void Face()
    {
        if (enemy == null || player.Look == null) return;
        // The visible body's middle (colliders can be big perception triggers).
        Vector3 aim = enemy.transform.position + Vector3.up * 1.1f;
        var renderers = enemy.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) if (r.enabled) bounds.Encapsulate(r.bounds);
            aim = bounds.center;
        }
        Vector3 to = aim - player.Look.PitchTransform.position;
        player.Look.SetYaw(Quaternion.LookRotation(new Vector3(to.x, 0f, to.z)).eulerAngles.y);
        player.Look.SetPitch(Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
    }

    // goto enemy|chest|breakable <metres>: stand that far from the nearest one, facing it.
    Component target;
    void GoTo(string what, float distance)
    {
        Component best = null; float bestD = float.MaxValue;
        System.Collections.Generic.IEnumerable<Component> pool = what switch
        {
            "chest" => System.Linq.Enumerable.Where(FindObjectsByType<DungeonContainer>(), c => c.chest),
            "breakable" => FindObjectsByType<DungeonDestructible>(),
            _ => System.Linq.Enumerable.Where(EnemyBrain.Active, b => b != null && b.Character.IsAlive),
        };
        foreach (var c in pool)
        {
            if (c == null) continue;
            float d = Vector3.Distance(c.transform.position, player.transform.position);
            if (d < bestD) { bestD = d; best = c; }
        }
        if (best == null) { Log($"no {what}"); return; }
        target = best;
        if (best is EnemyBrain brain) { enemy = brain.Character; enemy.Damaged += OnEnemyDamaged; enemy.Died += OnEnemyDied; }
        Vector3 at = best.transform.position;
        Vector3 from = at + (player.transform.position - at).normalized * distance; from.y = at.y;
        if (UnityEngine.AI.NavMesh.SamplePosition(from, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) from = hit.position;
        Vector3 look = at - from; look.y = 0f;
        player.Warp(from, Quaternion.LookRotation(look.sqrMagnitude > .01f ? look : Vector3.forward));
        if (player.Look != null) { player.Look.SetYaw(Quaternion.LookRotation(look).eulerAngles.y); player.Look.SetPitch(-12f); }
        Log($"at {what} {best.name}, {Vector3.Distance(from, at):0.0} m");
    }

#if UNITY_EDITOR
    // chest <metres> [wood|iron|gold]: a chest from the level's props, set down in front of the player (needs a dungeon).
    void SpawnChest(float distance, string tier)
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(tier == "iron" ? "Assets/Game/Levels/Shared/Props/Chests/Iron chest.prefab"
            : tier == "gold" ? "Assets/Game/Levels/Shared/Props/Chests/Gold chest.prefab" : "Assets/Game/Levels/Shared/Props/Chests/Supply chest.prefab");
        var dungeon = FindAnyObjectByType<DungeonGenerator>();
        if (prefab == null || dungeon == null) { Log("no chest prefab or dungeon"); return; }
        Vector3 forward = player.Look != null ? player.Look.YawTransform.forward : player.transform.forward; forward.y = 0f; forward.Normalize();
        Vector3 at = player.transform.position + forward * distance;
        if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
        var go = Instantiate(prefab, at, Quaternion.LookRotation(-forward), dungeon.transform);
        var chest = go.GetComponent<DungeonContainer>();
        if (dungeon.Biome != null && dungeon.Biome.loot != null) chest.loot = dungeon.Biome.loot;
        chest.seed = Random.Range(0, 99999);
        target = chest;
        Log("chest placed, loot " + (chest.loot != null ? chest.loot.name : "none"));
    }
#endif

    void OpenChest()
    {
        if (target is IInteractable i && i.CanInteract(player)) { i.Interact(player); Log("opened " + target.name); }
        else Log("nothing to open");
    }

    // Like a player keeping the enemy in view.
    void LateUpdate()
    {
        if (tracking && Running && enemy != null && enemy.IsAlive) Face();
        if (god && player != null && player.IsAlive) Heal(player);
        // The shield's state changes, timed like the swings.
        var combat = player != null ? player.GetComponent<PlayerCombat>() : null;
        if (Running && combat != null && combat.IsGuarding != loggedGuard) { loggedGuard = combat.IsGuarding; Log(loggedGuard ? "guard up" : "guard down"); }
    }
    bool loggedGuard;

    void SetBrain(bool on) { if (enemy != null && enemy.TryGetComponent<EnemyBrain>(out var b)) b.enabled = on; }
    // cm <field> <value>: set a CombatManager field for this play session.
    void SetTuning(string field, string value)
    {
        var f = typeof(CombatManager).GetField(field);
        if (f == null || !CombatManager.HasInstance) { Log($"no CombatManager field {field}"); return; }
        object v = f.FieldType == typeof(bool) ? bool.Parse(value) : f.FieldType == typeof(int) ? int.Parse(value)
            : (object)float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        f.SetValue(CombatManager.Instance, v);
        Log($"{field} = {v}");
    }

    static void Heal(Character c) { if (c != null && c.Stats != null) c.Stats.Heal(999f); }

    // ── Input ─────────────────────────────────────────────────────────

    static void Press(bool down) => Send("PrimaryHeld", down ? "PrimaryPressed" : "PrimaryReleased", down);
    static void Block(bool down) => Send("SecondaryHeld", down ? "SecondaryPressed" : "SecondaryReleased", down);

    static void Send(string held, string evt, bool down)
    {
        if (!InputManager.HasInstance) return;
        var im = InputManager.Instance;
        var prop = typeof(InputManager).GetProperty(held);
        if ((bool)prop.GetValue(im) == down) return;
        prop.GetSetMethod(true).Invoke(im, new object[] { down });
        var field = typeof(InputManager).GetField(evt, BindingFlags.Instance | BindingFlags.NonPublic);
        (field?.GetValue(im) as System.Action)?.Invoke();
    }

    // ── Frames ────────────────────────────────────────────────────────

    // Fixed 30 fps game time while capturing, so every frame is 1/30 s apart however slow the capture is.
    IEnumerator Capture(float seconds, string name)
    {
        string dir = System.IO.Path.Combine("Temp", "CombatTest", name);
        if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
        System.IO.Directory.CreateDirectory(dir);
        int previous = Time.captureFramerate;
        Time.captureFramerate = 30;
        int frames = Mathf.CeilToInt(seconds * 30f);
        for (int i = 0; i < frames; i++)
        {
            yield return new WaitForEndOfFrame();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"{i:000}.png"), shot.EncodeToPNG());
            Destroy(shot);
        }
        Time.captureFramerate = previous;
        Log($"captured {frames} frames to {dir}");
    }

    void OnDestroy() { if (instance == this) instance = null; Time.captureFramerate = 0; }
}
#endif
