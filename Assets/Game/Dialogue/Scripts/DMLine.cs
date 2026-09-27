using UnityEngine;

// One thing the Dungeon Master says: text, and either a recorded voice or the generic mumble.
[CreateAssetMenu(menuName = "LoomRoom/DM Line")]
public class DMLine : ScriptableObject
{
    [TextArea] public string text;
    [Tooltip("Recorded voice. Empty uses the mumble if Mumble is on.")] public AudioData voice;
    [Tooltip("Barony-style gibberish while the text appears.")] public bool mumble = true;
}
