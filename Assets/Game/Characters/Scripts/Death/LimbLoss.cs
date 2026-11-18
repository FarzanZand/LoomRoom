using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// A limb that comes off (the Black Knight in the Arena: his left arm, on the 9th blow the player lands or the blow that leaves him at 70% health, whichever comes first). The skinned
// mesh is baked in the pose on screen and the triangles that follow the limb's bones become one physics piece thrown
// off to its side; on the body those bones are shrunk to nothing at the joint, so the limb is gone from the mesh and a
// stump is left. Whatever the limb held (a shield in the hand) falls with it. Blood spurts from the stump and he says
// his line, a bark, so the fight goes on. Works on any humanoid; after it the death (DeathBurst or the ragdoll) takes
// the body as it is.
// The blood is comedy level (the user, 2026-11-15: "make it just be so much more, I mean, comedy levels. Also make the
// head gush upwards when that gets chopped up too"): after the first spurt the stump jets in heartbeats for a quarter
// of a minute, and when his head comes off the neck gushes straight up. His head is his helmet (the body has no head
// under it): it is knocked off on a counted blow of its own (Head Hits; the user, the same day: "arm should fall of
// after 6 hits, head should fall off after 12", after a blow to the head took it off on the first hit by HelmetPop's
// chance, which is 0 on him now; since 2026-11-18 the arm comes off on the 9th blow or at 70% health, the head on the
// 18th or at 30%, whichever comes first) and he fights on without it. All of it stops the moment he dies (the user: "he
// doesn't need to keep bleeding after you killed him"): the drops in the air fall, and no more come.
[RequireComponent(typeof(Character)), DefaultExecutionOrder(9000)]
public class LimbLoss : MonoBehaviour
{
    [Tooltip("The limb that comes off, from this bone down (its children with it).")]
    public HumanBodyBones limb = HumanBodyBones.LeftUpperArm;
    [Min(1), Tooltip("Blows the player lands (doing damage, not blocked) before it comes off, unless his health comes down to Lose At Health first.")]
    public int hitsToLose = 9;
    [Range(0f, 1f), Tooltip("It also comes off on the blow that leaves him at or under this share of his health, whichever comes first with Hits To Lose. 0: by count only.")]
    public float loseAtHealth = .7f;
    [Tooltip("His words (Dialogue/Conversations): its Limb Lost Line is said as the limb comes off, as a bark (the fight goes on), where its placement says (in the dialogue box with his portrait, Standard or a corner; Simple: a subtitle), under its name. Empty: nothing is said.")]
    public BossDialogueData words;
    [Min(0), Tooltip("Seconds after the cut before the line, so the arm is seen to fall first.")]
    public float lineDelay = .5f;
    [Min(0), Tooltip("Seconds he stands stunned by it (his swing dropped). 0: he fights on at once.")]
    public float stunSeconds = 1.1f;

    [Header("The piece")]
    [Min(0), Tooltip("Metres a second the limb is thrown out to its side.")] public float throwSpeed = 2.4f;
    [Min(0)] public float throwUp = 2f;
    [Min(0), Tooltip("Random spin, radians a second.")] public float spin = 7f;
    [Min(.01f)] public float pieceMass = 2f;
    [Min(.5f), Tooltip("Seconds before the piece freezes where it lies.")] public float settleSeconds = 5f;

    [Header("Blood")]
    [Tooltip("Played once at the stump as the limb comes off (the Black Knight: Sound Effects Library > Blood Guts Bones > Blood_Squirts_01).")]
    public AudioClip bleedSound;
    [Range(0, 1)] public float bleedVolume = 1f;
    [Min(1f), Tooltip("How many times as much blood leaves a wound in its first moment as after (it settles within half a second).")]
    public float firstGout = 2.5f;
    [Min(0), Tooltip("Seconds the stump goes on jetting. Over its last stretch it weakens to a dribble.")]
    public float gushSeconds = 16f;
    [Min(0), Tooltip("Drops a second at the top of a heartbeat (times the character's gore amount).")] public float gushDrops = 1800f;
    [Min(.1f), Tooltip("Heartbeats a second: each throws the jet out far, and it sinks back before the next.")] public float gushBeats = 1.6f;
    [Min(0), Tooltip("Metres a second the drops leave at the top of a heartbeat (7.5 carries about 3 m from shoulder height).")] public float gushSpeed = 7.5f;
    [Range(-1f, 1f), Tooltip("How far the jet points down (-1) or up (1) as it leaves sideways.")] public float gushTilt = .3f;
    [Range(0f, .5f), Tooltip("How far the jet fans out.")] public float gushSpread = .05f;
    [Min(0), Tooltip("Stains the jet leaves at most, spread over its time (the floor keeps 260 in all).")] public int gushStains = 90;
    [Tooltip("Metres out from the shoulder joint, to its side, and down, where the blood leaves (under the shoulder armour, not through it).")]
    public float sprayOut = .02f, sprayDown = .05f;   // .14 and .1 started it in the air beside him (the user, 2026-11-12)
    [Min(.1f), Tooltip("The drops' size against a hit's blood (smaller and finer: the stump's spurts showed big squares).")]
    public float dropSize = .6f;

