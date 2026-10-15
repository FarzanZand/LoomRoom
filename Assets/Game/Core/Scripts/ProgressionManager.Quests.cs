using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Quests: short tasks the quest log shows (UI/Scripts/QuestLogUI.cs, under the HUD) until they are done. A quest is the
// flag "quest.<id>" (0 not given, 1 open, 2 done), so AdventureSave keeps it with the other flags and the log follows
// FlagChanged. The first one: once Shield bash is unlocked, the next run asks the player to try it, and the first bash
// completes it (ShieldBash).
public partial class ProgressionManager
{
    [Serializable]
    public class Quest
    {
        [Tooltip("Stable save key (flag quest.<id>); never change it once players have saves.")] public string id;
        [TextArea, Tooltip("What the log says. {attack} and {block} are replaced by the player's own keys.")] public string text;
    }

    public const string QuestPrefix = "quest.", ShieldBashQuest = "shieldBash";

    [FoldoutGroup("Quests"), ListDrawerSettings(ShowFoldout = true)]
    public List<Quest> quests = new() { new Quest { id = ShieldBashQuest, text = "Try your shield bash: hold {block} and press {attack}." } };

    public static string QuestFlag(string id) => QuestPrefix + id;
    public int QuestState(string id) => GetFlag(QuestFlag(id));
    public Quest FindQuest(string id) => quests.Find(q => q != null && q.id == id);

    // Opens a quest that was never given; false when it was given before (open or done).
    public bool GiveQuest(string id)
    {
        if (QuestState(id) != 0 || FindQuest(id) == null) return false;
        SetFlag(QuestFlag(id), 1);
        return true;
    }

    // Completes an open quest; false when it isn't open.
    public bool CompleteQuest(string id)
    {
        if (QuestState(id) != 1) return false;
        SetFlag(QuestFlag(id), 2);
        return true;
    }

    // The quest's text with the player's keys put in.
    public string QuestText(Quest q)
    {
        if (q == null) return "";
        string text = q.text ?? "";
        if (InputManager.HasInstance)
        {
            text = text.Replace("{attack}", KeyName(InputManager.Instance.BindingDisplay("PrimaryAction")));
            text = text.Replace("{block}", KeyName(InputManager.Instance.BindingDisplay("SecondaryAction")));
        }
        return text;
    }

    static string KeyName(string binding) => binding == "LMB" ? "left mouse" : binding == "RMB" ? "right mouse" : binding;

    void Start()
    {
        if (RunManager.HasInstance) RunManager.Instance.RunStarted += OfferQuestsForRun;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (RunManager.HasInstance) RunManager.Instance.RunStarted -= OfferQuestsForRun;
    }

    void OfferQuestsForRun() { if (isActiveAndEnabled) StartCoroutine(OfferWhenRunReady()); }

    // A frame later, so the run's ranks are in (AdventurerProgress.Begin).
    IEnumerator OfferWhenRunReady()
    {
        yield return null;
        if (!MetaActive || QuestState(ShieldBashQuest) != 0 || !PlayerManager.HasInstance) yield break;
        var table = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        var progress = table != null ? table.GetComponent<AdventurerProgress>() : null;
        if (Unlocked(AdventureSkill.Blocking, 0, progress != null ? progress.Rank(AdventureSkill.Blocking) : 0)) GiveQuest(ShieldBashQuest);
    }
}
