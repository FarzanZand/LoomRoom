using UnityEngine;

// A blow to the head can knock the helmet off. The worn helmet (a skinned attachment whose name says
// so) is baked into a loose physics prop that flies off and stays on the floor; the enemy loses its armour bonus.
// On the humanoid enemy base.
[RequireComponent(typeof(Character))]
public class HelmetPop : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] float chance = .3f;
    [Tooltip("A hit counts as on the head within this distance of the head bone (metres).")]
    [SerializeField, Min(0f)] float headRadius = .3f;
    [Tooltip("Armour the enemy loses with its helmet.")]
    [SerializeField, Min(0f)] float armorLost = 1f;
    [SerializeField] string[] helmetNames = { "Helmet", "Helm", "Hood", "Hat", "Mask" };

    Character character;
    bool popped;

    void Awake() => character = GetComponent<Character>();
    void OnEnable() => character.Damaged += OnDamaged;
    void OnDisable() => character.Damaged -= OnDamaged;

    void OnDamaged(DamageInfo info)
    {
        if (popped || info.Blocked || info.Amount <= 0f || !character.IsAlive || Random.value > chance) return;
        var anim = GetComponentInChildren<Animator>();
        var head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null || Vector3.Distance(info.HitPoint, head.position) > headRadius * Mathf.Max(.01f, transform.lossyScale.y)) return;
        // A helmet is either a mesh hung on the head bone or a skinned attachment; its name says which it is.
        Renderer helmet = null;
        foreach (var r in GetComponentsInChildren<Renderer>())
            if ((r is MeshRenderer || r is SkinnedMeshRenderer) && r.enabled && r.gameObject.activeInHierarchy &&
                System.Array.Exists(helmetNames, n => r.name.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0)) { helmet = r; break; }
        if (helmet == null) return;
        popped = true;
        if (helmet is SkinnedMeshRenderer skinned) Knock(skinned, info);
        else Loosen(helmet.transform, info);
        if (armorLost > 0f && character.Stats != null)
        {
            float lose = Mathf.Min(armorLost, Mathf.Max(0f, character.Stats.GetFinal(StatType.Armor)));
            if (lose > 0f) character.Stats.AddModifier(new StatModifier(StatType.Armor, -lose, ModifierType.Flat, this));
        }
    }

    // A rigid helmet on the head bone comes off whole.
    void Loosen(Transform helmet, DamageInfo info)
    {
        helmet.SetParent(transform.parent, true);
        helmet.name += " (knocked off)";
        var filter = helmet.GetComponent<MeshFilter>();
        var box = helmet.gameObject.AddComponent<BoxCollider>();
        if (filter != null && filter.sharedMesh != null) { box.center = filter.sharedMesh.bounds.center; box.size = Vector3.Max(filter.sharedMesh.bounds.size, Vector3.one * .05f); }
        Throw(helmet.gameObject.AddComponent<Rigidbody>(), box, info);
    }

    void Throw(Rigidbody rb, Collider box, DamageInfo info)
    {
        rb.mass = .5f;
        Vector3 away = info.Direction.sqrMagnitude > .001f ? info.Direction : -transform.forward;
        rb.linearVelocity = away * 2.5f + info.SwingDirection * 1.5f + Vector3.up * 3.2f;
        rb.angularVelocity = Random.insideUnitSphere * 14f;
        foreach (var p in FindObjectsByType<Player>())
            foreach (var c in p.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(box, c);
    }

    void Knock(SkinnedMeshRenderer helmet, DamageInfo info)
    {
        var mesh = new Mesh();
        helmet.BakeMesh(mesh, true);
        var loose = new GameObject(helmet.name + " (knocked off)");
        loose.transform.SetParent(transform.parent, true);
        loose.transform.SetPositionAndRotation(helmet.transform.position, helmet.transform.rotation);
        loose.AddComponent<MeshFilter>().sharedMesh = mesh;
        loose.AddComponent<OwnedMesh>().mesh = mesh;
        loose.AddComponent<MeshRenderer>().sharedMaterials = helmet.sharedMaterials;
        var box = loose.AddComponent<BoxCollider>();
        box.center = mesh.bounds.center; box.size = mesh.bounds.size;
        helmet.enabled = false;
        // It never hits the player: loose props shouldn't trip anyone up.
        Throw(loose.AddComponent<Rigidbody>(), box, info);
    }
}
