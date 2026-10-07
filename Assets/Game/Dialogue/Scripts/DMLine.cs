using UnityEngine;

// One thing the Dungeon Master says: text, and either a recorded voice or the generic mumble. The
// simple-lines system (biome entries, the DM's remarks); real conversations are in the Dialogue Database.
[CreateAssetMenu(menuName = "LoomRoom/DM Line")]
public class DMLine : ScriptableObject
{
    [TextArea] public string text;
    [Tooltip("Recorded voice. Empty uses the mumble if Mumble is on.")] public AudioData voice;
    [Tooltip("Barony-style gibberish while the text appears.")] public bool mumble = true;
    [Tooltip("Said across the table in the room. Off: heard inside your head, in the dungeon.")] public bool inPerson;

    // The line with its text changed (a name filled in), keeping voice and delivery.
    public DungeonMaster.Line With(string newText) => new() { text = newText, voice = voice, mumble = mumble, inPerson = inPerson };
}
