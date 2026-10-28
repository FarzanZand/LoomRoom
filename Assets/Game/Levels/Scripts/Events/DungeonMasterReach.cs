using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// The Dungeon Master reaching into a floor (his meddling, the dice room): he lifts the floor's ceilings off like a lid,
// stands at the table's edge nearest the spot and leans in, sets a figure down, takes something away or lets something
// drop, then puts the lid back. The same moves the boss stage uses (DungeonMasterHand, DungeonMasterSeat), for any room.
// Coroutines: the caller runs them (StartCoroutine) and waits for them.
public static class DungeonMasterReach
{
    public static DungeonMasterHand Hand => TableManager.HasInstance && TableManager.Instance.DM != null ? TableManager.Instance.DM.GetComponentInChildren<DungeonMasterHand>(true) : null;
    public static DungeonMasterSeat Seat => Object.FindAnyObjectByType<DungeonMasterSeat>();

    // Something already has his hands (a boss fight, another reach).
    public static bool Busy => BossStageEncounter.Current != null || Reaching || Hand != null && Hand.Busy;
    // He is reaching into a floor. A reach whose floor went (unloaded mid-reach: its coroutine never finished) or that
    // never ended frees his hands and sends him back, so he is never left busy for good.
    static bool reaching; static Object reachFloor; static float reachStarted;
    public static bool Reaching
    {
        get
        {
            if (!reaching) return false;
            if (reachFloor != null && Time.time - reachStarted < 30f) return true;
            reaching = false; standingAt = Side.Own;
            if (Hand != null) Hand.Release();
            if (Seat != null) { Seat.Unfocus(); Hurry(Seat, false); }
            return false;
        }
        private set
        {
            reaching = value;
            if (value) { reachStarted = Time.time; reachFloor = TableManager.Loader != null ? TableManager.Loader.Dungeon : null; }
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { reaching = false; reachFloor = null; standingAt = Side.Own; impulse = null; walkSpeed = -1f; }

    public static float TableTop(DungeonGenerator dungeon) =>
        TableManager.HasInstance && TableManager.Instance.GetComponent<TableCut>() is { } cut && cut.Bounds.size != Vector3.zero ? cut.Bounds.max.y
        : dungeon != null ? dungeon.transform.position.y : 0f;

    static Bounds TableBox(Vector3 spot) =>
        TableManager.HasInstance && TableManager.Instance.GetComponent<TableCut>() is { } cut && cut.Bounds.size != Vector3.zero ? cut.Bounds
        : TableManager.HasInstance && TableManager.Instance.GetComponent<Collider>() is { } c ? c.bounds : new Bounds(spot, Vector3.one);

    // The sides of the table he can stand at: his own and the two ends beside it (never the player's side across from him,
    // where the room player stands). His own side first when an end is about as near.
    enum Side { Own, Left, Right }

    // His side of the table's box: the axis and sign pointing from the table toward him; the ends lie across it.
    static void Frame(Bounds box, out Vector3 own, out Vector3 across)
    {
        var dm = TableManager.HasInstance && TableManager.Instance.DM != null ? TableManager.Instance.DM.transform.position : box.center + Vector3.right * 30f;
        var d = dm - box.center;
        own = Mathf.Abs(d.x / Mathf.Max(.01f, box.extents.x)) >= Mathf.Abs(d.z / Mathf.Max(.01f, box.extents.z)) ? new Vector3(Mathf.Sign(d.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(d.z));
        across = Vector3.Cross(Vector3.up, own);
    }

    static float Extent(Bounds box, Vector3 axis) => Mathf.Abs(Vector3.Dot(box.extents, axis));

    static Side SideFor(Vector3 spot, out Vector3 at)
    {
        var box = TableBox(spot);
        Frame(box, out var own, out var across);
        float back = Seat != null ? Seat.leanBack : 6f;
        var rel = spot - box.center; rel.y = 0f;
        float y = TableManager.HasInstance && TableManager.Instance.DM != null ? TableManager.Instance.DM.transform.position.y : spot.y;
        // How far in from each of his three edges the spot lies.
        float fromOwn = Extent(box, own) - Vector3.Dot(rel, own);
        float fromLeft = Extent(box, across) - Vector3.Dot(rel, across);
        float fromRight = Extent(box, across) + Vector3.Dot(rel, across);
        var centre = new Vector3(box.center.x, y, box.center.z);
        if (fromOwn <= Mathf.Min(fromLeft, fromRight) + 4f)
        {
            at = centre + own * (Extent(box, own) + back) + across * Vector3.Dot(rel, across);
            return Side.Own;
        }
        bool left = fromLeft < fromRight;
        var sign = left ? 1f : -1f;
        at = centre + across * sign * (Extent(box, across) + back) + own * Vector3.Dot(rel, own);
        return left ? Side.Left : Side.Right;
    }

    // Where he stands to reach a spot: at the nearest of his three sides of the table, a step back from its edge.
    public static Vector3 StandFor(Vector3 spot) { SideFor(spot, out var at); return at; }

    // The corner he walks round to reach an end of the table from his own side (and back).
    static Vector3 Corner(Vector3 spot, Side side)
    {
        var box = TableBox(spot);
        Frame(box, out var own, out var across);
        float back = (Seat != null ? Seat.leanBack : 6f) + 2f;
        float y = TableManager.HasInstance && TableManager.Instance.DM != null ? TableManager.Instance.DM.transform.position.y : spot.y;
        return new Vector3(box.center.x, y, box.center.z) + own * (Extent(box, own) + back) + across * (side == Side.Left ? 1f : -1f) * (Extent(box, across) + back);
    }

    static Side standingAt = Side.Own;
    static Vector3 lastSpot;

    // The spot, or the nearest point toward it he can reach from the table's edge.
    public static Vector3 Reachable(Vector3 spot)
    {
        var hand = Hand;
        if (hand == null || CanReach(spot)) return spot;
        var stand = StandFor(spot);
        var flat = spot - stand; flat.y = 0f;
        var p = stand + flat.normalized * hand.Reach * .9f;
        return new Vector3(p.x, spot.y, p.z);
    }

    // As far toward the spot as he reaches from where he stands now (a toss goes the rest of the way).
    public static Vector3 ReachableFromHere(Vector3 spot)
    {
        var hand = Hand;
        if (hand == null) return spot;
        var from = hand.transform.position;
        var flat = spot - from; flat.y = 0f;
        var p = from + flat.normalized * Mathf.Min(flat.magnitude, hand.Reach * .85f);
        return new Vector3(p.x, spot.y, p.z);
    }

    // He turns where he stands to face the spot, and leans in (no walk).
    static IEnumerator TurnTo(Vector3 spot)
    {
        var seat = Seat; var hand = Hand;
        if (hand != null) { hand.Watch(spot, true); if (seat != null) seat.Focus(hand.transform.position, spot); }
        standingAt = Side.Own; lastSpot = spot;
        yield return new WaitForSeconds(.5f);
    }

    public static bool CanReach(Vector3 spot)
    {
        var hand = Hand;
        if (hand == null || Seat == null) return false;
        var stand = StandFor(spot);
        var flat = spot - stand; flat.y = 0f;
        return flat.magnitude <= hand.Reach;
    }

    // ── The lid ──────────────────────────────────────────────────────

    // The floor's ceilings rise off like a box's lid, tipped toward him, and settle back with a thud.
    // It comes up a little, tipped toward him, and is gone (lifted off out of sight), so the bedroom and he show above;
    // putting it back it is there again just above and comes down with a thud.
    public static IEnumerator Lid(DungeonGenerator dungeon, bool open, float seconds = .7f, float height = 4f)
    {
        var lid = dungeon != null ? dungeon.Ceilings : null;
        if (lid == null) yield break;
        var up = Vector3.up * height;
        var tip = Quaternion.Euler(0f, 0f, 4f);
        if (!open) { lid.localPosition = up; lid.localRotation = tip; lid.gameObject.SetActive(true); }
        var from = lid.localPosition; var fromRotation = lid.localRotation;
        var to = open ? up : Vector3.zero; var toRotation = open ? tip : Quaternion.identity;
        for (float t = 0; t < seconds && lid != null; t += Time.deltaTime)
        {
            float u = open ? Mathf.SmoothStep(0, 1, t / seconds) : (t / seconds) * (t / seconds);
            lid.localPosition = Vector3.Lerp(from, to, u);
            lid.localRotation = Quaternion.Slerp(fromRotation, toRotation, u);
            yield return null;
        }
        if (lid == null) yield break;
        lid.localPosition = to; lid.localRotation = toRotation;
        if (open) lid.gameObject.SetActive(false);
        else Shake(.45f);
    }

    static Unity.Cinemachine.CinemachineImpulseSource impulse;
    public static void Shake(float force)
    {
        if (impulse == null)
        {
            var go = new GameObject("DM reach shake");
            impulse = go.AddComponent<Unity.Cinemachine.CinemachineImpulseSource>();
        }
        impulse.GenerateImpulseWithForce(force);
    }

    // ── His moves ────────────────────────────────────────────────────

    // He walks to where he can reach the spot: straight along his own side, or round the corner to an end of the table.
    // He hurries along the table while he reaches (his boss-fight pace is a slow, looming walk).
    const float ReachWalkSpeed = 22f;
    static float walkSpeed = -1f;
    static void Hurry(DungeonMasterSeat seat, bool on)
    {
        if (seat == null) return;
        if (on) { if (walkSpeed < 0f) walkSpeed = seat.focusSpeed; seat.focusSpeed = Mathf.Max(walkSpeed, ReachWalkSpeed); }
        else if (walkSpeed >= 0f) { seat.focusSpeed = walkSpeed; walkSpeed = -1f; }
    }

    static IEnumerator Approach(Vector3 spot)
    {
        var seat = Seat; var hand = Hand;
        Hurry(seat, true);
        var side = SideFor(spot, out var at);
        if (hand != null) hand.Watch(spot, true);
        if (seat != null && side != Side.Own)
        {
            seat.Focus(Corner(spot, side), spot);
            for (float t = 0; !seat.Arrived && t < 6f; t += Time.deltaTime) yield return null;
        }
        if (seat != null) seat.Focus(at, spot);
        for (float t = 0; seat != null && !seat.Arrived && t < 6f; t += Time.deltaTime) yield return null;
        standingAt = side; lastSpot = spot;
        yield return new WaitForSeconds(.25f);
    }

    // Back to his place at his own side, round the corner if he went to an end. The caller runs it once it is done with
    // him (after the lid is back on), and his hands are free again at its end.
    public static IEnumerator Leave()
    {
        var seat = Seat; var hand = Hand;
        if (hand != null) hand.Watch(Vector3.zero, false);
        if (seat != null && standingAt != Side.Own)
        {
            seat.Focus(Corner(lastSpot, standingAt), TableBox(lastSpot).center);
            for (float t = 0; !seat.Arrived && t < 6f; t += Time.deltaTime) yield return null;
        }
        standingAt = Side.Own;
        if (seat != null) seat.Unfocus();
        // Back at his own pace once he is home (the walk home itself is hurried too).
        for (float t = 0; seat != null && !seat.Arrived && t < 4f; t += Time.deltaTime) yield return null;
        Hurry(seat, false);
        Reaching = false;
    }

    // He sets an enemy down on spot (spawned, its brain off until it lands) and it goes for the player at once.
    public static IEnumerator Put(Character figure, Vector3 spot, float seconds = 2f, bool alert = true)
    {
        Reaching = true;
        var brain = figure.GetComponent<EnemyBrain>();
        var agent = figure.GetComponent<NavMeshAgent>();
        var motor = figure.GetComponent<EnemyMotor>();
        var bodies = figure.GetComponentsInChildren<Rigidbody>();
        var colliders = figure.GetComponentsInChildren<Collider>(true);
        var solid = new bool[colliders.Length]; var kinematic = new bool[bodies.Length];
        if (brain != null) brain.enabled = false;
        if (agent != null) agent.enabled = false;
        if (motor != null) motor.enabled = false;
        for (int i = 0; i < colliders.Length; i++) { solid[i] = colliders[i].enabled; colliders[i].enabled = false; }
        for (int i = 0; i < bodies.Length; i++) { kinematic[i] = bodies[i].isKinematic; bodies[i].isKinematic = true; }
        // Out of sight below the table until his hand brings it up.
        figure.transform.position = spot + Vector3.down * 50f;
        var hand = Hand;
        var landed = spot;
        if (!CanReach(spot)) yield return TossIn(figure.transform, spot);
        else
        {
            yield return Approach(spot);
            if (hand != null && figure != null)
            {
                yield return Carrying(hand, hand.Place(figure.transform, spot, seconds, TableTop(figure.GetComponentInParent<DungeonGenerator>()), () => Landed(spot)));
                landed = hand.LastPlacedAt;
            }
            else if (figure != null) { figure.transform.position = spot; Landed(spot); }
        }
        if (figure != null)
        {
            for (int i = 0; i < bodies.Length; i++) if (bodies[i] != null) bodies[i].isKinematic = kinematic[i];
            for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = solid[i];
            if (motor != null) motor.enabled = true;
            if (agent != null) { agent.enabled = true; agent.Warp(landed); if (agent.isOnNavMesh) { agent.ResetPath(); agent.velocity = Vector3.zero; } }
            if (brain != null) { brain.enabled = true; if (alert) brain.Alert(); }
        }
    }

    // He sets a thing (a chest) down on spot.
    public static IEnumerator PutThing(Transform thing, Vector3 spot, float seconds = 2f)
    {
        Reaching = true;
        var colliders = thing.GetComponentsInChildren<Collider>(true);
        var solid = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++) { solid[i] = colliders[i].enabled; colliders[i].enabled = false; }
        var facing = thing.rotation;
        thing.position = spot + Vector3.down * 50f;
        var hand = Hand;
        if (!CanReach(spot)) yield return TossIn(thing, spot);
        else
        {
            yield return Approach(spot);
            if (hand != null && thing != null) yield return Carrying(hand, hand.Place(thing, spot, seconds, TableTop(thing.GetComponentInParent<DungeonGenerator>()), () => Landed(spot)));
        }
        if (thing != null)
        {
            thing.position = new Vector3(thing.position.x, spot.y, thing.position.z);
            thing.rotation = facing;
            for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = solid[i];
        }
    }

    // He takes a thing off the floor; it is destroyed once it is below the table's edge.
    public static IEnumerator Take(Transform thing, float seconds = 2.2f)
    {
        Reaching = true;
        foreach (var c in thing.GetComponentsInChildren<Collider>()) c.enabled = false;
        yield return Approach(thing.position);
        var hand = Hand;
        if (hand != null && thing != null) yield return hand.Take(thing, seconds, TableTop(thing.GetComponentInParent<DungeonGenerator>()), () => { if (thing != null) Object.Destroy(thing.gameObject); });
        else if (thing != null) Object.Destroy(thing.gameObject);
    }

    // He brings a thing up over a spot and lets go of it high above (released: the caller moves it from there).
    // walk false: he stays where he stands, turns to the spot and brings it up as far toward it as he reaches (a toss
    // follows; for a spot out of his reach, no walk round the table first).
    public static IEnumerator DropOver(Transform thing, Vector3 over, float seconds, System.Action released, bool walk = true)
    {
        Reaching = true;
        thing.position = over + Vector3.down * 50f;
        if (walk) yield return Approach(over);
        else { var target = over; over = ReachableFromHere(target); yield return TurnTo(target); }
        var hand = Hand;
        if (hand != null) yield return hand.Drop(thing, over, seconds, released);
        else { thing.position = over + Vector3.up * 30f; released?.Invoke(); }
    }

    // Out of his reach: his hand comes up at the nearest point he can reach, and he tosses it the rest of the way in a
    // high arc, upright; it lands on the spot with a thump.
    const float Gravity = 30f;
    static IEnumerator TossIn(Transform thing, Vector3 spot)
    {
        var over = ReachableFromHere(spot);
        yield return TurnTo(spot);
        var hand = Hand;
        var look = spot - over; look.y = 0f;
        var upright = Quaternion.LookRotation(look.sqrMagnitude > .01f ? look : Vector3.forward);
        thing.rotation = upright;
        // His hand lets go and the flight starts at once; his hand drops back while it flies (the rest of Drop, after).
        System.Collections.IEnumerator drop = null;
        if (hand != null)
        {
            bool let = false;
            drop = hand.Drop(thing, over, 1.4f, () => let = true);
            while (!let && drop.MoveNext()) yield return drop.Current;
        }
        else thing.position = over + Vector3.up * 10f;
        if (thing == null) yield break;
        // A high arc: up, over the walls between, and down onto the spot.
        var from = thing.position;
        var flat = spot - from; flat.y = 0f;
        float t = Mathf.Clamp(.6f + flat.magnitude * .03f, 1.1f, 2.2f);
        var v = flat / t; v.y = (spot.y - from.y + .5f * Gravity * t * t) / t;
        var p = from;
        for (float u = 0; u < t && thing != null; u += Time.deltaTime)
        {
            v.y -= Gravity * Time.deltaTime;
            p += v * Time.deltaTime;
            thing.SetPositionAndRotation(p, upright);
            yield return null;
        }
        if (thing == null) yield break;
        thing.SetPositionAndRotation(spot, upright);
        Landed(spot);
        if (drop != null) while (drop.MoveNext()) yield return drop.Current;
    }

    // What he carries into a lit room is lit gently (the boss stage's carry light is for a room gone dark).
    const float CarryLight = 8f;
    static IEnumerator Carrying(DungeonMasterHand hand, IEnumerator move)
    {
        float light = hand.carryLight;
        hand.carryLight = Mathf.Min(light, CarryLight);
        try { yield return move; }
        finally { if (hand != null) hand.carryLight = light; }
    }

    static void Landed(Vector3 spot)
    {
        var dust = WorldManager.HasInstance && WorldManager.Instance.tableLevelReveal != null ? WorldManager.Instance.tableLevelReveal.dustPrefab : null;
        if (dust != null)
        {
            var puff = Object.Instantiate(dust, spot + Vector3.up * .1f, Quaternion.identity);
            var shape = puff.shape; shape.scale = new Vector3(2f, 2f, .1f);
            var emission = puff.emission; emission.rateOverTimeMultiplier = 0f; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });
            var main = puff.main; main.loop = false;
            puff.Play();
            Object.Destroy(puff.gameObject, 4f);
        }
        Shake(.35f);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > .0001f ? v.normalized : Vector3.forward; }
}
