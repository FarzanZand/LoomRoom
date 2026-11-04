using DG.Tweening;
using UnityEngine;

// The set of dice on the big table's border (the symbol puzzle's prize, ProgressionManager.Dice). While the dice are
// carried, using the border puts them down: one after another with the equip chime, a small burst and a short warm
// light, while the Dungeon Master says placedLine, where the border was aimed at (so they land in view), and they lie
// there after a load (flag room.dice.at). Once down they stay, and Trading is higher while they lie there.
// The dice are plain children placed by hand: move, turn or add them freely. Aiming at the border is caught by
// interaction triggers this makes at start along the cap (Reach), each well under half the table's size, so it takes
// the aim from the table's own Play (InteractController hands a small target on a big one the aim).
public class TableDice : MonoBehaviour, IInteractable
{
    [Tooltip("The dice lying on the border, shown once they are placed.")] public GameObject[] dice = { };
    public string placePrompt = "Put the dice on the table";
    [Tooltip("UI Library key played as each die goes down.")] public string putKey = "equipItem";
    [Tooltip("A die set down on wood, played where each one lands.")] public AudioClip[] knocks = { };
    [Tooltip("A short burst over the dice as they go down (Items/Effects/Chest burst), scaled up to the room.")] public GameObject putEffect;
    [Min(.1f), Tooltip("The burst is made for the dungeon (1 unit to the metre); the room is ~22 units to the metre.")] public float effectScale = 12f;
    [Tooltip("A short warm light over the dice as they go down, fading out over Flash Seconds (0 = none).")] public float flashIntensity = 250f;
    public float flashRange = 25f, flashSeconds = .9f;
    public Color flashColor = new(1f, .78f, .45f);
    public DungeonMaster.Line placedLine = new() { text = "Lucky dice. The merchants will give you better prices now.", inPerson = true };
    [Tooltip("The size of each box along the border that takes the aim while the dice are carried (local units; this object sits on the west frame's cap near its south end).")]
    public Vector3 reachSize = new(5f, 4f, 40f);
    [Tooltip("Where the boxes' middles lie along the cap (local z): together they cover the west side's whole length.")]
    public float[] reachAlong = { 3f, 43f, 83f };
    [Tooltip("How far along the cap (local z) the dice may go down from where they were placed by hand: they land where the border is aimed at, kept within these.")]
    public Vector2 spotRange = new(-10f, 96f);

    // Where along the cap they went down, in tenths of a unit from where they were placed by hand.
    public const string SpotFlag = "room.dice.at";
    Vector3[] rest;
    float Along => Meta != null ? Meta.GetFlag(SpotFlag) / 10f : 0f;

    static ProgressionManager Meta => ProgressionManager.HasInstance ? ProgressionManager.Instance : null;

    public string Prompt => placePrompt;
    public bool CanInteract(Character who) => who is Player p && p.kind == PlayerKind.Room && Meta != null && Meta.DiceCarried;

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        float along = AimedAlong(who);
        Meta.SetFlag(SpotFlag, Mathf.RoundToInt(along * 10f));
        Meta.PlaceDice();
        Refresh();
        for (int i = 0; i < dice.Length; i++)
        {
            var die = dice[i]; if (die == null) continue;
            die.SetActive(true);
            var t = die.transform; var landing = t.localPosition; float delay = i * .18f;
            t.localPosition = landing + Vector3.up * 1.5f;
            t.DOLocalMove(landing, .25f).SetEase(Ease.OutBounce).SetDelay(delay).SetLink(die);
            DOVirtual.DelayedCall(delay + .12f, () =>
            {
                if (!AudioManager.HasInstance) return;
                if (knocks.Length > 0 && knocks[Random.Range(0, knocks.Length)] is AudioClip clip) AudioManager.Instance.PlaySFX(clip, t.position, .9f, .08f);
            }).SetLink(die);
        }
        if (AudioManager.HasInstance && !string.IsNullOrEmpty(putKey)) AudioManager.Instance.PlayUI(putKey);
        Burst(transform.TransformPoint(Vector3.forward * along) + Vector3.up * 2f);
        if (placedLine != null && !placedLine.IsEmpty) DungeonMaster.Say(placedLine);
    }

    // Where the look meets the cap's top, along the cap (local z): the dice go down there.
    float AimedAlong(Character who)
    {
        var view = who != null && who.TryGetComponent<InteractController>(out var aim) ? aim.ViewCamera : null;
        if (view == null) return Along;
        var ray = new Ray(view.transform.position, view.transform.forward);
        if (!new Plane(Vector3.up, transform.position).Raycast(ray, out float distance)) return Along;
        return Mathf.Clamp(transform.InverseTransformPoint(ray.GetPoint(distance)).z, spotRange.x, spotRange.y);
    }

    // The burst sized for the room (every particle system scales with its transform), and a warm flash that dies away.
    void Burst(Vector3 at)
    {
        if (putEffect != null)
        {
            var fx = Instantiate(putEffect, at, Quaternion.identity);
            fx.transform.localScale = putEffect.transform.localScale * effectScale;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
            Destroy(fx, 4f);
        }
        if (flashIntensity <= 0f) return;
        var flash = new GameObject("Dice flash").AddComponent<Light>();
        flash.transform.position = at + Vector3.up * 3f;
        flash.type = LightType.Point; flash.range = flashRange; flash.color = flashColor; flash.intensity = flashIntensity; flash.shadows = LightShadows.None;
        flash.DOIntensity(0f, flashSeconds).SetEase(Ease.OutQuad).SetLink(flash.gameObject).OnComplete(() => Destroy(flash.gameObject));
    }

    void Awake()
    {
        rest = new Vector3[dice.Length];
        for (int i = 0; i < dice.Length; i++) if (dice[i] != null) rest[i] = dice[i].transform.localPosition;
        for (int i = 0; i < reachAlong.Length; i++)
        {
            var reach = new GameObject($"Dice reach {i + 1}") { layer = gameObject.layer };
            reach.transform.SetParent(transform, false);
            // Just above the cap, so a look at it never ends in the cap's own collider.
            reach.transform.localPosition = new Vector3(0f, .3f + reachSize.y / 2f, reachAlong[i]);
            var box = reach.AddComponent<BoxCollider>(); box.size = reachSize; box.isTrigger = true;
            reach.AddComponent<InteractableTrigger>();
        }
    }

    void Start()
    {
        if (Meta != null) Meta.FlagChanged += OnFlag;
        Refresh();
    }
    void OnDestroy() { if (Meta != null) Meta.FlagChanged -= OnFlag; }
    void OnFlag(string key, int value) { if (key == ProgressionManager.DiceFlag) Refresh(); }

    public void Refresh()
    {
        bool down = Meta != null && Meta.DicePlaced;
        for (int i = 0; i < dice.Length; i++)
        {
            if (dice[i] == null) continue;
            if (rest != null && i < rest.Length) dice[i].transform.localPosition = rest[i] + Vector3.forward * Along;
            dice[i].SetActive(down);
        }
    }
}
