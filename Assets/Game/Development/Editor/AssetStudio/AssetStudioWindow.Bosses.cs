using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's Bosses view (the user, 2026-10-31: one place to edit each boss), over the whole width: every guardian
// in the game (each biome, or level base biome, that has one) in a list, and for the one picked everything that makes it
// on one page: play it; its fight (BossFight: the music, the intro with its dialogue, spotlight, sounds and card, the
// beam and its dance, the timed events); its guardian settings on the biome (title, reward, arena); and the boss's own
// character data (health, damage, spells), each its own inspector, edited in place.
public partial class AssetStudioWindow
{
    Vector2 bossListScroll, bossPageScroll;
    int bossPicked;
    readonly Dictionary<Object, Editor> bossEditors = new();
    readonly Dictionary<string, bool> bossFolds = new() { ["Fight"] = true };
    bool showingBosses => studioView == StudioView.Bosses;

    void OpenBosses() { studioView = StudioView.Bosses; GUI.FocusControl(null); }

    // Biome files and the levels' base biomes with a guardian (the same list the Test buttons look through).
    List<DungeonBiome> BossBiomes() => World.biomes.Concat(World.levels.Where(l => !l.tool && !l.Staged && l.data.baseBiome != null).Select(l => l.data.baseBiome))
        .Where(b => b != null && b.guardian != null && b.guardian.enabled && b.guardian.boss != null).Distinct().ToList();

    static string BossName(DungeonBiome b) => b.guardian.fight != null && !string.IsNullOrWhiteSpace(b.guardian.fight.bossName) ? b.guardian.fight.bossName : b.guardian.boss.name;

    void DrawBosses(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        var bosses = BossBiomes();
        var list = new Rect(r.x + 10, r.y + 10, 290, r.height - 20);
        var page = new Rect(list.xMax + 12, r.y + 10, r.xMax - list.xMax - 22, r.height - 20);
        GUILayout.BeginArea(list);
        bossListScroll = GUILayout.BeginScrollView(bossListScroll);
        GUILayout.Label($"{bosses.Count} bosses", dim);
        for (int i = 0; i < bosses.Count; i++)
        {
            var b = bosses[i];
            string label = $"  {BossName(b)}\n  <color=#a9a49a>{b.guardian.title} · {b.name}</color>";
            bool on = GUILayout.Toggle(i == bossPicked, label, i == bossPicked ? cellSelected : cell, GUILayout.Height(46));
            if (on && i != bossPicked) { bossPicked = i; bossPageScroll = Vector2.zero; GUI.FocusControl(null); }
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        if (bosses.Count == 0) return;
        bossPicked = Mathf.Clamp(bossPicked, 0, bosses.Count - 1);
        GUILayout.BeginArea(page);
        pageWidth = page.width - 20;
        bossPageScroll = GUILayout.BeginScrollView(bossPageScroll);
        try { BossPage(bosses[bossPicked]); }
        finally { GUILayout.EndScrollView(); GUILayout.EndArea(); }
    }

    void BossPage(DungeonBiome biome)
    {
        var guardian = biome.guardian;
        var fight = guardian.fight;
        var prefab = guardian.boss;
        var character = prefab != null ? prefab.GetComponent<Character>() : null;
        GUILayout.Label(BossName(biome), StudioSkin.Strong);
        GUILayout.Label($"{guardian.title}   ·   {prefab.name}   ·   guardian of {biome.name}" + (fight != null ? $"   ·   fight {fight.name}" : "   ·   no fight asset (fought where it stands)"), dim);
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("Fight it", $"Straight to {biome.name}'s boss floor."), GUILayout.Height(28), GUILayout.Width(140))) GameLauncher.PlayBiome(biome, true);
        if (fight != null && GUILayout.Button(new GUIContent("On the boss stage", "The toolbar's Boss start: just off the stage in front of the Dungeon Master; walk on to begin."), GUILayout.Height(28), GUILayout.Width(170))) GameLauncher.PlayBossStage();
        if (fight != null && GUILayout.Button("Select the fight", GUILayout.Height(28), GUILayout.Width(150))) { Selection.activeObject = fight; EditorGUIUtility.PingObject(fight); }
        if (GUILayout.Button("Open the boss prefab", GUILayout.Height(28), GUILayout.Width(170))) AssetDatabase.OpenAsset(prefab);
        GUILayout.EndHorizontal();
        GUILayout.Space(10);

        if (fight != null) Section("Fight", "music, intro (dialogue, spotlight, sounds, card), beam, events", () => Inspect(fight));
        if (character != null && character.data != null) Section("Boss", "the boss's own data: health, damage, armour, spells", () => Inspect(character.data));
        Section("Guardian", $"on {biome.name}: title, intro message, reward, arena", () =>
        {
            var so = new SerializedObject(biome);
            so.Update();
            var property = so.FindProperty("guardian");
            if (property != null) EditorGUILayout.PropertyField(property, true);
            so.ApplyModifiedProperties();
        });
    }

    void Section(string title, string about, System.Action draw)
    {
        bossFolds.TryGetValue(title, out bool open);
        GUILayout.BeginHorizontal();
        open = GUILayout.Toggle(open, $"  {(open ? "▼" : "►")}  {title}", StudioSkin.Strong, GUILayout.ExpandWidth(false));
        GUILayout.Label("   " + about, dim);
        GUILayout.EndHorizontal();
        bossFolds[title] = open;
        if (open) { GUILayout.Space(4); draw(); }
        GUILayout.Space(12);
    }

    void Inspect(Object target)
    {
        if (!bossEditors.TryGetValue(target, out var editor) || editor == null) bossEditors[target] = editor = Editor.CreateEditor(target);
        editor.OnInspectorGUI();
    }
}
