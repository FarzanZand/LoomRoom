using UnityEngine;

// An enemy that kneels and weeps where it was set down (the Not a witch room's moonlight) and leaves the player alone until
// someone hurts it: it notices nothing (EnemyBrain.Entranced, no close awareness behind it), so a quiet approach and a
// blow from behind is an easy backstab. The first hit wakes it for good: it stands, screams and fights (its own
// melee and spells, EnemyData). It drops a random piece of gear rolled Legendary (the highest rarity) and spoils when it dies.
// On the enemy's prefab, next to its EnemyBrain.
public class WeepingEnemy : MonoBehaviour
{
    [Tooltip("Looped while it weeps (3D, from where it kneels).")] public AudioClip weeping;
    [Range(0f, 1f)] public float weepingVolume = .8f;
    [Min(1f)] public float weepingRange = 18f;
    [Tooltip("Played once when it is hurt and wakes.")] public AudioData wakeScream;
    [Tooltip("Said by the Dungeon Master when it wakes.")]
    public DungeonMaster.Line wakeLine = new() { text = "You shouldn't have done that." };
    [Tooltip("Wakes with its fireball volley (the first volley in its spells) before anything else.")]
    public bool openWithVolley = true;
    [Header("Reward")]
    [Tooltip("The highest gear tier its Legendary drop is picked from (the demo's gear is tiers 1 and 2).")]
    [Range(1, 4)] public int maxTier = 2;
    [Tooltip("The spoils it always drops (empty: the meta progression's biggest kind).")] public ItemData spoils;
    [Min(1)] public int spoilsCount = 1;

    EnemyBrain brain;
    Character character;
    AudioSource source;
    bool awake;

    public bool Weeping => !awake;

    void Start()
    {
        brain = GetComponent<EnemyBrain>();
        character = GetComponent<Character>();
        if (brain == null || character == null) { enabled = false; return; }
        brain.Entranced = true;
        if (brain.Perception != null) brain.Perception.NoCloseAwareness = true;
        Kneel(true);
        if (weeping != null)
        {
            source = gameObject.AddComponent<AudioSource>();
            source.clip = weeping; source.loop = true; source.volume = weepingVolume;
            source.spatialBlend = 1f; source.rolloffMode = AudioRolloffMode.Linear; source.minDistance = 1.5f; source.maxDistance = weepingRange;
            source.Play();
        }
        character.Damaged += Hurt;
        character.Died += Dropped;
    }

    void OnDestroy()
    {
        if (character != null) { character.Damaged -= Hurt; character.Died -= Dropped; }
    }

    // Kneeling in the moonlight, kept there: an entranced brain does nothing, the animator holds the pose.
    void Kneel(bool on)
    {
        var anim = character.Animator;
        if (anim != null && Character.HasParameter(anim, "Kneel", AnimatorControllerParameterType.Bool)) anim.SetBool("Kneel", on);
    }

    void Hurt(DamageInfo info)
    {
        if (awake || !character.IsAlive) return;
        awake = true;
        Kneel(false);
        if (source != null) source.Stop();
        if (wakeScream != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData(wakeScream, transform.position);
        if (wakeLine != null && !wakeLine.IsEmpty) DungeonMaster.Say(wakeLine);
        var at = info.Source != null ? info.Source.transform.position : transform.position;
        brain.Provoke(at);
        // The fight opens with its volley (the first volley spell in its data).
        if (openWithVolley && brain.Data != null && brain.Data.spells != null)
            foreach (var s in brain.Data.spells) if (s != null && s.volley && s.spell != null) { brain.OpenWith(s); break; }
    }

    // Its reward: a random piece of gear, Legendary, and the spoils, thrown out where it fell.
    void Dropped()
    {
        if (source != null) source.Stop();
        var parent = transform.parent;
        var at = transform.position + Vector3.up * .3f;
        var floor = TableManager.Loader != null ? Mathf.Max(1, TableManager.Loader.FloorNumber) : 1;
        var random = new System.Random(System.Environment.TickCount ^ (int)(transform.position.x * 1000f));
        var item = InventoryManager.GivenGear(ItemRarity.Legendary, maxTier, floor, random);
        if (item != null) DungeonPickup.Spawn(item, at + transform.right * .6f, parent);
        var spoilsItem = spoils;
        if (spoilsItem == null && ProgressionManager.HasInstance && ProgressionManager.MetaActive)
        {
            int best = -1;
            foreach (var kind in ProgressionManager.Instance.spoils) if (kind != null && kind.item != null && kind.worth > best) { best = kind.worth; spoilsItem = kind.item; }
        }
        if (spoilsItem != null && (ProgressionManager.MetaActive || spoils != null)) DungeonPickup.Spawn(spoilsItem, at - transform.right * .6f, parent, spoilsCount);
    }
}
