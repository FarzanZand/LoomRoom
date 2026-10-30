using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Quests: short tasks the quest log shows (UI/Scripts/HUD/QuestLogUI.cs, under the HUD) until they are done. A quest is the
// flag "quest.<id>" (0 not given, 1 open, 2 done), so AdventureSave keeps it with the other flags and the log follows
// FlagChanged. The first one: once Deflect (Blocking's first milestone) is unlocked, the next run asks the player to send a shot back, and the first deflect
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

    public const string QuestPrefix = "quest.", ShieldBashQuest = "shieldBash", DeflectQuest = "deflect", SheetQuest = "checkSheet",
        SpendQuest = "spendSpoils", TableQuest = "backToTable", TrophyQuest = "hangTrophies", InventoryQuest = "openInventory",
        RumourQuest = "buyRumour";
    public const string InventoryOpenedFlag = "story.inventoryOpened";
    // Quests the log shows only while the table player is in a dungeon (the shield bash guide, the deflect).
    public static bool DungeonOnly(string id) => id == ShieldBashQuest || id == DeflectQuest || id == RumourQuest;
    // Quests done in the room (the sheet, the trophy wall, the table, the symbol puzzle): the log leaves them out while
    // the table player is in a dungeon, where they can't be done.
    public static bool RoomOnly(string id) => id == SheetQuest || id == SpendQuest || id == TableQuest || id == TrophyQuest || id == TileQuest || id == PosterQuest;

    [FoldoutGroup("Quests"), ListDrawerSettings(ShowFoldout = true)]
    public List<Quest> quests = BuiltInQuests();

    // The quests the game's own code gives; a scene saved before one existed gets it added at Play (EnsureBuiltInQuests).
    static List<Quest> BuiltInQuests() => new()
    {
        new Quest { id = SheetQuest, text = "Look at the character sheet by the wall." },
        new Quest { id = ShieldBashQuest, text = "Shield bash: hold {block}, press {attack}." },
        new Quest { id = DeflectQuest, text = "Block an arrow or spell to send it back." },
        new Quest { id = InventoryQuest, text = "Open your inventory ({inventory} or Tab)." },   // Tab is the fixed second binding
        new Quest { id = SpendQuest, text = "Train Blocking on the character sheet." },
        new Quest { id = TableQuest, text = "Sit down at the table." },
        new Quest { id = TrophyQuest, text = "Hang your trophies on the wall." },
        new Quest { id = TileQuest, text = "Put the missing tile in the symbol puzzle." },
        new Quest { id = PosterQuest, text = "Put up the new poster on the wall by the bed." },
        new Quest { id = RumourQuest, text = "Buy a rumour from the tavern keeper." },
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
        bool carrying = TrophyCount(TrophyState.Carried) > 0;
        int trophy = QuestState(TrophyQuest);
        if (carrying && trophy != 1) SetFlag(QuestFlag(TrophyQuest), 1);   // given again for each new lot
        else if (!carrying && trophy == 1) CompleteQuest(TrophyQuest);
        // Back to the table only once the trophies are on the wall (none still to be handed over or carried).
        if (QuestState(SpendQuest) == 2 && QuestState(TableQuest) == 0 && !carrying && TrophyCount(TrophyState.Earned) == 0) GiveQuest(TableQuest);
    }

    // The lesson after the first run: no new run until Blocking is trained at the sheet, from the moment the run ends
    // (the sheet quest, before the sheet is even opened) until the spend-spoils quest is done (TableManager).
    public bool LessonHoldsRuns => HasFlag(LedgerIntroFlag) && !BlockingTaught;

    // Each quest's flag key is made once: QuestState is read every frame (Update, the sheet's table).
    static readonly Dictionary<string, string> questFlags = new();
    public static string QuestFlag(string id)
    {
        if (id == null) return QuestPrefix;
        if (!questFlags.TryGetValue(id, out var flag)) questFlags[id] = flag = QuestPrefix + id;
        return flag;
    }
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
            string inventory = KeyName(InputManager.Instance.BindingDisplay("Inventory"));
            // Tab is the fixed second binding: with Inventory bound to Tab too, it is said once.
            if (string.Equals(inventory, "Tab", StringComparison.OrdinalIgnoreCase)) text = text.Replace("{inventory} or Tab", inventory);
            text = text.Replace("{inventory}", inventory);
            text = text.Replace("{crouch}", KeyName(InputManager.Instance.BindingDisplay("Crouch")));
        }
        return text;
    }

    static string KeyName(string binding) => binding == "LMB" ? "left mouse" : binding == "RMB" ? "right mouse" : binding;

    void Start()
    {
        EnsureBuiltInQuests();
        if (RunManager.HasInstance) RunManager.Instance.RunStarted += OfferQuestsForRun;
        if (InventoryManager.HasInstance) InventoryManager.Instance.ItemPickedUp += ClassItemPickedUp;
        if (PlayerManager.HasInstance)
        {
            PlayerManager.Instance.PlayerSwapped += WithdrawDungeonQuests;
            if (PlayerManager.Instance.Active != null) WithdrawDungeonQuests(PlayerManager.Instance.Active);
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (RunManager.HasInstance) RunManager.Instance.RunStarted -= OfferQuestsForRun;
        if (InventoryManager.HasInstance) InventoryManager.Instance.ItemPickedUp -= ClassItemPickedUp;
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped -= WithdrawDungeonQuests;
    }

    // The inventory quest is a dungeon one: in the apartment it is taken back (not done), and the next run offers it again.
    void WithdrawDungeonQuests(Player player)
    {
        if (player == null || player.kind != PlayerKind.Room) return;
        if (QuestState(InventoryQuest) == 1) SetFlag(QuestFlag(InventoryQuest), 0);
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
        // The shield bash guide, from the first run (the intro's included) until the first bash (ShieldBash completes it).
        if (QuestState(ShieldBashQuest) == 0) GiveQuest(ShieldBashQuest);
        if (isActiveAndEnabled) StartCoroutine(OfferWhenRunReady());
    }

    // A frame later, so the run's ranks are in (AdventurerProgress.Begin).
    IEnumerator OfferWhenRunReady()
    {
        yield return null;
        if (!MetaActive || QuestState(DeflectQuest) != 0 || !PlayerManager.HasInstance) yield break;
        var table = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        var progress = table != null ? table.GetComponent<AdventurerProgress>() : null;
        if (Unlocked(AdventureSkill.Blocking, 0, progress != null ? progress.Rank(AdventureSkill.Blocking) : 0)) GiveQuest(DeflectQuest);
    }

    // A shot sent back off the shield (ArrowProjectile, SpellProjectile).
    public void Deflected()
    {
        if (QuestState(DeflectQuest) == 1) CompleteQuest(DeflectQuest);
    }
}
