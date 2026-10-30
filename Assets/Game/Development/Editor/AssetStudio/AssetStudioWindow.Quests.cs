using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's Quests view (the user, 2026-10-30: every quest in the game in one place), over the whole width: the
// intro's practice-room quests (IntroController.roomQuests) and the ProgressionManager's quests, in the order a player
// meets them, each with its log text (edited in place), where the log shows it (in a dungeon, in the room or both), when
// it is given and when it is done (as the code that gives and completes each one says: ProgressionManager.Quests and its
// partials, TavernRoom, ShieldBash, CampaignManager, IntroController.Practice), and its save flag. In Play mode each row
// also shows its state and can be given, completed or reset. The quests live on scene objects (the room's
// ProgressionManager and IntroController): save the scene after changing a text.
public partial class AssetStudioWindow
{
    Vector2 questScroll;
    bool showingQuests => studioView == StudioView.Quests;

    void OpenQuests() { studioView = StudioView.Quests; GUI.FocusControl(null); }

    // When each of the game's own quests is given and done, as its code says, and where in the story it comes.
    static readonly Dictionary<string, (int order, string given, string done)> QuestRules = new()
    {
        [ProgressionManager.InventoryQuest] = (10, "at the start of every run, the intro's included, until the inventory has been opened once (taken back in the room, offered again next run)", "the first time the inventory opens"),
        [ProgressionManager.ShieldBashQuest] = (11, "at the start of every run, the intro's included, until the first shield bash", "the first shield bash"),
        [ProgressionManager.SheetQuest] = (20, "back in the room after the first run that ends, once the Dungeon Master has pointed at the sheet", "the sheet is opened"),
        [ProgressionManager.SpendQuest] = (21, "once the sheet has been opened, until Blocking is trained (no run starts meanwhile)", "Blocking's first level is bought"),
        [ProgressionManager.TrophyQuest] = (22, "whenever trophies are carried home (again for each new lot)", "none are left to hang"),
        [ProgressionManager.TableQuest] = (23, "once Blocking is trained and every trophy is on the wall", "a run starts"),
        [ProgressionManager.DeflectQuest] = (30, "at the start of a run once Deflect (Blocking 15) is unlocked", "the first arrow or spell sent back off the shield"),
        [ProgressionManager.RumourQuest] = (35, "the first visit to a tavern while a rumour is on sale", "a rumour is bought"),
        [ProgressionManager.TileQuest] = (40, "the missing tile is carried home from a run", "the tile is put in the symbol puzzle"),
        [ProgressionManager.PosterQuest] = (45, "a class unlocked in play brings its poster home", "the poster is up on the wall by the bed"),
        [ProgressionManager.DiceQuest] = (50, "the symbol puzzle is solved and its dice carried", "the dice lie on the big table's border"),
    };

