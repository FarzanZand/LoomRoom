using System.Collections;
using UnityEngine;

// The Dungeon Master's energy beam in a boss fight (BossFight > Beam, fired by BossStageEncounter), drawn with the Spells
// Pack's art: his palm gathers light (Casting Light) while thin arcs feel for the player (the warning), then a thick
// stream pours down onto them for BossFight.beamSeconds: a soft halo (Glow), a flowing body (Spell 5's flame wisps
// running along it), a white-hot core, two crackling arcs (cut from its Thunder bolts), sparks running down it, and a
// burst of light (Spell 3) on the player's shield that sprays sparks, lights the arms and shakes the view. Widths are
// shares of the screen, so the stream looks as thick next to the player as at his hand. Holding it off takes the shield
// up, facing him, the whole time; any moment the player isn't blocking, it burns. On the DM (next to DungeonMasterHand,
// which raises his palm). Textures: Tools/dm_beam_texture.py.
public class DungeonMasterBeam : MonoBehaviour
{
    [Header("Art (Spells Pack)")]
    [Tooltip("The flowing body (DM beam body: Spell 5's wisps).")] public Material bodyMaterial;
    [Tooltip("The white-hot middle (DM beam core).")] public Material coreMaterial;
    [Tooltip("The soft halo round it (pack Glow).")] public Material glowMaterial;
    [Tooltip("The crackling arcs (a pack Thunder bolt turned along the beam).")] public Material boltMaterial;
    [Tooltip("The burst on the shield (DM beam flare: Spell 3).")] public Material flareMaterial;
    [Tooltip("The sparks running down the beam and off the shield (pack Glow).")] public Material sparkMaterial;
    [Tooltip("Gathers in his palm while he charges and pours (pack Casting Light).")] public SpellVisual handEffect;
    [Min(0)] public float handEffectScale = 1.6f;
    [Tooltip("Where the beam meets the shield or the player, again and again (pack Explosion Light).")] public SpellVisual impactEffect;
    [Min(.05f)] public float impactInterval = .35f;
    [Min(0), Tooltip("Metres up the beam from the shield where the bursts go off.")] public float impactOut = 2.5f;
    [Tooltip("Sparks off a raised shield (BlockSparks).")] public GameObject blockSparks;

    [Header("Shape (shares of the screen's height)")]
    [Range(0, 1)] public float glowScreen = .26f;
    [Range(0, 1)] public float bodyScreen = .11f;
    [Range(0, 1)] public float coreScreen = .028f;
    [Range(0, 1)] public float boltScreen = .07f;
    [Range(0, 1)] public float warningScreen = .012f;
    [Range(0, 1), Tooltip("The burst on the shield.")] public float flareScreen = .28f;
    [Min(1), Tooltip("How much wider the stream grows over its last stretch, where it meets the player.")] public float endSwell = 1.5f;
    [Min(.05f), Tooltip("World units one repeat of the body's wisps covers along the beam.")] public float flowRepeat = 6f;
    [Tooltip("How fast the body and core run toward the player (texture repeats a second).")] public float scroll = -3.5f;
    [Min(.02f), Tooltip("Seconds between the arcs' jumps.")] public float boltFlicker = .07f;
    [Range(0, 1), Tooltip("How much the halo's and body's widths ripple along the beam (swells running toward the player).")]
    public float ripple = .35f;
    [Min(0), Tooltip("Ripples a second running down the beam.")] public float rippleSpeed = 2.2f;
    [Min(0), Tooltip("Bright pulses running down the core toward the player, at once (0: a steady core).")] public int corePulses = 2;
    [Min(0), Tooltip("Seconds a core pulse takes from his palm to the player.")] public float pulseSeconds = .45f;
    [Min(.05f)] public float damageTick = .5f;

