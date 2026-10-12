using System.Collections.Generic;
using UnityEngine;

// Heathen's bloodied blade: every cut that lands spatters the held weapon with the victim's blood (its
// CharacterData.gore, not bone dust), on the striking part of the blade (the far two thirds, away from the grip);
// each stain dries darker and shrinks away over CombatManager.bladeBloodFadeSeconds. Uses GoreManager's stain art.
// On the table player; off with CombatManager.bladeBlood.
[RequireComponent(typeof(Character))]
public class BladeBlood : MonoBehaviour
{
    [SerializeField, Tooltip("Stains added per hit (more for heavy hits and criticals).")] Vector2Int perHit = new(2, 4);
    [SerializeField, Tooltip("Size of one stain in metres.")] Vector2 size = new(.018f, .045f);

    class Stain { public Transform transform; public MeshRenderer renderer; public float born, scale; public Color color; }

    Character character;
    Equipment equipment;
    GameObject stainedWeapon;
    readonly List<Stain> stains = new();
    readonly List<(Vector3 point, Vector3 normal)> surface = new();
    MaterialPropertyBlock block;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Awake() { character = GetComponent<Character>(); equipment = GetComponent<Equipment>(); }
    void OnEnable() => character.HitLanded += OnHitLanded;
    void OnDisable() => character.HitLanded -= OnHitLanded;

    void OnHitLanded(DamageInfo info)
    {
        if (!CombatManager.HasInstance || !CombatManager.Instance.bladeBlood || !GoreManager.HasInstance || GoreManager.Instance.StainMaterial == null) return;
        if (info.Blocked || info.Magic || info.Amount <= 0f || info.Target == null || info.Target.data == null) return;
        var gore = info.Target.data.gore;
        if (gore == null || !gore.Stains) return;
        var weapon = equipment != null ? equipment.HeldObject(EquipmentSlot.RightHand) : null;
        if (weapon == null) return;
        if (weapon != stainedWeapon) { stains.Clear(); surface.Clear(); stainedWeapon = weapon; FindBlade(weapon); }
        if (surface.Count == 0) return;
        int count = Random.Range(perHit.x, perHit.y + 1) + (info.Heavy || info.Critical ? 2 : 0);
        for (int i = 0; i < count; i++) Add(weapon.transform, gore.color);
    }

    // The striking part of the weapon: its mesh's vertices along the long axis, from a third of the way to the tip
    // (the tip is the end farther from where the hand holds it, the object's origin).
    void FindBlade(GameObject weapon)
    {
        MeshFilter best = null; float bestSize = 0f;
        foreach (var mf in weapon.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null && mf.GetComponent<MeshRenderer>() != null && mf.sharedMesh.bounds.size.sqrMagnitude > bestSize) { best = mf; bestSize = mf.sharedMesh.bounds.size.sqrMagnitude; }
        if (best == null) return;
        var mesh = best.sharedMesh; var b = mesh.bounds;
        int axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : b.size.y >= b.size.z ? 1 : 2;
        Vector3 grip = best.transform.InverseTransformPoint(weapon.transform.position);
        bool tipAtMax = Mathf.Abs(b.max[axis] - grip[axis]) > Mathf.Abs(b.min[axis] - grip[axis]);
        float T(Vector3 p) { float t = (p[axis] - b.min[axis]) / Mathf.Max(1e-5f, b.size[axis]); return tipAtMax ? t : 1f - t; }
        if (mesh.isReadable)
        {
            var vertices = mesh.vertices; var normals = mesh.normals;
            for (int i = 0; i < vertices.Length; i += 1 + vertices.Length / 600)
                if (T(vertices[i]) > .35f && normals.Length == vertices.Length)
                    surface.Add((best.transform.TransformPoint(vertices[i]) - weapon.transform.position, best.transform.TransformDirection(normals[i])));
            // Stored in the weapon's own space so they follow the swing.
            for (int i = 0; i < surface.Count; i++)
                surface[i] = (weapon.transform.InverseTransformPoint(weapon.transform.position + surface[i].point), weapon.transform.InverseTransformDirection(surface[i].normal).normalized);
        }
        else
        {
            // Unreadable mesh: points on the two broad faces of its box along the blade.
            int thin = axis == 0 ? (b.size.y < b.size.z ? 1 : 2) : axis == 1 ? (b.size.x < b.size.z ? 0 : 2) : (b.size.x < b.size.y ? 0 : 1);
            for (int i = 0; i < 40; i++)
            {
                var p = b.center; float t = Mathf.Lerp(.35f, .95f, i / 39f);
                p[axis] = tipAtMax ? Mathf.Lerp(b.min[axis], b.max[axis], t) : Mathf.Lerp(b.max[axis], b.min[axis], t);
                var n = Vector3.zero; n[thin] = i % 2 == 0 ? 1f : -1f; p[thin] = b.center[thin] + n[thin] * b.extents[thin];
                surface.Add((weapon.transform.InverseTransformPoint(best.transform.TransformPoint(p)), weapon.transform.InverseTransformDirection(best.transform.TransformDirection(n)).normalized));
            }
        }
    }

