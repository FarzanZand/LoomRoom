using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

// How he gets his hand to the enemy. Serialized by integer: append only.
public enum FlickReach
{
    WalkOrDie = 0,   // he walks to the table's edge and flicks it with his finger, or out of his reach flicks a die at it
    Appear = 1,      // he is there at once beside it, standing in the table, and flicks it with his finger wherever it is
    DieOnly = 2,     // he flicks a die at it from where he stands
    Portal = 3,      // a portal opens in the room beside it and the die shoots out of it at it
}

[Serializable]
public class FlickSettings
{
    [FoldoutGroup("Flick", Expanded = true), Tooltip("Walk Or Die: he walks to the table's edge for his finger, or flicks a die from where he is when it is out of reach. Appear: he is there at once beside the enemy, standing in the table, and flicks it with his finger anywhere. Die Only: always the die.")]
    public FlickReach reach = FlickReach.WalkOrDie;
    [FoldoutGroup("Flick"), ShowIf("@reach == FlickReach.Appear"), Min(4f), Tooltip("How far from the enemy he appears (level, world units): his finger reaches it from there.")]
    public float appearDistance = 13f;
    [FoldoutGroup("Flick"), ShowIf("@reach == FlickReach.Appear"), Range(0f, 1f), Tooltip("How far ahead of the enemy (along the player's view) he stands, against beside it: ahead, he is seen; beside, it flies across the view.")]
    public float appearAhead = .75f;
    [FoldoutGroup("Flick"), ShowIf("@reach == FlickReach.Appear"), Min(0f), Tooltip("He stands at least this far outside the roof that comes off (world units), so it can come back down while he is still there; he leaves only once it is back on and hides him. Out to Appear Farthest from the enemy.")]
    public float clearOfRoof = 4f;
    [FoldoutGroup("Flick"), ShowIf("@reach == FlickReach.Appear"), Min(0f), Tooltip("With no roof over the spot (an open-sky room, a board, the boss stage) he stands at least this far from the player (level, world units), so his body doesn't fill their view.")]
    public float openSkyFromPlayer = 18f;
    [FoldoutGroup("Flick"), ShowIf("@reach == FlickReach.Appear"), Min(4f)] public float appearFarthest = 20f;
    [FoldoutGroup("Flick"), ShowIf("@reach == FlickReach.Appear"), Range(0f, 60f), Tooltip("Degrees he stands turned to the left of the enemy, so it is ahead and to his right and his right hand reaches it without crossing his body.")]
    public float turnRight = 25f;
    [FoldoutGroup("Flick"), Min(1f), Tooltip("Furthest from the player the enemy may stand.")]
    public float range = 16f;
    [FoldoutGroup("Flick"), Range(1f, 30f), Tooltip("Degrees off the crosshair an enemy may stand and still be the one picked.")]
    public float aimCone = 15f;
    [FoldoutGroup("Flick"), Min(.2f), Tooltip("Seconds his hand takes to come in over the enemy once he stands at the table.")]
    public float reachSeconds = 1.6f;
    [FoldoutGroup("Flick"), Min(1f), Tooltip("How far his flicking fingertip reaches past the table's edge (level, world units). Further, he flicks the die instead.")]
    public float fingerReach = 12f;
    [FoldoutGroup("Flick"), Min(1f), Tooltip("How far his hand reaches back from the enemy along the flick (world units): he flicks it the way his hand has room behind it.")]
    public float handLength = 5f;
    [FoldoutGroup("Flick"), Min(0f), Tooltip("Open floor the enemy needs round it (metres to the nearest wall) for his hand to come down into the room; in a tight spot he flicks the die at it instead.")]
    public float handRoom = 1.8f;
    [FoldoutGroup("Flick"), Min(0f), Tooltip("How far from the table's edge he stands for it (world units; he stands further back to set figures down).")]
    public float edgeGap = 1.5f;
    [FoldoutGroup("Flick"), Min(0f), Tooltip("Seconds he holds his cocked finger before the snap.")]
    public float windup = .35f;
    [FoldoutGroup("Flick"), Range(0f, 90f), Tooltip("Degrees either side of straight away from him he may flick it: he picks the way with the most room to fly that isn't toward the player.")]
    public float sideways = 70f;
    [FoldoutGroup("Flick"), Min(0f), Tooltip("Furthest he walks along the table for a flick with his finger (world units). Further, he flicks the die from where he stands.")]
    public float maxWalk = 40f;
    [FoldoutGroup("Flick"), Min(1f), Tooltip("How fast he walks to the table's edge for it (world units a second).")]
    public float walkSpeed = 45f;
    [FoldoutGroup("Flick"), Min(1f), Tooltip("How fast the enemy leaves his finger (metres a second, level).")]
    public float launchSpeed = 11f;
    [FoldoutGroup("Flick"), Min(0f), Tooltip("Its speed upward as it leaves.")]
    public float launchUp = 7f;
    [FoldoutGroup("Flick"), Min(1f)] public float gravity = 25f;
    [FoldoutGroup("Flick"), Tooltip("Degrees a second it tumbles in the air.")]
    public float spin = 720f;
    [FoldoutGroup("Flick"), Min(0f), Tooltip("Seconds it lies stunned where it lands, if it lives.")]
    public float stunSeconds = 1.5f;
    [FoldoutGroup("Flick"), Range(0f, 1f), Tooltip("Share of the spell's power dealt to every other enemy it is flicked into.")]
    public float bowlShare = .5f;
    [FoldoutGroup("Out of his reach"), AssetsOnly, Tooltip("Too far from the table's edge for his hand: he flicks this at it from the nearest point he reaches.")]
    public GameObject die;
    [FoldoutGroup("Out of his reach"), Min(.05f)] public float dieScale = 1.5f;
    [FoldoutGroup("Out of his reach"), Min(1f), Tooltip("How far above the higher of his hand and the enemy the die's arc peaks: high enough to be seen coming over the walls.")]
    public float dieArc = 7f;
    [FoldoutGroup("Out of his reach"), Min(1f), Tooltip("Pull on the die in flight (lower: a slower, higher arc that is seen coming).")]
    public float dieGravity = 16f;
    [FoldoutGroup("Out of his reach"), Min(0f), Tooltip("Light the die carries, so it is seen crossing the dark.")]
    public float dieLight = 12f;
    [FoldoutGroup("Out of his reach"), Tooltip("One of these, at random, each time the die strikes or bounces.")] public AudioClip[] dieKnocks = new AudioClip[0];
    [FoldoutGroup("Out of his reach"), Tooltip("The die tumbling away after it strikes.")] public AudioClip dieRollSound;
    [FoldoutGroup("Out of his reach"), Range(0, 1)] public float dieVolume = 1f;
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal"), Tooltip("The portal's look: a strip of frames of a hole burned through the air (Tools/dice_portal_texture.py) on the Portal Vortex shader, copied per portal and stepped through.")]
    public Material portalMaterial;
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal"), Min(1), Tooltip("Frames side by side in the material's texture.")]
    public int portalFrames = 8;
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal"), Min(1f), Tooltip("Frames a second.")]
    public float portalFps = 12f;
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal")] public Color portalColor = new(.32f, .48f, 1f, 1f);
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal"), Min(.2f), Tooltip("The portal's radius (metres).")]
    public float portalRadius = .8f;
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal"), Min(1.5f), Tooltip("How far from the enemy it opens at most (metres); closer where the room is too small.")]
    public float portalDistance = 5.5f;
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal"), Min(.05f), Tooltip("Seconds it takes to open.")]
    public float portalOpenSeconds = .35f;
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal"), Min(0f), Tooltip("Seconds it stands open before the die comes out.")]
    public float portalHoldSeconds = .3f;
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal"), Min(.05f), Tooltip("Seconds it takes to close once the die is out.")]
    public float portalCloseSeconds = .3f;
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal"), Min(4f), Tooltip("How fast the die shoots out of it (metres a second).")]
    public float portalDieSpeed = 20f;
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal"), Min(0f)] public float portalLight = 6f;
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal")] public AudioClip portalOpenSound;
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal"), Tooltip("The die shooting out.")] public AudioClip portalShootSound;
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal")] public AudioClip portalCloseSound;
    [FoldoutGroup("Portal"), ShowIf("@reach == FlickReach.Portal"), Range(0, 1)] public float portalVolume = .9f;
    [FoldoutGroup("Lines"), Tooltip("He says one as he comes.")]
    public string[] lines = { "Hold still.", "Allow me.", "Let me get that for you." };
    [FoldoutGroup("Look")] public AudioClip snapSound;
    [FoldoutGroup("Look"), Range(0, 1)] public float snapVolume = 1f;
    [FoldoutGroup("Look"), Tooltip("The enemy struck, by his finger or the die.")] public AudioClip hitSound;
    [FoldoutGroup("Look"), Range(0, 1)] public float hitVolume = 1f;
    [FoldoutGroup("Look"), Tooltip("When it slams into a wall or the floor.")] public AudioClip slamSound;
    [FoldoutGroup("Look"), Range(0, 1)] public float slamVolume = 1f;
    [FoldoutGroup("Look"), Min(0f), Tooltip("How hard the view shakes as his finger or the die strikes the enemy.")] public float hitShake = 1.2f;
    [FoldoutGroup("Look"), Min(0f), Tooltip("How hard the view shakes as the flicked enemy slams into a wall or the floor.")] public float slamShake = 1.5f;
    [FoldoutGroup("Look"), Tooltip("The roof over the player's room (and the enemy's) coming off and landing back on: sounds and dust.")]
    public LidEffects lid = new();
    [FoldoutGroup("Look"), AssetsOnly, Tooltip("Clings to the enemy he has picked, from the cast until his finger or die strikes it.")]
    public SpellVisual mark;
    [FoldoutGroup("Look"), Tooltip("Lights the enemy he has picked while he comes for it.")] public Color markColor = new(1f, .85f, .55f);
    [FoldoutGroup("Look"), Min(0f)] public float markLight = 3f;
}

