using System.Collections.Generic;
using UnityEngine;

// Every key item the room player can carry home, in one list: the trophies the Dungeon Master hands over, the puzzle's
// missing tile and the class posters. The key items row (KeyItemsUI) shows the carried ones and the dev console's
// addroomitem can give any of them, so a new kind of room item is one more entry in RoomKeyItems and works in both.
public partial class ProgressionManager
{
    public struct RoomKeyItem
    {
        public string name;               // as the console lists it and the key items row names its slot
        public Sprite icon;
        public bool carried;              // carried home and not yet put up, hung or placed
        public System.Action carry;       // the console: carried home now, whatever its state (taken down again if it was up)
    }

    // In the key items row's order (each new one is drawn to the left of the last).
    public List<RoomKeyItem> RoomKeyItems()
    {
        var list = new List<RoomKeyItem>();
        foreach (var (t, m) in AllMilestones())
        {
            var track = t; var milestone = m;
            list.Add(new RoomKeyItem
            {
                name = $"{track.displayName} {milestone.value} trophy",
                icon = milestone.trophy != null ? milestone.trophy.icon : track.icon,
                carried = Trophy(track, milestone) == TrophyState.Carried,
                carry = () => CarryTrophy(track, milestone),
            });
        }
        if (missingTile != null)
            list.Add(new RoomKeyItem { name = missingTile.itemName, icon = missingTile.icon, carried = TileState == 1, carry = () => CarryTile() });
        if (classRules != null && classRules.classes != null)
            foreach (var c in classRules.classes)
            {
                if (c == null || string.IsNullOrEmpty(c.unlockFlag)) continue;   // up from the start: never carried
                var cls = c;
                list.Add(new RoomKeyItem { name = $"{cls.displayName} poster", icon = cls.portrait, carried = PosterCarried(cls), carry = () => CarryPoster(cls) });
            }
        return list;
    }
}
