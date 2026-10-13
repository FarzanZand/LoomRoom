using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Co-op: the other players at the top left of the HUD, one row each (authored in the scene; Set Up Co-op builds it
// from the health bar): their name and, on the table, their health, or what they are doing instead.
public class PartyHudUI : MonoBehaviour
{
    [System.Serializable]
    public class Row
    {
        public GameObject root;
        public TMP_Text name;
        public Image fill;
        public TMP_Text value;
    }

    [SerializeField] List<Row> rows = new();

    readonly List<CoopPlayer> others = new();

    void LateUpdate()
    {
        others.Clear();
        if (Coop.Running)
            foreach (var p in MultiplayerManager.Instance.Players) if (p != null && !p.IsOwner) others.Add(p);
        others.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            bool shown = i < others.Count;
            if (row.root != null && row.root.activeSelf != shown) row.root.SetActive(shown);
            if (!shown) continue;
            var p = others[i];
            var s = p.Current;
            if (row.name != null) row.name.text = p.DisplayName;
            bool onTable = s.inRun && s.place == CoopPlace.Table;
            UIBar.Set(row.fill, onTable && s.alive ? s.health / 255f : 0f);
            if (row.value != null)
                row.value.text = s.inRun && !s.alive ? "watching" : onTable ? "" : s.ready ? "ready" : s.atTable ? "at the table" : "in the room";
        }
    }
}