    [Header("Light and shake")]
    [Min(0), Tooltip("Light in his palm while he pours (his face and hand light up; he is about twenty times a person's size).")]
    public float palmLight = 160f;
    [Min(0)] public float palmLightRange = 10f;
    [Min(0), Tooltip("Light on the player's shield and arms where the beam lands.")] public float endLight = 3f;
    [Min(0)] public float endLightRange = 1.6f;
    [Min(0), Tooltip("Shake each damage tick while it is held off, and while it burns.")] public float blockedShake = .12f, hitShake = .35f;
    [Min(.1f), Tooltip("While a raised shield holds the beam off, its block sound repeats this often (the fight's Beam Blocked Sound, or the game's block sound when that is empty).")]
    public float blockSoundInterval = .35f;

    public bool Firing { get; private set; }
    // What the last frame of the beam saw (tests read it).
    public string State { get; private set; } = "";

    // Points along each line: with only its two ends, width and texture don't follow a line that runs up to the camera.
    const int Points = 24;
    readonly Vector3[] path = new Vector3[Points];
    readonly Keyframe[] widthKeys = new Keyframe[Points];

    DungeonMasterHand hand;
    LineRenderer body, core, glow, boltA, boltB;
    ParticleSystem sparks, spray;
    Transform flare;
    Material flareInstance;
    Light palm, shieldLight;
    AudioSource sound;
    bool cancelled;
    Player strained;   // whose shield shakes against the beam
    SpellVisual charge;
    Unity.Cinemachine.CinemachineCamera view;
    Unity.Cinemachine.CinemachineImpulseSource impulse;

    void Awake()
    {
        hand = GetComponent<DungeonMasterHand>();
        glow = MakeLine("Beam glow", glowMaterial);
        body = MakeLine("Beam body", bodyMaterial);
        core = MakeLine("Beam core", coreMaterial);
        boltA = MakeLine("Beam arc A", boltMaterial);
        boltB = MakeLine("Beam arc B", boltMaterial);
        sparks = MakeSparks("Beam sparks", 70f, .45f, new ParticleSystem.MinMaxCurve(.2f, .55f), .06f, 1.5f);
        // Off the shield: a wide spray back toward him that falls away.
        spray = MakeSparks("Beam spray", 0f, .35f, new ParticleSystem.MinMaxCurve(.012f, .03f), .03f, 75f);
        var sprayRenderer = spray.GetComponent<ParticleSystemRenderer>(); sprayRenderer.renderMode = ParticleSystemRenderMode.Stretch; sprayRenderer.velocityScale = .05f;
        var sprayMain = spray.main; sprayMain.gravityModifier = .8f; sprayMain.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        flare = MakeFlare();
        palm = MakeLight("Beam palm light");
        shieldLight = MakeLight("Beam shield light");
        // Only the player's arms and shield: his own hand rests on the table right by them.
        FirstPersonLighting.SetLayers(shieldLight, FirstPersonLighting.ArmsLayer);
        impulse = gameObject.AddComponent<Unity.Cinemachine.CinemachineImpulseSource>();
        sound = gameObject.AddComponent<AudioSource>(); sound.playOnAwake = false; sound.spatialBlend = 0f; sound.loop = true;
    }

    LineRenderer MakeLine(string name, Material material)
    {
        var go = new GameObject(name); go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true; line.positionCount = Points; line.enabled = false;
        line.sharedMaterial = material; line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
        line.alignment = LineAlignment.View;
        return line;
    }

