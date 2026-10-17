using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Blood. Every hit that lands sprays the victim's own blood (CharacterData.gore) from where the
// blade met it, along the swing; drops that reach a floor, wall or pillar leave a stain that stays for the whole
// floor. Kills spray more, and soft creatures (gore.burstOnDeath) burst into chunks. Skeletons spill bone dust
// and chips, never stains. Lives under Systems; CombatManager's contact flash is separate.
public class GoreManager : Singleton<GoreManager>
{
    [Header("Spray")]
    [Tooltip("Drop particles (GoreDroplets on it reports where drops land). Coloured per creature when spawned.")]
    [SerializeField] ParticleSystem sprayPrefab;
    [Tooltip("Bone chips and dust for skeletons.")]
    [SerializeField] ParticleSystem dustPrefab;
    [SerializeField, Min(0)] int dropsPerHit = 45;
    [SerializeField, Min(0)] int dropsPerKill = 110;
    [Tooltip("Big blobs that burst from the cut for a few frames, per hit.")]
    [SerializeField, Min(0)] int splashBlobs = 7;
    [SerializeField] Vector2 splashSize = new(.12f, .28f);
    [SerializeField, Min(0)] float sprayLifetime = 2.5f;
    [Tooltip("Light hits spray less, criticals and heavy hits more.")]
    [SerializeField, Min(1)] float strongHitMultiplier = 1.6f;

    [Header("Stains")]
    [Tooltip("Pixel-lit, alpha-clipped material with the splat atlas (2 x 2 shapes).")]
    [SerializeField] Material stainMaterial;
    [SerializeField] Vector2 stainSize = new(.12f, .34f);
    [Tooltip("Stains kept per floor; the oldest go first.")]
    [SerializeField, Min(1)] int maxStains = 260;
    [Range(0f, 1f), Tooltip("Share of landing drops that leave a stain.")]
    [SerializeField] float stainChance = .55f;

    [Header("Chunks")]
    [Tooltip("Material for the chunks a bursting body leaves (tinted with its blood).")]
    [SerializeField] Material chunkMaterial;
    [SerializeField, Min(0)] int chunksPerBurst = 7;
    [SerializeField, Min(1)] int maxChunks = 80;

    readonly Queue<GameObject> stains = new();
    readonly Dictionary<ParticleSystem, Vector2> speedRanges = new();
    readonly Dictionary<Rigidbody, Coroutine> settling = new();
    readonly Queue<GameObject> chunks = new();
    [System.NonSerialized] Mesh[] stainMeshes;
    MaterialPropertyBlock block;
    // Stains share a few tinted copies of the stain material (three shades per colour) instead of a property block each:
    // a property block keeps the SRP Batcher from drawing them together (measured: 260 stains cost 500+ SetPass calls).
    readonly Dictionary<Color32, Material> stainShades = new();
    Transform levelRoot;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // ── Hits and kills ────────────────────────────────────────────────

    public void Hit(Character victim, DamageInfo info)
    {
        var gore = victim != null && victim.data != null ? victim.data.gore : null;
        if (gore == null || gore.kind == BloodKind.None || victim is Player || info.Blocked || info.Amount <= 0f) return;
        float strength = info.Critical || info.Backstab || info.Heavy ? strongHitMultiplier : 1f;
        Spray(gore, info.HitPoint != Vector3.zero ? info.HitPoint : Center(victim), SprayDirection(info, victim), Mathf.RoundToInt(dropsPerHit * gore.amount * strength * (gore.kind == BloodKind.BoneDust ? .6f : 1f)), 1f);
    }

