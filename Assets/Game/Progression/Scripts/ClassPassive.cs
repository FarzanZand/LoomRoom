using Sirenix.OdinInspector;
using UnityEngine;

// A class's passive: something every run of that class has from the start, shown on the hero select's class card
// (name, icon, one short line). One asset per passive beside its class (Characters/Classes/<Class>/); a passive with
// tuning or behaviour of its own is a subclass (the Pianist's Crescendo). AdventurerClass.passive points at it.
[CreateAssetMenu(menuName = "LoomRoom/Class Passive")]
public class ClassPassive : ScriptableObject
{
    public string displayName;
    [PreviewField(32), Tooltip("Shown beside the name on the class card.")] public Sprite icon;
    [TextArea(2, 4), Tooltip("One short, literal line: what it does.")] public string description;
}
