using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

// Staged gameplay for trailers and TikTok clips: drives the active player through InputManager (move, attack)
// and PlayerLook (turning), and records each shot with ClipRecorder. Editor only, Play mode only.
// Typical use from the CLI: TrailerShots.Run("Fight", TrailerShots.Fight(4)) then poll TrailerShots.Status.
public static class TrailerShots
{
    public static string Status => TrailerRunner.Instance.Status;

    static Player Player => PlayerManager.HasInstance ? PlayerManager.Instance.Active as Player : null;

    // ── Running and recording ─────────────────────────────────────────

    public static void Run(string clipName, IEnumerator shot, bool record = true, int width = 1080, int height = 1920, bool clean = true)
    {
        var runner = TrailerRunner.Instance;
        runner.Clean = clean;
        runner.StopAllCoroutines();
        runner.StartCoroutine(Wrap(clipName, shot, record, width, height));
    }

    static IEnumerator Wrap(string clipName, IEnumerator shot, bool record, int width, int height)
    {
        var runner = TrailerRunner.Instance;
        runner.Status = "running " + clipName;
        string file = record ? ClipRecorder.Begin(clipName, width, height) : null;
        yield return null;
        Exception error = null;
        while (true)
        {
            object current;
            try { if (!shot.MoveNext()) break; current = shot.Current; }
            catch (Exception e) { error = e; break; }
            yield return current;
        }
        SetMove(Vector2.zero);
        if (record) ClipRecorder.End();
        runner.Status = error != null ? "error " + error.Message : "done " + (file ?? clipName);
        if (error != null) Debug.LogException(error);
    }

    // ── Input ─────────────────────────────────────────────────────────

    public static void SetMove(Vector2 move)
    {
        if (!InputManager.HasInstance) return;
        typeof(InputManager).GetProperty("Move").SetValue(InputManager.Instance, move);
    }

    static void Raise(string eventName)
    {
        var field = typeof(InputManager).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
        (field?.GetValue(InputManager.Instance) as Action)?.Invoke();
    }

    static void SetHeld(bool held)
    {
        var p = typeof(InputManager).GetProperty("PrimaryHeld");
        if (p != null && p.CanWrite) p.SetValue(InputManager.Instance, held);
        else p?.GetSetMethod(true)?.Invoke(InputManager.Instance, new object[] { held });
    }

    public static IEnumerator Click(float hold = .08f)
    {
        SetHeld(true); Raise("PrimaryPressed");
        yield return new WaitForSeconds(hold);
        SetHeld(false); Raise("PrimaryReleased");
    }

    public static IEnumerator HeavyClick(float hold = 1.1f) => Click(hold);

    public static void Interact() => Raise("InteractPressed");
    public static void Submit() => Raise("SubmitPressed");

    // ── Movement and looking ──────────────────────────────────────────

