using Sirenix.OdinInspector;
using UnityEngine;

// Something a skill gives besides its rank bonus: Blocking's shield bash (there from the start) and Deflect (its first
// milestone). One asset per ability in its skill's folder (Characters/Skills/<Skill>/), listed on the skill's data
// (AdventureSkillData.abilities); a milestone (ProgressionManager > Meta progression > Tracks) points at the one it
// unlocks. An ability with tuning of its own is a subclass (ShieldBashAbility).
[CreateAssetMenu(menuName = "LoomRoom/Skill Ability")]
public class SkillAbility : ScriptableObject
{
    public AdventureSkill skill;
    public string displayName;
    [PreviewField(32), Tooltip("Shown on the character sheet and the character panel's milestone markers.")] public Sprite icon;
    [TextArea(2, 4), Tooltip("Shown on the sheet's hover card.")] public string description;

#if UNITY_EDITOR
    const string ManagerPrefab = "Assets/Game/Core/Prefabs/ProgressionManager.prefab";

    // The progression manager the inspector reads milestones from: the open scene's, else the prefab's.
    public static ProgressionManager EditorManager()
    {
        if (ProgressionManager.HasInstance) return ProgressionManager.Instance;
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefab);
        return prefab != null ? prefab.GetComponent<ProgressionManager>() : null;
    }

    [ShowInInspector, ReadOnly, PropertyOrder(-1), Tooltip("A milestone that points at this ability unlocks it (bought on the sheet, or its trophy mounted); with none, it is there from the start.")]
    string Unlocked
    {
        get
        {
            var pm = EditorManager();
            if (pm != null)
                foreach (var (track, m) in pm.AllMilestones())
                    if (m.ability == this) return $"At {track.displayName} {m.value} (a milestone)";
            return "From the start";
        }
    }
#endif
}
