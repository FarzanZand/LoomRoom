using System;
using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

// How he gets to the player. Serialized by integer: append only.
public enum HelpReach
{
    WalkOrPotion = 0,   // he walks to the table's edge and holds his hand over them, or out of his reach flicks them a potion
    Appear = 1,         // he is there at once in front of them, standing in the table, and holds his hand over them
    PotionOnly = 2,     // he flicks them a potion from where he stands
}

[Serializable]
public class HelpingHandSettings
{
    [FoldoutGroup("His hand", Expanded = true), Tooltip("Walk Or Potion: he walks to the table's edge for his hand, or flicks a potion from where he is when they are out of reach. Appear: he is there at once in front of the player, standing in the table, and holds his hand over them anywhere. Potion Only: always the potion.")]
    public HelpReach reach = HelpReach.Appear;
    [FoldoutGroup("His hand"), ShowIf("@reach == HelpReach.Appear"), Min(4f), Tooltip("How far in front of the player he appears (level, world units, along where they look).")]
    public float appearDistance = 17f;
    [FoldoutGroup("His hand"), Min(.2f), Tooltip("Seconds his hand takes to come in over the player once he stands at the table.")]
    public float reachSeconds = 1.4f;
    [FoldoutGroup("His hand"), Min(.2f), Tooltip("Seconds he holds his hand over the player, healing them all the while (the spell's power in all).")]
    public float holdSeconds = 2.2f;
    [FoldoutGroup("His hand"), Min(1f), Tooltip("How far his hand reaches past the table's edge (level, world units). Further, he flicks a potion instead.")]
    public float handReach = 12f;
    [FoldoutGroup("His hand"), Min(0f), Tooltip("Open floor the player needs round them (metres to the nearest wall) for his hand to come down into the room; in a tight spot he flicks a potion instead.")]
    public float handRoom = 1.5f;
    [FoldoutGroup("His hand"), Min(0f), Tooltip("How far from the table's edge he stands for it (world units).")]
    public float edgeGap = 1.5f;
    [FoldoutGroup("His hand"), Min(0f), Tooltip("Furthest he walks along the table to hold his hand over the player (world units). Further, he flicks a potion from where he stands.")]
    public float maxWalk = 40f;
    [FoldoutGroup("His hand"), Min(1f), Tooltip("How fast he walks to the table's edge (world units a second).")]
    public float walkSpeed = 45f;
    [FoldoutGroup("His hand"), Min(1f), Tooltip("Height above the player's feet the middle of his palm is held at (metres).")]
    public float palmHeight = 7f;
    [FoldoutGroup("His hand"), Min(0f), Tooltip("How far in front of the player his palm is held (metres along where they look), so it is seen looking up a little.")]
    public float palmAhead = 4.5f;
    [FoldoutGroup("His hand"), AssetsOnly, Tooltip("Pours down onto the player while his hand is over them (kept at their feet).")]
    public SpellVisual pour;
    [FoldoutGroup("His hand"), AssetsOnly, Tooltip("A shaft of light from his palm down onto the player (a moonbeam prefab: its light and shaft come down from Beam Source onto its own origin; it is turned and sized to reach from his palm to their feet).")]
    public GameObject beam;
    [FoldoutGroup("His hand"), Tooltip("Where in the beam prefab its light comes from (local).")]
    public Vector3 beamSource = new(-1.2f, 11f, -.6f);
    [FoldoutGroup("His hand"), Range(.05f, 1f), Tooltip("How wide the shaft is, as a share of the moonbeam's own width.")]
    public float beamWidth = .35f;
    [FoldoutGroup("His hand"), Tooltip("The shaft's colour (its material's base colour, alpha kept as its strength).")]
    public Color beamTint = new(.36f, .28f, .12f, .5f);
    [FoldoutGroup("His hand"), Range(0f, 1f), Tooltip("The beam's lights at this share of their strength.")]
    public float beamLight = .35f;
    [FoldoutGroup("His hand"), AssetsOnly, Tooltip("Glows under his palm while he holds it over the player.")]
    public SpellVisual palmGlow;
    [FoldoutGroup("His hand"), Min(.1f)] public float palmGlowScale = 12f;
    [FoldoutGroup("His hand"), Min(0f), Tooltip("Light shining down from his palm onto the player.")]
    public float palmLight = 8f;
    [FoldoutGroup("Out of his reach"), AssetsOnly, Tooltip("Too far from the table's edge for his hand: he flicks this to the player from where he stands. It bursts at their feet.")]
    public GameObject potion;
    [FoldoutGroup("Out of his reach"), Min(.05f)] public float potionScale = 3f;
    [FoldoutGroup("Out of his reach"), Min(1f), Tooltip("How far above the higher of his hand and the player the potion's arc peaks.")]
    public float potionArc = 6f;
    [FoldoutGroup("Out of his reach"), Min(1f), Tooltip("Pull on the potion in flight (lower: a slower, higher arc).")]
    public float potionGravity = 16f;
    [FoldoutGroup("Out of his reach"), Min(0f), Tooltip("Light the potion carries, so it is seen crossing the dark.")]
    public float potionLight = 10f;
    [FoldoutGroup("Out of his reach"), Min(0f), Tooltip("How far in front of the player it lands (metres along where they look).")]
    public float landAhead = 1.2f;
    [FoldoutGroup("Out of his reach"), Min(.1f), Tooltip("Seconds the heal takes once it has burst.")]
    public float burstHealSeconds = 1f;
    [FoldoutGroup("Out of his reach"), AssetsOnly, Tooltip("Where it bursts.")]
    public SpellVisual burst;
    [FoldoutGroup("Out of his reach")] public AudioClip smashSound;
    [FoldoutGroup("Out of his reach"), Range(0, 1)] public float smashVolume = 1f;
    [FoldoutGroup("Lid"), Tooltip("The roof over the player's room coming off and landing back on: sounds and dust.")]
    public LidEffects lid = new();
    [FoldoutGroup("Lines"), Tooltip("He says one as he comes.")]
    public string[] lines = { "Here.", "Hold still.", "Try not to need that again." };
    [FoldoutGroup("Look")] public Color color = new(1f, .9f, .6f);
}