    public static IEnumerator Walk(float seconds, Vector2 move, float turnPerSecond = 0f)
    {
        float t = 0;
        while (t < seconds)
        {
            SetMove(move);
            if (turnPerSecond != 0 && Player != null) Player.Look.RotateYaw(turnPerSecond * Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }
        SetMove(Vector2.zero);
    }

    public static IEnumerator Wait(float seconds) { yield return new WaitForSeconds(seconds); }

    // Smoothly turns the player's view toward a point (yaw and pitch).
    public static IEnumerator LookAt(Vector3 point, float seconds, float pitchBias = 0f)
    {
        var p = Player;
        if (p == null) yield break;
        var cam = Camera.main.transform;
        float startYaw = p.Look.YawTransform.eulerAngles.y, startPitch = p.Look.Pitch;
        var dir = point - cam.position;
        float endYaw = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z)).eulerAngles.y;
        float endPitch = Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg + pitchBias;
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / seconds);
            p.Look.SetYaw(Mathf.LerpAngle(startYaw, endYaw, k));
            p.Look.SetPitch(Mathf.Lerp(startPitch, endPitch, k));
            yield return null;
        }
        p.Look.SetYaw(endYaw); p.Look.SetPitch(endPitch);
    }

    // ── Fights ────────────────────────────────────────────────────────

    public static EnemyBrain NearestEnemy(float maxDistance = 60f)
    {
        var p = Player;
        if (p == null) return null;
        return UnityEngine.Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None)
            .Where(e => e != null && e.isActiveAndEnabled && e.GetComponent<Character>() is Character c && c.Stats != null && c.Stats.IsAlive)
            .OrderBy(e => (e.transform.position - p.transform.position).sqrMagnitude)
            .FirstOrDefault(e => (e.transform.position - p.transform.position).magnitude <= maxDistance);
    }

    // Walks up to the nearest enemy along the navmesh, faces it and swings until it dies (or time runs out).
    public static IEnumerator Fight(float maxSeconds = 10f, float reach = 1.6f, bool heavyFirst = false, float maxDistance = 60f)
    {
        var p = Player;
        var enemy = NearestEnemy(maxDistance);
        if (p == null || enemy == null) yield break;
        var target = enemy.GetComponent<Character>();
        float t = 0, nextSwing = 0, nextPath = 0;
        bool heavyDone = !heavyFirst;
        var path = new UnityEngine.AI.NavMeshPath();
        Vector3 waypoint = target.transform.position;
        while (t < maxSeconds && target != null && target.Stats.IsAlive && p.Stats.IsAlive)
        {
            var cam = Camera.main.transform;
            var flat = target.transform.position - p.transform.position; flat.y = 0;
            bool close = flat.magnitude <= reach + 1.5f;
            if (t >= nextPath)
            {
                nextPath = t + .25f;
                waypoint = target.transform.position;
                if (!close && UnityEngine.AI.NavMesh.SamplePosition(p.transform.position, out var from, 2f, UnityEngine.AI.NavMesh.AllAreas)
                    && UnityEngine.AI.NavMesh.CalculatePath(from.position, target.transform.position, UnityEngine.AI.NavMesh.AllAreas, path) && path.corners.Length > 1)
                    waypoint = path.corners[1];
            }
            var aim = close ? target.transform.position + Vector3.up * 1.1f : waypoint + Vector3.up * 1.4f;
            var to = aim - cam.position;
            float yaw = Quaternion.LookRotation(new Vector3(to.x, 0, to.z)).eulerAngles.y;
            p.Look.SetYaw(Mathf.LerpAngle(p.Look.YawTransform.eulerAngles.y, yaw, 1 - Mathf.Exp(-8 * Time.deltaTime)));
            float pitch = close ? Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg : -2f;
            p.Look.SetPitch(Mathf.Lerp(p.Look.Pitch, pitch, 1 - Mathf.Exp(-5 * Time.deltaTime)));
            SetMove(flat.magnitude > reach ? Vector2.up : Vector2.zero);
            if (flat.magnitude <= reach + .4f && t >= nextSwing)
            {
                if (!heavyDone) { heavyDone = true; TrailerRunner.Instance.StartCoroutine(HeavyClick()); nextSwing = t + 1.6f; }
                else { TrailerRunner.Instance.StartCoroutine(Click()); nextSwing = t + .47f; }
            }
            t += Time.deltaTime;
            yield return null;
        }
        SetMove(Vector2.zero);
    }

    // Fights every enemy within range in turn, nearest first.
    public static IEnumerator Clear(float maxSeconds = 40f, float maxDistance = 25f)
    {
        float start = Time.time;
        while (Time.time - start < maxSeconds && Player != null && Player.Stats.IsAlive && NearestEnemy(maxDistance) != null)
            yield return Fight(Mathf.Min(12f, maxSeconds - (Time.time - start)), maxDistance: maxDistance);
    }

    // Walks a navmesh path to a point, looking where it goes.
    public static IEnumerator WalkTo(Vector3 point, float maxSeconds = 15f, float stopAt = 1f)
    {
        var p = Player;
        if (p == null) yield break;
        var path = new UnityEngine.AI.NavMeshPath();
        float t = 0;
        while (t < maxSeconds)
        {
            var flat = point - p.transform.position; flat.y = 0;
            if (flat.magnitude <= stopAt) break;
            Vector3 waypoint = point;
            if (UnityEngine.AI.NavMesh.SamplePosition(p.transform.position, out var from, 2f, UnityEngine.AI.NavMesh.AllAreas)
                && UnityEngine.AI.NavMesh.CalculatePath(from.position, point, UnityEngine.AI.NavMesh.AllAreas, path) && path.corners.Length > 1)
                waypoint = path.corners[1];
            var to = waypoint - p.transform.position;
            float yaw = Quaternion.LookRotation(new Vector3(to.x, 0, to.z)).eulerAngles.y;
            p.Look.SetYaw(Mathf.LerpAngle(p.Look.YawTransform.eulerAngles.y, yaw, 1 - Mathf.Exp(-5 * Time.deltaTime)));
            p.Look.SetPitch(Mathf.Lerp(p.Look.Pitch, -2f, 1 - Mathf.Exp(-3 * Time.deltaTime)));
            SetMove(Vector2.up);
            t += Time.deltaTime;
            yield return null;
        }
        SetMove(Vector2.zero);
    }

    // ── Staged duels ──────────────────────────────────────────────────

    // Chest height of a character, from its colliders (falls back to 1.1 m).
    public static Vector3 AimPoint(Character c)
    {
        var col = c.GetComponent<Collider>();
        return col != null ? col.bounds.center + Vector3.up * col.bounds.extents.y * .25f : c.transform.position + Vector3.up * 1.1f;
    }

    // Eases the view toward a point: steady framing, no snapping.
    public static void Track(Vector3 point, float sharpness = 6f)
    {
        var p = Player;
        if (p == null) return;
        var to = point - Camera.main.transform.position;
        float yaw = Quaternion.LookRotation(new Vector3(to.x, 0, to.z)).eulerAngles.y;
        float pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
        float k = 1 - Mathf.Exp(-sharpness * Time.deltaTime);
        p.Look.SetYaw(Mathf.LerpAngle(p.Look.YawTransform.eulerAngles.y, yaw, k));
        p.Look.SetPitch(Mathf.Lerp(p.Look.Pitch, pitch, k));
    }

    public static EnemyBrain Spawn(string prefabPath, float distance, float sideways = 0f)
    {
        var p = Player;
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var f = Camera.main.transform.forward; f.y = 0; f.Normalize();
        var r = Vector3.Cross(Vector3.up, f);
        var pos = p.transform.position + f * distance + r * sideways;
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
        var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.LookRotation(-f));
        return go.GetComponent<EnemyBrain>();
    }

    // Casts the held spell at a target and waits for the release; returns through hit whether it lost health.
    public static IEnumerator CastAt(Character target, System.Action<bool> hit = null)
    {
        var p = Player;
        var caster = p.GetComponent<PlayerSpellcasting>();
        float before = target.Stats.CurrentHealth;
        for (float t = 0; t < .5f; t += Time.deltaTime) { Track(AimPoint(target), 8f); yield return null; }
        p.Stats.RestoreMana(9999);
        caster.Cast();
        float w = 0;
        while (caster.Busy && w < 3f) { Track(AimPoint(target), 10f); w += Time.deltaTime; yield return null; }
        for (float t = 0; t < .8f; t += Time.deltaTime) { Track(AimPoint(target), 6f); yield return null; }
        hit?.Invoke(target == null || target.Stats.CurrentHealth < before);
    }

    // Holds position facing the target and swings when it is in reach; steps in only when it is out of reach.
    public static IEnumerator Melee(Character target, float maxSeconds = 10f, float reach = 1.7f)
    {
        var p = Player;
        float t = 0, next = 0;
        while (t < maxSeconds && target != null && target.Stats.IsAlive && p.Stats.IsAlive)
        {
            Track(AimPoint(target), 7f);
            var flat = target.transform.position - p.transform.position; flat.y = 0;
            SetMove(flat.magnitude > reach ? Vector2.up * .8f : Vector2.zero);
            if (flat.magnitude <= reach + .3f && t >= next) { TrailerRunner.Instance.StartCoroutine(Click()); next = t + .55f; }
            t += Time.deltaTime;
            yield return null;
        }
        SetMove(Vector2.zero);
        for (float h = 0; h < .6f; h += Time.deltaTime) yield return null;
    }

    // Holds or releases the block (secondary button). With a spell tome in the left hand the press casts instead.
    public static void SetBlock(bool held)
    {
        if (!InputManager.HasInstance) return;
        var p = typeof(InputManager).GetProperty("SecondaryHeld");
        bool was = (bool)p.GetValue(InputManager.Instance);
        if (was == held) return;
        p.GetSetMethod(true).Invoke(InputManager.Instance, new object[] { held });
        Raise(held ? "SecondaryPressed" : "SecondaryReleased");
    }

    // A livelier melee: circles the target, raises the shield while it winds up and strikes in the openings.
    public static IEnumerator ActionMelee(Character target, float maxSeconds = 14f, float reach = 1.8f, float circle = .55f)
    {
        var p = Player;
        var anim = target != null ? target.Animator : null;
        float t = 0, next = 0, side = 1f, flip = 1.6f;
        while (t < maxSeconds && target != null && target.Stats.IsAlive && p.Stats.IsAlive)
        {
            Track(AimPoint(target), 7f);
            var flat = target.transform.position - p.transform.position; flat.y = 0;
            bool enemyAttacking = anim != null && AnimatorHelper.AnyLayerHasTag(anim, Animator.StringToHash("Attack"));
            bool shield = p.Combat.ShieldHeld;
            SetBlock(shield && enemyAttacking);
            if ((t -= 0) > flip) { side = -side; flip = t + UnityEngine.Random.Range(1.2f, 2.2f); }
            float forward = flat.magnitude > reach ? .8f : flat.magnitude < reach - .5f ? -.4f : 0f;
            SetMove(new Vector2(side * circle, forward));
            if (!enemyAttacking && flat.magnitude <= reach + .3f && t >= next) { TrailerRunner.Instance.StartCoroutine(Click()); next = t + .55f; }
            t += Time.deltaTime;
            yield return null;
        }
        SetBlock(false);
        SetMove(Vector2.zero);
        for (float h = 0; h < .6f; h += Time.deltaTime) yield return null;
    }

    // ── Presentation ──────────────────────────────────────────────────

    public static void ShowHud(bool on)
    {
        foreach (var name in new[] { "PlayerVitals", "HotbarUI", "Experience", "Gold counter", "Left Hand HUD", "Stealth Eye", "Message log", "DM line", "InteractUI" })
        {
            var t = GameObject.Find("UI/Canvas")?.transform.Find(name);
            if (t != null)
            {
                var g = t.GetComponent<CanvasGroup>();
                if (g == null) g = t.gameObject.AddComponent<CanvasGroup>();
                g.alpha = on ? 1 : 0;
            }
        }
        var dungeonHud = GameObject.Find("DungeonHud") ?? GameObject.Find("Dungeon HUD");
        if (dungeonHud != null) foreach (var c in dungeonHud.GetComponentsInChildren<Canvas>(true)) c.enabled = on;
    }

    public static void Teleport(Vector3 position, float yaw)
    {
        var p = Player;
        if (p == null) return;
        var cc = p.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        p.transform.position = position;
        if (cc != null) cc.enabled = true;
        p.Look.SetYaw(yaw);
    }
}
