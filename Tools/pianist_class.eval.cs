// The Pianist (the user, 2026-11-25), safe to run again: the class asset Characters/Classes/Pianist.asset (made once with
// its starting numbers; a second run leaves its numbers, kit and words as they are and only fills links that are empty),
// its place at the end of Adventure Rules > Classes (the one list every class screen reads; never reorder it), its sheet
// portraits and face imported as the other classes' are, its piano hit sounds (Audio/Data/Pianist, from the clips
// Tools/piano_hits.py writes) set on its Crescendo, and its dungeon track imported Streaming.
// Before it: python Tools/piano_hits.py, python Tools/sheet_portraits.py Pianist, python Tools/class_faces.py Pianist.
if (UnityEngine.Application.isPlaying) return "playing";
var log = new System.Text.StringBuilder();
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
const string Classes = "Assets/Game/Characters/Classes/", Portraits = "Assets/Game/UI/Sprites/Meta/Portraits/";
const string Clips = "Assets/Game/Audio/Clips/Pianist/", Data = "Assets/Game/Audio/Data/Pianist/";
const string Music = "Assets/Game/Audio/Music/Self made/Journey.mp3";

// The dungeon track: music is streamed (a long clip unpacked on first play stalls the game).
var musicImporter = (UnityEditor.AudioImporter)UnityEditor.AssetImporter.GetAtPath(Music);
if (musicImporter == null) return "no " + Music;
var musicSettings = musicImporter.defaultSampleSettings;
if (musicSettings.loadType != UnityEngine.AudioClipLoadType.Streaming || musicSettings.preloadAudioData)
{
    musicSettings.loadType = UnityEngine.AudioClipLoadType.Streaming; musicSettings.preloadAudioData = false;
    musicImporter.defaultSampleSettings = musicSettings; musicImporter.SaveAndReimport();
    log.Append("Journey: streaming; ");
}

// The piano clips: short, so loaded with the scene (read from disk on first play, a clip costs a few ms mid-fight).
UnityEngine.AudioClip[] ClipsNamed(string prefix)
{
    var found = new System.Collections.Generic.List<UnityEngine.AudioClip>();
    for (int i = 1; i < 20; i++)
    {
        string path = Clips + prefix + " " + i + ".wav";
        var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.AudioImporter;
        if (importer == null) break;
        var s = importer.defaultSampleSettings;
        if (!s.preloadAudioData || importer.loadInBackground) { s.preloadAudioData = true; importer.defaultSampleSettings = s; importer.loadInBackground = false; importer.SaveAndReimport(); }
        found.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AudioClip>(path));
    }
    return found.ToArray();
}
AudioData Sound(string name, UnityEngine.AudioClip[] clips, float volume)
{
    if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Game/Audio/Data/Pianist")) UnityEditor.AssetDatabase.CreateFolder("Assets/Game/Audio/Data", "Pianist");
    string path = Data + name + ".asset";
    var data = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioData>(path);
    if (data == null)
    {
        data = UnityEngine.ScriptableObject.CreateInstance<AudioData>();
        // No pitch variance: the keys are in the key of the dungeon track, and a shifted clip would be out of tune with it.
        data.volume = volume; data.pitch = 1f; data.pitchVariance = 0f; data.minDistance = 2f; data.maxDistance = 30f;
        UnityEditor.AssetDatabase.CreateAsset(data, path);
        log.Append("made " + name + "; ");
    }
    if (clips.Length > 0 && (data.clips == null || data.clips.Length != clips.Length)) { data.clips = clips; UnityEditor.EditorUtility.SetDirty(data); log.Append(name + ": " + clips.Length + " clips; "); }
    return data;
}
var hits = ClipsNamed("Piano hit"); var big = ClipsNamed("Piano crescendo");
if (hits.Length == 0 || big.Length == 0) return "no piano clips in " + Clips + " (python Tools/piano_hits.py)";
var hitSound = Sound("Piano hit", hits, .5f);
var crescendoSound = Sound("Piano crescendo", big, .8f);