// Helping Hand: the Dungeon Master heals you. He comes to the nearest edge of the table, lifts the floor's lid and holds
// his open hand over you; light pours down from his palm and heals you while it does. Too far from the table's edge for
// his hand, he flicks you a potion from where he stands instead, which bursts at your feet. Run on its own object, one at
// a time (and never while a Flick has his hands).
public class HelpingHand : MonoBehaviour
{
    static HelpingHand running;
    public static bool Running => running != null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { running = null; }

    Player caster;
    SpellDefinition spell;
    HelpingHandSettings settings;
    AdventurerProgress progress;
    float power, healed;
    Vector3 ahead;   // where the player looked as they cast: his palm is held that way (turning the view doesn't swing his arm)

    static Transform View(Player player) => player.CameraRig != null && player.CameraRig.Camera != null ? player.CameraRig.Camera.transform : player.transform;

    public static string Refusal(Player player, SpellDefinition spell)
    {
        var floor = TableManager.Loader != null ? TableManager.Loader.Dungeon : null;
        if (floor == null || DungeonMasterReach.Hand == null || DungeonMasterReach.Seat == null) return "He can't reach you here";
        if (Running || Flick.Running || DungeonMasterReach.Busy) return "He's busy";
        if (player.Stats.CurrentHealth >= player.Stats.MaxHealth) return "Health is full";
        return null;
    }

    public static void Cast(Player player, SpellDefinition spell, float power, AdventurerProgress progress)
    {
        if (Running) return;
        var go = new GameObject(spell.name + " (helping hand)");
        var help = go.AddComponent<HelpingHand>();
        help.caster = player; help.spell = spell; help.settings = spell.helpingHand; help.power = power; help.progress = progress;
        help.ahead = Flat(View(player).forward);
        running = help;
        help.StartCoroutine(help.Run());
    }