    [Header("Head")]
    [Min(0), Tooltip("Blows the player lands before his head comes off (his helmet, knocked off through HelmetPop, whatever part of him the blow lands on), unless his health comes down to Head At Health first. 0: never by count.")]
    public int headHits = 18;
    [Range(0f, 1f), Tooltip("His head also comes off on the blow that leaves him at or under this share of his health, whichever comes first with Head Hits. 0: by count only.")]
    public float headAtHealth = .3f;
    [Tooltip("When his head comes off while he lives (a helmet on the head bone that is his whole head, knocked off on its counted blow), the neck gushes straight up and the head trails blood.")]
    public bool neckGushes = true;
    [Min(0), Tooltip("Seconds the neck gushes when the head is knocked off while he still fights.")] public float neckSeconds = 14f;
    [Min(0), Tooltip("Drops a second (times the character's gore amount).")] public float neckDrops = 2600f;
    // A tight, thick column that stays in view from sword range: at 8.5 m/s and a wide fan (the first try) it left the top
    // of the view and came back as loose drops all over the screen, and nothing read as coming out of the neck.
    [Min(0), Tooltip("Metres a second the drops leave the neck at the top of a heartbeat (6.3 rises about 1.3 m).")] public float neckSpeed = 6.3f;
    [Range(0f, .5f)] public float neckSpread = .1f;
    [Range(0f, 1f), Tooltip("How much of the neck's fountain comes in heartbeats, so the column pumps up and down (0 steady, 1 as the arm's jet).")] public float neckBeat = .6f;
    [Min(0), Tooltip("Stains the neck's fountain leaves at most.")] public int neckStains = 110;
    [Min(0), Tooltip("Seconds the head itself trails blood from its neck hole as it flies and where it lands.")] public float headSeconds = 3f;
    [Min(0)] public float headDrops = 500f;
    [Min(0), Tooltip("Metres a second the head's drops leave it.")] public float headSpeed = 3.6f;

    Character character;
    int hits;
    bool lost;
    readonly List<Transform> shrunk = new();
    public bool Lost => lost;

    // A wound that keeps bleeding: a point and a direction on a bone (or on the loose head). `up` is how much the blood
    // is turned to straight up, whichever way the wound points. `inside` is the collider it lies within (GoreManager.Feed).
    sealed class Wound { public Transform on; public Vector3 point, axis; public float up; public Collider inside; }
    bool dead;   // he has died: every wound stops
    Transform headBone, headPiece;   // the head bone and the helmet hung on it
    Vector3 holePoint, holeAxis;     // the neck hole in the helmet's own space
    bool headOff;
    bool headKnocked;   // its blow has come and the helmet was knocked off

    void Awake() => character = GetComponent<Character>();
    void OnEnable() { character.Damaged += OnDamaged; character.Died += OnDied; }
    void OnDisable() { character.Damaged -= OnDamaged; character.Died -= OnDied; }

    void Start()
    {
        var anim = character.Animator;
        headBone = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
        if (headBone == null || headBone.parent == null) return;
        foreach (var mr in headBone.GetComponentsInChildren<MeshRenderer>())
            if (mr.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null) { headPiece = mr.transform; break; }
        if (headPiece == null) return;
        holePoint = headPiece.InverseTransformPoint(headBone.position);
        holeAxis = headPiece.InverseTransformDirection((headBone.parent.position - headBone.position).normalized);
    }

