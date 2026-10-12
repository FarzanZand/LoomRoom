using UnityEngine;

// Editor and development builds: the console names each room the table player walks into (its room template, or
// "generated"). Clicking the line selects the template prefab in the Project window.
public partial class DungeonGenerator
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    int loggedRoom = -1;
    object loggedFloor; // the templates array, new for every floor

    void Update()
    {
        if (Layout == null || templates == null || !PlayerManager.HasInstance) return;
        var player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        if (player == null || !player.isActiveAndEnabled) return;
        int room = Layout.RoomAt(CellOf(player.transform.position));
        if (room < 0 || (room == loggedRoom && loggedFloor == templates)) return;
        loggedRoom = room; loggedFloor = templates;
        var template = room < templates.Length ? templates[room] : null;
        string role = Roles != null && room < Roles.Length ? Roles[room].ToString() : "?";
        if (template == null) { Debug.Log($"[Room] Floor {FloorNumber}, room {room} ({role}): generated, no template", this); return; }
        string path = template.gameObject.name;
#if UNITY_EDITOR
        string asset = UnityEditor.AssetDatabase.GetAssetPath(template.gameObject);
        if (!string.IsNullOrEmpty(asset)) path = asset;
#endif
        Debug.Log($"[Room] Floor {FloorNumber}, room {room} ({role}): <b>{template.gameObject.name}</b>  {path}", template.gameObject);
    }
#endif
}
