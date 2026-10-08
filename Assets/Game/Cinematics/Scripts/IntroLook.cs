using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

// Serialized by integer: append only.
public enum IntroStyle { Warm = 0, Cold = 1, Dusk = 2 }

// One colour scheme for the intro: the lamp, the Dungeon Master's eyes, the practice board's light and
// the class card (its art from Tools/paper_card.py, and its text colours). IntroController holds one per
// IntroStyle and applies the chosen one.
[Serializable]
public class IntroLook
{
    [HideLabel, Title("$style")] public IntroStyle style;
    [ColorUsage(false)] public Color lamp = Color.white, eyes = Color.white, boardLight = Color.white;

    [FoldoutGroup("Card"), PreviewField(40)] public Sprite card, banner, inset, divider;
    [FoldoutGroup("Card"), ColorUsage(false)] public Color ink = Color.black, inkSoft = Color.gray, name = Color.white;
    [FoldoutGroup("Card"), ColorUsage(false)] public Color health = Color.red, mana = Color.blue, stamina = Color.green, rank = Color.yellow, tier = Color.gray;

    // The card's parts, found by name on the scene-authored card.
    public void ApplyToCard(ClassFigures figures)
    {
        if (figures == null || figures.floatingCard == null) return;
        ApplyTo(figures.floatingCard.transform);
        var ui = figures.card;
        if (ui == null) return;
        ui.healthColor = health; ui.manaColor = mana; ui.staminaColor = stamina; ui.rankColor = rank; ui.tierColor = tier;
        ui.pipOn = ink; ui.pipOff = Color.Lerp(ink, inkSoft, .6f); ui.pipLocked = inkSoft * .6f;
        if (ui.isActiveAndEnabled) ui.Refresh();
    }

    // Any card-like canvas built from the same parts (the class card, the character sheet).
    public void ApplyTo(Transform root)
    {
        if (root == null) return;
        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            var n = image.gameObject.name;
            if (n == "Card") Set(image, card);
            else if (n == "Banner") Set(image, banner);
            else if (n == "Divider") Set(image, divider);
            else if (n == "Portrait Frame" || n.StartsWith("Slot") || n == "Previous" || n == "Next") Set(image, inset);
            else if (n == "Arrow") image.color = ink;
        }
        foreach (var text in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            var n = text.gameObject.name;
            if (n == "Stamp") continue; // keeps its own red
            text.color = n == "Name" ? name : n == "Label" || n == "Skills Label" || n == "Locked Hint" || n == "How" ? inkSoft : ink;
        }
    }

    static void Set(Image image, Sprite sprite)
    {
        if (sprite == null) return;
        image.sprite = sprite;
        image.color = Color.white;
    }
}
