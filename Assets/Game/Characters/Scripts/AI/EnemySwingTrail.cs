using UnityEngine;

// The enemy's swing: a short bright streak follows the weapon's head through the blow, so a hit
// you take reads as a swing you could have seen. On the humanoid enemy base; uses the held weapon
// from EnemyWeaponLoadout and the swing's hit time from EnemyBrain.
[RequireComponent(typeof(EnemyBrain))]
public class EnemySwingTrail : MonoBehaviour
{
    [SerializeField] Material material;
    [SerializeField] Color color = new(1f, .93f, .62f, .85f);
    [SerializeField, Min(.001f)] float width = .05f;
    [SerializeField, Min(.01f)] float trailTime = .09f;
    [Tooltip("Seconds before the blow lands that the streak starts, and after it that it stops.")]
    [SerializeField] Vector2 window = new(.16f, .06f);

    EnemyBrain brain;
    EnemyWeaponLoadout loadout;
    TrailRenderer trail;
    GameObject trailWeapon;

    void Awake()
    {
        brain = GetComponent<EnemyBrain>();
        loadout = GetComponent<EnemyWeaponLoadout>();
    }

    void LateUpdate()
    {
        if (material == null || loadout == null) return;
        var weapon = loadout.Visual;
        if (weapon != trailWeapon) Attach(weapon);
        if (trail == null) return;
        float hitAt = brain.SwingHitAt;
        trail.emitting = hitAt > 0f && Time.time >= hitAt - window.x && Time.time <= hitAt + window.y && brain.Character.IsAlive;
    }

    // The streak rides the weapon's far end: the point of its bounds farthest from the hand.
    void Attach(GameObject weapon)
    {
        trailWeapon = weapon;
        if (trail != null) Destroy(trail.gameObject);
        trail = null;
        if (weapon == null) return;
        var r = weapon.GetComponentInChildren<Renderer>();
        if (r == null) return;
        var head = new GameObject("Swing trail");
        head.transform.SetParent(weapon.transform, false);
        Vector3 hand = weapon.transform.position, far = r.bounds.center + (r.bounds.center - hand).normalized * r.bounds.extents.magnitude * .8f;
        head.transform.position = far;
        trail = head.AddComponent<TrailRenderer>();
        trail.sharedMaterial = material;
        trail.time = trailTime;
        trail.widthMultiplier = width;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        trail.minVertexDistance = .02f;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var tail = color; tail.a = 0f;
        trail.colorGradient = new Gradient
        {
            colorKeys = new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(0f, 1f) },
        };
        trail.emitting = false;
    }
}