    public void Kill(Character victim, DamageInfo last)
    {
        var gore = victim != null && victim.data != null ? victim.data.gore : null;
        if (gore == null || victim is Player) return;
        Vector3 center = Center(victim);
        if (gore.killSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData(gore.killSound, center);
        if (gore.kind == BloodKind.None) return;
        Vector3 dir = SprayDirection(last, victim);
        Spray(gore, center, dir, Mathf.RoundToInt(dropsPerKill * gore.amount * (gore.burstOnDeath ? 1.6f : 1f)), gore.burstOnDeath ? 1.5f : 1.2f);
        if (gore.burstOnDeath) Burst(victim, gore, center, dir);
    }

    // Along the swing, out of the body, a little up: the way sprays leave a cut.
    static Vector3 SprayDirection(DamageInfo info, Character victim)
    {
        Vector3 away = info.Direction.sqrMagnitude > .001f ? info.Direction : -victim.transform.forward;
        Vector3 dir = away + info.SwingDirection * 1.2f + Vector3.up * .45f;
        return dir.sqrMagnitude > .001f ? dir.normalized : Vector3.up;
    }

    static Vector3 Center(Character c)
    {
        var col = c.GetComponent<Collider>();
        return col != null ? col.bounds.center : c.transform.position + Vector3.up;
    }

    void Spray(GoreProfile gore, Vector3 at, Vector3 dir, int count, float speed)
    {
        var prefab = gore.kind == BloodKind.BoneDust ? dustPrefab : sprayPrefab;
        if (prefab == null || count <= 0) return;
        var go = PoolManager.SpawnOrInstantiate(prefab.gameObject, at, Quaternion.LookRotation(dir), sprayLifetime);
        var ps = go.GetComponent<ParticleSystem>();
        var main = ps.main;
        main.startColor = gore.kind == BloodKind.BoneDust ? new ParticleSystem.MinMaxGradient(gore.color, Color.Lerp(gore.color, Color.white, .5f)) : new ParticleSystem.MinMaxGradient(gore.color, gore.color * .7f);
        // Scale the prefab's own speed range (setting the multiplier would replace it).
        if (!speedRanges.TryGetValue(prefab, out var range)) { var m0 = prefab.main.startSpeed; speedRanges[prefab] = range = new Vector2(m0.constantMin, m0.constantMax); }
        main.startSpeed = new ParticleSystem.MinMaxCurve(range.x * speed, range.y * speed);
        if (go.TryGetComponent<GoreDroplets>(out var drops)) drops.Color = gore.Stains ? gore.color : Color.clear;
        ps.Emit(count);
        // The splash: a few big blobs that pop out of the cut and are gone in a few frames.
        if (gore.kind == BloodKind.BoneDust) return;
        var blob = new ParticleSystem.EmitParams { applyShapeToPosition = true };
        int blobs = Mathf.RoundToInt(splashBlobs * gore.amount * speed);
        for (int i = 0; i < blobs; i++)
        {
            blob.startSize = Random.Range(splashSize.x, splashSize.y) * speed;
            blob.startLifetime = Random.Range(.08f, .2f);
            blob.velocity = (dir + Random.insideUnitSphere * .9f) * Random.Range(.6f, 2f);
            blob.startColor = Color.Lerp(gore.color, gore.color * 1.25f, Random.value);
            ps.Emit(blob, 1);
        }
    }

    // ── Stains ────────────────────────────────────────────────────────

    // A drop landed: a stain flat on the surface, in the creature's colour, kept for the floor. On a breakable (`on`) it
    // is that object's child, so it goes when the object breaks.
    public void Stain(Vector3 point, Vector3 normal, Color color, Transform on = null)
    {
        if (stainMaterial == null || color.a <= 0f || Random.value > stainChance) return;
        var root = LevelRoot();
        if (root == null) return;
        if (stainMeshes == null || stainMeshes.Length == 0) stainMeshes = BuildStainMeshes();
        block ??= new MaterialPropertyBlock();
        GameObject stain;
        // Stains that went with a broken barrel are gone from the queue's front: skip them.
        while (stains.Count > 0 && stains.Peek() == null) stains.Dequeue();
        if (stains.Count >= maxStains) stain = stains.Dequeue();
        else
        {
            stain = new GameObject("Blood stain", typeof(MeshFilter), typeof(MeshRenderer));
            var r = stain.GetComponent<MeshRenderer>();
            r.sharedMaterial = stainMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        stain.transform.SetParent(root, false);
        stain.GetComponent<MeshFilter>().sharedMesh = stainMeshes[Random.Range(0, stainMeshes.Length)];
        // Floors get the bigger pools, walls the smaller flecks.
        float floor = Mathf.Clamp01(Vector3.Dot(normal, Vector3.up));
        float size = Mathf.Lerp(stainSize.x, stainSize.y, Random.value * (.4f + .6f * floor));
        stain.transform.SetPositionAndRotation(point + normal * .006f, Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
        stain.transform.localScale = Vector3.one * size;
        if (on != null) stain.transform.SetParent(on, true);
        var renderer = stain.GetComponent<MeshRenderer>();
        renderer.SetPropertyBlock(null);
        renderer.sharedMaterial = StainShade(color, Random.Range(0, 3));
        stains.Enqueue(stain);
    }

    Material StainShade(Color color, int shade)
    {
        Color32 key = Color.Lerp(color, color * .75f, shade / 2f);
        if (!stainShades.TryGetValue(key, out var material) || material == null)
        {
            material = new Material(stainMaterial) { name = stainMaterial.name + " " + key };
            material.SetColor(BaseColorId, (Color)key);
            stainShades[key] = material;
        }
        return material;
    }

    // Stains belong to the floor that is loaded: they go with it.
    Transform LevelRoot()
    {
        if (levelRoot == null)
        {
            var dungeon = FindAnyObjectByType<DungeonGenerator>();
            levelRoot = dungeon != null ? dungeon.transform : null;
            if (levelRoot != null) { stains.Clear(); chunks.Clear(); settling.Clear(); }
        }
        return levelRoot;
    }

    // One quad per cell of the 2 x 2 splat atlas, facing +Z.
    // The stain material and its four splat shapes, for other blood (BladeBlood).
    public Material StainMaterial => stainMaterial;
    public Mesh StainMesh(int index)
    {
        if (stainMeshes == null || stainMeshes.Length == 0) stainMeshes = BuildStainMeshes();
        return stainMeshes[Mathf.Abs(index) % stainMeshes.Length];
    }

    static Mesh[] BuildStainMeshes()
    {
        var meshes = new Mesh[4];
        for (int i = 0; i < 4; i++)
        {
            float u = i % 2 * .5f, v = i / 2 * .5f;
            meshes[i] = new Mesh
            {
                name = "Stain " + i,
                vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(-.5f, .5f), new Vector3(.5f, .5f) },
                uv = new[] { new Vector2(u, v), new Vector2(u + .5f, v), new Vector2(u, v + .5f), new Vector2(u + .5f, v + .5f) },
                normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
                triangles = new[] { 0, 2, 1, 2, 3, 1 },
            };
        }
        return meshes;
    }

    // ── Bursting bodies ───────────────────────────────────────────────

    void Burst(Character victim, GoreProfile gore, Vector3 center, Vector3 dir)
    {
        foreach (var r in victim.GetComponentsInChildren<Renderer>()) if (!(r is ParticleSystemRenderer)) r.enabled = false;
        // A pool where it stood.
        if (Physics.Raycast(center, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            for (int i = 0; i < 3; i++) { float keep = stainChance; stainChance = 1f; Stain(hit.point + Random.insideUnitSphere * .2f, hit.normal, gore.color); stainChance = keep; }
        if (chunkMaterial == null) return;
        var root = LevelRoot();
        if (root == null) return;
        block ??= new MaterialPropertyBlock();
        for (int i = 0; i < chunksPerBurst; i++)
        {
            GameObject chunk;
            if (chunks.Count >= maxChunks) { chunk = chunks.Dequeue(); if (chunk == null) continue; }
            else
            {
                // A soft lump, not a box (cubes read as dice next to the pixel art).
                chunk = new GameObject("Gore chunk");
                chunk.AddComponent<MeshFilter>().sharedMesh = Lump(chunks.Count);
                chunk.AddComponent<MeshRenderer>().sharedMaterial = chunkMaterial;
                chunk.AddComponent<SphereCollider>().radius = .45f;
                chunk.AddComponent<Rigidbody>();
            }
            chunk.transform.SetParent(root, true);
            chunk.transform.position = center + Random.insideUnitSphere * .15f;
            chunk.transform.rotation = Random.rotation;
            chunk.transform.localScale = Vector3.one * Random.Range(.05f, .11f);
            block.SetColor(BaseColorId, Color.Lerp(gore.color, gore.color * .55f, Random.value));
            chunk.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            var rb = chunk.GetComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.mass = .2f;
            rb.linearVelocity = (dir * 2.2f + Random.insideUnitSphere * 1.6f + Vector3.up * 1.8f);
            rb.angularVelocity = Random.insideUnitSphere * 12f;
            chunks.Enqueue(chunk);
            if (settling.TryGetValue(rb, out var old) && old != null) StopCoroutine(old);
            settling[rb] = StartCoroutine(Settle(rb));
        }
    }

    // A few irregular, squashed lumps (an octahedron split once, its corners pushed in and out), shared by all chunks.
    static Mesh[] lumps;
    static Mesh Lump(int index)
    {
        // Made at run time: a mesh from an earlier Play session is gone, so they are made again.
        if (lumps == null || lumps[0] == null)
        {
            lumps = new Mesh[4];
            var rng = new System.Random(17);
            for (int k = 0; k < lumps.Length; k++)
            {
                var corners = new List<Vector3> { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                int[] faces = { 2, 4, 0, 2, 0, 5, 2, 5, 1, 2, 1, 4, 3, 0, 4, 3, 5, 0, 3, 1, 5, 3, 4, 1 };
                var mids = new Dictionary<(int, int), int>();
                int Mid(int a, int b)
                {
                    var key = a < b ? (a, b) : (b, a);
                    if (!mids.TryGetValue(key, out int m)) { m = corners.Count; corners.Add((corners[a] + corners[b]).normalized); mids[key] = m; }
                    return m;
                }
                var split = new List<int>();
                for (int f = 0; f < faces.Length; f += 3)
                {
                    int a = faces[f], b = faces[f + 1], c = faces[f + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    split.AddRange(new[] { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca });
                }
                for (int i = 0; i < corners.Count; i++)
                {
                    var v = corners[i] * (.38f + (float)rng.NextDouble() * .2f); v.y *= .6f; corners[i] = v;
                }
                // Flat facets: each triangle has its own corners.
                var verts = new List<Vector3>(); var tris = new List<int>();
                foreach (int i in split) { tris.Add(verts.Count); verts.Add(corners[i]); }
                var mesh = new Mesh { name = "Gore lump" };
                mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                lumps[k] = mesh;
            }
        }
        return lumps[index % lumps.Length];
    }

    // Chunks freeze where they lie (saves physics time) and stay for the floor.
    IEnumerator Settle(Rigidbody rb)
    {
        yield return new WaitForSeconds(3f);
        if (rb != null) rb.isKinematic = true;
        settling.Remove(rb);
    }
}
