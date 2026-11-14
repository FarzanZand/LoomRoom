using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The Dungeon Master's answer to being shot at (the user, 2026-11-14: "If you shoot the dungeon master with 3 spells in
// one run, he throws a dice on you and you instant die and get knocked back like a ragdoll. Just make sure it still
// connects with you if you try to for instance leave the room or hide behind a wall. Let him say a funny line too.").
//
// DungeonMasterRemarks counts the player's spells that hit him in a run (DieDue) and starts this on the last of them:
// he says his line, turns to the player, lifts the roof off over them, holds a die out over the table's edge and flicks
// it (the Flick spell's own moves: DungeonMasterReach, DungeonMasterHand.Flick). The die can't be escaped: it is not a
// physics body while it flies, it steers onto the player's chest every frame and covers whatever is left of the way in
// the time left, through walls, floors and doors, so it lands at the end of Flight Seconds wherever the player went.
// It kills at once (magic, so no armour or block; god mode still saves the player, as from every death), the body is
// thrown the way the die was going and the view goes head over heels once (PlayerDeathFall.Tumble). The death itself
// is the usual one: RunManager's slow motion and the recap. The die then bounces away as a real body, like the Flick's.
//
// Never during a boss fight (the count isn't taken there, as the house rule about it isn't given there).
[Serializable]
public class DungeonMasterDieSettings
{
    [Min(0), Tooltip("Spells of the player's that hit him in one run before he throws his die at them. 0 = he never does.")]
    public int hits = 3;
    [Tooltip("What he says as he reaches for the die (one at random). It is his answer to that last hit.")]
    public List<DungeonMaster.Line> lines = new()
    {
        new() { text = "That's three. *My turn.*" },
        new() { text = "Three times. I counted. *Catch.*" },
        new() { text = "You get three. *I only need one.*" },
        new() { text = "I warned you. *Roll for survival.*" },
        new() { text = "Enough. *Hold still.*" },
    };
    [Tooltip("The Flick spell: its die, the die's light and sounds, and the roof's sounds are used here too.")]
    public SpellDefinition looks;
    [Min(.05f), Tooltip("The die's size against its model (0.8 m across, painted at the dungeon's 128 texels a metre: at 1 its pixels match the floor's).")]
    public float dieScale = 1f;
    [Min(0f), Tooltip("Seconds his hand takes to come out over the table's edge with the die.")]
    public float reachSeconds = 1.2f;
    [Min(0f), Tooltip("Seconds he holds his cocked finger before the flick.")]
    public float windup = .4f;
    [Min(.3f), Tooltip("Seconds the die is in the air. It lands on the player at the end of them, wherever they are by then.")]
    public float flightSeconds = 2.4f;
    [Min(0f), Tooltip("How high over the straight line to the player the die flies (metres) until it comes down onto them: over the walls, so it is seen coming.")]
    public float arc = 7f;
    [Range(5f, 80f), Tooltip("How steeply it comes down onto the player at the end (degrees from level). Steeper clears nearer walls; past about 40 it comes from above the top of the view.")]
    public float diveAngle = 30f;
    [Min(0f), Tooltip("How hard the view shakes as it lands.")]
    public float hitShake = 2.2f;
    [Min(0f), Tooltip("How fast the body leaves, level (metres a second), the way the die was going.")]
    public float throwSpeed = 10f;
    [Min(0f), Tooltip("Its speed upward as it leaves.")]
    public float throwUp = 6.5f;
    [Min(1f), Tooltip("Pull on the thrown body.")]
    public float throwGravity = 24f;
    [Min(0f), Tooltip("How quickly the body loses its speed once it slides on the floor (metres a second, each second).")]
    public float throwFriction = 16f;
    [Min(.1f), Tooltip("Seconds the view takes to go head over heels as the body is thrown.")]
    public float tumbleSeconds = .85f;
    [Tooltip("Times the view goes over backwards in that time. 0 = it only falls over.")]
    public float tumbleTurns = 1f;
    [Tooltip("What the recap says killed the player.")]
    public string killerName = "the Dungeon Master's die";
}

public class DungeonMasterDie : MonoBehaviour
{
    public static bool Running => running != null;
    static DungeonMasterDie running;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => running = null;

    DungeonMasterDieSettings settings;
    Player target;
    bool flying;

