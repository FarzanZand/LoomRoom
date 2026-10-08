using System;
using UnityEngine;

// Serialized by integer: append only.
public enum TutorialGoal { Walk = 0, Hit = 1, Kill = 2, PickUp = 3, Guard = 4, Wait = 5 }

// One thing the Dungeon Master teaches on the practice board: his line, a control hint for the feed,
// and what the player has to do before the next step. A goal already met (the player ran ahead) is
// skipped without a word.
[Serializable]
public class TutorialStep
{
    public TutorialGoal goal;
    [Tooltip("Waits until the player walks into this room (its number on the level's floor plan) before starting. -1 starts straight away.")]
    public int room = -1;
    [Tooltip("Walk: distance (world units). Hit, Kill, Pick Up: how many. Guard and Wait: seconds.")]
    [Min(0)] public float amount = 1;
    [Tooltip("Said when the step starts, inside the player's head.")]
    public DMLine line;
    [Tooltip("Said when the player does it (not when the step is given up on).")]
    public DMLine doneLine;
    [TextArea(1, 3), Tooltip("Posted to the feed only if the player has not done it after Hint Delay. {Move}, {PrimaryAction}, {SecondaryAction}, {Interact}, {Inventory} … become the Table map's current bindings.")]
    public string hint;
    [Min(0)] public float hintDelay = 6f;
    [Min(0), Tooltip("Seconds before the step is given up on and the next one starts. 0 waits for ever.")]
    public float giveUpAfter;
}
