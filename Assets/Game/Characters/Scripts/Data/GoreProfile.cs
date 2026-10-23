using System;
using UnityEngine;

// What a creature spills when it is hit (GoreManager). Serialized by integer: append only.
public enum BloodKind
{
    None     = 0,   // nothing (ghosts, constructs)
    Blood    = 1,   // drops that stain floors and walls
    Slime    = 2,   // the same, in the slime's own colour, thicker
    BoneDust = 3,   // skeletons: chips and dust, no stains
}

// Lives on CharacterData (Presentation > Gore), so each creature type has its own blood.
[Serializable]
public class GoreProfile
{
    public BloodKind kind = BloodKind.Blood;
    [Tooltip("Colour of the spray and of the stains it leaves (bone dust uses it for the chips).")]
    public Color color = new(.72f, .05f, .04f);
    [Range(0f, 3f), Tooltip("How much sprays per hit, against GoreManager's default amount.")]
    public float amount = 1f;
    [Tooltip("On death the body bursts: the model disappears in a big spray and a few chunks (slimes, bats). Off: the body stays (ragdoll or bone burst).")]
    public bool burstOnDeath;
    [Tooltip("Played where the creature dies, on top of its death cry.")]
    public AudioData killSound;

    public bool Stains => kind == BloodKind.Blood || kind == BloodKind.Slime;
}