// Flick: the Dungeon Master does it for you. The enemy under the crosshair is held where it stands; he gets up, comes to
// the nearest edge of the table, lifts the floor's lid, reaches in and flicks it across the room with his finger: it
// tumbles through the air and slams into the wall or the floor (and anyone in its way). Too far from the table's edge for
// his hand, he flicks a die at it from as close as he reaches instead; or (Portal) a portal opens in the room and the die
// shoots out of it. Run on its own object, one at a time.
public class Flick : MonoBehaviour
{
    static Flick running;
    public static bool Running => running != null;
    // The enemy the running flick is for (a camera or test can follow it).
    public static Character Watched => running != null ? running.target : null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { running = null; picked = null; }

    Player caster;
    SpellDefinition spell;
    FlickSettings settings;
    AdventurerProgress progress;
    Character target;
    float power;
    bool flying;
    Transform looseDie;

    static Transform View(Player player) => player.CameraRig != null && player.CameraRig.Camera != null ? player.CameraRig.Camera.transform : player.transform;

    // The enemy picked as the cast began: kept through the wind-up, so one that steps in close meanwhile is still the one.
    static Character picked; static float pickedAt;

    // The enemy under the crosshair, or the nearest one close to it, in sight and in range (the one picked as the cast
    // began, while it lives and stays in range).
    public static Character Target(Player player, FlickSettings s)
    {
        var view = View(player);
        if (picked != null && picked.IsAlive && Time.time - pickedAt < 3f && Vector3.Distance(picked.transform.position, view.position) <= s.range + 2f) return picked;
        Character best = null;
        float bestScore = float.MaxValue;
        foreach (var c in SpellEffects.HostilesNear(player, view.position, s.range))
        {
            // A boss fight's own enemies and the boss itself can be flicked too (by the player's avatar: RunFromRoom).
            if (c.GetComponent<Summon>() != null) continue;
            var chest = SpellEffects.Chest(c);
            // Off the crosshair by its nearest point between feet and head (aiming a little high or low still picks it).
            var feet = c.transform.position + Vector3.up * .2f;
            var head = feet + Vector3.up * Mathf.Max(.3f, (chest.y - feet.y) * 1.8f);
            var onRay = view.position + view.forward * Mathf.Max(0f, Vector3.Dot(chest - view.position, view.forward));
            var nearest = feet + Vector3.Project(onRay - feet, head - feet);
            nearest.y = Mathf.Clamp(nearest.y, feet.y, head.y);
            var to = nearest - view.position;
            float angle = Vector3.Angle(view.forward, to);
            if (angle > s.aimCone) continue;
            if (!LineOfSight.Clear(view.position, chest, ~0, c.transform, player.transform, 0f, false)) continue;
            float score = angle * 2f + to.magnitude * .2f;
            if (score < bestScore) { best = c; bestScore = score; }
        }
        if (best != null) { picked = best; pickedAt = Time.time; }
        return best;
    }