// The sheet portraits and the face: imported as the Cleric's are.
UnityEngine.Sprite Portrait(string kind)
{
    string path = Portraits + "Pianist " + kind + ".png", like = Portraits + "Cleric " + kind + ".png";
    var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
    var from = UnityEditor.AssetImporter.GetAtPath(like) as UnityEditor.TextureImporter;
    if (importer == null || from == null) { log.Append("no " + path + "; "); return null; }
    if (importer.textureType != from.textureType || importer.spriteImportMode != from.spriteImportMode || importer.filterMode != from.filterMode || importer.mipmapEnabled != from.mipmapEnabled
        || importer.textureCompression != from.textureCompression || importer.maxTextureSize != from.maxTextureSize || importer.spritePixelsPerUnit != from.spritePixelsPerUnit)
    {
        var settings = new UnityEditor.TextureImporterSettings();
        from.ReadTextureSettings(settings); importer.SetTextureSettings(settings);
        importer.textureCompression = from.textureCompression; importer.maxTextureSize = from.maxTextureSize;
        importer.SaveAndReimport();
        log.Append("imported " + kind + "; ");
    }
    return UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(path);
}
var pencil = Portrait("pencil"); var colour = Portrait("colour"); var face = Portrait("face");

T Load<T>(string path) where T : UnityEngine.Object
{
    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
    if (asset == null) log.Append("MISSING " + path + "; ");
    return asset;
}

