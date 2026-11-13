using UnityEngine;

// Z's floating over a sleeper's head (the user, 2026-11-13: "When someone is sleeping, add floating text like Z z z z z
// above their head"). EnemyBrain.Sleep puts one under every enemy that falls asleep (a sleeping pack, the sleeping giant:
// SleepZs.Over, from the prefab Characters/Resources/Sleep Zs), and it is gone the moment its sleeper wakes or dies.
// With each breath a row of them pops up from the head outward, small z's first and a big Z at its far end, leaning to
// the eye's left so it reads Z z z z across the screen; the row drifts up slowly and fades from the head end. The letters
// are the game's pixel font as sprites (Tools/sleep_zs.py), drawn unlit in a pale tone under white with a dark outline:
// readable in a dark room without glowing, and hidden by walls like anything else. Sized to the body (the giant's are
// bigger). A sleeper with no room over its head (a bat roosting under the ceiling, hanging head down) hangs its row down
// and to the side under it instead.
public class SleepZs : MonoBehaviour
{
    [Tooltip("The letters: a particle system with the sheet (tile 0 the big Z, tile 1 the small z), emitted by this script.")]
    public ParticleSystem letters;
    [Min(.5f), Tooltip("Seconds from one breath's row of Z's to the next.")] public float breathSeconds = 3f;
    [Tooltip("A breath's letters from the head outward: each one's size as a share of the tile. The last is the big Z, the others small z's.")]
    public float[] sizes = { .5f, .64f, .8f, 1f };
    [Min(.05f), Tooltip("Seconds between a breath's letters popping up, nearest the head first.")] public float letterGap = .26f;
    [Min(0f), Tooltip("Gap between two letters in the row, as a share of the tile.")] public float spacing = .12f;
    [Range(0f, 1.5f), Tooltip("How far the row leans to the side: 0 straight up, 1 as far across as up.")] public float lean = .7f;
    [Min(0f), Tooltip("Metres a second the row drifts along its line, on a body a man's size.")] public float rise = .09f;
    [Min(0f), Tooltip("Metres over the head the row starts, on a body a man's size.")] public float aboveHead = .2f;
    [Min(.01f), Tooltip("A letter tile's size in metres on a body a man's size (24 texels: the big Z is three quarters of it tall). Floating text, so sized to be read from across a room, not to the dungeon's texels: at their size the letters could not be read past two metres.")]
    public float tileSize = .3f;
    [Tooltip("Least and most the body's size scales them (a rat's are smaller, the giant's bigger).")] public Vector2 bodyScale = new(.7f, 2.2f);

    static SleepZs template;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => template = null;

    EnemyBrain brain;
    Transform head;
    Collider body;
    float scale = 1f, next, along;
    int given;
    Vector3 from, line;

    // Z's over this sleeper, until it wakes. Safe to call again while it sleeps.
    public static void Over(EnemyBrain sleeper)
    {
        if (sleeper == null || !Application.isPlaying || sleeper.GetComponentInChildren<SleepZs>() != null) return;
        if (template == null) template = Resources.Load<SleepZs>("Sleep Zs");
        if (template == null) return;
        Instantiate(template, sleeper.transform).brain = sleeper;
    }

    void Start()
    {
        if (brain == null) brain = GetComponentInParent<EnemyBrain>();
        if (brain == null || letters == null) { Destroy(gameObject); return; }
        var character = brain.Character;
        var anim = character != null ? character.Animator : null;
        if (anim != null && anim.isHuman) head = anim.GetBoneTransform(HumanBodyBones.Head);
        if (head == null)
            foreach (var skin in brain.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                foreach (var bone in skin.bones)
                    if (bone != null && bone.name.ToLowerInvariant().Contains("head")) { head = bone; break; }
                if (head != null) break;
            }
        body = brain.GetComponent<Collider>();
        // Sized to the body: by its collider's height as it stands (a capsule's, whichever way the body lies now).
        float height = body is CapsuleCollider capsule ? capsule.height * brain.transform.lossyScale.y : body != null ? body.bounds.size.y : 1.8f;
        scale = Mathf.Clamp(height / 1.8f, bodyScale.x, bodyScale.y);
        // The letters are sized in metres here, whatever the body's own scale.
        var parent = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(1f / Mathf.Max(parent.x, .01f), 1f / Mathf.Max(parent.y, .01f), 1f / Mathf.Max(parent.z, .01f));
        next = Random.Range(.3f, 1.2f);   // a pack doesn't breathe as one
    }

    void Update()
    {
        if (brain == null || !brain.Asleep || brain.Character == null || !brain.Character.IsAlive)
        {
            // Awake: the Z's are gone at once, not left to fade over a body that is already getting up.
            if (letters != null) letters.Clear(true);
            Destroy(gameObject);
            return;
        }
        if ((next -= Time.deltaTime) > 0f) return;
        if (given == 0) BeginRow();
        Emit(given);
        given++;
        if (given >= sizes.Length) { given = 0; next = Mathf.Max(.2f, breathSeconds - letterGap * (sizes.Length - 1)); }
        else next = letterGap;
    }

    // Where this breath's row starts and which way it runs: up from the head and to the eye's left, so the big Z at its
    // far end is the leftmost and it reads Z z z z; down and to the left under a sleeper with no room over its head.
    void BeginRow()
    {
        var eye = Camera.main != null ? Camera.main.transform : null;
        var left = eye != null ? -Vector3.ProjectOnPlane(eye.right, Vector3.up).normalized : -brain.transform.right;
        var up = NoRoomAbove() ? Vector3.down : Vector3.up;
        line = (up + left * lean).normalized;
        from = Head() + up * (aboveHead * scale);
        along = 0f;
    }

    Vector3 Head()
    {
        if (head != null) return head.position;
        if (body != null) return new Vector3(body.bounds.center.x, body.bounds.max.y, body.bounds.center.z);
        return brain.transform.position + Vector3.up * scale;
    }

    // Something solid close over the head that is not the sleeper itself (a roosting bat's ceiling).
    bool NoRoomAbove()
    {
        float need = (aboveHead + tileSize * 2.2f) * scale;
        foreach (var hit in Physics.RaycastAll(Head(), Vector3.up, need, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(brain.transform)) return true;
        return false;
    }

    void Emit(int index)
    {
        var sheet = letters.textureSheetAnimation;
        int tiles = Mathf.Max(1, sheet.numTilesX);
        int tile = index == sizes.Length - 1 ? 0 : Mathf.Min(1, tiles - 1);
        sheet.startFrame = new ParticleSystem.MinMaxCurve((tile + .5f) / tiles);
        float size = tileSize * scale * sizes[index];
        // A glyph fills about three quarters of its tile: the next letter starts that far on, plus the gap.
        if (index > 0) along += (size + tileSize * scale * sizes[index - 1]) * .375f + spacing * tileSize * scale;
        // Letters that came before have drifted on since: this one starts where the row is now.
        var at = from + line * (along + rise * scale * letterGap * index);
        letters.Emit(new ParticleSystem.EmitParams { position = at, velocity = line * (rise * scale), startSize = size, applyShapeToPosition = false }, 1);
    }
}