    void OnDestroy() { if (running == this) running = null; }

    IEnumerator Run()
    {
        var floor = TableManager.Loader != null ? TableManager.Loader.Dungeon : null;
        var hand = DungeonMasterReach.Hand;
        if (settings.lines != null && settings.lines.Length > 0) DungeonMaster.Say(settings.lines[UnityEngine.Random.Range(0, settings.lines.Length)]);
        DungeonMasterReach.BeginReach();
        var spot = caster.transform.position;
        bool appear = settings.reach == HelpReach.Appear && Appear();
        bool near = appear || settings.reach == HelpReach.WalkOrPotion && DungeonMasterReach.InHandReach(spot, settings.handReach, settings.edgeGap, settings.maxWalk) && Roomy(spot);
        if (appear) yield return new WaitForSeconds(.35f);
        else if (near)
        {
            yield return DungeonMasterReach.Approach(spot, settings.walkSpeed);
            yield return DungeonMasterReach.StepIn(spot, settings.edgeGap);
        }
        else yield return DungeonMasterReach.TurnTo(spot);
        // Only the roof over the player's room or corridor comes off.
        yield return DungeonMasterReach.Lid(floor, true, new[] { caster.transform.position }, settings.lid);
        if (caster != null && caster.IsAlive && hand != null)
        {
            if (near) yield return Hold(hand);
            else yield return Toss(hand);
        }
        yield return new WaitForSeconds(.4f);
        // The roof only comes down once he stands up straight, and where he stood in the table he is back in his place
        // first: it lands on the room, never on him.
        for (float t = 0; hand != null && !hand.Upright && t < 3f; t += Time.deltaTime) yield return null;
        if (appear) DungeonMasterReach.Vanish();
        yield return DungeonMasterReach.Lid(floor, false, null, settings.lid);
        if (!appear) yield return DungeonMasterReach.StepBack(spot);
        yield return DungeonMasterReach.Leave();
        Destroy(gameObject);
    }

    // He appears in front of the player, along where they look, facing them.
    bool Appear()
    {
        var at = caster.transform.position + ahead * settings.appearDistance;
        if (!DungeonMasterReach.Appear(at, caster.transform.position)) return false;
        DungeonMasterReach.Shake(.3f);
        return true;
    }

    // Open floor round the spot, enough for his hand to come down into the room without going through its walls.
    bool Roomy(Vector3 spot) => !NavMesh.FindClosestEdge(spot, out var edge, NavMesh.AllAreas) || edge.distance >= settings.handRoom;

    // Where his palm is held: over and a little ahead of the player, where they look.
    Vector3 PalmSpot()
    {
        return caster.transform.position + ahead * settings.palmAhead + Vector3.up * settings.palmHeight;
    }

    // ── His hand over the player ────────────────────────────────────

