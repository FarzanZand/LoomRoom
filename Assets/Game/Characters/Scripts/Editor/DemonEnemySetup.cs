using UnityEditor;
using UnityEngine;

// Makes the Demon enemy (Characters/Enemies/Crypt/Demon): a copy of the Crypt Soldier (a sword fighter on the Humanoid
// enemy base) wearing the Synty Dark Fantasy demon: the base's own demon body and hair (already restyled in the pixel look)
// switched on, the soldier's skeleton off, and no bone burst. Red blood. Safe to run again.
public static class DemonEnemySetup
{
    const string Template = "Assets/Game/Characters/Enemies/Crypt/Crypt Soldier.prefab";
    static readonly string[] Parts = { "SM_Chr_Demon_Male_01", "SM_Chr_Attach_Hair_Demon_01" };
    const string Path_ = "Assets/Game/Characters/Enemies/Crypt/Demon.prefab";

    public static string Build()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Path_) == null)
            NewCharacterWindow.Create("Demon", AssetDatabase.LoadAssetAtPath<GameObject>(Template), Path_);

        var root = PrefabUtility.LoadPrefabContents(Path_);
        try
        {
            // The humanoid bases carry every Synty body, each switched on or off: the demon's body and hair on, the rest off.
            int parts = 0;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.name.StartsWith("Demon ")) { Object.DestroyImmediate(smr.gameObject); continue; }   // an older build's copies
                bool on = System.Array.IndexOf(Parts, smr.name) >= 0;
                smr.gameObject.SetActive(on);
                if (on) parts++;
            }

            // No helmet over the horns (the soldier's helmet sits on the head bone; HelmetPop finds none and does nothing).
            foreach (var mesh in root.GetComponentsInChildren<MeshRenderer>(true))
                if (mesh.name.Contains("Helmet")) mesh.gameObject.SetActive(false);

            // A demon is flesh: no bone burst (the soldier's skeleton death); the Humanoid base's ragdoll throws it instead.
            foreach (var burst in root.GetComponentsInChildren<DeathBurst>(true)) Object.DestroyImmediate(burst);

            var character = root.GetComponent<Character>();
            var data = character.data;
            if (data != null)
            {
                data.characterName = "Demon";
                data.creatureKind = CreatureKind.Humanoid;
                data.gore ??= new GoreProfile();
                data.gore.kind = BloodKind.Blood;
                data.gore.color = new Color(.72f, .05f, .04f);
                data.gore.burstOnDeath = false;
                EditorUtility.SetDirty(data);
            }
            PrefabUtility.SaveAsPrefabAsset(root, Path_);
            AssetDatabase.SaveAssets();
            return $"Demon at {Path_}: {parts} demon parts on, red blood";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
