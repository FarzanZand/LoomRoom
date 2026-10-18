using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Quests: short tasks the quest log shows (UI/Scripts/QuestLogUI.cs, under the HUD) until they are done. A quest is the
// flag "quest.<id>" (0 not given, 1 open, 2 done), so AdventureSave keeps it with the other flags and the log follows
// FlagChanged. The first one: once Shield bash is unlocked, the next run asks the player to try it, and the first bash
// completes it (ShieldBash). Before that, after the first death the DM's word about the sheet gives "look at the sheet",
// done when the sheet is opened (CharacterSheetTable).
public partial class ProgressionManager
{
    [Serializable]
    public class Quest
    {
        [Tooltip("Stable save key (flag quest.<id>); never change it once players have saves.")] public string id;
        [TextArea, Tooltip("What the log says. {attack} and {block} are replaced by the player's own keys.")] public string text;
    }

    public const string QuestPrefix = "quest.", ShieldBashQuest = "shieldBash", SheetQuest = "checkSheet",
        SpendQuest = "spendSpoils", TableQuest = "backToTable", TrophyQuest = "hangTrophies", InventoryQuest = "openInventory";
    public const string InventoryOpenedFlag = "story.inventoryOpened";

    [FoldoutGroup("Quests"), ListDrawerSettings(ShowFoldout = true)]
    public List<Quest> quests = BuiltInQuests();

    // The quests the game's own code gives; a scene saved before one existed gets it added at Play (EnsureBuiltInQuests).
    static List<Quest> BuiltInQuests() => new()
    {
        new Quest { id = SheetQuest, text = "Look at the character sheet on the little table by the wall." },
        new Quest { id = ShieldBashQuest, text = "Try your shield bash: hold {block} and press {attack}." },
        new Quest { id = InventoryQuest, text = "Open your inventory with {inventory} or Tab." },   // Tab is the fixed second binding
        new Quest { id = SpendQuest, text = "Train Blocking on the sheet first. The other rows open after it." },
        new Quest { id = TableQuest, text = "Sit down at the table for the next run." },
        new Quest { id = TrophyQuest, text = "Hang your trophies on the wall by the hobby table." },
        new Quest { id = TileQuest, text = "Put the missing tile into the symbol puzzle in the room." },
    };

    void EnsureBuiltInQuests()
    {
        foreach (var q in BuiltInQuests()) if (FindQuest(q.id) == null) quests.Add(q);
    }

    // The room's quests after a run, from the lesson's flags: look at the sheet (once the first run has ended), spend
    // the spoils there (done with the first Blocking level), then back to the table (done when a run starts); and hang
    // the trophies whenever some are carried (done when none are). The trophy quest can come back after later buys.
    void UpdateSheetQuest()
    {
        bool opened = HasFlag(CharacterSheetTable.OpenedFlag);
        int sheet = QuestState(SheetQuest);
        if (sheet == 0 && HasFlag(LedgerIntroFlag) && !opened && (HasFlag(LedgerSaidFlag) || RoomPlayerFree)) GiveQuest(SheetQuest);
        else if (sheet == 1 && opened) CompleteQuest(SheetQuest);

        int spend = QuestState(SpendQuest);
        if (spend == 0 && opened && HasFlag(LedgerIntroFlag) && !BlockingTaught) GiveQuest(SpendQuest);
        else if (spend == 1 && BlockingTaught) CompleteQuest(SpendQuest);

        if (Time.frameCount % 15 != 0) return;   // the trophy count builds a list: a few times a second is plenty
        bool carrying = TrophiesIn(TrophyState.Carried).Count > 0;
        int trophy = QuestState(TrophyQuest);
        if (carrying && trophy != 1) SetFlag(QuestFlag(TrophyQuest), 1);   // given again for each new lot
        else if (!carrying && trophy == 1) CompleteQuest(TrophyQuest);
        // Back to the table only once the trophies are on the wall (none still to be handed over or carried).
        if (QuestState(SpendQuest) == 2 && QuestState(TableQuest) == 0 && !carrying && TrophiesIn(TrophyState.Earned).Count == 0) GiveQuest(TableQuest);
    }

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
            text = text.Replace("{inventory}", KeyName(InputManager.Instance.BindingDisplay("Inventory")));
            text = text.Replace("{crouch}", KeyName(InputManager.Instance.BindingDisplay("Crouch")));
        }
        return text;
    }

    static string KeyName(string binding) => binding == "LMB" ? "left mouse" : binding == "RMB" ? "right mouse" : binding;

    void Start()
    {
        EnsureBuiltInQuests();
        if (RunManager.HasInstance) RunManager.Instance.RunStarted += OfferQuestsForRun;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (RunManager.HasInstance) RunManager.Instance.RunStarted -= OfferQuestsForRun;
    }

    // The first time the inventory opens: its quest is done for good.
    public void InventoryOpened()
    {
        if (HasFlag(InventoryOpenedFlag)) return;
        SetFlag(InventoryOpenedFlag);
        CompleteQuest(InventoryQuest);
    }

    void OfferQuestsForRun()
    {
        CompleteQuest(TableQuest);
        // Until the player has opened the inventory once, every run starts by saying how (the intro's included).
        if (!HasFlag(InventoryOpenedFlag) && QuestState(InventoryQuest) == 0) GiveQuest(InventoryQuest);
        if (isActiveAndEnabled) StartCoroutine(OfferWhenRunReady());
    }

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
