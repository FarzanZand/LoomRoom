using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Everything a boss says in the dialogue box (PortraitDialogueUI), one asset per boss in Dialogue/Conversations: the
// name on its boxes, its title and the boss bar, the intro conversation (boxes, answers and replies) and its lines in
// the middle of the fight. The fight (BossFight > Intro > Dialogue) points at it; the Dungeon Master's own lines in the
// fight stay on the BossFight. Asset Studio's Dialogue view lists and edits it (Boss fights).
[CreateAssetMenu(menuName = "LoomRoom/Boss Dialogue")]
public class BossDialogueData : ScriptableObject
{
    [DialogueText, Tooltip("The boss's name on its boxes, its title and the boss bar. Empty: the boss's own name.")]
    public string bossName;

    [Title("Intro"), ListDrawerSettings(ShowFoldout = true), Tooltip("Said once the spotlight is on it, before the fight begins (when the fight's Intro Dialogue is on).")]
    public List<PortraitLine> introLines = new();

    [Title("During the fight"), TextArea(1, 3), Tooltip("Said as the second phase begins (the fight goes on under it), in place of the Dungeon Master's Phase Two Line. Empty: his line.")]
    public string phaseTwoLine = "";
    [TextArea(1, 3), Tooltip("Said as it calls its backup dancers in.")]
    public string backupCallLine = "";
    [TextArea(1, 3), Tooltip("Said once the last of its backup dancers is down.")]
    public string backupBrokenLine = "";
}