// The class.
string classPath = Classes + "Pianist.asset";
var cls = UnityEditor.AssetDatabase.LoadAssetAtPath<AdventurerClass>(classPath);
var cleric = Load<AdventurerClass>(Classes + "Cleric.asset");
if (cls == null)
{
    cls = UnityEngine.ScriptableObject.CreateInstance<AdventurerClass>();
    cls.id = "pianist";                                   // saved: never change it
    cls.displayName = "Pianist";                          // no "The": the game writes "Level 1 Pianist", "as the Pianist"
    cls.description = "Plays the dungeon like a piano. Every enemy is a wrong note.";
    cls.unlockFlag = "";                                  // there from the start (for now, the user)
    cls.posterLine = new DungeonMaster.Line { text = "The Pianist. Count to eight.", mumble = true };
    cls.equipment = new[] { Load<ItemData>("Assets/Game/Items/Data/Weapons/Bronze Sword.asset"), Load<ItemData>("Assets/Game/Items/Data/Shields/Bronze Shield.asset") };
    cls.supplies = new[]
    {
        new ItemStack(Load<ItemData>("Assets/Game/Items/Data/Consumables/healPotion.asset"), 1),
        new ItemStack(Load<ItemData>("Assets/Game/Items/Data/Consumables/Mana Potion.asset"), 2),
        new ItemStack(Load<ItemData>("Assets/Game/Items/Data/Consumables/Bread.asset"), 2),
    };
    cls.leftHand = LeftHandSpell.Shield;
    // Quick and precise: two points of Dexterity (critical chance, backstab damage, swing speed), one off Intelligence
    // as the Warrior has, and no body armour. Crescendo is where its damage comes from. Health 35 against the Warrior's
    // 40 with armour: at 25 (its first numbers, with Constitution -1) a plain player died in the first pack room of
    // floor 1 where the Warrior got through three more (round 2, 2026-11-25).
    cls.strength = 0; cls.dexterity = 2; cls.constitution = 0; cls.intelligence = -1;
    cls.health = 35; cls.mana = 40; cls.stamina = 25;
    cls.healthPerLevel = 1.5f; cls.manaPerLevel = 1f;
    // 50 skill points: Swords 20, Blocking 10, Athletics 15, Trading 5.
    cls.skills = new int[AdventureSkills.Count];
    cls.skills[(int)AdventureSkill.Swords] = 20; cls.skills[(int)AdventureSkill.Blocking] = 10; cls.skills[(int)AdventureSkill.Athletics] = 15; cls.skills[(int)AdventureSkill.Trading] = 5;
    // Level with Dungeon1's own track as it is played: that one measures -14.2 LUFS and plays at .5 (-20.2), Journey
    // measures -18.0, so it needs -2.2 dB: .78, kept as .8 (ffmpeg ebur128 on both files, 2026-11-25).
    cls.dungeonMusicVolume = .8f;
    cls.ownDungeonFilter = true; cls.dungeonFilter = ScreenFilter.Noir;
    UnityEditor.AssetDatabase.CreateAsset(cls, classPath);
    log.Append("made the class; ");
}
// Links, filled only where empty (so a hand edit or Ciri's art stays).
bool changed = false;
void Fill<T>(ref T field, T value, string what) where T : UnityEngine.Object { if (field == null && value != null) { field = value; changed = true; log.Append("set " + what + "; "); } }
// The painting: HalfElfAxeman (the user, 2026-11-25: one no other class uses; it was DwarfWarrior, the Cleric's).
Fill(ref cls.portrait, Load<UnityEngine.Sprite>(Classes + "Portraits Highquality/HalfElfAxeman.jpg"), "portrait (Portraits Highquality/HalfElfAxeman)");
Fill(ref cls.sheetPortrait, pencil, "sheet portrait");
Fill(ref cls.sheetPortraitColour, colour, "sheet portrait in colour");
Fill(ref cls.face, face, "face");
Fill(ref cls.miniature, Load<UnityEngine.GameObject>("Assets/Game/Progression/Room/Miniatures/Pianist Miniature.prefab"), "table figure");
Fill(ref cls.deskProp, UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Game/World/Art/Class props/Pianist statue.prefab"), "desk statue");
Fill(ref cls.dungeonMusic, Load<UnityEngine.AudioClip>(Music), "dungeon music");
var crescendo = UnityEditor.AssetDatabase.LoadAssetAtPath<ClassPassive>(Classes + "Pianist/Crescendo.asset");
var mood = UnityEditor.AssetDatabase.LoadAssetAtPath<ClassAction>(Classes + "Pianist/Mood.asset");
Fill(ref cls.passive, crescendo, "passive Crescendo");
Fill(ref cls.action, mood, "action Mood");
if (crescendo == null) log.Append("no Crescendo.asset yet; ");
if (mood == null) log.Append("no Mood.asset yet; ");
if (changed) UnityEditor.EditorUtility.SetDirty(cls);

// Crescendo's sound (only this field; the rest of that asset is Karlach's). Piano hit is made but no longer played: only
// the crescendo plays piano (the user, 2026-11-25).
if (crescendo is CrescendoPassive passive && passive.crescendoSound != crescendoSound)
{
    var so = new UnityEditor.SerializedObject(passive);
    so.FindProperty("crescendoSound").objectReferenceValue = crescendoSound;
    so.ApplyModifiedPropertiesWithoutUndo();
    UnityEditor.EditorUtility.SetDirty(passive);
    log.Append("Crescendo: sound set; ");
}

// Every class screen reads Adventure Rules > Classes: the Pianist goes on its end.
var rules = Load<AdventureRules>("Assets/Game/Progression/Data/Adventure Rules.asset");
if (rules != null && System.Array.IndexOf(rules.classes, cls) < 0)
{
    var list = new System.Collections.Generic.List<AdventurerClass>(rules.classes) { cls };
    rules.classes = list.ToArray();
    UnityEditor.EditorUtility.SetDirty(rules);
    log.Append("added to Adventure Rules (place " + list.Count + "); ");
}
UnityEditor.AssetDatabase.SaveAssets();
log.Append("classes: ");
if (rules != null) foreach (var c in rules.classes) log.Append(c != null ? c.displayName + (c.Unlocked ? "" : " (locked)") + ", " : "null, ");
log.Append("scene dirty " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty);
return log.ToString();