    public static string Refusal(Player player, SpellDefinition spell)
    {
        var floor = TableManager.Loader != null ? TableManager.Loader.Dungeon : null;
        bool portal = spell.flick.reach == FlickReach.Portal;
        // In a boss fight he is at the table already, busy with it: the player's own avatar in the room flicks instead.
        if (floor != null && !portal && BossStageEncounter.Current != null)
        {
            if (RoomBody() == null) return "You can't reach it";
            if (Running) return "Not yet";
            return Target(player, spell.flick) == null ? "Nothing to flick" : null;
        }
        if (floor == null || !portal && (DungeonMasterReach.Hand == null || DungeonMasterReach.Seat == null)) return "He can't reach you here";
        if (Running || !portal && DungeonMasterReach.Busy) return "He's busy";
        if (Target(player, spell.flick) == null) return "Nothing to flick";
        return null;
    }

    public static void Cast(Player player, SpellDefinition spell, float power, AdventurerProgress progress)
    {
        var target = Target(player, spell.flick);
        picked = null;
        if (target == null || Running) return;
        var go = new GameObject(spell.name + " (flick)");
        var flick = go.AddComponent<Flick>();
        flick.caster = player; flick.spell = spell; flick.settings = spell.flick; flick.power = power; flick.progress = progress; flick.target = target;
        running = flick;
        flick.StartCoroutine(flick.Run());
    }

    void OnDestroy() { if (running == this) running = null; }

    IEnumerator Run()
    {
        if (settings.reach == FlickReach.Portal) { yield return RunPortal(); Destroy(gameObject); yield break; }
        if (BossStageEncounter.Current != null) { yield return RunFromRoom(); Destroy(gameObject); yield break; }
        var floor = TableManager.Loader != null ? TableManager.Loader.Dungeon : null;
        var hand = DungeonMasterReach.Hand;
        var held = Hold(target);
        if (settings.lines != null && settings.lines.Length > 0) DungeonMaster.Say(settings.lines[UnityEngine.Random.Range(0, settings.lines.Length)]);
        DungeonMasterReach.BeginReach();
        var spot = target.transform.position;
        // Decided before he walks (where he will stand is worked out from his own side of the table): his finger reaches
        // the enemy from the table's edge, and it stands in the open.
        bool appear = settings.reach == FlickReach.Appear && AppearBy(target);
        bool near = appear || settings.reach == FlickReach.WalkOrDie && DungeonMasterReach.InHandReach(spot, settings.fingerReach, settings.edgeGap, settings.maxWalk) && Roomy(spot);
        if (appear) yield return new WaitForSeconds(DungeonMasterReach.AppearFade);   // he fades in where he stands
        else if (near)
        {
            yield return DungeonMasterReach.Approach(spot, settings.walkSpeed);
            yield return DungeonMasterReach.StepIn(spot, settings.edgeGap);
        }
        // Out of his reach he stays where he is and turns to it.
        else yield return DungeonMasterReach.TurnTo(spot);
        // Only the roof over the player and the enemy comes off.
        yield return DungeonMasterReach.Lid(floor, true, new[] { caster.transform.position, target != null ? target.transform.position : caster.transform.position }, settings.lid);
        if (target != null && target.IsAlive && hand != null)
        {
            spot = target.transform.position;
            if (near)
            {
                // Away from him (he stands on his side of it and flicks it on across the table), the way with the most room
                // to fly, and never at the player: it flies across their view, not into their face.
                var away = FlightDirection(target, Flat(spot - hand.transform.position));
                var point = SpellEffects.Chest(target);
                var body = target;
                yield return hand.Flick(point, away, settings.reachSeconds, settings.windup, null, () => { Release(held); Snap(SpellEffects.Chest(body)); Hit(SpellEffects.Chest(body)); StartCoroutine(Fly(body, away, power, true)); },
                    () => body != null ? SpellEffects.Chest(body) : point);
            }
            else
            {
                // In his fingers out in front of him toward it, over the table's edge: the die flies the rest of the way.
                var over = hand.HoldPoint(spot);
                var die = MakeDie(over);
                var toward = Flat(spot - over);
                yield return hand.Flick(over, toward, settings.reachSeconds, settings.windup, die != null ? die.transform : null, () =>
                {
                    Snap(over);
                    if (die != null) StartCoroutine(DieFly(die.transform, held));
                    else { Release(held); StartCoroutine(Fly(target, toward, power, true)); }
                });
            }
        }
        else Release(held);
        for (float t = 0; flying && t < 8f; t += Time.deltaTime) yield return null;
        yield return new WaitForSeconds(.5f);
        // The roof only comes down once he stands up straight. Where he appeared clear of it he is still there as it lands
        // and goes only once it hides him; inside its footprint he goes first, so it never lands on him.
        for (float t = 0; hand != null && !hand.Upright && t < 3f; t += Time.deltaTime) yield return null;
        if (appear && !clearOfLid) yield return DungeonMasterReach.FadeAway();
        yield return DungeonMasterReach.Lid(floor, false, null, settings.lid);
        if (appear && clearOfLid) yield return DungeonMasterReach.FadeAway();
        if (!appear) yield return DungeonMasterReach.StepBack(spot);
        yield return DungeonMasterReach.Leave();
        Destroy(gameObject);
    }

    // ── The player's own avatar in the room (a boss fight: the Dungeon Master is at the table already) ──────────

