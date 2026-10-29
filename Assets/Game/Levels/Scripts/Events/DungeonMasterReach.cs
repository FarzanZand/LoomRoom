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
    static void ResetStatics() { reaching = false; reachFloor = null; standingAt = Side.Own; impulse = null; walkSpeed = -1f; steppedIn = false; steppedAgent = null; appeared = false; roomLid = null; liftedPieces.Clear(); heldChunks.Clear(); shownPieces.Clear(); }

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
    public static IEnumerator TurnTo(Vector3 spot)
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

    // Only the roof over the rooms or corridors (layout regions) the points stand in comes off, the rest of the floor's
    // ceiling stays on (a spell for the player: the roof over them and the enemy). The merged ceiling chunks touching it
    // are swapped for the pieces they were made of while it is off, so it can come away on its own. With no region found
    // it lifts the whole lid. Going up it breaks free with a jolt, hangs a moment and heaves away, tipping toward him;
    // coming down it drops and lands with a thud, a shake and dust blown out along its edges (fx: its sounds and dust).
    static Transform roomLid;
    static Bounds lidArea;
    static readonly System.Collections.Generic.List<(Transform piece, Transform parent)> liftedPieces = new();
    static readonly System.Collections.Generic.List<Renderer> heldChunks = new(), shownPieces = new();
    public static IEnumerator Lid(DungeonGenerator dungeon, bool open, Vector3[] over, LidEffects fx, float seconds = .9f, float height = 4f)
    {
        fx ??= new LidEffects();
        AudioSource rising = open ? Play(fx.rising, dungeon, fx.volume) : null;
        try
        {
            var inner = LidMove(dungeon, open, over, fx, seconds, height);
            while (inner.MoveNext()) yield return inner.Current;
        }
        finally { FadeOut(rising, fx.rising, .4f); }
    }

    // A sound under the lid faded out over seconds (on AudioManager, the static class runs no coroutines), only while its
    // pooled source still plays that clip.
    static void FadeOut(AudioSource source, AudioClip clip, float seconds)
    {
        if (source == null || clip == null || !AudioManager.HasInstance) return;
        AudioManager.Instance.StartCoroutine(Fade());
        IEnumerator Fade()
        {
            float from = source.volume;
            for (float t = 0; t < seconds && source != null && source.clip == clip && source.isPlaying; t += Time.deltaTime)
            {
                source.volume = from * (1f - t / seconds);
                yield return null;
            }
            if (source != null && source.clip == clip) { source.Stop(); source.volume = from; }
        }
    }

    // How high the lid is (0-1 of the way) and how far it has tipped (degrees) a share n of the way up: a jolt as it breaks
    // free, a small sag while it hangs, then a heave that gathers speed.
    const float Jolt = .1f, JoltEnd = .18f, HangEnd = .34f;
    static float LiftAt(float n, out float tipDegrees)
    {
        if (n < JoltEnd) { float a = n / JoltEnd; tipDegrees = 1.5f * a; return Jolt * (1f - (1f - a) * (1f - a)); }
        if (n < HangEnd) { float a = (n - JoltEnd) / (HangEnd - JoltEnd); tipDegrees = 1.5f + Mathf.Sin(a * Mathf.PI) * .4f; return Jolt - .02f * Mathf.Sin(a * Mathf.PI); }
        float c = (n - HangEnd) / (1f - HangEnd);
        tipDegrees = Mathf.Lerp(1.5f, 6f, c * c);
        return Jolt + (1f - Jolt) * c * c * c;
    }

    static IEnumerator LidMove(DungeonGenerator dungeon, bool open, Vector3[] over, LidEffects fx, float seconds, float height)
    {
        float lead = fx.close != null ? Shape(fx.close).peak - Shape(fx.close).onset : 0f;
        if (open && !OpenRoomLid(dungeon, over)) { Play(fx.open, dungeon, fx.volume); yield return Lid(dungeon, true, seconds, height); yield break; }
        if (!open && roomLid == null)
        {
            // The whole lid: stepped here so the thud can start before it lands.
            var whole = Lid(dungeon, false, seconds, height);
            bool sounded = false;
            for (float t = 0; whole.MoveNext(); t += Time.deltaTime)
            {
                if (!sounded && t >= seconds - lead) { sounded = true; Play(fx.close, dungeon, fx.volume); }
                yield return whole.Current;
            }
            if (!sounded) Play(fx.close, dungeon, fx.volume);
            Play(fx.land, dungeon, fx.volume);
            yield break;
        }
        var lid = roomLid;
        var up = Vector3.up * height;
        if (open) Play(fx.open, dungeon, fx.volume);
        else { lid.gameObject.SetActive(true); lid.localPosition = lidRest + up; lid.localRotation = Quaternion.Euler(0f, 0f, 6f); }
        var downFrom = lid.localRotation;
        bool thud = open;
        for (float t = 0; t < seconds && lid != null; t += Time.deltaTime)
        {
            float n = t / seconds;
            if (!thud && t >= seconds - lead) { thud = true; Play(fx.close, dungeon, fx.volume); }
            if (open)
            {
                float h = LiftAt(n, out float tip);
                lid.localPosition = lidRest + up * h;
                lid.localRotation = Quaternion.Euler(0f, 0f, tip);
            }
            else
            {
                // It drops, gathering speed, and levels as it comes.
                lid.localPosition = lidRest + up * (1f - n * n);
                lid.localRotation = Quaternion.Slerp(downFrom, Quaternion.identity, n);
            }
            yield return null;
        }
        if (!thud) Play(fx.close, dungeon, fx.volume);
        if (lid == null) { CloseRoomLid(); yield break; }
        if (open) { lid.localPosition = lidRest + up; lid.gameObject.SetActive(false); yield break; }
        lid.localPosition = lidRest; lid.localRotation = Quaternion.identity;
        Play(fx.land, dungeon, fx.volume);
        if (fx.dust) Smoke(lidArea);
        CloseRoomLid();
        Shake(.45f);
    }

    // Dust knocked out along the roof's edge as it lands, the way the table reveal's entrance throws it (its soft round
    // dust material): puffs from the seam where roof met wall, pushed into the room and a little down, growing,
    // spreading and thinning until they clear.
    static void Smoke(Bounds area)
    {
        var look = WorldManager.HasInstance ? WorldManager.Instance.tableLevelReveal : null;
        if (look == null || look.entranceDustMaterial == null) return;
        var go = new GameObject("Roof dust");
        var puff = go.AddComponent<ParticleSystem>();
        puff.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        float perimeter = 2f * (area.size.x + area.size.z);
        int count = Mathf.Clamp(Mathf.RoundToInt(perimeter * 2.2f), 24, 160);
        var main = puff.main;
        main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count + 10; main.gravityModifier = .04f;
        var emission = puff.emission; emission.enabled = false;
        var shape = puff.shape; shape.enabled = false;
        var grow = puff.sizeOverLifetime; grow.enabled = true; grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .6f, 1f, 1.8f));
        var slow = puff.limitVelocityOverLifetime; slow.enabled = true; slow.limit = .8f; slow.dampen = .12f;
        var fade = puff.colorOverLifetime; fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .06f), new GradientAlphaKey(.6f, .45f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = look.entranceDustMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        puff.Play();
        float y = area.min.y;
        var p = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            // A point on the roof's edge, and into the room from there.
            float side = Random.value, along = Random.value;
            var at = side < .5f ? new Vector3(side < .25f ? area.min.x : area.max.x, y, Mathf.Lerp(area.min.z, area.max.z, along))
                                : new Vector3(Mathf.Lerp(area.min.x, area.max.x, along), y, side < .75f ? area.min.z : area.max.z);
            var inward = area.center - at; inward.y = 0f; inward = inward.normalized;
            p.position = at + inward * Random.Range(.3f, .8f) - Vector3.up * Random.Range(.1f, .5f);
            p.velocity = inward * Random.Range(.6f, 2.2f) - Vector3.up * Random.Range(.2f, 1f);
            p.startSize = Random.Range(1f, 2.4f);
            p.startLifetime = Random.Range(1.4f, 2.4f);
            p.startColor = Color.Lerp(new Color(.82f, .78f, .72f, .9f), new Color(.66f, .62f, .56f, .75f), Random.value);
            p.rotation = Random.Range(0f, 360f);
            puff.Emit(p, 1);
        }
        Object.Destroy(go, 3.5f);
    }
    static Vector3 lidRest;

    // Played from its first loud moment (a door or lid clip opens on a stretch of silence).
    static AudioSource Play(AudioClip sound, DungeonGenerator dungeon, float volume)
    {
        if (sound == null || !AudioManager.HasInstance) return null;
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        var at = player != null ? player.transform.position + Vector3.up * 3f : dungeon != null ? dungeon.transform.position : Vector3.zero;
        var source = AudioManager.Instance.PlaySFX(sound, at, volume);
        float onset = Shape(sound).onset;
        if (source != null && source.clip == sound && onset > 0f && onset < sound.length) source.time = onset;
        return source;
    }

    // Where a clip's sound starts (first above a fifth of its loudest) and where it is loudest, in seconds; read once
    // from its samples (0 for both when they can't be read).
    static readonly System.Collections.Generic.Dictionary<AudioClip, (float onset, float peak)> shapes = new();
    static (float onset, float peak) Shape(AudioClip clip)
    {
        if (clip == null) return (0f, 0f);
        if (shapes.TryGetValue(clip, out var known)) return known;
        var shape = (0f, 0f);
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        var data = new float[clip.samples * clip.channels];
        if (clip.loadState == AudioDataLoadState.Loaded && data.Length > 0 && clip.GetData(data, 0))
        {
            int loudest = 0; float peak = 0f;
            for (int i = 0; i < data.Length; i++) { float a = Mathf.Abs(data[i]); if (a > peak) { peak = a; loudest = i; } }
            int first = 0;
            while (first < data.Length && Mathf.Abs(data[first]) < peak * .2f) first++;
            float rate = clip.frequency * (float)clip.channels;
            shape = (first / rate, loudest / rate);
        }
        shapes[clip] = shape;
        return shape;
    }

    static bool OpenRoomLid(DungeonGenerator dungeon, Vector3[] over)
    {
        CloseRoomLid();
        var ceilings = dungeon != null ? dungeon.Ceilings : null;
        var layout = dungeon != null ? dungeon.Layout : null;
        if (ceilings == null || layout == null || layout.RegionIds == null || over == null) return false;
        var regions = new System.Collections.Generic.HashSet<int>();
        foreach (var p in over) { var c = dungeon.CellOf(p); if (layout.InBounds(c) && layout.floor[c.x, c.y]) regions.Add(layout.RegionIds[c.x, c.y]); }
        if (regions.Count == 0) return false;
        bool InRegion(Vector3 world) { var c = dungeon.CellOf(world); return layout.InBounds(c) && regions.Contains(layout.RegionIds[c.x, c.y]); }
        var geometry = dungeon.GetComponent<DungeonStaticGeometry>();
        var merged = geometry != null ? new System.Collections.Generic.HashSet<MeshRenderer>(geometry.MergedPieces) : new System.Collections.Generic.HashSet<MeshRenderer>();
        // The region's own pieces: drawn ones, and ones merged away into a chunk.
        var pieces = new System.Collections.Generic.List<Renderer>();
        foreach (var r in ceilings.GetComponentsInChildren<MeshRenderer>(true))
            if (r.name != "Static chunk" && (r.enabled || merged.Contains(r)) && InRegion(r.transform.position)) pieces.Add(r);
        if (pieces.Count == 0) return false;
        var area = pieces[0].bounds; foreach (var r in pieces) area.Encapsulate(r.bounds);
        area.Expand(.2f);
        // Every drawn chunk touching it is held off and the pieces it was made of drawn instead.
        foreach (var chunk in ceilings.GetComponentsInChildren<MeshRenderer>())
        {
            if (chunk.name != "Static chunk" || !chunk.enabled || !chunk.bounds.Intersects(area)) continue;
            chunk.enabled = false; heldChunks.Add(chunk);
            var box = chunk.bounds; box.Expand(.05f);
            foreach (var piece in merged)
                if (piece != null && !piece.enabled && piece.sharedMaterial == chunk.sharedMaterial && box.Contains(piece.bounds.center)) { piece.enabled = true; shownPieces.Add(piece); }
        }
        foreach (var r in pieces) if (!r.enabled) { r.enabled = true; shownPieces.Add(r); }
        // The lid turns about the middle of its own roof.
        roomLid = new GameObject("Room lid").transform;
        roomLid.SetParent(ceilings, false);
        roomLid.position = new Vector3(area.center.x, area.max.y, area.center.z);
        lidRest = roomLid.localPosition;
        lidArea = area;
        foreach (var r in pieces) { liftedPieces.Add((r.transform, r.transform.parent)); r.transform.SetParent(roomLid, true); }
        return true;
    }

    // Everything back as it was: the pieces under their own parents (the lid is down where they were), drawn as before.
    static void CloseRoomLid()
    {
        foreach (var (piece, parent) in liftedPieces) if (piece != null && parent != null) piece.SetParent(parent, true);
        foreach (var r in shownPieces) if (r != null) r.enabled = false;
        foreach (var r in heldChunks) if (r != null) r.enabled = true;
        liftedPieces.Clear(); shownPieces.Clear(); heldChunks.Clear();
        if (roomLid != null) Object.Destroy(roomLid.gameObject);
        roomLid = null;
    }

    // The view shaken (the player camera's impulse listener): a hard jolt that rattles side to side and dies away over
    // seconds (an Explosion impulse, not a single bump, so it reads as a wobble), its first kick a random sideways and
    // downward direction. Force 1 moves the view about 15 cm at its first kick (measured; the impulse itself moves it about
    // 1.65 m per unit, ShakeScale brings that down).
    const float ShakeScale = .09f;
    // Degrees the view rolls at the first swing of its wobble, per unit of force.
    const float ViewDegrees = 2.2f;
    static Unity.Cinemachine.CinemachineImpulseSource impulse;
    public static void Shake(float force, float seconds = .6f)
    {
        if (impulse == null)
        {
            var go = new GameObject("DM reach shake");
            impulse = go.AddComponent<Unity.Cinemachine.CinemachineImpulseSource>();
            impulse.ImpulseDefinition.ImpulseType = Unity.Cinemachine.CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            impulse.ImpulseDefinition.ImpulseShape = Unity.Cinemachine.CinemachineImpulseDefinition.ImpulseShapes.Explosion;
        }
        impulse.ImpulseDefinition.ImpulseDuration = Mathf.Max(.1f, seconds);
        var kick = new Vector3(Random.value < .5f ? -1f : 1f, -.7f, Random.Range(-.3f, .3f)).normalized;
        impulse.GenerateImpulseWithVelocity(kick * force * ShakeScale);
        // The view wobbles too (rolls and pitches back and forth): moving the camera alone barely shows.
        ViewShake.Add(force * ViewDegrees, seconds);
    }

    // ── His moves ────────────────────────────────────────────────────

    // He walks to where he can reach the spot: straight along his own side, or round the corner to an end of the table.
    // He hurries along the table while he reaches (his boss-fight pace is a slow, looming walk).
    const float ReachWalkSpeed = 22f;
    static float walkSpeed = -1f;
    static void Hurry(DungeonMasterSeat seat, bool on, float speed = ReachWalkSpeed)
    {
        if (seat == null) return;
        if (on) { if (walkSpeed < 0f) walkSpeed = seat.focusSpeed; seat.focusSpeed = Mathf.Max(walkSpeed, speed); }
        else if (walkSpeed >= 0f) { seat.focusSpeed = walkSpeed; walkSpeed = -1f; }
    }

    // speed: how fast he walks there (a flick hurries more than his other reaches).
    public static IEnumerator Approach(Vector3 spot, float speed = ReachWalkSpeed)
    {
        var seat = Seat; var hand = Hand;
        Hurry(seat, true, speed);
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

    // Marks his hands taken for a reach run by someone else (the Flick spell); Leave frees them.
    public static void BeginReach() => Reaching = true;

    // Whether his hand reaches the spot from right up against the table's edge (reach: how far past the edge, level, world
    // units; edgeGap: how far from the edge he stands then), with a walk along the table of at most maxWalk.
    public static bool InHandReach(Vector3 spot, float reach, float edgeGap, float maxWalk)
    {
        var hand = Hand;
        if (hand == null) return false;
        var stand = StandFor(spot);
        float fromEdge = Mathf.Max(0f, (Seat != null ? Seat.leanBack : 6f) - edgeGap);
        float level = new Vector2(spot.x - stand.x, spot.z - stand.z).magnitude - fromEdge;
        return level <= reach && new Vector2(stand.x - hand.transform.position.x, stand.z - hand.transform.position.z).magnitude <= maxWalk;
    }

    // Right up against the table's edge, edgeGap from it (he stands further back to set figures down): his hand has to
    // reach into the room itself. His navigation agent keeps him on the bedroom's floor, which stops short of the table:
    // it is off until StepBack.
    static UnityEngine.AI.NavMeshAgent steppedAgent; static bool steppedAgentWasOn; static Vector3 steppedFrom; static bool steppedIn;
    public static IEnumerator StepIn(Vector3 spot, float edgeGap)
    {
        var seat = Seat; var hand = Hand;
        if (seat == null || hand == null) yield break;
        steppedFrom = hand.transform.position;
        steppedAgent = hand.GetComponent<NavMeshAgent>();
        steppedAgentWasOn = steppedAgent != null && steppedAgent.enabled;
        if (steppedAgentWasOn) steppedAgent.enabled = false;
        steppedIn = true;
        var toward = Flat(spot - steppedFrom);
        seat.Focus(steppedFrom + toward * Mathf.Max(0f, seat.leanBack - edgeGap), spot);
        for (float t = 0; !seat.Arrived && t < 2f; t += Time.deltaTime) yield return null;
    }

    // He is there at once beside the spot, wherever it is on the table (standing in it), facing it: his hand reaches it
    // from where he stands. Off his navigation agent until Vanish.
    static bool appeared;
    public static bool Appear(Vector3 standAt, Vector3 spot)
    {
        var seat = Seat; var hand = Hand;
        if (seat == null || hand == null) return false;
        steppedAgent = hand.GetComponent<NavMeshAgent>();
        steppedAgentWasOn = steppedAgent != null && steppedAgent.enabled;
        if (steppedAgentWasOn) steppedAgent.enabled = false;
        if (!seat.Teleport(standAt, spot)) { if (steppedAgentWasOn) steppedAgent.enabled = true; return false; }
        appeared = true; standingAt = Side.Own;
        hand.Watch(spot, true);
        return true;
    }

    // Back at his place at once (after Appear).
    public static void Vanish()
    {
        if (!appeared) return;
        appeared = false;
        if (Seat != null) Seat.TeleportHome();
        if (steppedAgent != null && steppedAgentWasOn) { steppedAgent.enabled = true; steppedAgent.Warp(steppedAgent.transform.position); }
        steppedAgent = null;
    }

    // A step back from the edge, onto his own floor again (after StepIn), before he goes.
    public static IEnumerator StepBack(Vector3 spot)
    {
        if (!steppedIn) yield break;
        steppedIn = false;
        var seat = Seat;
        if (seat != null)
        {
            seat.Focus(steppedFrom, steppedFrom + Flat(spot - steppedFrom));
            for (float t = 0; !seat.Arrived && t < 2f; t += Time.deltaTime) yield return null;
        }
        if (steppedAgent != null && steppedAgentWasOn) { steppedAgent.enabled = true; steppedAgent.Warp(steppedAgent.transform.position); }
        steppedAgent = null;
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

// The roof's sounds and dust as the Dungeon Master lifts it off a room and puts it back (DungeonMasterReach.Lid).
[System.Serializable]
public class LidEffects
{
    [Tooltip("As it starts up (a big lid opening; played from its first sound, so it starts with the lid).")] public AudioClip open;
    [Tooltip("Under it while it rises (grit and dust as it breaks free), faded out once it is off.")] public AudioClip rising;
    [Tooltip("Coming down; started early so its loudest moment is the landing.")] public AudioClip close;
    [Tooltip("The weight of it landing, on the landing frame.")] public AudioClip land;
    [Range(0, 1)] public float volume = .8f;
    [Tooltip("Dust blown out along its edges as it lands.")] public bool dust = true;
}
