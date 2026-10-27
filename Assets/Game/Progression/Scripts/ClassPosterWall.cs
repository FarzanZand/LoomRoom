using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

// The writing desk by the bed with a poster per class over it and a prop per class on it (prefab World/Art/Class
// posters/Class poster wall). A class there from the start has its poster and prop up; a class unlocked in play brings
// its poster home as a key item, and using the wall puts both up while the Dungeon Master says the class's posterLine
// (ProgressionManager.Posters). Posters and props are plain children placed by hand: move, turn or add them freely (a
// new one goes in the list with its class). Built by Tools/class_posters.eval.cs and Tools/class_desk_props.eval.cs.
// With no poster to put up, using it chooses the hero for the next run: the campaign's hero select opens over the room
// (CampaignManager.ChooseHeroInRoom), the same one the figure on the map opens.
public class ClassPosterWall : MonoBehaviour, IInteractable
{
    [Serializable]
    public class Poster
    {
        public AdventurerClass adventurerClass;
        [Tooltip("The poster and its tape; shown while it is up.")] public GameObject poster;
        [Tooltip("The class's desk prop (an instance of AdventurerClass.deskProp), shown with the poster.")] public GameObject prop;
    }

    public List<Poster> posters = new();
    [Tooltip("UI Library key played as a poster goes up.")] public string putUpKey = "equipItem";
    [Tooltip("Paper pressed onto the wall, played where the poster goes up.")] public AudioClip putUpClip;
    [Tooltip("A short burst on each poster as it goes up (Items/Effects/Chest burst), scaled up to the room.")] public GameObject putUpEffect;
    [Min(.1f), Tooltip("The burst is made for the dungeon (1 unit to the metre); the room is ~22 units to the metre.")] public float effectScale = 20f;
    [Tooltip("A short warm light on the poster as it goes up, fading out over Flash Seconds (0 = none).")] public float flashIntensity = 400f;
    public float flashRange = 30f, flashSeconds = .9f;
    public Color flashColor = new(1f, .78f, .45f);

    [Tooltip("The prompt when there is no poster to put up and using the wall chooses the hero.")]
    public string choosePrompt = "Choose your hero";

    bool PostersCarried => ProgressionManager.HasInstance && Carried(ProgressionManager.Instance) > 0;
    // The hero can be chosen here once the map is on the table (after the first morning), outside a run.
    static bool CanChoose => CampaignManager.HasInstance && CampaignManager.Instance.CanChooseHeroInRoom &&
        ProgressionManager.HasInstance && ProgressionManager.Instance.HasFlag(CampaignManager.SeenFlag);

    public string Prompt => PostersCarried ? "Put up the poster" : choosePrompt;
    public bool CanInteract(Character who) => who is Player p && p.kind == PlayerKind.Room && (PostersCarried || CanChoose);

    int Carried(ProgressionManager meta)
    {
        int n = 0;
        foreach (var p in posters) if (p.poster != null && meta.PosterCarried(p.adventurerClass)) n++;
        return n;
    }

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        if (!PostersCarried) { CampaignManager.Instance.ChooseHeroInRoom(); return; }
        var meta = ProgressionManager.Instance;
        int n = 0;
        foreach (var p in posters)
        {
            if (p.poster == null || !meta.PosterCarried(p.adventurerClass)) continue;
            meta.PutUpPoster(p.adventurerClass);
            p.poster.SetActive(true);
            if (p.prop != null)
            {
                p.prop.SetActive(true);
                var ps = p.prop.transform.localScale;
                p.prop.transform.localScale = ps * .9f;
                p.prop.transform.DOScale(ps, .3f).SetEase(Ease.OutBack).SetDelay(n * .25f).SetLink(p.prop);
            }
            // pressed flat onto the wall, one after another
            var tr = p.poster.transform;
            var scale = tr.localScale;
            float delay = n++ * .25f;
            tr.localScale = scale * .9f;
            tr.DOScale(scale, .3f).SetEase(Ease.OutBack).SetDelay(delay).SetLink(p.poster);
            var line = p.adventurerClass.posterLine;
            DOVirtual.DelayedCall(delay, () =>
            {
                if (AudioManager.HasInstance)
                {
                    if (putUpClip != null) AudioManager.Instance.PlaySFX(putUpClip, tr.position, .9f, .05f);
                    if (!string.IsNullOrEmpty(putUpKey)) AudioManager.Instance.PlayUI(putUpKey);
                }
                if (putUpEffect != null) Burst(tr.position - transform.forward * 1.5f);
                if (line != null && !line.IsEmpty) DungeonMaster.Say(line);
            }).SetLink(gameObject);
        }
    }

    // The burst sized for the room: every particle system scales with its transform. It faces out of the wall.
    void Burst(Vector3 at)
    {
        var fx = Instantiate(putUpEffect, at, Quaternion.LookRotation(-transform.forward));
        fx.transform.localScale = putUpEffect.transform.localScale * effectScale;
        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
        Destroy(fx, 4f);
        if (flashIntensity <= 0f) return;
        // the flash lights the poster from just in front of it, then dies away
        var flash = new GameObject("Poster flash").AddComponent<Light>();
        flash.transform.position = at - transform.forward * 4f;
        flash.type = LightType.Point; flash.range = flashRange; flash.color = flashColor; flash.intensity = flashIntensity; flash.shadows = LightShadows.None;
        flash.DOIntensity(0f, flashSeconds).SetEase(Ease.OutQuad).SetLink(flash.gameObject).OnComplete(() => Destroy(flash.gameObject));
    }

    void Start()
    {
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged += OnFlag;
        Refresh();
    }
    void OnDestroy()
    {
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged -= OnFlag;
    }
    // A class unlocked, a poster put up, or a save loaded after Start (it sets its flags one by one).
    void OnFlag(string key, int value)
    {
        if (key.StartsWith(ProgressionManager.PosterPrefix)) { Refresh(); return; }
        foreach (var p in posters) if (p.adventurerClass != null && p.adventurerClass.unlockFlag == key) { Refresh(); return; }
    }

    public void Refresh()
    {
        var meta = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        foreach (var p in posters)
        {
            bool up = meta != null ? meta.PosterUp(p.adventurerClass) : p.adventurerClass != null && p.adventurerClass.Unlocked;
            if (p.poster != null) p.poster.SetActive(up);
            if (p.prop != null) p.prop.SetActive(up);
        }
    }
}
