using System.Collections.Generic;
using UnityEngine;

public enum EliteTrait { Tough = 0, Brutal = 1, Swift = 2 }

// An elite: now and then a generated enemy comes out as one (DungeonBiome.eliteChance, rolled in DungeonGenerator.SetUpEnemy).
// It grows as if it were DungeonBiome.eliteFloors deeper (stats and the loot it drops), and has one trait on top: Tough
// (more health), Brutal (harder hits) or Swift (faster swings). Its name says so ("Brutal Crypt Soldier"), a thin outline
// in the trait's colour is drawn round its body, and it is worth twice the experience (RunManager).
public class EliteEnemy : MonoBehaviour
{
    public EliteTrait trait;
    [Tooltip("How many floors deeper it grows than the floor it is on.")]
    public float extraFloors;

    public const float ToughHealth = .6f, BrutalDamage = .35f, SwiftAttackSpeed = .25f, Experience = 2f;
    // About two of the dungeon's texels (128 a metre): it reads as a drawn edge, not a glow.
    const float OutlineWidth = .016f;

    public static string Word(EliteTrait t) => t switch { EliteTrait.Tough => "Tough", EliteTrait.Brutal => "Brutal", _ => "Swift" };
    static Color Glow(EliteTrait t) => t switch { EliteTrait.Tough => new Color(.55f, .95f, .35f), EliteTrait.Brutal => new Color(1f, .28f, .2f), _ => new Color(.45f, .85f, 1f) };

    static readonly Dictionary<Mesh, Mesh> smoothed = new();
    readonly List<GameObject> shells = new();
    Material outline;
    CharacterStats stats;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => smoothed.Clear();

    // Made at spawn, right after the floor's balance is applied (so its health is full at the elite's maximum).
    public static EliteEnemy Make(Character character, EliteTrait trait, float extraFloors)
    {
        if (character == null) return null;
        var elite = character.gameObject.AddComponent<EliteEnemy>();
        elite.trait = trait; elite.extraFloors = extraFloors;
        var stats = character.Stats;
        if (stats != null)
            switch (trait)
            {
                case EliteTrait.Tough: stats.AddModifier(new StatModifier(StatType.MaxHealth, ToughHealth, ModifierType.PercentMultiply, elite)); break;
                case EliteTrait.Brutal: stats.AddModifier(new StatModifier(StatType.AttackDamage, BrutalDamage, ModifierType.PercentMultiply, elite)); break;
                case EliteTrait.Swift: stats.AddModifier(new StatModifier(StatType.AttackSpeed, SwiftAttackSpeed, ModifierType.PercentMultiply, elite)); break;
            }
        character.namePrefix = Word(trait);
        // Not knocked around or into pits, like a boss: it reels harder instead (EnemyMotor.Steadfast).
        if (character.TryGetComponent<EnemyMotor>(out var motor)) motor.Steadfast = true;
        elite.Outline(character, Glow(trait));
        return elite;
    }

    // The tell (a soft light at its chest only lit a coloured pool at its feet): each skinned body part gets a shell
    // beside it on the same bones, its mesh with smoothed normals (so hard edges don't split the line), drawn by
    // Characters/Enemies/Elite/Elite Outline.shader (back faces pushed out, one flat colour, unlit, so no bloom; pushed
    // back in depth, so the edges of plates and bones layered inside the body don't show it; a hit flash's tint doesn't
    // reach its _Color). Gone the moment it dies, before the body is thrown or broken up.
    void Outline(Character character, Color colour)
    {
        var template = Resources.Load<Material>("Elite outline");
        if (template == null) { Debug.LogWarning("[EliteEnemy] Resources/Elite outline material is missing."); return; }
        outline = new Material(template) { name = "Elite outline" };
        outline.SetColor("_Color", colour * .9f);
        outline.SetFloat("_Width", OutlineWidth);
        foreach (var body in character.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!body.enabled || body.sharedMesh == null) continue;
            var mesh = Smoothed(body.sharedMesh);
            if (mesh == null) continue;
            var shell = new GameObject("Elite outline");
            shell.transform.SetParent(body.transform.parent, false);
            shell.transform.SetLocalPositionAndRotation(body.transform.localPosition, body.transform.localRotation);
            shell.transform.localScale = body.transform.localScale;
            shell.layer = body.gameObject.layer;
            var skin = shell.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh; skin.bones = body.bones; skin.rootBone = body.rootBone; skin.localBounds = body.localBounds;
            var mats = new Material[mesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = outline;
            skin.sharedMaterials = mats;
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; skin.receiveShadows = false;
            skin.renderingLayerMask = body.renderingLayerMask; skin.updateWhenOffscreen = body.updateWhenOffscreen;
            shells.Add(shell);
        }
        // A body whose mesh can't be read (not saved readable) gets no shell: said, so a missing tell is noticed.
        if (shells.Count == 0) Debug.LogWarning($"[EliteEnemy] {character.name}: no readable skinned body, so no elite outline.", character);
        stats = character.Stats;
        if (stats != null) stats.Damaged += OnDamaged;   // raised before Died, so the outline goes before the body breaks up
    }

    void OnDamaged(DamageInfo info) { if (stats != null && stats.CurrentHealth <= 0f) RemoveOutline(); }

    // Switched off at once, not only destroyed: Destroy waits for the frame's end, and DeathBurst splits every enabled
    // skinned mesh in that frame's LateUpdate, which turned the shells into flat-coloured bone pieces on the floor.
    void RemoveOutline()
    {
        foreach (var s in shells) if (s != null) { s.SetActive(false); Destroy(s); }
        shells.Clear();
    }

    // A copy of the mesh whose normals are averaged over every vertex at one place; made once per mesh.
    static Mesh Smoothed(Mesh source)
    {
        if (smoothed.TryGetValue(source, out var done)) return done;
        if (!source.isReadable) return smoothed[source] = null;
        var v = source.vertices; var n = source.normals;
        if (n == null || n.Length != v.Length) return smoothed[source] = null;
        var sums = new Dictionary<Vector3Int, Vector3>();
        Vector3Int Key(Vector3 p) => Vector3Int.RoundToInt(p * 1000f);
        for (int i = 0; i < v.Length; i++) { var k = Key(v[i]); sums[k] = (sums.TryGetValue(k, out var s) ? s : Vector3.zero) + n[i]; }
        var smooth = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) { var s = sums[Key(v[i])]; smooth[i] = s.sqrMagnitude > 1e-12f ? s.normalized : n[i]; }
        var mesh = Instantiate(source);
        mesh.name = source.name + " elite outline";
        mesh.normals = smooth;
        return smoothed[source] = mesh;
    }

    void OnDestroy()
    {
        if (stats != null) stats.Damaged -= OnDamaged;
        RemoveOutline();
        if (outline != null) Destroy(outline);
    }
}