    IEnumerator Hold(DungeonMasterHand hand)
    {
        // Fingers pointing away from him, over the player's head.
        var along = Flat(caster.transform.position - hand.transform.position);
        SpellVisual pour = null, glow = null; Light down = null; GameObject shaft = null;
        bool holding = false;
        var move = hand.Bless(PalmSpot(), along, settings.reachSeconds, settings.holdSeconds, settings.color, () =>
        {
            holding = true;
            if (settings.pour != null) pour = Instantiate(settings.pour, caster.transform.position, Quaternion.identity);
            if (settings.beam != null)
            {
                shaft = Instantiate(settings.beam);
                foreach (var l in shaft.GetComponentsInChildren<Light>()) { l.intensity *= settings.beamLight; l.color = settings.color; }
                var tint = new MaterialPropertyBlock();
                tint.SetColor("_BaseColor", settings.beamTint); tint.SetColor("_Color", settings.beamTint);
                foreach (var r in shaft.GetComponentsInChildren<Renderer>()) if (r is not ParticleSystemRenderer) r.SetPropertyBlock(tint);
                Aim(shaft);
            }
            if (settings.palmGlow != null) { glow = Instantiate(settings.palmGlow, PalmSpot(), Quaternion.identity); glow.transform.localScale *= settings.palmGlowScale; }
            if (settings.palmLight > 0f)
            {
                down = new GameObject("Helping hand light").AddComponent<Light>();
                down.type = LightType.Spot; down.spotAngle = 50f; down.innerSpotAngle = 20f; down.range = settings.palmHeight * 2.5f;
                down.intensity = 0f; down.color = settings.color; down.shadows = LightShadows.None;
            }
            if (spell.impactSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(spell.impactSound, spell.impactVolume);
        }, () => caster != null ? PalmSpot() : Vector3.zero, () =>
        {
            // His hand goes: the rest of the heal at once, and the light goes with it.
            holding = false;
            Heal(power - healed);
            if (pour != null) pour.Stop();
            if (glow != null) glow.Stop();
            if (shaft != null) StartCoroutine(Fade(shaft));
            if (down != null) down.gameObject.AddComponent<LightFade>().seconds = .3f;
            pour = glow = null; shaft = null; down = null;
        });
        float held = 0f;
        while (move.MoveNext())
        {
            if (holding && caster != null && caster.IsAlive)
            {
                held += Time.deltaTime;
                Heal(power * Time.deltaTime / settings.holdSeconds);
                if (pour != null) pour.transform.position = caster.transform.position;
                if (glow != null) glow.transform.position = PalmSpot() - Vector3.up * .5f;
                if (shaft != null) Aim(shaft);
                if (down != null)
                {
                    var palm = PalmSpot();
                    down.transform.SetPositionAndRotation(palm, Quaternion.LookRotation(caster.transform.position - palm));
                    // Up as he arrives, down as his hand goes.
                    float fade = Mathf.Clamp01(held / .3f) * Mathf.Clamp01((settings.holdSeconds - held) / .4f);
                    down.intensity = settings.palmLight * fade;
                }
            }
            yield return move.Current;
        }
        // Whatever the frames left over.
        Heal(power - healed);
        if (pour != null) pour.Stop();
        if (glow != null) glow.Stop();
        if (shaft != null) StartCoroutine(Fade(shaft));
        if (down != null) down.gameObject.AddComponent<LightFade>().seconds = .3f;
    }

    // The beam from his palm onto the player's feet.
    void Aim(GameObject shaft)
    {
        var feet = caster.transform.position;
        var palm = PalmSpot() - Vector3.up * .5f;
        // Narrowed to a column (the moonbeam's foot is a wide pool), then turned and sized so its source is his palm.
        var size = new Vector3(settings.beamWidth, 1f, settings.beamWidth);
        var source = Vector3.Scale(settings.beamSource, size);
        float scale = Vector3.Distance(feet, palm) / Mathf.Max(.1f, source.magnitude);
        shaft.transform.SetPositionAndRotation(feet, Quaternion.FromToRotation(source.normalized, (palm - feet).normalized));
        shaft.transform.localScale = size * scale;
    }

    // The beam narrows to nothing and is gone.
    static IEnumerator Fade(GameObject shaft)
    {
        var lights = shaft.GetComponentsInChildren<Light>();
        var strength = System.Array.ConvertAll(lights, l => l.intensity);
        var size = shaft.transform.localScale;
        foreach (var ps in shaft.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        for (float t = 0; t < .35f && shaft != null; t += Time.deltaTime)
        {
            float u = 1f - t / .35f;
            shaft.transform.localScale = new Vector3(size.x * u, size.y, size.z * u);
            for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].intensity = strength[i] * u;
            yield return null;
        }
        if (shaft != null) Destroy(shaft);
    }

    // ── The potion (out of his reach) ───────────────────────────────

    IEnumerator Toss(DungeonMasterHand hand)
    {
        var spot = caster.transform.position;
        var over = hand.HoldPoint(spot);
        var potion = settings.potion != null ? Instantiate(settings.potion, over, Quaternion.identity) : null;
        if (potion != null)
        {
            potion.name = "Helping hand potion (held)";
            foreach (var c in potion.GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var r in potion.GetComponentsInChildren<Rigidbody>()) r.isKinematic = true;
            foreach (var b in potion.GetComponentsInChildren<MonoBehaviour>()) b.enabled = false;
            potion.transform.localScale *= settings.potionScale;
            if (settings.potionLight > 0f)
            {
                var glow = new GameObject("Potion light").AddComponent<Light>();
                glow.transform.SetParent(potion.transform, false);
                glow.type = LightType.Point; glow.range = 5f; glow.intensity = settings.potionLight; glow.color = settings.color; glow.shadows = LightShadows.None;
            }
        }
        bool flying = false;
        yield return hand.Flick(over, Flat(spot - over), settings.reachSeconds * .8f, .2f, potion != null ? potion.transform : null, () =>
        {
            if (potion != null) { flying = true; StartCoroutine(Fly(potion.transform, () => flying = false)); }
            else StartCoroutine(Mend(power, settings.burstHealSeconds));
        });
        for (float t = 0; flying && t < 6f; t += Time.deltaTime) yield return null;
    }

    // Up in an arc over the walls and down at the player's feet (steered onto where they are now), where it bursts.
    IEnumerator Fly(Transform potion, Action done)
    {
        potion.name = "Helping hand potion";   // in the air (a watcher follows it from here)
        Vector3 Landing() => caster.transform.position + Flat(View(caster).forward) * settings.landAhead + Vector3.up * .3f;
        var from = potion.position;
        var to = Landing();
        float g = settings.potionGravity;
        float peak = Mathf.Max(from.y, to.y) + settings.potionArc;
        float up = Mathf.Sqrt(2f * g * (peak - from.y)), t = up / g + Mathf.Sqrt(2f * (peak - to.y) / g);
        var v = (to - from) / t; v.y = up;
        var spinAxis = Vector3.Cross(Vector3.up, Flat(to - from));
        for (float u = 0; u < t && potion != null && caster != null; u += Time.deltaTime)
        {
            float left = t - u;
            if (left > Time.deltaTime) { var now = Landing() - potion.position; v.x = now.x / left; v.z = now.z / left; }
            v.y -= g * Time.deltaTime;
            potion.position += v * Time.deltaTime;
            potion.rotation = Quaternion.AngleAxis(500f * Time.deltaTime, spinAxis) * potion.rotation;
            yield return null;
        }
        var at = potion != null ? potion.position : Landing();
        if (potion != null) Destroy(potion.gameObject);
        if (settings.smashSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(settings.smashSound, at, settings.smashVolume);
        if (settings.burst != null) Instantiate(settings.burst, at, Quaternion.identity);
        if (settings.pour != null && caster != null)
        {
            var pour = Instantiate(settings.pour, caster.transform.position, Quaternion.identity);
            StartCoroutine(Keep(pour, settings.burstHealSeconds + .4f));
        }
        if (spell.impactSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(spell.impactSound, spell.impactVolume);
        yield return Mend(power, settings.burstHealSeconds);
        done();
    }

    // The pour kept at the player's feet for a while, then let die away.
    IEnumerator Keep(SpellVisual pour, float seconds)
    {
        for (float t = 0; t < seconds && pour != null && caster != null; t += Time.deltaTime) { pour.transform.position = caster.transform.position; yield return null; }
        if (pour != null) pour.Stop();
    }

    // The amount, evenly over seconds.
    IEnumerator Mend(float amount, float seconds)
    {
        float given = 0f;
        for (float t = 0; t < seconds && caster != null && caster.IsAlive; t += Time.deltaTime)
        {
            float step = amount * Time.deltaTime / seconds;
            Heal(step); given += step;
            yield return null;
        }
        Heal(amount - given);
    }

    void Heal(float amount)
    {
        if (caster == null || !caster.IsAlive || amount <= 0f) return;
        float before = caster.Stats.CurrentHealth;
        caster.Stats.Heal(amount);
        healed += amount;
        progress?.PractiseHeal(caster.Stats.CurrentHealth - before);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > .0001f ? v.normalized : Vector3.forward; }
}