    // Sparks: born at his palm and sent down the beam (the emitter is pointed at the target every frame), or sprayed
    // back off the shield.
    ParticleSystem MakeSparks(string name, float rate, float life, ParticleSystem.MinMaxCurve size, float maxScreen, float angle)
    {
        var go = new GameObject(name); go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = life; main.startSize = size; main.maxParticles = 400;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        var emission = ps.emission; emission.rateOverTime = rate;
        // The shape scales with him (Shape scaling): radii in world units are undone from his size.
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = angle; shape.radius = (angle > 10f ? .04f : .3f) / Mathf.Max(.001f, transform.lossyScale.x);
        var sizeOverLife = ps.sizeOverLifetime; sizeOverLife.enabled = true; sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, .25f));
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = sparkMaterial; r.maxParticleSize = maxScreen;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }

    void OnDestroy() { if (flareInstance != null) Destroy(flareInstance); }

    // The burst on the shield: a quad turned to the camera every frame.
    Transform MakeFlare()
    {
        var go = new GameObject("Beam flare"); go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var r = go.AddComponent<MeshRenderer>();
        if (flareMaterial != null) r.sharedMaterial = flareInstance = new Material(flareMaterial);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        go.SetActive(false);
        return go.transform;
    }

    Light MakeLight(string name)
    {
        var go = new GameObject(name); go.transform.SetParent(transform, false);
        var l = go.AddComponent<Light>(); l.type = LightType.Point; l.shadows = LightShadows.None; l.enabled = false;
        return l;
    }

    // One ring of the shield holding the beam off: the fight's own sound, else the game's block sound, at the shield.
    static void ShieldSound(BossFight fight, Vector3 at)
    {
        if (!AudioManager.HasInstance) return;
        if (fight != null && fight.beamBlockedSound != null) { AudioManager.Instance.PlaySFX2D(fight.beamBlockedSound, fight.beamVolume, .06f); return; }
        if (CombatManager.HasInstance && CombatManager.Instance.blockAudio != null) AudioManager.Instance.PlaySFXData(CombatManager.Instance.blockAudio, at);
    }

    // Warns, pours for the fight's beam time, then stops. result: true when the player held it off throughout.
    public IEnumerator Fire(Player target, BossFight fight, System.Action<bool> result)
    {
        if (Firing || target == null || fight == null) yield break;
        Firing = true; cancelled = false;
        bool held = true;
        view = target.CameraRig != null ? target.CameraRig.Camera : null;
        if (AudioManager.HasInstance && AudioManager.Instance.SfxGroup != null) sound.outputAudioMixerGroup = AudioManager.Instance.SfxGroup;
        charge = null;
        if (handEffect != null)
        {
            charge = Instantiate(handEffect, Palm(), Quaternion.identity);
            charge.transform.localScale = Vector3.one * handEffectScale;
        }
        var colour = fight.beamColor;
        var hot = Color.Lerp(colour, Color.white, .65f) * 1.4f;
        var sparkMain = sparks.main; sparkMain.startColor = Color.Lerp(colour, Color.white, .35f);
        var sprayMain = spray.main; sprayMain.startColor = Color.Lerp(colour, Color.white, .5f);
        palm.color = shieldLight.color = Normalised(colour);
        palm.range = palmLightRange; shieldLight.range = endLightRange;
        if (fight.beamChargeSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(fight.beamChargeSound, fight.beamVolume);

        // Warning: his palm comes up and gathers light (and lights his face), thin arcs feel their way to the player.
        palm.enabled = true;
        for (float t = 0; t < fight.beamWarning; t += Time.deltaTime)
        {
            if (!Alive(target) || cancelled) break;
            var origin = Palm();
            var aim = EndPoint(target, origin, false);
            if (hand != null) hand.Aim(aim);
            float u = t / Mathf.Max(.01f, fight.beamWarning);
            if (charge != null) { charge.transform.position = origin; charge.SetCharge(u); }
            palm.transform.position = origin; palm.intensity = palmLight * .6f * u * (1f + .2f * Mathf.Sin(t * 30f));
            // The arc only reaches out in the second half, and only now and then.
            if (u > .45f && Mathf.Repeat(t, .28f) < .12f) Bolt(boltA, origin, aim, warningScreen * (1f + 2f * u), colour * (.5f + .5f * u), t);
            else boltA.enabled = false;
            boltB.enabled = core.enabled = body.enabled = glow.enabled = false;
            yield return null;
        }

        // The beam.
        if (fight.beamLoopSound != null) { sound.clip = fight.beamLoopSound; sound.volume = fight.beamVolume; sound.Play(); }
        sparks.Play(); spray.Play();
        flare.gameObject.SetActive(true); shieldLight.enabled = true;
        float tick = 0, impact = impactInterval, blockSound = 0f;
        bool lastCovered = false, lastBlocking = false; var lastEnd = Vector3.zero;
        // One tick's worth (the seconds since the last): nothing behind cover, stamina on a held shield, a burn otherwise.
        void Pay()
        {
            if (lastCovered) return;
            if (lastBlocking)
            {
                if (fight.beamStaminaPerSecond > 0f && target.Stats != null && !target.Stats.TryUseStamina(fight.beamStaminaPerSecond * tick, .6f)) { held = false; Burn(target, fight, tick, lastEnd); }
                impulse.GenerateImpulseWithForce(blockedShake);
            }
            else { held = false; Burn(target, fight, tick, lastEnd); impulse.GenerateImpulseWithForce(hitShake); }
        }
        for (float t = 0; t < fight.beamSeconds && Alive(target) && !cancelled; t += Time.deltaTime)
        {
            var origin = Palm();
            // Something solid between his palm and the player (a pillar on the stage) takes the beam instead.
            bool covered = Cover(target, origin, out var coverPoint);
            bool blocking = covered || Blocking(target, origin, fight);
            // The shield shakes while it holds the beam off.
            if (target.Combat != null) target.Combat.SetShieldStrain(blocking && !covered);
            strained = target;
            var end = covered ? coverPoint : EndPoint(target, origin, blocking);
            if (hand != null) hand.Aim(end);
            if (charge != null) { charge.transform.position = origin; charge.SetCharge(1f); }
            // It bursts out over the first moment, and throbs.
            float swell = Mathf.SmoothStep(0, 1, t / .18f) * (1f + .1f * Mathf.Sin(t * 26f)) * (t < .12f ? 1.6f - 5f * t : 1f);
            Pour(origin, end, swell, colour, hot, t);
            AimSparks(origin, end, blocking);
            Flare(end, swell * (blocking ? 1f : .7f), blocking ? Color.Lerp(colour, hot, .4f) : Color.Lerp(colour, new Color(1.6f, .35f, .2f), .5f), t);
            palm.transform.position = origin; palm.intensity = palmLight * (1f + .15f * Mathf.Sin(t * 31f));
            shieldLight.transform.position = end - (view != null ? view.transform.forward : Vector3.forward) * .1f;
            shieldLight.intensity = endLight * swell;
            tick += Time.deltaTime; impact += Time.deltaTime;
            // The shield rings with every moment it holds the beam off (a pillar taking it is silent).
            blockSound -= Time.deltaTime;
            if (blocking && !covered && blockSound <= 0f) { blockSound = blockSoundInterval; ShieldSound(fight, end); }
            else if (!blocking || covered) blockSound = 0f;
            if (impact >= impactInterval && impactEffect != null)
            {
                impact = 0;
                // A little way up the beam from the shield: at the shield itself the burst would be a cloud over the view.
                var burst = end + (origin - end).normalized * impactOut;
                Instantiate(impactEffect, burst, Quaternion.LookRotation(origin - end));
                if (blocking && blockSparks != null) Destroy(Instantiate(blockSparks, burst, Quaternion.LookRotation(origin - end)), 2f);
            }
            State = $"guard {(target.Combat != null && target.Combat.IsGuarding)} dot {FacingDot(target, origin):F2} blocking {blocking} covered {covered}";
            lastCovered = covered; lastBlocking = blocking; lastEnd = end;
            if (tick >= damageTick) { Pay(); tick = 0; }
            yield return null;
        }
        // The time since the last tick is paid as the pour ends, so a full pour deals Damage Per Second x Beam Seconds.
        if (!cancelled && Alive(target) && tick > 0f) { Pay(); tick = 0; }

        sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        spray.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        boltA.enabled = boltB.enabled = false;
        if (target.Combat != null) target.Combat.SetShieldStrain(false);
        // It thins out instead of vanishing.
        for (float t = 0; t < .25f && !cancelled && Alive(target); t += Time.deltaTime)
        {
            float f = 1f - t / .25f;
            var origin = Palm(); var end = EndPoint(target, origin, true);
            Pour(origin, end, f * f, colour * f, hot * f, t, false);
            Flare(end, f, hot * f, t);
            palm.intensity = palmLight * f; shieldLight.intensity = endLight * f;
            yield return null;
        }
        Clear();
        result?.Invoke(!cancelled && held && Alive(target));
    }

    // Cut short (the fight ended). The beam may run in another component's coroutine: it sees the flag next frame.
    public void Stop()
    {
        cancelled = true;
        StopAllCoroutines();
        Clear();
    }

    void Clear()
    {
        foreach (var l in new[] { body, core, glow, boltA, boltB }) if (l != null) l.enabled = false;
        if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (spray != null) spray.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (flare != null) flare.gameObject.SetActive(false);
        if (palm != null) palm.enabled = false;
        if (shieldLight != null) shieldLight.enabled = false;
        if (sound != null) sound.Stop();
        if (charge != null) charge.Stop();
        charge = null;
        if (hand != null) hand.StopAiming();
        if (strained != null && strained.Combat != null) strained.Combat.SetShieldStrain(false);
        strained = null;
        Firing = false;
    }

    // The stream's layers, the arcs only while it pours at full strength.
    void Pour(Vector3 origin, Vector3 end, float swell, Color colour, Color hot, float t, bool arcs = true)
    {
        float flow = Vector3.Distance(origin, end) / flowRepeat;
        rippleTime = t;
        rippleAmount = ripple; Draw(glow, origin, end, glowScreen * swell, colour * .55f, 0f, 1f);
        rippleAmount = ripple * .5f; Draw(body, origin, end, bodyScreen * swell, colour, t * scroll, flow);
        rippleAmount = 0f; Draw(core, origin, end, coreScreen * swell, hot, t * scroll * 1.6f, flow * .5f);
        Pulses(core, hot, t);
        if (!arcs) return;
        Bolt(boltA, origin, end, boltScreen * swell, Color.Lerp(colour, Color.white, .5f), t);
        Bolt(boltB, origin, end, boltScreen * .7f * swell, colour, t + 13.7f);
    }

    // The burst where it lands: turned to the camera, sized on the screen, flickering.
    void Flare(Vector3 at, float size, Color colour, float t)
    {
        if (flare == null) return;
        var cam = view != null ? view.transform : null;
        float depth = cam != null ? Mathf.Max(.05f, Vector3.Dot(at - cam.position, cam.forward)) : 1f;
        flare.position = at - (cam != null ? cam.forward * .05f : Vector3.zero);
        if (cam != null) flare.rotation = Quaternion.LookRotation(cam.forward, cam.up) * Quaternion.Euler(0, 0, t * 90f);
        float flicker = .85f + .3f * Mathf.PerlinNoise(t * 14f, 3.1f);
        // He is many times a person's size and the flare hangs under him: its world size is undone from his scale.
        flare.localScale = Vector3.one * (ScreenToWorld(flareScreen, depth) * size * flicker / Mathf.Max(.001f, transform.lossyScale.x));
        if (flareInstance != null) flareInstance.SetColor("_BaseColor", colour);
    }

    void Burn(Player target, BossFight fight, float seconds, Vector3 at)
    {
        if (target.Stats == null) return;
        target.Stats.TakeDamage(new DamageInfo
        {
            Amount = fight.beamDamagePerSecond * seconds, Magic = true, SourceName = "the Dungeon Master",
            HitPoint = at, Direction = Flat(at - Palm()),
        });
    }

    static float FacingDot(Player target, Vector3 origin)
    {
        var view = target.CameraRig != null && target.CameraRig.Camera != null ? target.CameraRig.Camera.transform.forward : target.transform.forward;
        return Vector3.Dot(Flat(view), Flat(origin - target.transform.position));
    }

    // The first solid thing (not a character, not him) on the line from his palm to the player's eyes, on the player's
    // half of it (the table's edge near him is not cover), if any.
    bool Cover(Player target, Vector3 origin, out Vector3 point)
    {
        point = default;
        var eye = target.CameraRig != null && target.CameraRig.Camera != null ? target.CameraRig.Camera.transform.position : target.transform.position + Vector3.up * 1.6f;
        var dir = eye - origin;
        float length = dir.magnitude, nearest = float.MaxValue;
        if (length < .1f) return false;
        foreach (var hit in Physics.RaycastAll(origin, dir / length, length - .3f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance < length * .5f || hit.distance >= nearest) continue;
            if (hit.collider.GetComponentInParent<Character>() != null || hit.transform.IsChildOf(transform)) continue;
            // The stage's invisible edges only keep walkers in: ended on one, the beam ran end-on at the player's eye and wasn't seen.
            if (InvisibleWall.Is(hit.collider)) continue;
            nearest = hit.distance; point = hit.point;
        }
        return nearest < float.MaxValue;
    }

    // Shield up (PlayerCombat.IsGuarding) and facing him.
    static bool Blocking(Player target, Vector3 origin, BossFight fight) =>
        target.Combat != null && target.Combat.IsGuarding && FacingDot(target, origin) >= fight.beamFacing;

    // The middle of his raised palm.
    Vector3 Palm()
    {
        var bone = hand != null ? hand.LeftHand : null;
        if (bone == null) return transform.position + Vector3.up * 25f;
        var fingers = bone.childCount > 0 ? bone.GetChild(0).position : bone.position;
        return Vector3.Lerp(bone.position, fingers, .5f);
    }

    // Where the beam ends, fixed on the player's screen: on the shield's face, in the middle of the view a little low (the block clips hold it there
    // since 2026-10-17; or the bottom of the view when it gets
    // through). Ending on the line of sight to his palm would hide the beam: it would be seen end-on, a stub.
    static Vector3 EndPoint(Player p, Vector3 origin, bool shield)
    {
        var cam = p.CameraRig != null && p.CameraRig.Camera != null ? p.CameraRig.Camera.transform : p.transform;
        var eye = p.CameraRig != null && p.CameraRig.Camera != null ? cam.position : p.transform.position + Vector3.up * 1.6f;
        return shield ? eye + cam.forward * .85f - cam.up * .25f
                      : eye + cam.forward * .7f - cam.right * .1f - cam.up * .55f;
    }

    static bool Alive(Player p) => p != null && p.IsAlive && p.isActiveAndEnabled;

    // World size that covers share of the screen's height at depth in front of the player's camera.
    float ScreenToWorld(float share, float depth) =>
        share * 2f * depth * Mathf.Tan((view != null ? view.Lens.FieldOfView : 60f) * .5f * Mathf.Deg2Rad);

    // repeats: how many times the texture runs along the line; flip mirrors it across the line. width is a share of the
    // screen: each point gets the world width that covers it at its depth, swelling over the last stretch.
    void Draw(LineRenderer line, Vector3 from, Vector3 to, float width, Color color, float offset, float repeats, bool flip = false, float jitter = 0f, int seed = 0)
    {
        line.enabled = true;
        var side = Vector3.Cross(to - from, Vector3.up).normalized;
        var up = Vector3.Cross(side, to - from).normalized;
        var rng = new System.Random(seed);
        var cam = view != null ? view.transform : null;
        for (int i = 0; i < Points; i++)
        {
            float u = i / (Points - 1f);
            var p = Vector3.Lerp(from, to, u);
            // Arcs wander off the line, most in the middle, not at either end.
            if (jitter > 0f && i > 0 && i < Points - 1)
            {
                float k = Mathf.Sin(u * Mathf.PI) * jitter * (1f - u * .8f);
                p += side * ((float)rng.NextDouble() * 2f - 1f) * k + up * ((float)rng.NextDouble() * 2f - 1f) * k;
            }
            path[i] = p;
            float depth = cam != null ? Mathf.Max(.05f, Vector3.Dot(p - cam.position, cam.forward)) : 10f;
            // Out of his palm it starts narrow (the glow there covers it), and it swells where it meets the player.
            float shape = Mathf.Lerp(.45f, 1f, Mathf.Clamp01(u / .08f)) * Mathf.Lerp(1f, endSwell, Mathf.SmoothStep(0, 1, (u - .75f) / .25f));
            // A ripple: swells that run from his palm toward the player, uneven (two noise layers), never at the ends.
            if (rippleAmount > 0f)
            {
                float run = u * 3.2f - rippleTime * rippleSpeed;
                float n = Mathf.PerlinNoise(run, line == glow ? 1.3f : 4.7f) * .7f + Mathf.PerlinNoise(run * 2.3f, 7.1f) * .3f;
                shape *= 1f + (n - .5f) * 2f * rippleAmount * Mathf.Clamp01(u * 6f) * Mathf.Clamp01((1f - u) * 6f);
            }
            widthKeys[i] = new Keyframe(u, ScreenToWorld(width, depth) * shape);
        }
        line.SetPositions(path);
        line.widthMultiplier = 1f;
        line.widthCurve = new AnimationCurve(widthKeys);
        line.startColor = line.endColor = color;
        if (line.sharedMaterial != null)
        {
            var m = line.material;
            m.mainTextureScale = new Vector2(Mathf.Max(.01f, repeats), flip ? -1f : 1f);
            m.mainTextureOffset = new Vector2(offset, 0f);
        }
    }

    float rippleTime, rippleAmount;

    // Bright pulses running down the core: the core's alpha along its length, dim between them.
    readonly GradientAlphaKey[] pulseKeys = new GradientAlphaKey[8];
    void Pulses(LineRenderer line, Color colour, float t)
    {
        var g = new Gradient();
        var c = new Color(colour.r, colour.g, colour.b, 1f);
        int n = 0;
        pulseKeys[n++] = new GradientAlphaKey(.7f, 0f);
        for (int k = 0; k < Mathf.Min(corePulses, 2); k++)
        {
            float at = Mathf.Repeat(t / Mathf.Max(.05f, pulseSeconds) + k / (float)Mathf.Max(1, corePulses), 1f);
            pulseKeys[n++] = new GradientAlphaKey(.55f, Mathf.Clamp01(at - .07f));
            pulseKeys[n++] = new GradientAlphaKey(1f, at);
            pulseKeys[n++] = new GradientAlphaKey(.55f, Mathf.Clamp01(at + .07f));
        }
        pulseKeys[n++] = new GradientAlphaKey(.7f, 1f);
        var keys = new GradientAlphaKey[n];
        System.Array.Copy(pulseKeys, keys, n);
        System.Array.Sort(keys, (a, b) => a.time.CompareTo(b.time));
        g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) }, keys);
        line.colorGradient = g;
    }

    // A crackling arc: the bolt texture, jumping to a new shape every Bolt Flicker, flipped at random.
    void Bolt(LineRenderer line, Vector3 from, Vector3 to, float width, Color color, float time)
    {
        int step = Mathf.FloorToInt(time / boltFlicker);
        var rng = new System.Random(step * 7919 + (line == boltA ? 17 : 91));
        float repeats = Mathf.Max(1f, Vector3.Distance(from, to) / 9f);
        Draw(line, from, to, width * (.7f + (float)rng.NextDouble() * .6f), color * (.6f + (float)rng.NextDouble() * .6f), (float)rng.NextDouble(), repeats, rng.Next(2) == 0,
            Vector3.Distance(from, to) * .05f, step * 31 + (line == boltA ? 1 : 2));
    }

    void AimSparks(Vector3 from, Vector3 to, bool blocking)
    {
        var dir = to - from;
        sparks.transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir));
        var main = sparks.main;
        main.startSpeed = dir.magnitude / Mathf.Max(.05f, main.startLifetime.constantMax);
        // The spray leaves the shield back toward him, wide; fewer when it gets through.
        spray.transform.SetPositionAndRotation(to, Quaternion.LookRotation(-dir));
        var emission = spray.emission; emission.rateOverTime = blocking ? 160f : 50f;
    }

    static Color Normalised(Color c) { float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b)); return m > 0f ? new Color(c.r / m, c.g / m, c.b / m, 1f) : Color.white; }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > .0001f ? v.normalized : Vector3.forward; }
}
