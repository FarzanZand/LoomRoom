using UnityEngine;

// One plaque of the trophy wall as a place to use (the user, 2026-11-11: a carried trophy's own place is highlighted
// and it is hung by standing in front of that place). Made at run time by TrophyWall on a child of each plaque, with a
// trigger box round the plaque and an InteractableTrigger; while the plaque's trophy is carried it can be used, and
// using it hangs that one trophy (TrophyWall.Hang). Nothing to set up by hand.
public class TrophyMountSpot : MonoBehaviour, IInteractable
{
    TrophyWall wall;
    TrophyWall.Mount mount;

    public void Bind(TrophyWall owner, TrophyWall.Mount forMount) { wall = owner; mount = forMount; }

    public string Prompt
    {
        get
        {
            var item = wall != null ? wall.TrophyOf(mount) : null;
            return item != null ? $"Hang the {item.itemName}" : "Hang the trophy";
        }
    }

    public bool CanInteract(Character who) => wall != null && who is Player p && p.kind == PlayerKind.Room && wall.IsCarried(mount);

    public void Interact(Character who)
    {
        if (CanInteract(who)) wall.Hang(mount);
    }
}