    // The room player's body (its humanoid animator), switched off with the room player while a run is played.
    static Animator RoomBody()
    {
        var room = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Room) : null;
        if (room == null) return null;
        // Its body sits beside its controller under the room player's root (Players/RoomPlayer/BodyVisuals).
        var root = room.transform.parent != null ? room.transform.parent : room.transform;
        foreach (var a in root.GetComponentsInChildren<Animator>(true)) if (a.isHuman && a.gameObject.activeSelf) return a;
        return null;
    }

    // From the player's own side of the table: a stand-in of their room avatar fades in at the table's edge across from
    // the Dungeon Master (the room player is switched off while a run is played), holds the die out over the edge, flicks
    // it across onto the enemy and fades out again. No lid and no walk: the boss stage is open and he is busy with it.
    IEnumerator RunFromRoom()
    {
        var body = RoomBody();
        var floor = TableManager.Loader != null ? TableManager.Loader.Dungeon : null;
        var dm = TableManager.HasInstance ? TableManager.Instance.DM : null;
        if (body == null || floor == null || dm == null || target == null) yield break;
        // Across the table from him, as far from the board's middle as he is.
        var middle = floor.transform.position; var him = dm.transform.position;
        var away = Flat(middle - him); if (away.sqrMagnitude < .01f) away = Vector3.forward;
        var span = him - middle; span.y = 0f; float reach = span.magnitude;   // (Flat normalizes)
        var room = PlayerManager.Instance.GetPlayer(PlayerKind.Room);
        // The avatar itself when it stands at the table (where the player left it to play): it flicks from there.
        var fromMiddle = body.transform.position - middle; fromMiddle.y = 0f;
        if (body.isActiveAndEnabled && fromMiddle.magnitude <= reach * 1.15f && Vector3.Dot(fromMiddle, away) > 0f)
        {
            var own = body.TryGetComponent<DungeonMasterHand>(out var h) ? h : body.gameObject.AddComponent<DungeonMasterHand>();
            yield return FlickFrom(own);
            yield break;
        }
        var standAt = middle + away * reach; standAt.y = room != null ? room.transform.position.y : him.y;
        // A copy made inside a switched-off holder, so none of the avatar's own scripts wake; only its animator and renderers stay.
        var holder = new GameObject("Avatar stand-in holder"); holder.SetActive(false);
        var standIn = Instantiate(body.gameObject, holder.transform);
        standIn.name = "Room avatar (flick)";
        // At once, while still switched off (a deferred Destroy would let them wake as it is switched on); twice, for any
        // that another one required.
        for (int pass = 0; pass < 2; pass++)
            foreach (var c in standIn.GetComponentsInChildren<MonoBehaviour>(true)) if (c != null) DestroyImmediate(c);
        foreach (var c in standIn.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        standIn.transform.SetParent(null, false);
        standIn.transform.localScale = body.transform.lossyScale;
        standIn.transform.SetPositionAndRotation(standAt, Quaternion.LookRotation(-away));
        Destroy(holder);
        var hand = standIn.AddComponent<DungeonMasterHand>();
        // The avatar itself stood elsewhere in the room: it isn't there twice while the stand-in flicks.
        var shown = new List<Renderer>();
        foreach (var r in body.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; shown.Add(r); }
        DungeonMasterFade.Begin(standIn); DungeonMasterFade.Set(0f);
        yield return DungeonMasterFade.To(1f, DungeonMasterReach.AppearFade, true);
        yield return FlickFrom(hand);
        DungeonMasterFade.Begin(standIn);
        yield return DungeonMasterFade.To(0f, DungeonMasterReach.AppearFade, false);
        DungeonMasterFade.End();
        Destroy(standIn);
        foreach (var r in shown) if (r != null) r.enabled = true;
    }

    // The die held out over the table's edge in this hand, flicked across onto the enemy.
    IEnumerator FlickFrom(DungeonMasterHand hand)
    {
        if (target == null) yield break;
        var held = Hold(target);
        var spot = target.transform.position;
        var over = hand.HoldPoint(spot);
        var die = MakeDie(over);
        var toward = Flat(spot - over);
        yield return hand.Flick(over, toward, settings.reachSeconds, settings.windup, die != null ? die.transform : null, () =>
        {
            Snap(over);
            if (die != null) StartCoroutine(DieFly(die.transform, held));
            else { Release(held); if (target != null) StartCoroutine(Fly(target, toward, power, true)); }
        });
        for (float t = 0; flying && t < 8f; t += Time.deltaTime) yield return null;
        hand.Release();
        yield return new WaitForSeconds(.3f);
    }

    // The die held in the fingers before it is flicked: no collisions or physics until it flies, its own small light.
    GameObject MakeDie(Vector3 over)
    {
        var die = settings.die != null ? Instantiate(settings.die, over, Quaternion.identity) : null;
        if (die == null) return null;
        die.name = "Flick die (held)";
        if (settings.dieLight > 0f)
        {
            var glow = new GameObject("Die light").AddComponent<Light>();
            glow.transform.SetParent(die.transform, false);
            glow.type = LightType.Point; glow.range = 6f; glow.intensity = settings.dieLight; glow.color = settings.markColor; glow.shadows = LightShadows.None;
        }
        foreach (var c in die.GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (var r in die.GetComponentsInChildren<Rigidbody>()) r.isKinematic = true;
        die.transform.localScale *= settings.dieScale;
        return die;
    }

    // He appears beside the enemy, on whichever side gives it the longer flight: a little ahead of it along the player's
    // view (so he is seen) and to one side (so it flies across the view, not at the player).
    // Outside the roof that comes off (true when he stands clear of it): it can come down with him still there.
    bool clearOfLid;

    bool AppearBy(Character body)
    {
        var view = Flat(View(caster).forward);
        var right = Vector3.Cross(Vector3.up, view);
        var spot = body.transform.position;
        var floor = TableManager.Loader != null ? TableManager.Loader.Dungeon : null;
        bool hasRoof = DungeonMasterReach.RoofArea(floor, new[] { caster.transform.position, spot }, out var roof);
        var openRoom = roof;
        if (hasRoof) roof.Expand(new Vector3(settings.clearOfRoof * 2f, 0f, settings.clearOfRoof * 2f));
        float lean = DungeonMasterReach.Hand != null ? DungeonMasterReach.Hand.flickAboveLean + 5f : 20f;
        // Each side, then a few turns further round, each out from Appear Distance to Appear Farthest: the first spot
        // where nothing of the floor is inside his body (a tall room's walls rise past his head) and, if it can be, clear
        // of the roof that comes off. The side where his right arm (the flicking one) is toward the player comes first,
        // so it is seen reaching across the view; from the other side it reaches out behind his own body and head.
        var sides = new System.Collections.Generic.List<(Vector3 from, float score)>();
        foreach (var side in new[] { 1f, -1f })
            foreach (var turn in new[] { 0f, 30f, -30f, 60f, -60f })
            {
                var from = Quaternion.Euler(0f, turn, 0f) * (right * side + view * settings.appearAhead).normalized;
                if (Vector3.Dot(from, -view) > .3f) continue;   // never behind the player
                FlightDirection(body, -from, out float room);
                if (Vector3.Dot(Vector3.Cross(Vector3.up, -from), -view) > 0f) room += 1000f;
                sides.Add((from, room - Mathf.Abs(turn)));
            }
        sides.Sort((x, y) => y.score.CompareTo(x.score));
        // Under open sky nothing hides his body (under a roof he stands outside the room and only his hand is seen): he
        // stands ahead of the player, beyond the enemy, and reaches back to it, seen in front as on the boss stage, not
        // filling the side of their view.
        if (!hasRoof)
            foreach (var turn in new[] { 45f, -45f, 25f, -25f, 0f })
                sides.Insert(0, (Quaternion.Euler(0f, turn, 0f) * view, 0f));
        Vector3 at = Vector3.zero; bool found = false; clearOfLid = false;
        foreach (bool wantClear in new[] { true, false })
        {
            foreach (var (from, _) in sides)
            {
                for (float d = settings.appearDistance; d <= settings.appearFarthest; d += 1f)
                {
                    var p = spot + from * d;
                    var level = p; if (hasRoof) level.y = roof.center.y;
                    // Clear: outside the roof that comes off, or with no roof, far enough from the player to stay out of their face.
                    bool clear = hasRoof ? !roof.Contains(level) : Flat(p - caster.transform.position).magnitude >= settings.openSkyFromPlayer;
                    if (wantClear && !clear) continue;
                    // Under a roof only its open room's walls matter; under open sky every piece of the floor round him does.
                    if (hasRoof ? !DungeonMasterReach.BodyClear(floor, p, spot, lean, openRoom) : !DungeonMasterReach.BodyClearOpen(floor, p, spot, lean)) continue;
                    at = p; clearOfLid = hasRoof && clear; found = true; break;
                }
                if (found) break;
            }
            if (found) break;
        }
        // Nowhere he fits: he flicks the die at it from his own place instead.
        if (!found) return false;
        if (!DungeonMasterReach.Appear(at, spot, settings.turnRight)) return false;
        return true;
    }

    // Open floor round the spot, enough for his hand to come down into the room without going through its walls.
    bool Roomy(Vector3 spot) => !NavMesh.FindClosestEdge(spot, out var edge, NavMesh.AllAreas) || edge.distance >= settings.handRoom;

    // Of the directions within Sideways of straight away from him, the one with the longest clear flight at chest height
    // (out to where it would land), turned off any that head at the player.
    Vector3 FlightDirection(Character body, Vector3 away) => FlightDirection(body, away, out _);
    Vector3 FlightDirection(Character body, Vector3 away, out float bestScore)
    {
        var chest = SpellEffects.Chest(body);
        var toPlayer = Flat(caster.transform.position - body.transform.position);
        float flight = settings.launchSpeed * (2f * settings.launchUp / settings.gravity + .3f);
        Vector3 best = away; bestScore = float.MinValue;
        for (float a = -settings.sideways; a <= settings.sideways + .01f; a += 10f)
        {
            var dir = Quaternion.Euler(0f, a, 0f) * away;
            float room = flight;
            if (Physics.SphereCast(chest, .35f, dir, out var hit, flight, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Character>() == null) room = hit.distance;
            // His hand lies behind the enemy, back along the flick: a wall there and his hand would come through it.
            float handRoom = settings.handLength;
            if (Physics.SphereCast(chest + Vector3.up * .5f, .5f, -dir, out var back, settings.handLength, ~0, QueryTriggerInteraction.Ignore) && back.collider.GetComponentInParent<Character>() == null) handRoom = back.distance;
            float score = Mathf.Min(room, flight) - Mathf.Max(0f, Vector3.Dot(dir, toPlayer) - .3f) * flight * 2f - Mathf.Abs(a) * .01f
                - (settings.handLength - handRoom) * 3f;
            if (score > bestScore) { bestScore = score; best = dir; }
        }
        return best;
    }

    // ── Holding it while he comes ───────────────────────────────────

    class Held { public Character who; public EnemyBrain brain; public Light mark; public SpellVisual effect; }

    Held Hold(Character c)
    {
        var held = new Held { who = c, brain = c.GetComponent<EnemyBrain>() };
        if (settings.mark != null) held.effect = Instantiate(settings.mark, c.transform.position + Vector3.up * .05f, Quaternion.identity, c.transform);
        if (held.brain != null) { held.brain.enabled = false; held.brain.Motor?.Stop(); }
        if (settings.markLight > 0f)
        {
            var go = new GameObject("Flick mark");
            go.transform.SetParent(c.transform, false);
            go.transform.localPosition = Vector3.up * 2.2f;
            held.mark = go.AddComponent<Light>();
            held.mark.type = LightType.Point; held.mark.range = 3.5f; held.mark.intensity = settings.markLight; held.mark.color = settings.markColor; held.mark.shadows = LightShadows.None;
        }
        return held;
    }

    static void Release(Held held)
    {
        if (held == null) return;
        if (held.mark != null) { held.mark.transform.SetParent(null, true); held.mark.gameObject.AddComponent<LightFade>().seconds = .4f; }
        if (held.effect != null) held.effect.Stop();
        if (held.brain != null && held.who != null && held.who.IsAlive && !held.brain.enabled) { held.brain.enabled = true; held.brain.Motor?.Resume(); }
    }

    void Snap(Vector3 at)
    {
        if (settings.snapSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(settings.snapSound, at, settings.snapVolume);
        DungeonMasterReach.Shake(.25f);
    }

    // ── The die (out of his reach) ──────────────────────────────────

    void DieSound(AudioClip clip, Vector3 at)
    {
        if (clip != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(clip, at, settings.dieVolume);
    }

    void Hit(Vector3 at)
    {
        if (settings.hitSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(settings.hitSound, at, settings.hitVolume);
        if (settings.hitShake > 0f) DungeonMasterReach.Shake(settings.hitShake);
    }

    void Knock(Vector3 at)
    {
        if (settings.dieKnocks != null && settings.dieKnocks.Length > 0) DieSound(settings.dieKnocks[UnityEngine.Random.Range(0, settings.dieKnocks.Length)], at);
    }

    IEnumerator DieFly(Transform die, Held held)
    {
        flying = true;
        die.name = "Flick die";   // in the air (a watcher follows it from here)
        var from = die.position;
        var to = target != null ? SpellEffects.Chest(target) : from;
        float g = settings.dieGravity;
        float peak = Mathf.Max(from.y, to.y) + settings.dieArc;
        float up = Mathf.Sqrt(2f * g * (peak - from.y)), t = up / g + Mathf.Sqrt(2f * (peak - to.y) / g);
        var v = (to - from) / t; v.y = up;
        var spinAxis = Vector3.Cross(Vector3.up, Flat(to - from));
        for (float u = 0; u < t && die != null; u += Time.deltaTime)
        {
            // Steered level onto where the body is now (it may have been pushed since the throw), the arc kept.
            float left = t - u;
            if (target != null && left > Time.deltaTime)
            {
                var now = SpellEffects.Chest(target) - die.position;
                v.x = now.x / left; v.z = now.z / left;
            }
            v.y -= g * Time.deltaTime;
            die.position += v * Time.deltaTime;
            die.rotation = Quaternion.AngleAxis(900f * Time.deltaTime, spinAxis) * die.rotation;
            yield return null;
        }
        yield return DieStrike(die, held, v, g, spinAxis);
    }

    // The way the die knocks the enemy: the way it was going, turned off the player when it heads at them (a portal's die
    // can come from a little beyond the enemy).
    Vector3 KnockWay(Vector3 v)
    {
        var way = Flat(v);
        if (settings.reach != FlickReach.Portal || target == null) return way;
        var toPlayer = Flat(caster.transform.position - target.transform.position);
        float at = Vector3.Dot(way, toPlayer);
        return at > 0f ? Flat(way - toPlayer * at * 1.2f) : way;
    }

    // The die strikes the enemy (knocking it across the room), then falls as a real body: it springs back off the enemy,
    // drops with gravity, bounces and rolls to rest on the floor (its box collider and a rigidbody, kept
    // out of the players' and the living's way like a body's pieces), lies there a moment and is gone.
    IEnumerator DieStrike(Transform die, Held held, Vector3 v, float g, Vector3 spinAxis)
    {
        Release(held);
        if (die != null) { Hit(die.position); Knock(die.position); }
        if (target != null && target.IsAlive) StartCoroutine(Fly(target, KnockWay(v), power, false));
        if (die == null) { flying = false; yield break; }
        foreach (var c in die.GetComponentsInChildren<Collider>(true)) { c.enabled = true; c.sharedMaterial = DieBounce; }
        var body = die.GetComponent<Rigidbody>();
        if (body == null) body = die.gameObject.AddComponent<Rigidbody>();
        body.isKinematic = false;
        body.mass = 8f;
        body.angularDamping = .6f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        DeathPhysics.IgnorePlayers(new List<Rigidbody> { body });
        // It springs back off the enemy the way it came, turned off the player (back toward them it bounced into the
        // player's face).
        var back = -Flat(v);
        var toPlayer = Flat(caster.transform.position - die.position);
        float atPlayer = Vector3.Dot(back, toPlayer);
        if (atPlayer > 0f) back = Flat(back - toPlayer * atPlayer * 1.3f);
        body.linearVelocity = back * DieRebound + Vector3.up * 5.5f;
        body.angularVelocity = spinAxis.normalized * 9f + UnityEngine.Random.insideUnitSphere * 4f;
        // A knock each time it lands hard, the roll as it first meets the floor; it lies still a moment, then shrinks away.
        bool rolling = false;
        float lastVy = body.linearVelocity.y, still = 0f;
        for (float t = 0f; t < 4f && die != null; t += Time.deltaTime)
        {
            yield return new WaitForFixedUpdate();
            if (die == null) break;
            // Heavier than the world's pull (a die this big falling at plain gravity looks slow, and leans on edges).
            body.AddForce(Physics.gravity * DieExtraGravity, ForceMode.Acceleration);
            float vy = body.linearVelocity.y;
            if (lastVy < -1.5f && vy > lastVy + 1.5f)
            {
                Knock(die.position);
                if (!rolling) { rolling = true; DieSound(settings.dieRollSound, die.position); }
            }
            lastVy = vy;
            still = body.linearVelocity.sqrMagnitude < .02f && body.angularVelocity.sqrMagnitude < .05f ? still + Time.fixedDeltaTime : 0f;
            if (still > .6f) break;
        }
        if (die != null)
        {
            body.isKinematic = true;
            var start = die.localScale;
            for (float u = 0; u < .25f && die != null; u += Time.deltaTime) { die.localScale = start * (1f - u / .25f); yield return null; }
            if (die != null) Destroy(die.gameObject);
        }
    }

    // How much more than the world's gravity pulls the die down after its strike, and how fast it springs back off the
    // enemy (metres a second, level).
    const float DieExtraGravity = 1.5f, DieRebound = 4f;

    // The die's bounce off floors and walls: a little lively, gripping enough to roll rather than slide.
    static PhysicsMaterial dieBounce;
    static PhysicsMaterial DieBounce => dieBounce != null ? dieBounce : dieBounce = new PhysicsMaterial("Die bounce")
    {
        bounciness = .3f, dynamicFriction = .6f, staticFriction = .7f,
        bounceCombine = PhysicsMaterialCombine.Maximum, frictionCombine = PhysicsMaterialCombine.Average,
    };

    // ── The portal ──────────────────────────────────────────────────

    // A portal opens in the room beside the enemy, across the player's view; the die shoots out of it, strikes the enemy
    // and knocks it across the room, and the portal closes behind it. The Dungeon Master never leaves his chair.
    IEnumerator RunPortal()
    {
        var held = Hold(target);
        if (settings.lines != null && settings.lines.Length > 0) DungeonMaster.Say(settings.lines[UnityEngine.Random.Range(0, settings.lines.Length)]);
        PortalSpot(target, out var at, out var facing, out float radius);
        var portal = MakePortal(at, facing, radius, out var vortex, out var glow);
        PortalSound(settings.portalOpenSound, at);
        StartCoroutine(Swirl(portal, vortex, glow));
        // It opens from a point, a little past its size and back.
        for (float t = 0; t < settings.portalOpenSeconds; t += Time.deltaTime)
        {
            float u = t / settings.portalOpenSeconds;
            portal.localScale = Vector3.one * (u < .75f ? Mathf.SmoothStep(0f, 1.12f, u / .75f) : Mathf.Lerp(1.12f, 1f, (u - .75f) / .25f));
            yield return null;
        }
        portal.localScale = Vector3.one;
        for (float t = 0; t < settings.portalHoldSeconds; t += Time.deltaTime)
        {
            if (target != null && target.IsAlive) { facing = (SpellEffects.Chest(target) - at).normalized; portal.rotation = PortalTurn(at, facing); }
            yield return null;
        }
        if (target != null && target.IsAlive && settings.die != null)
        {
            // Out of the portal's middle, small at first as if from far off, growing to its size as it leaves.
            var die = Instantiate(settings.die, at, Quaternion.LookRotation(facing) * UnityEngine.Random.rotation).transform;
            foreach (var c in die.GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var r in die.GetComponentsInChildren<Rigidbody>()) r.isKinematic = true;
            if (settings.dieLight > 0f)
            {
                var light = new GameObject("Die light").AddComponent<Light>();
                light.transform.SetParent(die, false);
                light.type = LightType.Point; light.range = 6f; light.intensity = settings.dieLight; light.color = settings.markColor; light.shadows = LightShadows.None;
            }
            var size = die.localScale * settings.dieScale;
            PortalSound(settings.portalShootSound, at);
            DungeonMasterReach.Shake(.2f);
            StartCoroutine(DieShoot(die, held, size));
            yield return new WaitForSeconds(.25f);
        }
        else Release(held);
        // It closes, folding back to a point.
        PortalSound(settings.portalCloseSound, at);
        float from = portal.localScale.x;
        for (float t = 0; t < settings.portalCloseSeconds; t += Time.deltaTime)
        {
            portal.localScale = Vector3.one * from * (1f - Mathf.SmoothStep(0f, 1f, t / settings.portalCloseSeconds));
            yield return null;
        }
        Destroy(portal.gameObject);
        for (float t = 0; (flying || looseDie != null) && t < 8f; t += Time.deltaTime) yield return null;
        if (looseDie != null) Destroy(looseDie.gameObject);
    }

    // Straight and fast out of the portal onto the enemy's chest (steered onto where it is now), a slight drop on the way.
    IEnumerator DieShoot(Transform die, Held held, Vector3 size)
    {
        flying = true;
        looseDie = die;
        die.name = "Flick die";
        var from = die.position;
        var to = target != null ? SpellEffects.Chest(target) : from;
        float t = Mathf.Max(.15f, Vector3.Distance(from, to) / settings.portalDieSpeed);
        const float g = 10f;
        var v = (to - from) / t; v.y += .5f * g * t;
        var spinAxis = Vector3.Cross(Vector3.up, Flat(to - from));
        for (float u = 0; u < t && die != null; u += Time.deltaTime)
        {
            float left = t - u;
            if (target != null && left > Time.deltaTime)
            {
                var now = SpellEffects.Chest(target) - die.position;
                v.x = now.x / left; v.z = now.z / left;
            }
            v.y -= g * Time.deltaTime;
            die.position += v * Time.deltaTime;
            die.localScale = size * Mathf.Lerp(.25f, 1f, Mathf.Clamp01(u / .12f));
            die.rotation = Quaternion.AngleAxis(1100f * Time.deltaTime, spinAxis) * die.rotation;
            yield return null;
        }
        if (die != null) die.localScale = size;
        yield return DieStrike(die, held, v, settings.dieGravity, spinAxis);
    }

    // Where the portal opens: beside the enemy across the player's view (so the die flies across it, never at them), in
    // open air between its chest and the ceiling, with a clear line to it, and seen from the player's eyes if it can be.
    // Too tight for that, it opens over the enemy's head and the die drops onto it.
    void PortalSpot(Character body, out Vector3 at, out Vector3 facing, out float radius)
    {
        var view = View(caster);
        var chest = SpellEffects.Chest(body);
        var feet = body.transform.position;
        radius = settings.portalRadius;
        float ceiling = Physics.Raycast(feet + Vector3.up * .3f, Vector3.up, out var roof, 20f, ~0, QueryTriggerInteraction.Ignore) ? roof.point.y : feet.y + 8f;
        float height = Mathf.Max(chest.y, Mathf.Min(feet.y + 1.8f, ceiling - radius - .25f));
        // Round the enemy from straight beside it (90 degrees off the way to the player) to a little beyond it, either side.
        var toPlayer = Flat(view.position - feet);
        float best = float.MinValue; at = Vector3.zero;
        foreach (float off in new[] { 90f, 110f, 130f, 75f })
        foreach (float side in new[] { 1f, -1f })
        foreach (float share in new[] { .65f, .5f, .8f, .38f, 1f })
        {
            float distance = settings.portalDistance * share;
            var p = new Vector3(feet.x, height, feet.z) + Quaternion.Euler(0f, off * side, 0f) * toPlayer * distance;
            if (Physics.CheckSphere(p, radius, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (!Physics.Raycast(p, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;   // over a pit or outside the floor
            if (!LineOfSight.Clear(p, chest, ~0, body.transform, null, .25f, false)) continue;
            var toPortal = p - view.position;
            float angle = Vector3.Angle(view.forward, toPortal);
            bool seen = angle < 30f && LineOfSight.Clear(view.position, p, ~0, caster.transform, null, 0f, false);
            // The left of the view is kept clear (the quest log, the minimap and the weapon are on the right).
            bool left = Vector3.Dot(toPortal, view.right) < 0f;
            float score = -Mathf.Abs(share - .65f) * 4f - (seen ? 0f : 20f) - angle * .08f - Mathf.Abs(off - 100f) * .01f + (left ? 1.5f : 0f);
            if (score > best) { best = score; at = p; }
        }
        if (best == float.MinValue)
        {
            radius *= .7f;
            at = new Vector3(feet.x, Mathf.Max(chest.y + 1f, Mathf.Min(feet.y + 3.5f, ceiling - .35f)), feet.z);
        }
        facing = (chest - at).normalized;
    }

    // Turned halfway between the way the die leaves and the player's eye: seen face on enough to read as a portal.
    Quaternion PortalTurn(Vector3 at, Vector3 facing) => Quaternion.LookRotation(Vector3.Slerp(facing, (View(caster).position - at).normalized, .5f));

    // The vortex on a square card (its texture cuts it round) and a light; scaled from its middle.
    Transform MakePortal(Vector3 at, Vector3 facing, float radius, out Material vortex, out Light glow)
    {
        var root = new GameObject(spell.name + " portal").transform;
        root.SetPositionAndRotation(at, PortalTurn(at, facing));
        root.localScale = Vector3.zero;
        vortex = settings.portalMaterial != null ? new Material(settings.portalMaterial) : null;
        var card = new GameObject("Vortex");
        card.transform.SetParent(root, false);
        card.AddComponent<MeshFilter>().sharedMesh = Card(radius);
        var r = card.AddComponent<MeshRenderer>();
        r.sharedMaterial = vortex;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        glow = new GameObject("Portal light").AddComponent<Light>();
        glow.transform.SetParent(root, false);
        glow.transform.localPosition = Vector3.forward * .4f;
        glow.type = LightType.Point; glow.range = 6f; glow.intensity = 0f; glow.color = settings.portalColor; glow.shadows = LightShadows.None;
        return root;
    }

    // A square in the XY plane facing +Z, the radius from its middle to the middle of each side.
    static Mesh Card(float radius)
    {
        var mesh = new Mesh { name = "Portal card" };
        mesh.SetVertices(new List<Vector3> { new(-radius, -radius, 0f), new(radius, -radius, 0f), new(radius, radius, 0f), new(-radius, radius, 0f) });
        mesh.SetUVs(0, new List<Vector2> { new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f) });
        mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // While it lives: the burn's embers flicker (its frames stepped through at their own rate, so its pixels stay whole),
    // its light parts pulse, and the light follows the portal's size.
    IEnumerator Swirl(Transform portal, Material vortex, Light glow)
    {
        int frames = Mathf.Max(1, settings.portalFrames);
        if (vortex != null) vortex.SetTextureScale("_BaseMap", new Vector2(1f / frames, 1f));
        for (float t = 0; portal != null; t += Time.deltaTime)
        {
            float pulse = .8f + .15f * Mathf.Sin(t * 9f) + .05f * Mathf.Sin(t * 23f);
            if (vortex != null)
            {
                int frame = Mathf.FloorToInt(t * settings.portalFps) % frames;
                vortex.SetTextureOffset("_BaseMap", new Vector2((float)frame / frames, 0f));
                vortex.SetFloat("_Glow", 1.5f * pulse);
            }
            if (glow != null) glow.intensity = settings.portalLight * portal.localScale.x * pulse;
            yield return null;
        }
        if (vortex != null) Destroy(vortex);
    }

    void PortalSound(AudioClip clip, Vector3 at)
    {
        if (clip != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(clip, at, settings.portalVolume);
    }

    // ── The flight ──────────────────────────────────────────────────

    // It flies off its feet along direction, tumbling, until it meets the floor; the first wall or floor it hits does the
    // damage. Anyone else in its way is bowled over. A body that lives through it lies stunned where it lands.
    IEnumerator Fly(Character body, Vector3 direction, float damage, bool fromFinger)
    {
        if (body == null || !body.IsAlive) yield break;
        // A boss or an elite can't be sent flying (EnemyMotor.Steadfast): the blow lands where it stands, and it reels.
        if (body.TryGetComponent<EnemyMotor>(out var steady) && steady.Steadfast)
        {
            var at = SpellEffects.Chest(body);
            SpellEffects.Strike(caster, body, damage, at, at - Flat(direction), spell.displayName);
            yield break;
        }
        flying = true;
        var brain = body.GetComponent<EnemyBrain>();
        var agent = body.GetComponent<NavMeshAgent>();
        var motor = body.GetComponent<EnemyMotor>();
        var colliders = body.GetComponentsInChildren<Collider>(true);
        var bodies = body.GetComponentsInChildren<Rigidbody>(true);
        var solid = new bool[colliders.Length]; var kinematic = new bool[bodies.Length];
        if (brain != null) brain.enabled = false;
        if (agent != null) agent.enabled = false;
        if (motor != null) motor.enabled = false;
        for (int i = 0; i < colliders.Length; i++) { solid[i] = colliders[i].enabled; colliders[i].enabled = false; }
        for (int i = 0; i < bodies.Length; i++) { kinematic[i] = bodies[i].isKinematic; bodies[i].isKinematic = true; }
        void Restore()
        {
            for (int i = 0; i < bodies.Length; i++) if (bodies[i] != null) bodies[i].isKinematic = kinematic[i];
            for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = solid[i];
            if (motor != null) motor.enabled = true;
        }

        var dir = Flat(direction);
        float height = solid.Length > 0 && colliders[0] != null ? Mathf.Max(.4f, colliders[0].bounds.size.y) : 1.6f;
        var v = dir * settings.launchSpeed + Vector3.up * settings.launchUp;
        var p = body.transform.position;
        var axis = Vector3.Cross(Vector3.up, dir);
        var bowled = new HashSet<Character> { body };
        bool struck = false, landed = false;
        // The first wall or floor: the blow. Its colliders come back first, so a death throws or bursts it as usual.
        bool Strike(Vector3 at)
        {
            if (struck) return false;
            struck = true;
            if (settings.slamSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(settings.slamSound, at, settings.slamVolume);
            if (settings.slamShake > 0f) DungeonMasterReach.Shake(settings.slamShake, .75f);
            bool lethal = body.Stats.CurrentHealth <= damage;
            if (lethal) Restore();
            SpellEffects.Strike(caster, body, damage, at, at - dir, spell.displayName);
            progress?.Practise(spell.Skill, SkillAction.Cast);
            return !body.IsAlive;
        }
        for (float t = 0; t < 4f && body != null && !landed; t += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            v.y -= settings.gravity * dt;
            var step = v * dt;
            var centre = p + Vector3.up * height * .5f;
            float radius = Mathf.Min(.4f, height * .35f);
            if (Solid(centre, radius, step, out var hit))
            {
                if (hit.normal.y > .55f && v.y <= 0f)
                {
                    p += step.normalized * Mathf.Max(0f, hit.distance - .02f);
                    p.y = hit.point.y;
                    landed = true;
                    if (Strike(hit.point)) { flying = false; yield break; }
                    break;
                }
                // A wall: it slams into it and slides down.
                p += step.normalized * Mathf.Max(0f, hit.distance - .05f);
                var level = new Vector3(v.x, 0f, v.z);
                level = Vector3.Reflect(level, new Vector3(hit.normal.x, 0f, hit.normal.z).normalized) * .2f;
                v = new Vector3(level.x, Mathf.Min(v.y, 0f), level.z);
                if (Strike(hit.point)) { flying = false; yield break; }
                body.transform.position = p;
                yield return null;
                continue;
            }
            p += step;
            body.transform.SetPositionAndRotation(p, Quaternion.AngleAxis(settings.spin * dt, axis) * body.transform.rotation);
            // Anyone it flies into is knocked down too.
            foreach (var c in SpellEffects.HostilesNear(caster, p + Vector3.up * height * .5f, .9f))
            {
                if (!bowled.Add(c)) continue;
                SpellEffects.Strike(caster, c, damage * settings.bowlShare, c.transform.position + Vector3.up, p, spell.displayName);
                if (c.IsAlive && c.TryGetComponent<EnemyBrain>(out var b)) b.Stun(settings.stunSeconds);
            }
            yield return null;
        }
        if (body == null) { flying = false; yield break; }
        if (!struck && Strike(p)) { flying = false; yield break; }
        // It lived: on its feet again where it landed, facing back the way it came, and stunned a moment.
        var face = -dir;
        if (NavMesh.SamplePosition(p, out var onMesh, 3f, NavMesh.AllAreas)) p = onMesh.position;
        body.transform.SetPositionAndRotation(p, Quaternion.LookRotation(face));
        Restore();
        if (agent != null) { agent.enabled = true; agent.Warp(p); if (agent.isOnNavMesh) { agent.ResetPath(); agent.velocity = Vector3.zero; } }
        if (brain != null) { brain.enabled = true; brain.Motor?.Resume(); brain.Stun(settings.stunSeconds); }
        flying = false;
    }

    // The world in the way of a step (characters and triggers left out: only walls, floors and props).
    static readonly RaycastHit[] hits = new RaycastHit[16];
    static bool Solid(Vector3 from, float radius, Vector3 step, out RaycastHit nearest)
    {
        nearest = default;
        float distance = step.magnitude;
        if (distance < 1e-5f) return false;
        int n = Physics.SphereCastNonAlloc(from, radius, step / distance, hits, distance, ~0, QueryTriggerInteraction.Ignore);
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (h.distance <= 0f && h.point == Vector3.zero) continue;   // started inside it
            if (h.collider.GetComponentInParent<Character>() != null || DeathPhysics.IsCorpse(h.collider)) continue;
            if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue;
            if (!any || h.distance < nearest.distance) { nearest = h; any = true; }
        }
        return any;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > .0001f ? v.normalized : Vector3.forward; }
}
