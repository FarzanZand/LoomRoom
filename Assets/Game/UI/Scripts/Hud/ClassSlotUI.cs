using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The class slot: one HUD slot right of the hotbar's last slot for the table player's class action (AdventurerClass.action,
// the Pianist's Mood), used with the Class Action key (X) or a click. It shows only while the table player is the one
// playing and its class has an action, and it is a child of the hotbar, so it fades with it.
//
// It is drawn as a copy of the hotbar's last slot (made by ClassSlotSetup.Build() from an eval) and keeps looking like
// it: every frame it takes the neighbour's background colour, its key label's font, size and colour and its dungeon
// frame, so the HUD style (Rugged, Pixel) and the dungeon look follow without a second copy of those rules. A toggle
// that is on shows the hotbar's gold "in hand" frame; off, the action's off icon, or its icon dimmed.
//
// While the class's passive is Crescendo (the Pianist), the slot's lower right corner counts the hits toward the next
// crescendo, "5/8" (CrescendoPassive.Count, read every frame, so a run's start and a crescendo show 0 at once), in the
// font and look of the hotbar's own count label (a scroll's charges). On the crescendo it pops once in gold.
public class ClassSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Tooltip("Fades the whole slot in and out: hidden for a class without an action.")]
    [SerializeField] CanvasGroup group;
    [SerializeField] Image background;
    [SerializeField] Image icon;
    [Tooltip("The gold frame shown while a toggle is on (the hotbar's in-hand highlight).")]
    [SerializeField] Image onFrame;
    [SerializeField] Image dungeonFrame;
    [SerializeField] TMP_Text keyLabel;
    [Tooltip("The Crescendo counter in the lower right corner (hidden for a class whose passive is not Crescendo).")]
    [SerializeField] TMP_Text counterLabel;

    [Header("Looks like")]
    [Tooltip("The hotbar slot whose colours, key font and dungeon frame this slot copies (the last one).")]
    [SerializeField] Image matchBackground;
    [SerializeField] TMP_Text matchKeyLabel;
    [SerializeField] Image matchDungeonFrame;
    [Tooltip("The hotbar slot's count label (a stack's size, a scroll's charges), whose font and size the counter copies.")]
    [SerializeField] TMP_Text matchCountLabel;

    [Header("Look")]
    [Tooltip("The icon's tint while a toggle is off and the action has no off icon of its own.")]
    [SerializeField] Color offTint = new(.45f, .45f, .45f, 1f);
    [Tooltip("The input action whose key is shown on the slot.")]
    [SerializeField] string actionName = "ClassAction";
    [Tooltip("The counter's material: the HUD's underlay for its font, so it reads over the icon, noir and bright floors. Used while the counter has that font; another font takes its own.")]
    [SerializeField] Material counterMaterial;
    [Tooltip("While the counter shows, the icon moves by this (canvas units, up and to the left) so the counter's corner stays clear of it.")]
    [SerializeField] Vector2 iconShiftWithCounter = new(-6f, 8f);
    [Tooltip("And is drawn this much of its size.")]
    [SerializeField] float iconScaleWithCounter = .8f;
    [Tooltip("The counter's colour.")]
    [SerializeField] Color counterColor = Color.white;
    [Tooltip("The counter's colour as it pops on the crescendo, fading back over the pop.")]
    [SerializeField] Color crescendoColor = new(1f, .83f, .35f, 1f);
    [Tooltip("How much bigger the counter grows on the crescendo.")]
    [SerializeField] float crescendoPop = 1.5f;
    [Tooltip("Seconds of the crescendo's pop.")]
    [SerializeField] float crescendoPopSeconds = .35f;

    Player player;
    ClassAction shown;
    bool hovering, wasOn;
    // The counter as last shown (-1: hidden), to redraw only on a change and to see the crescendo (the count going back
    // to 0 from the last hit before it).
    int shownCount = -2, shownOf;
    Coroutine counterPop, pulse;
    Vector2 iconPosition; bool iconRead;

    ClassAction Action => ClassActionRunner.ActionOf(player);

    void OnEnable()
    {
        ClassAction.Changed += OnActionChanged;
        if (InputManager.HasInstance) InputManager.Instance.BindingsChanged += RefreshKey;
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped += OnPlayerSwapped;
        RefreshKey();
        Refresh();
    }

    void OnDisable()
    {
        ClassAction.Changed -= OnActionChanged;
        if (InputManager.HasInstance) InputManager.Instance.BindingsChanged -= RefreshKey;
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped -= OnPlayerSwapped;
        if (hovering && TooltipUI.HasInstance) TooltipUI.Instance.Hide();
        hovering = false;
        if (counterPop != null) { counterPop = null; if (counterLabel != null) { counterLabel.rectTransform.localScale = Vector3.one; counterLabel.color = counterColor; } }
        shownCount = -2;
        pulse = null; transform.localScale = Vector3.one;
    }

    void Start()
    {
        // Managers may wake after this UI's OnEnable.
        if (InputManager.HasInstance) { InputManager.Instance.BindingsChanged -= RefreshKey; InputManager.Instance.BindingsChanged += RefreshKey; }
        if (PlayerManager.HasInstance) { PlayerManager.Instance.PlayerSwapped -= OnPlayerSwapped; PlayerManager.Instance.PlayerSwapped += OnPlayerSwapped; }
        RefreshKey();
        Refresh();
    }

    void OnPlayerSwapped(Player p) => Refresh();
    void OnActionChanged(ClassAction a)
    {
        if (a != shown) return;
        bool on = a.IsOn(player);
        if (on != wasOn) Pulse();
        Refresh();
    }

    void RefreshKey()
    {
        if (keyLabel != null && InputManager.HasInstance) keyLabel.text = InputManager.Instance.BindingDisplay(actionName);
    }

    void LateUpdate()
    {
        // The class can change between runs (the hero select) without any event this slot hears.
        var active = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        var p = active != null && active.kind == PlayerKind.Table ? active : null;
        if (p != player || Action != shown) Refresh();
        CopyLook();
        RefreshCounter();
    }

    // The neighbour slot's looks, so both HUD styles and the dungeon look match without rules of our own.
    void CopyLook()
    {
        if (background != null && matchBackground != null) background.color = matchBackground.color;
        if (dungeonFrame != null && matchDungeonFrame != null)
        {
            if (dungeonFrame.enabled != matchDungeonFrame.enabled) dungeonFrame.enabled = matchDungeonFrame.enabled;
            dungeonFrame.color = matchDungeonFrame.color;
        }
        if (keyLabel != null && matchKeyLabel != null)
        {
            if (keyLabel.font != matchKeyLabel.font) keyLabel.font = matchKeyLabel.font;
            keyLabel.fontSize = matchKeyLabel.fontSize;
            keyLabel.color = matchKeyLabel.color;
        }
        if (counterLabel != null && matchCountLabel != null)
        {
            var font = matchCountLabel.font;
            if (counterLabel.font != font) counterLabel.font = font;
            var material = font != null && counterMaterial != null && counterMaterial.GetTexture(ShaderUtilities.ID_MainTex) == font.atlasTexture ? counterMaterial : font != null ? font.material : null;
            if (material != null && counterLabel.fontSharedMaterial != material) counterLabel.fontSharedMaterial = material;
            counterLabel.fontSize = matchCountLabel.fontSize;
        }
    }

    // ── Crescendo counter ──

    // The Crescendo of the class the table player plays, if it has one.
    CrescendoPassive Crescendo
    {
        get
        {
            if (player == null || shown == null) return null;
            var progress = player.GetComponent<AdventurerProgress>();
            return progress != null && progress.selectedClass != null ? progress.selectedClass.passive as CrescendoPassive : null;
        }
    }

    void RefreshCounter()
    {
        if (counterLabel == null) return;
        var crescendo = Crescendo;
        int count = crescendo != null ? CrescendoPassive.Count(player) : -1;
        int of = crescendo != null ? crescendo.hitsPerCrescendo : 0;
        if (count == shownCount && of == shownOf) return;
        // Back to 0 from the hit before the crescendo: that hit was the crescendo (a run's start clears it too, but a
        // new run starts with the slot hidden behind the reveal).
        bool crescendoHit = count == 0 && shownCount == of - 1 && of == shownOf && of > 0;
        shownCount = count; shownOf = of;
        bool on = count >= 0;
        if (counterLabel.gameObject.activeSelf != on) counterLabel.gameObject.SetActive(on);
        PlaceIcon(on);
        if (!on) return;
        counterLabel.text = $"{count}/{of}";
        if (crescendoHit && isActiveAndEnabled)
        {
            if (counterPop != null) StopCoroutine(counterPop);
            counterPop = StartCoroutine(CounterPop());
        }
        else if (counterPop == null) counterLabel.color = counterColor;
    }

    // The icon steps up and left and a little smaller while the counter is in its corner.
    void PlaceIcon(bool counter)
    {
        if (icon == null) return;
        var t = icon.rectTransform;
        if (!iconRead) { iconPosition = t.anchoredPosition; iconRead = true; }
        t.anchoredPosition = iconPosition + (counter ? iconShiftWithCounter : Vector2.zero);
        t.localScale = Vector3.one * (counter ? iconScaleWithCounter : 1f);
    }

    // One plain pop on the crescendo: the counter grows and turns gold, then settles back. On game time, as the hit it
    // marks: the crescendo only lands while the game runs, and a recording at a fixed frame rate (CombatTestBot's
    // capture) then shows it as it plays (on real time it ended within two slow capture frames).
    IEnumerator CounterPop()
    {
        var t = counterLabel.rectTransform;
        float seconds = Mathf.Max(.05f, crescendoPopSeconds);
        for (float e = 0f; e < seconds; e += Time.deltaTime)
        {
            float k = e / seconds;
            float grow = k < .25f ? k / .25f : 1f - (k - .25f) / .75f;
            t.localScale = Vector3.one * Mathf.Lerp(1f, crescendoPop, Mathf.SmoothStep(0f, 1f, grow));
            counterLabel.color = Color.Lerp(counterColor, crescendoColor, k < .5f ? 1f : 1f - (k - .5f) / .5f);
            yield return null;
        }
        t.localScale = Vector3.one;
        counterLabel.color = counterColor;
        counterPop = null;
    }

    public void Refresh()
    {
        var active = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        player = active != null && active.kind == PlayerKind.Table ? active : null;
        shown = Action;
        bool visible = shown != null;
        if (group != null)
        {
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = group.interactable = visible;
        }
        if (!visible)
        {
            if (hovering && TooltipUI.HasInstance) TooltipUI.Instance.Hide();
            hovering = false;
            return;
        }
        bool on = !shown.IsToggle || shown.IsOn(player);
        wasOn = on;
        if (icon != null)
        {
            var sprite = !on && shown.iconOff != null ? shown.iconOff : shown.icon;
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            icon.color = on || shown.iconOff != null ? Color.white : offTint;
        }
        if (onFrame != null) onFrame.gameObject.SetActive(shown.IsToggle && on);
        if (hovering) ShowTooltip();
    }

    // ── Pointer ──

    public void OnPointerEnter(PointerEventData e) { if (shown == null) return; hovering = true; ShowTooltip(); }
    public void OnPointerExit(PointerEventData e) { hovering = false; if (TooltipUI.HasInstance) TooltipUI.Instance.Hide(); }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || shown == null || player == null) return;
        Use();
    }

    // The same use as the key (ClassActionRunner: the feed says why when it can't be used).
    void Use()
    {
        ClassActionRunner.TryUse(player);
        Refresh();
    }

    void ShowTooltip()
    {
        if (shown == null || !TooltipUI.HasInstance) return;
        string state = shown.IsToggle ? (shown.IsOn(player) ? "On" : "Off") : null;
        // Beside the slot on its right, its foot at the screen's foot (TooltipUI keeps it 8 px in), over open ground:
        // above the slot it covered the hotbar (at the mouse), the line a use posts ("Mood off.") and the Dungeon
        // Master's line, all in the middle; level with the slot, its top ran over the open bag's hint line.
        var corners = new Vector3[4];
        ((RectTransform)transform).GetWorldCorners(corners);
        TooltipUI.Instance.ShowTextAt(shown.displayName, state, shown.description, new Vector2(corners[2].x, 0f), Vector2.zero);
    }

    // ── Pulse ──

    void Pulse()
    {
        if (pulse != null) StopCoroutine(pulse);
        pulse = isActiveAndEnabled ? StartCoroutine(PulseRoutine()) : null;
    }

    IEnumerator PulseRoutine()
    {
        const float duration = .07f;
        var big = Vector3.one * 1.06f;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime) { transform.localScale = Vector3.Lerp(Vector3.one, big, t / duration); yield return null; }
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime) { transform.localScale = Vector3.Lerp(big, Vector3.one, t / duration); yield return null; }
        transform.localScale = Vector3.one;
        pulse = null;
    }
}