    // The manager's quests and the code's built-in ones it doesn't hold yet (a scene saved before one existed gets it added
    // at Play, ProgressionManager.EnsureBuiltInQuests): every quest the game can give. Editing a built-in one's text adds it.
    static List<ProgressionManager.Quest> AllQuests(ProgressionManager pm)
    {
        var list = pm != null ? pm.quests.Where(q => q != null).ToList() : new List<ProgressionManager.Quest>();
        var builtIn = typeof(ProgressionManager).GetMethod("BuiltInQuests", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.Invoke(null, null) as List<ProgressionManager.Quest>;
        if (builtIn != null) foreach (var q in builtIn) if (!list.Any(x => x.id == q.id)) list.Add(q);
        return list;
    }

    void DrawQuests(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        pageWidth = r.width - 28 - 18;
        GUILayout.BeginArea(new Rect(r.x + 14, r.y + 12, r.width - 28, r.height - 24));
        questScroll = GUILayout.BeginScrollView(questScroll);
        try { QuestsPage(); }
        finally { GUILayout.EndScrollView(); GUILayout.EndArea(); }
    }

    void QuestsPage()
    {
        var pm = StatsManager(out bool inScene);
        var intro = UnityEngine.Object.FindObjectsByType<IntroController>(FindObjectsInactive.Include).FirstOrDefault();
        var quests = AllQuests(pm);
        int count = quests.Count + (intro != null ? intro.roomQuests.Count(q => q != null) : 0);
        Title("Quests", $"{count} quests: the short tasks the quest log shows until they are done, in the order a player meets them. Edit a quest's text in place.");
        GUILayout.Label(inScene ? "The quests are on the room's ProgressionManager and IntroController in the open scene: save the scene after changing a text." : "<color=#e0a85a>The room scene isn't open: the quests shown are the ProgressionManager prefab's, and the intro's practice quests aren't shown. Open Room.unity to edit the ones the game reads.</color>", Wrapped, GUILayout.Width(pageWidth));
        if (Application.isPlaying) GUILayout.Label("<color=#6fd46a>Playing: each quest's state is shown, and the buttons give, complete or reset it.</color>", new GUIStyle(dim) { richText = true });
        var head = new GUIStyle(gold) { fontSize = StudioSkin.Size - 2 };
        float textW = Mathf.Clamp(pageWidth * .3f, 220, 380), ruleW = Mathf.Max(160, (pageWidth - textW - 90 - 120 - 200) / 2f);

        void Header()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Quest", head, GUILayout.Width(120));
            GUILayout.Label("The log says", head, GUILayout.Width(textW));
            GUILayout.Label("Shown", head, GUILayout.Width(90));
            GUILayout.Label("Given", head, GUILayout.Width(ruleW));
            GUILayout.Label("Done", head, GUILayout.Width(ruleW));
            GUILayout.EndHorizontal();
        }

        void Row(string id, string text, Action<string> setText, string shown, string given, string done, UnityEngine.Object owner)
        {
            GUILayout.BeginHorizontal(StudioSkin.Inset, GUILayout.Width(pageWidth));
            GUILayout.BeginVertical(GUILayout.Width(120));
            GUILayout.Label(id, StudioSkin.Strong);
            GUILayout.Label(ProgressionManager.QuestFlag(id), dimLine);
            if (Application.isPlaying && ProgressionManager.HasInstance)
            {
                int state = ProgressionManager.Instance.QuestState(id);
                GUILayout.Label(state == 0 ? "not given" : state == 1 ? "<color=#d8aa48>open</color>" : "<color=#6fd46a>done</color>", new GUIStyle(dim) { richText = true });
            }
            GUILayout.EndVertical();
            string typed = EditorGUILayout.DelayedTextField(text ?? "", new GUIStyle(EditorStyles.textField) { wordWrap = true }, GUILayout.Width(textW), GUILayout.Height(40));
            if (typed != (text ?? "") && owner != null)
            {
                Undo.RecordObject(owner, "Quest text"); setText(typed); EditorUtility.SetDirty(owner);
                if (owner is Component c && !EditorUtility.IsPersistent(c)) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
            }
            GUILayout.Space(6);
            GUILayout.Label(shown, new GUIStyle(dim) { wordWrap = true }, GUILayout.Width(84));
            GUILayout.Label(given, new GUIStyle(dim) { wordWrap = true }, GUILayout.Width(ruleW));
            GUILayout.Label(done, new GUIStyle(dim) { wordWrap = true }, GUILayout.Width(ruleW));
            if (Application.isPlaying && ProgressionManager.HasInstance)
            {
                var live = ProgressionManager.Instance;
                GUILayout.BeginVertical(GUILayout.Width(80));
                if (GUILayout.Button("Give", GUILayout.Height(18))) live.GiveQuest(id);
                if (GUILayout.Button("Complete", GUILayout.Height(18))) live.CompleteQuest(id);
                if (GUILayout.Button("Reset", GUILayout.Height(18))) live.SetFlag(ProgressionManager.QuestFlag(id), 0);
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
        }

        // The intro's practice board, room by room.
        if (intro != null && intro.roomQuests.Length > 0)
        {
            Section("1.  THE INTRO'S PRACTICE BOARD");
            GUILayout.Label("One for each practice room, in its order.", Wrapped, GUILayout.Width(pageWidth));
            Header();
            foreach (var q in intro.roomQuests.Where(q => q != null).OrderBy(q => q.room))
            {
                var quest = q;
                Row(q.id, q.text, t => quest.text = t, "the practice", $"walking into practice room {q.room}", "reaching a later room, or the room is dealt with, or the practice ends", intro);
            }
        }

        if (pm == null) { GUILayout.Label("<color=#e0a85a>No ProgressionManager found.</color>", dim); return; }
        // The game's own, by when they come; any the code here doesn't know about after them.
        var known = quests.Where(q => QuestRules.ContainsKey(q.id)).OrderBy(q => QuestRules[q.id].order).ToList();
        var other = quests.Where(q => !QuestRules.ContainsKey(q.id)).ToList();
        string[] steps = { "THE FIRST RUNS", "BACK IN THE ROOM AFTER A RUN", "LATER" };
        int n = intro != null && intro.roomQuests.Length > 0 ? 1 : 0;
        int band = -1;
        foreach (var q in known)
        {
            int order = QuestRules[q.id].order;
            int b = order < 20 ? 0 : order < 30 ? 1 : 2;
            if (b != band) { band = b; Section($"{++n}.  {steps[b]}"); Header(); }
            string shown = ProgressionManager.DungeonOnly(q.id) ? "in a dungeon" : ProgressionManager.RoomOnly(q.id) ? "in the room" : "anywhere";
            var quest = q;
            Row(q.id, q.text, t => { quest.text = t; if (!pm.quests.Contains(quest)) pm.quests.Add(quest); }, shown, QuestRules[q.id].given, QuestRules[q.id].done, pm);
        }
        if (other.Count > 0)
        {
            Section($"{++n}.  OTHERS");
            GUILayout.Label("On the quest list but given by no code Asset Studio knows of: given and done wherever a script calls GiveQuest and CompleteQuest with their ids.", Wrapped, GUILayout.Width(pageWidth));
            Header();
            foreach (var q in other) { var quest = q; Row(q.id, q.text, t => quest.text = t, "anywhere", "-", "-", pm); }
        }
        GUILayout.Space(8);
        GUILayout.Label("A new quest is one more entry in ProgressionManager > Quests (an id that never changes, and its text), given and completed from code (GiveQuest, CompleteQuest); {attack}, {block}, {inventory} and {crouch} in a text become the player's keys.", Wrapped, GUILayout.Width(pageWidth));
    }
}
