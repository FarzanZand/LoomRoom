using DG.Tweening;
using UnityEngine;

// The set of dice on the big table's border (the symbol puzzle's prize, ProgressionManager.Dice). While the dice are
// carried, using the border puts them down: one after another with the equip chime, a small burst and a short warm
// light, while the Dungeon Master says placedLine. Once down they stay, and Trading is higher while they lie there.
// The dice are plain children placed by hand: move, turn or add them freely.
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

    static ProgressionManager Meta => ProgressionManager.HasInstance ? ProgressionManager.Instance : null;

    public string Prompt => placePrompt;
    public bool CanInteract(Character who) => who is Player p && p.kind == PlayerKind.Room && Meta != null && Meta.DiceCarried;

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        Meta.PlaceDice();
        for (int i = 0; i < dice.Length; i++)
        {
            var die = dice[i]; if (die == null) continue;
            die.SetActive(true);
            var t = die.transform; var rest = t.localPosition; float delay = i * .18f;
            t.localPosition = rest + Vector3.up * 1.5f;
            t.DOLocalMove(rest, .25f).SetEase(Ease.OutBounce).SetDelay(delay).SetLink(die);
            DOVirtual.DelayedCall(delay + .12f, () =>
            {
                if (!AudioManager.HasInstance) return;
                if (knocks.Length > 0 && knocks[Random.Range(0, knocks.Length)] is AudioClip clip) AudioManager.Instance.PlaySFX(clip, t.position, .9f, .08f);
            }).SetLink(die);
        }
        if (AudioManager.HasInstance && !string.IsNullOrEmpty(putKey)) AudioManager.Instance.PlayUI(putKey);
        Burst(transform.position + Vector3.up * 2f);
        if (placedLine != null && !placedLine.IsEmpty) DungeonMaster.Say(placedLine);
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
        foreach (var die in dice) if (die != null) die.SetActive(down);
    }
}