    // He throws his die at this player. False when one is already on its way.
    public static bool Throw(Player target, DungeonMasterDieSettings settings)
    {
        if (running != null || target == null || settings == null) return false;
        var go = new GameObject("Dungeon Master's die (throw)");
        running = go.AddComponent<DungeonMasterDie>();
        running.target = target; running.settings = settings;
        running.StartCoroutine(running.Run());
        return true;
    }

    void OnDestroy() { if (running == this) running = null; }

    // The player it is for is still there to be hit: alive, in play, the run going on.
    bool Chased => target != null && target.IsAlive && target.isActiveAndEnabled
        && (!RunManager.HasInstance || RunManager.Instance.Running && !RunManager.Instance.Ended)
        && BossStageEncounter.Current == null;

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    IEnumerator Run()
    {
        var flick = settings.looks != null ? settings.looks.flick : null;
        if (settings.lines != null)
        {
            var usable = settings.lines.FindAll(l => l != null && !l.IsEmpty);
            if (usable.Count > 0) DungeonMaster.CutIn(usable[UnityEngine.Random.Range(0, usable.Count)], null, .3f);
        }
        // His hands may be in the floor already (a flick, his meddling): the die waits for them.
        for (float t = 0f; t < 15f && DungeonMasterReach.Busy && Chased; t += Time.deltaTime) yield return null;
        if (!Chased) { Destroy(gameObject); yield break; }

        var floor = TableManager.Loader != null ? TableManager.Loader.Dungeon : null;
        var hand = DungeonMasterReach.Hand;
        bool reach = hand != null && DungeonMasterReach.Seat != null && floor != null && !DungeonMasterReach.Busy;
        bool thrown = false, lid = false;
        if (reach)
        {
            DungeonMasterReach.BeginReach();
            yield return DungeonMasterReach.TurnTo(target.transform.position);
            // Only the roof over the player comes off, so the die is seen coming (it lands whether or not they stay under it).
            if (flick != null && Chased) { yield return DungeonMasterReach.Lid(floor, true, new[] { target.transform.position }, flick.lid); lid = true; }
            if (Chased)
            {
                var spot = target.transform.position;
                var over = hand.HoldPoint(spot);
                var die = MakeDie(over, flick);
                yield return hand.Flick(over, Flat(spot - over), settings.reachSeconds, settings.windup, die != null ? die.transform : null, () =>
                {
                    thrown = true;
                    if (flick != null && flick.snapSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(flick.snapSound, over, flick.snapVolume);
                    DungeonMasterReach.Shake(.25f);
                    if (die != null) StartCoroutine(Fly(die.transform, flick));
                });
                // His hand never let go of it (the player died or left meanwhile is handled in Fly; this is a flick that didn't play).
                if (!thrown && die != null) Destroy(die);
            }
        }
        // No hand of his to flick it (no Dungeon Master at the table in this scene, or he never got to it): it comes down from over him.
        if (!thrown && Chased)
        {
            var dm = TableManager.HasInstance ? TableManager.Instance.DM : null;
            var from = dm != null ? dm.transform.position + Vector3.up * 30f : target.transform.position + Vector3.up * 30f;
            var die = MakeDie(from, flick);
            if (die != null) { thrown = true; StartCoroutine(Fly(die.transform, flick)); }
        }
        // Real time from here on: the death's slow motion must not hold his hands and the roof up for half a minute.
        for (float t = 0f; flying && t < 12f; t += Time.unscaledDeltaTime) yield return null;
        if (reach)
        {
            if (hand != null) hand.Release();
            for (float t = 0f; t < .5f; t += Time.unscaledDeltaTime) yield return null;
            for (float t = 0f; hand != null && !hand.Upright && t < 3f; t += Time.unscaledDeltaTime) yield return null;
            if (lid && floor != null) yield return DungeonMasterReach.Lid(floor, false, null, flick.lid);
            yield return DungeonMasterReach.Leave();
        }
        Destroy(gameObject);
    }

    // The die in his fingers: no collisions or physics until it has landed, its own small light (as the Flick's).
    GameObject MakeDie(Vector3 at, FlickSettings flick)
    {
        var die = flick != null && flick.die != null ? Instantiate(flick.die, at, UnityEngine.Random.rotation) : null;
        if (die == null) return null;
        die.name = "Dungeon Master's die (held)";
        if (flick.dieLight > 0f)
        {
            var glow = new GameObject("Die light").AddComponent<Light>();
            glow.transform.SetParent(die.transform, false);
            glow.type = LightType.Point; glow.range = 6f; glow.intensity = flick.dieLight; glow.color = flick.markColor; glow.shadows = LightShadows.None;
        }
        foreach (var c in die.GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (var r in die.GetComponentsInChildren<Rigidbody>()) r.isKinematic = true;
        die.transform.localScale *= settings.dieScale;
        return die;
    }

    // In the air: a point that closes on the player's chest, covering what is left of the way in the time left (so it
    // arrives at the end of the flight however they move), drawn above that line and brought down onto it at the end.
    IEnumerator Fly(Transform die, FlickSettings flick)
    {
        flying = true;
        die.name = "Dungeon Master's die";
        var line = die.position;
        var velocity = Vector3.zero;
        var spinAxis = UnityEngine.Random.onUnitSphere;
        // Its light flies a little ahead of it on the player's side, so the faces they see are lit: over the dark room it
        // was a black speck until it was in their face (a light inside a die lights none of it).
        var glow = die.GetComponentInChildren<Light>();
        float flight = Mathf.Max(.3f, settings.flightSeconds);
        for (float t = 0f; ; t += Time.deltaTime)
        {
            if (die == null) { flying = false; yield break; }
            if (!Chased) { Destroy(die.gameObject); flying = false; yield break; }
            float dt = Time.deltaTime, left = flight - t;
            var chest = SpellEffects.Chest(target);
            if (left <= dt) { if (velocity.sqrMagnitude < .01f) velocity = chest - die.position; break; }
            if (dt > 0f)
            {
                line += (chest - line) / left * dt;
                // Up out of his hand to its height over the line, held there, then down a slope onto the player: the height
                // is never more than that slope allows for the level way still to go, so it is on the line as it lands.
                float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t + dt) / (flight * .2f)));
                float away = Flat(chest - line).magnitude;
                var at = line + Vector3.up * Mathf.Min(settings.arc * rise, away * Mathf.Tan(settings.diveAngle * Mathf.Deg2Rad));
                velocity = (at - die.position) / dt;
                die.position = at;
                die.rotation = Quaternion.AngleAxis(900f * dt, spinAxis) * die.rotation;
                if (glow != null) glow.transform.position = at + (chest - at).normalized * Mathf.Min(1.1f, (chest - at).magnitude * .5f);
            }
            yield return null;
        }
        if (glow != null) glow.transform.localPosition = Vector3.zero;
        yield return Strike(die, velocity, flick);
    }

    IEnumerator Strike(Transform die, Vector3 velocity, FlickSettings flick)
    {
        var chest = SpellEffects.Chest(target);
        die.position = chest;
        var way = Flat(velocity);
        if (way.sqrMagnitude < .01f) way = Flat(target.transform.forward) * -1f;
        way.Normalize();
        if (flick != null && AudioManager.HasInstance)
        {
            if (flick.hitSound != null) AudioManager.Instance.PlaySFX(flick.hitSound, chest, flick.hitVolume);
            Knock(die.position, flick);
        }
        if (settings.hitShake > 0f) DungeonMasterReach.Shake(settings.hitShake);

        // The death: all of the player's health and more, as magic (no armour, no block, no blessing).
        if (!string.IsNullOrEmpty(settings.killerName)) Character.ProperNames.Add(settings.killerName);
        if (target.Stats != null)
        {
            target.Stats.BlockBlessing = false;
            var hit = DamageInfo.Simple(target.Stats.CurrentHealth * 100f + 100000f);
            hit.Magic = true; hit.SourceName = settings.killerName; hit.HitPoint = chest; hit.Direction = way;
            target.Stats.TakeDamage(hit);
        }
        StartCoroutine(ThrowBody(target, way));

        // The die springs back off them and falls as a real body: bounces, rolls to rest, lies a moment and is gone.
        foreach (var c in die.GetComponentsInChildren<Collider>(true)) { c.enabled = true; c.sharedMaterial = Bounce; }
        var body = die.GetComponent<Rigidbody>();
        if (body == null) body = die.gameObject.AddComponent<Rigidbody>();
        body.isKinematic = false;
        body.mass = 8f;
        body.angularDamping = .6f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        DeathPhysics.IgnorePlayers(new List<Rigidbody> { body });
        // Off to the side of the way the body goes, so it doesn't fly along in the thrown player's face.
        var aside = Vector3.Cross(Vector3.up, way) * (UnityEngine.Random.value < .5f ? -1f : 1f);
        body.linearVelocity = (aside * .8f - way * .6f) * 4f + Vector3.up * 5.5f;
        body.angularVelocity = UnityEngine.Random.onUnitSphere * 10f;
        bool rolling = false;
        float lastVy = body.linearVelocity.y, still = 0f;
        for (float t = 0f; t < 4f && die != null; t += Time.deltaTime)
        {
            yield return new WaitForFixedUpdate();
            if (die == null) break;
            body.AddForce(Physics.gravity * 1.5f, ForceMode.Acceleration);
            float vy = body.linearVelocity.y;
            if (lastVy < -1.5f && vy > lastVy + 1.5f)
            {
                Knock(die.position, flick);
                if (!rolling) { rolling = true; if (flick != null && flick.dieRollSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(flick.dieRollSound, die.position, flick.dieVolume); }
            }
            lastVy = vy;
            still = body.linearVelocity.sqrMagnitude < .02f && body.angularVelocity.sqrMagnitude < .05f ? still + Time.fixedDeltaTime : 0f;
            if (still > .6f) break;
        }
        flying = false;
        if (die != null)
        {
            body.isKinematic = true;
            var start = die.localScale;
            for (float u = 0f; u < .25f && die != null; u += Time.unscaledDeltaTime) { die.localScale = start * (1f - u / .25f); yield return null; }
            if (die != null) Destroy(die.gameObject);
        }
    }

    void Knock(Vector3 at, FlickSettings flick)
    {
        if (flick == null || flick.dieKnocks == null || flick.dieKnocks.Length == 0 || !AudioManager.HasInstance) return;
        AudioManager.Instance.PlaySFX(flick.dieKnocks[UnityEngine.Random.Range(0, flick.dieKnocks.Length)], at, flick.dieVolume);
    }

    static PhysicsMaterial bounce;
    static PhysicsMaterial Bounce => bounce != null ? bounce : bounce = new PhysicsMaterial("Dungeon Master's die bounce")
    {
        bounciness = .3f, dynamicFriction = .6f, staticFriction = .7f,
        bounceCombine = PhysicsMaterialCombine.Maximum, frictionCombine = PhysicsMaterialCombine.Average,
    };

    // The body thrown the way the die was going: up and back in an arc, one small bounce, then a slide to rest. Moved by
    // its own CharacterController, so walls and ledges stop it as they stop a walk. On real time, like the death's fall
    // (PlayerDeathFall): the death's slow motion would stretch the flight past the recap.
    IEnumerator ThrowBody(Player who, Vector3 way)
    {
        var controller = who != null && who.Motor != null ? who.Motor.Controller : null;
        if (controller == null) yield break;
        if (!who.IsAlive) PlayerDeathFall.Tumble(settings.tumbleSeconds, settings.tumbleTurns);
        var v = way * settings.throwSpeed + Vector3.up * settings.throwUp;
        bool bounced = false;
        for (float t = 0f; t < 3f && who != null && controller != null; )
        {
            // Paused, or the body is no longer one to move (revived and walking, switched off with its floor).
            if (Time.timeScale <= 0f || !controller.enabled || !who.isActiveAndEnabled) { yield return null; continue; }
            if (who.IsAlive && t > .2f && !DevGodModeOn) break;
            // The frame's own length with the slow motion taken out (not the clock's: a frame that stalls is one step).
            float dt = Mathf.Min(Time.deltaTime / Time.timeScale, .05f);
            t += dt;
            v.y -= settings.throwGravity * dt;
            var before = who.transform.position;
            var met = controller.Move(v * dt);
            if ((met & CollisionFlags.Sides) != 0)
            {
                // Against a wall: it keeps only the speed it really made along it (what went into the wall is gone), less a little.
                var made = Flat(who.transform.position - before) / dt * .85f;
                v.x = made.x; v.z = made.z;
            }
            if ((met & CollisionFlags.Below) != 0 && v.y <= 0f)
            {
                if (!bounced && v.y < -4f) { v.y = -v.y * .22f; bounced = true; }
                else
                {
                    v.y = -1f;
                    var level = Vector3.MoveTowards(Flat(v), Vector3.zero, settings.throwFriction * dt);
                    v.x = level.x; v.z = level.z;
                    if (level.sqrMagnitude < .01f) break;
                }
            }
            yield return null;
        }
    }

    // God mode (Editor and development builds) keeps the player alive through it: the body is still thrown.
    static bool DevGodModeOn =>
#if UNITY_EDITOR || DEBUG
        DevGodMode.On;
#else
        false;
#endif
}