    void Add(Transform weapon, Color color)
    {
        Stain stain;
        int max = CombatManager.Instance.bladeBloodMax;
        if (stains.Count >= max) { stain = stains[0]; stains.RemoveAt(0); }
        else
        {
            var go = new GameObject("Blade blood", typeof(MeshFilter), typeof(MeshRenderer)) { layer = weapon.gameObject.layer };
            stain = new Stain { transform = go.transform, renderer = go.GetComponent<MeshRenderer>() };
            stain.renderer.sharedMaterial = GoreManager.Instance.StainMaterial;
            stain.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            stain.renderer.renderingLayerMask = weapon.GetComponentInChildren<Renderer>() is Renderer r ? r.renderingLayerMask : 1u;
        }
        stain.transform.GetComponent<MeshFilter>().sharedMesh = GoreManager.Instance.StainMesh(Random.Range(0, 4));
        var (point, normal) = surface[Random.Range(0, surface.Count)];
        stain.transform.SetParent(weapon, false);
        stain.transform.localPosition = point + normal * .002f / Mathf.Max(1e-4f, weapon.lossyScale.x);
        stain.transform.localRotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        stain.scale = Random.Range(size.x, size.y) / Mathf.Max(1e-4f, weapon.lossyScale.x);
        stain.transform.localScale = Vector3.one * stain.scale;
        stain.color = Color.Lerp(color, color * .8f, Random.value);
        stain.born = Time.time;
        stains.Add(stain);
        Tint(stain, 0f);
    }

    void Tint(Stain stain, float age)
    {
        block ??= new MaterialPropertyBlock();
        // Dries darker, then shrinks away over the last third.
        var c = Color.Lerp(stain.color, stain.color * .45f, Mathf.Clamp01(age * 1.5f)); c.a = 1f;
        block.SetColor(BaseColorId, c);
        stain.renderer.SetPropertyBlock(block);
    }

    void Update()
    {
        if (stains.Count == 0) return;
        if (stainedWeapon == null) { stains.Clear(); return; }
        float fade = CombatManager.HasInstance ? CombatManager.Instance.bladeBloodFadeSeconds : 45f;
        for (int i = stains.Count - 1; i >= 0; i--)
        {
            var s = stains[i];
            if (s.transform == null) { stains.RemoveAt(i); continue; }
            float age = (Time.time - s.born) / fade;
            if (age >= 1f) { Destroy(s.transform.gameObject); stains.RemoveAt(i); continue; }
            s.transform.localScale = Vector3.one * s.scale * Mathf.Clamp01((1f - age) * 3f);
            if (Time.frameCount % 10 == i % 10) Tint(s, age);
        }
    }
}
