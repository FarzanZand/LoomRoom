using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A class unlocked (the room puzzle): the class's card, on the same paper as the class cards at the table, fades in
// over the middle of the screen with its sheet portrait, "New class", its name and where to pick it, with
// AudioManager's classUnlocked sting. It stays a while and fades. Authored under the HUD canvas (built by
// ClassUnlockedSetup.Build()); code only fills and plays it.
public class ClassUnlockedUI : MonoBehaviour
{
    [SerializeField] CanvasGroup group;
    [SerializeField] RectTransform card;
    [SerializeField] Image portrait;
    [SerializeField] TMP_Text heading, className, line;
    [SerializeField, Min(0f), Tooltip("Seconds the card stays before it fades.")] float hold = 4f;
    [SerializeField, Min(0f), Tooltip("How far the card rises as it fades in (canvas units).")] float rise = 18f;
    [SerializeField, Tooltip("AudioManager UI Library key played as it shows.")] string soundKey = "classUnlocked";

    static ClassUnlockedUI instance;
    Sequence show;
    Vector2 rest;
    string classHeading, classLine;
    // A place opened by a rumour waits for the shop screen it was bought on to close (GameState Explore).
    (string name, Sprite picture)? pendingPlace;

    void Awake()
    {
        instance = this;
        if (group != null) group.alpha = 0f;
        if (card != null) rest = card.anchoredPosition;
        classHeading = heading != null ? heading.text : "New class";
        classLine = line != null ? line.text : "Pick it at the table.";
    }

    void Update()
    {
        if (pendingPlace == null || !GameManager.HasInstance || GameManager.Instance.State != GameState.Explore) return;
        var place = pendingPlace.Value; pendingPlace = null;
        Play(place.name, place.picture, PlaceHeading, PlaceLine);
    }

    public const string PlaceHeading = "New place", PlaceLine = "Now on the map in the room.";

    // A place a rumour opened (CampaignManager.BuyRumour): the same card, "New place", its name and its level's card art.
    public static void ShowPlace(CampaignEncounter place)
    {
        if (instance == null || place == null) return;
        instance.pendingPlace = (place.displayName, place.level != null ? place.level.cardArt : null);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        show?.Kill();
    }

    // Shows the card for a class (its sheet portrait and name; the name alone when there is no class asset).
    public static void Show(AdventurerClass unlocked, string fallbackName)
    {
        if (instance == null) return;
        instance.Play(unlocked != null && !string.IsNullOrEmpty(unlocked.displayName) ? unlocked.displayName : fallbackName,
            unlocked != null ? (ProgressionManager.HasInstance && ProgressionManager.Instance.SheetPortrait(unlocked) is Sprite s ? s : unlocked.sheetPortrait != null ? unlocked.sheetPortrait : unlocked.portrait) : null,
            instance.classHeading, instance.classLine);
    }

    void Play(string name, Sprite picture, string headingText, string lineText)
    {
        if (group == null || card == null) return;
        if (className != null) className.text = name;
        if (heading != null) heading.text = headingText;
        if (line != null) line.text = lineText;
        if (portrait != null) { portrait.sprite = picture; portrait.enabled = picture != null; }
        show?.Kill();
        group.alpha = 0f;
        card.anchoredPosition = rest - new Vector2(0f, rise);
        if (AudioManager.HasInstance && AudioManager.Instance.HasUI(soundKey)) AudioManager.Instance.PlayUI(soundKey);
        show = DOTween.Sequence().SetUpdate(true);
        show.Append(group.DOFade(1f, .6f).SetEase(Ease.OutQuad));
        show.Join(card.DOAnchorPos(rest, .8f).SetEase(Ease.OutQuad));
        show.AppendInterval(hold);
        show.Append(group.DOFade(0f, .9f).SetEase(Ease.InQuad));
    }
}
