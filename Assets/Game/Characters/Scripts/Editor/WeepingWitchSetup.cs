using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// WeepingWitchSetup.Build(), run from an eval (safe to run again):
// - Characters/Enemies/Crypt/Not a witch: a copy of the Demon (melee on the Humanoid enemy base) wearing the Synty Dark
//   Fantasy witch body and hair, casting the Crypt Lord's spells (Fireball, Fire Circle, the volley) and closing in to melee,
//   80 health and a skeleton soldier's damage for now; WeepingEnemy makes her kneel and weep (Audio/Clips/Weeping, Tools/weeping_loop.py) until hit.
// - Levels/Dungeon1/Rooms/Templates/Chunks/Sunken court: her own instance in the moonlight at the court's sunken middle, the
//   room's other Enemy sockets gone, and only its own (socketEnemiesOnly), so she weeps there alone. She is placed in
//   it as herself, so she can be tweaked there by hand; a run again keeps the instance that is already there.
public static class WeepingWitchSetup
{
    const string Demon = "Assets/Game/Characters/Enemies/Crypt/Demon.prefab";
    const string Soldier = "Assets/Game/Characters/Enemies/Crypt/Crypt Soldier.prefab";
    const string Lord = "Assets/Game/Characters/Enemies/Crypt/Crypt Lord.prefab";
    const string OldPath = "Assets/Game/Characters/Enemies/Crypt/Weeping witch.prefab";
    const string Path_ = "Assets/Game/Characters/Enemies/Crypt/Not a witch.prefab";
    const string Court = "Assets/Game/Levels/Dungeon1/Rooms/Templates/Chunks/Sunken court.prefab";
    const string Weeping = "Assets/Game/Audio/Clips/Weeping/Weeping loop.wav";
    static readonly string[] Parts = { "SM_Chr_Witch_Female_01", "SM_Chr_Attach_Hair_Witch_01" };

    public static string Build()
    {
        var log = new System.Text.StringBuilder();
        // The first build called her the Weeping witch: moved, so the court's socket keeps pointing at her.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Path_) == null && AssetDatabase.LoadAssetAtPath<GameObject>(OldPath) != null)
            log.Append(AssetDatabase.MoveAsset(OldPath, Path_) is var moved && string.IsNullOrEmpty(moved) ? "renamed | " : moved + " | ");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Path_) == null)
        {
            NewCharacterWindow.Create("Not a witch", AssetDatabase.LoadAssetAtPath<GameObject>(Demon), Path_);
            log.Append("made from the Demon | ");
        }
        var root = PrefabUtility.LoadPrefabContents(Path_);
        try
        {
            int on = 0;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool part = System.Array.IndexOf(Parts, smr.name) >= 0;
                smr.gameObject.SetActive(part);
                if (part) on++;
            }
            log.Append(on + " witch parts on | ");

            var data = root.GetComponent<EnemyBrain>().Data;
            var lord = AssetDatabase.LoadAssetAtPath<GameObject>(Lord).GetComponent<EnemyBrain>().Data;
            data.characterName = "Not a witch";
            data.name = "Not a witch";
            data.properName = true;   // "Not a witch hits you", never "The Not a witch"
            data.description = "She kneels in the moonlight and weeps. Leave her be.";
            // For testing: 80 health and a skeleton's (the Crypt Soldier's) damage.
            Set(data, StatType.MaxHealth, 80f);
            Set(data, StatType.AttackDamage, Get(AssetDatabase.LoadAssetAtPath<GameObject>(Soldier).GetComponent<EnemyBrain>().Data, StatType.AttackDamage, 6f));
            Set(data, StatType.Armor, 4f);
            // The Crypt Lord's spells, and his way of fighting: walks in to melee, casting on the way.
            data.caster = true;
            data.spells = new List<EnemySpell>();
            foreach (var s in lord.spells) data.spells.Add(JsonUtility.FromJson<EnemySpell>(JsonUtility.ToJson(s)));
            data.closesIn = lord.closesIn;
            data.spellGap = lord.spellGap;
            data.experience = 60;
            EditorUtility.SetDirty(data);
            log.Append(data.spells.Count + " spells | ");

            var weep = root.GetComponent<WeepingEnemy>() ?? root.AddComponent<WeepingEnemy>();
            weep.weeping = AssetDatabase.LoadAssetAtPath<AudioClip>(Weeping);
            PrefabUtility.SaveAsPrefabAsset(root, Path_);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        var witch = AssetDatabase.LoadAssetAtPath<GameObject>(Path_);
        var court = PrefabUtility.LoadPrefabContents(Court);
        try
        {
            // She is placed in the court as herself (a prefab instance; the generator keeps that very instance), so she
            // shows in Prefab Mode and can be moved, turned or tuned right there. The room's Enemy sockets and spawners
            // go, the first build's witch socket with them.
            foreach (var s in court.GetComponentsInChildren<DungeonSocket>(true))
                if (s.type == DungeonSocketType.Enemy) Object.DestroyImmediate(s.gameObject);
            foreach (var sp in court.GetComponentsInChildren<EnemySpawner>(true)) Object.DestroyImmediate(sp.gameObject);
            GameObject placed = null;
            foreach (var b in court.GetComponentsInChildren<EnemyBrain>(true))
                if (placed == null && PrefabUtility.GetCorrespondingObjectFromSource(b.gameObject) == witch) placed = b.gameObject;
            if (placed == null)
            {
                placed = (GameObject)PrefabUtility.InstantiatePrefab(witch, court.transform);
                placed.name = "Not a witch";
                var moon = court.transform.Find("Moonlight");
                placed.transform.localPosition = moon != null ? moon.localPosition : new Vector3(0f, -.9f, 0f);
                placed.transform.localRotation = Quaternion.identity;
            }
            var template = court.GetComponent<DungeonRoomTemplate>();
            template.socketEnemiesOnly = true;
            // What the DM says walking in (Enter Lines on the template, one at random): written once, edited there after.
            if (template.enterLines == null || template.enterLines.Length == 0)
                template.enterLines = new[]
                {
                    "Someone is mumbling down there. Let her be.",
                    "Hear that? She hasn't seen you. Keep it that way.",
                    "Quiet now. Whatever you do, don't touch her.",
                };
            PrefabUtility.SaveAsPrefabAsset(court, Court);
            log.Append("Sunken court: the witch in the moonlight, alone");
        }
        finally { PrefabUtility.UnloadPrefabContents(court); }
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    static float Get(CharacterData data, StatType stat, float fallback)
    {
        foreach (var s in data.stats) if (s.stat == stat) return s.baseValue;
        return fallback;
    }

    static void Set(CharacterData data, StatType stat, float value)
    {
        for (int i = 0; i < data.stats.Count; i++)
            if (data.stats[i].stat == stat) { var s = data.stats[i]; s.baseValue = value; data.stats[i] = s; return; }
    }
}