    void OnDamaged(DamageInfo info)
    {
        if (info.Source is not Player || info.Blocked || info.Amount <= 0f || !character.IsAlive) return;
        hits++;
        float health = HealthShare();
        if (!lost && (hits >= hitsToLose || health <= loseAtHealth)) StartCoroutine(Cut(info));
        bool headDue = (headHits > 0 && hits >= headHits) || health <= headAtHealth;
        if (headDue && !headKnocked && !headOff && TryGetComponent<HelmetPop>(out var helmet) && helmet.Pop(info))
        {
            headKnocked = true;
            if (stunSeconds > 0f && TryGetComponent<EnemyBrain>(out var brain)) brain.Stun(stunSeconds);
        }
    }

    // The share of his health left after the blow (TakeDamage lowers it before Damaged is raised). 1 with no stats.
    float HealthShare()
    {
        var stats = character.Stats;
        return stats != null && stats.MaxHealth > 0f ? stats.CurrentHealth / stats.MaxHealth : 1f;
    }

    // Kept shrunk after the animation and every bone tweak of the frame.
    void LateUpdate()
    {
        foreach (var b in shrunk) if (b != null) b.localScale = Vector3.one * .0001f;
        CheckHead();
    }

    // ── The head ──

    // The helmet has left the head bone, knocked off while he lives: the neck starts. (Thrown with the rest of him as he
    // falls apart, nothing bleeds.)
    void CheckHead()
    {
        if (headOff || !neckGushes || headPiece == null || headBone == null || headPiece.IsChildOf(headBone)) return;
        headOff = true;
        if (dead || !character.IsAlive) return;
        var gore = character.data != null ? character.data.gore : null;
        if (gore == null || !GoreManager.HasInstance) return;
        var neck = headBone.parent;
        Vector3 at = headBone.position, axis = (headBone.position - neck.position).normalized;
        var neckWound = new Wound { on = neck, point = neck.InverseTransformPoint(at), axis = neck.InverseTransformDirection(axis), up = 4f, inside = GetComponent<Collider>() };
        if (bleedSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(bleedSound, at, bleedVolume);
        StartCoroutine(Gush(neckWound, neckSeconds, neckDrops, neckSpeed, neckSpread, neckStains, neckBeat, 0f, 0f));
        var head = new Wound { on = headPiece, point = holePoint, axis = holeAxis, up = 1.3f, inside = LooseCollider(headPiece) };
        StartCoroutine(Gush(head, headSeconds, headDrops, headSpeed, .16f, 24, 0f, 0f, 0f));
    }

    void OnDied() => dead = true;

    // The collider a loose piece lands on (the box it was given as it came off; a helmet's own are switched off).
    static Collider LooseCollider(Transform piece)
    {
        foreach (var c in piece.GetComponents<Collider>()) if (c.enabled && !c.isTrigger) return c;
        return null;
    }

    // ── The arm ──

    IEnumerator Cut(DamageInfo info)
    {
        lost = true;
        var anim = character.Animator;
        var root = anim != null && anim.isHuman ? anim.GetBoneTransform(limb) : null;
        if (root == null) yield break;
        // Wait for the end of the frame: the pose the blow landed in, after every bone tweak.
        yield return new WaitForEndOfFrame();
        if (this == null || !character.IsAlive) yield break;

        var bones = new HashSet<Transform>(root.GetComponentsInChildren<Transform>(true));
        Vector3 stump = root.position;
        Vector3 side = (root.position - transform.position); side.y = 0f;
        side = side.sqrMagnitude > .001f ? side.normalized : -transform.right;
        var pieces = new List<Rigidbody>();

        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
            if (smr.enabled && smr.sharedMesh != null && !EliteEnemy.IsGlow(smr) && Piece(smr, bones) is Rigidbody rb) pieces.Add(rb);
        // What the limb holds comes off whole (the shield), not its skinned parts.
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (!mr.enabled || !mr.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
            var t = mr.transform;
            t.SetParent(transform.parent, true);
            foreach (var c in t.GetComponentsInChildren<Collider>()) c.enabled = false;
            var box = t.gameObject.AddComponent<BoxCollider>();
            var b = filter.sharedMesh.bounds;
            box.center = b.center; box.size = Vector3.Max(b.size, Vector3.one * .03f);
            pieces.Add(Throw(t.gameObject.AddComponent<Rigidbody>(), side));
        }
        shrunk.Add(root);
        root.localScale = Vector3.one * .0001f;
        DeathPhysics.IgnorePlayers(pieces);
        StartCoroutine(DeathPhysics.Settle(pieces, settleSeconds));
        if (stunSeconds > 0f && TryGetComponent<EnemyBrain>(out var brain)) brain.Stun(stunSeconds);
        var gore = character.data != null ? character.data.gore : null;
        if (gore != null && GoreManager.HasInstance)
        {
            // The jet: on the bone the limb hung from, so it turns and leans with him.
            var on = root.parent != null ? root.parent : transform;
            var armWound = new Wound { on = on, point = on.InverseTransformPoint(stump), axis = on.InverseTransformDirection(side), up = gushTilt, inside = GetComponent<Collider>() };
            StartCoroutine(Gush(armWound, gushSeconds, gushDrops, gushSpeed, gushSpread, gushStains, 1f, sprayOut, sprayDown));
        }
        if (bleedSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(bleedSound, stump, bleedVolume);

        if (lineDelay > 0f) yield return new WaitForSeconds(lineDelay);
        if (words != null && !string.IsNullOrWhiteSpace(words.limbLostLine) && character.IsAlive && PortraitDialogueUI.Current != null)
            PortraitDialogueUI.Current.Bark(words.limbLostLine, character, !string.IsNullOrWhiteSpace(words.bossName) ? words.bossName : character.TheNameCap, 1.6f, words.placement, true);
    }

    // A wound's blood for some seconds: one stream of its own (GoreManager.Stream), fed every frame from where the wound
    // is now, the way it points now. `beat` is how much of it comes in heartbeats (1: a jet thrown far on each beat that
    // sinks back between them; 0: steady). It starts `outward` metres along the wound's direction and `down` under it
    // (the arm: just under the shoulder armour, not through it). Over its last stretch it weakens to a dribble.
    IEnumerator Gush(Wound w, float seconds, float perSecond, float speed, float spread, int stains, float beat, float outward, float down)
    {
        var gore = character.data != null ? character.data.gore : null;
        if (gore == null || !GoreManager.HasInstance || seconds <= 0f || perSecond <= 0f || speed <= 0f) yield break;
        var manager = GoreManager.Instance;
        perSecond *= gore.amount;
        // Long enough to go up, come down and reach the floor from head height.
        float life = Mathf.Clamp(2f * speed / manager.DropGravity + .7f, .8f, 3f);
        float average = Mathf.Lerp(1f, .5f, beat);
        var stream = manager.Stream(gore, Mathf.CeilToInt(perSecond * life * 1.15f * Mathf.Max(1f, firstGout * .6f)) + 60, stains, perSecond * seconds * average);
        if (stream == null) yield break;
        float fadeSeconds = Mathf.Min(3.5f, seconds * .35f), started = Time.time, until = started + seconds, owed = 0f;
        while (!dead && Time.time < until && w.on != null && stream != null && manager != null)
        {
            float dt = Time.deltaTime, t = Time.time - started;
            // 1 at each heartbeat, sinking after it.
            float pulse = Mathf.Exp(-Mathf.Repeat(t * gushBeats, 1f) * 3.2f);
            float strength = Mathf.Lerp(1f, .22f + .78f * pulse, beat) * Mathf.Lerp(.15f, 1f, Mathf.Clamp01((until - Time.time) / Mathf.Max(.01f, fadeSeconds)));
            owed += perSecond * strength * (1f + (firstGout - 1f) * Mathf.Exp(-t * 6f)) * dt;
            if (owed >= 1f)
            {
                Vector3 axis = w.on.TransformDirection(w.axis).normalized;
                Vector3 from = w.on.TransformPoint(w.point) + axis * outward + Vector3.down * down;
                // A slow sway, so a fountain is never a ruled line.
                Vector3 aim = (axis + Vector3.up * w.up + new Vector3(Mathf.Sin(t * 2.3f), 0f, Mathf.Cos(t * 1.7f)) * .06f).normalized;
                float reach = speed * (.4f + .6f * strength);
                // A jet's drops leave at about one speed, so it is a line in the air; a steady fountain's spread out,
                // so it fills its whole height and falls back in a ring.
                float slowest = Mathf.Lerp(.5f, .9f, beat);
                for (; owed >= 1f; owed -= 1f)
                {
                    Vector3 velocity = (aim + Random.insideUnitSphere * spread).normalized * (reach * Random.Range(slowest, 1.05f));
                    // Started somewhere along this frame's flight, so a fast stream is a line and not a puff a frame.
                    manager.Feed(stream, gore, from + velocity * (Random.value * dt), velocity, dropSize, life, w.inside);
                }
            }
            yield return null;
        }
        if (stream != null) Destroy(stream.gameObject, life + .5f);
    }

    // The triangles of this renderer that follow the limb's bones, as one piece where they are on screen.
    Rigidbody Piece(SkinnedMeshRenderer smr, HashSet<Transform> bones)
    {
        var source = smr.sharedMesh;
        var weights = source.boneWeights;
        var smrBones = smr.bones;
        if (weights.Length == 0 || smrBones.Length == 0) return null;
        var inLimb = new bool[smrBones.Length];
        bool any = false;
        for (int i = 0; i < smrBones.Length; i++) if (smrBones[i] != null && bones.Contains(smrBones[i])) inLimb[i] = any = true;
        if (!any) return null;
        bool Takes(int v) { int b = weights[v].boneIndex0; return b >= 0 && b < inLimb.Length && inLimb[b]; }

        var baked = new Mesh();
        smr.BakeMesh(baked, false);
        Vector3[] positions = baked.vertices, normals = baked.normals;
        Vector2[] uvs = baked.uv;
        var toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        var remap = new Dictionary<int, int>();
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uv = new List<Vector2>();
        var subs = new List<int[]>(); var mats = new List<Material>();
        var materials = smr.sharedMaterials;
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            var tris = baked.GetTriangles(sub);
            var kept = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                // A triangle goes with the limb when most of its corners follow the limb.
                int n = (Takes(tris[i]) ? 1 : 0) + (Takes(tris[i + 1]) ? 1 : 0) + (Takes(tris[i + 2]) ? 1 : 0);
                if (n < 2) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[i + k];
                    if (!remap.TryGetValue(v, out int m))
                    {
                        m = remap[v] = verts.Count;
                        verts.Add(toWorld.MultiplyPoint3x4(positions[v]));
                        norms.Add(v < normals.Length ? toWorld.MultiplyVector(normals[v]) : Vector3.up);
                        uv.Add(v < uvs.Length ? uvs[v] : Vector2.zero);
                    }
                    kept.Add(m);
                }
            }
            if (kept.Count == 0) continue;
            subs.Add(kept.ToArray());
            mats.Add(sub < materials.Length ? materials[sub] : materials[materials.Length - 1]);
        }
        Destroy(baked);
        if (verts.Count == 0) return null;
        Vector3 centre = Vector3.zero;
        foreach (var v in verts) centre += v;
        centre /= verts.Count;
        for (int i = 0; i < verts.Count; i++) verts[i] -= centre;

        var mesh = new Mesh { name = source.name + " limb", indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uv);
        mesh.subMeshCount = subs.Count;
        for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s, false);
        mesh.RecalculateBounds();

        var go = new GameObject(name + " - " + limb);
        go.layer = smr.gameObject.layer;
        go.transform.SetParent(transform.parent, false);
        go.transform.position = centre;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<OwnedMesh>().mesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats.ToArray();
        mr.renderingLayerMask = smr.renderingLayerMask;
        mr.shadowCastingMode = smr.shadowCastingMode;
        var box = go.AddComponent<BoxCollider>();
        box.center = mesh.bounds.center; box.size = Vector3.Max(mesh.bounds.size, Vector3.one * .04f);
        var side = centre - transform.position; side.y = 0f;
        return Throw(go.AddComponent<Rigidbody>(), side.sqrMagnitude > .001f ? side.normalized : -transform.right);
    }

    Rigidbody Throw(Rigidbody rb, Vector3 side)
    {
        rb.mass = pieceMass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearVelocity = side * throwSpeed + Vector3.up * throwUp + Random.insideUnitSphere * .4f;
        rb.angularVelocity = Random.insideUnitSphere * spin;
        return rb;
    }
}
