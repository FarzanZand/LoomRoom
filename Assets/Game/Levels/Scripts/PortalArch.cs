using System.Collections;
using UnityEngine;

// A stone archway with a blue shimmer in it (prefab Levels/Shared/Portal/Portal arch): put it in any room template and
// set Room to a room template; walking through takes the table player to a pocket, that one room built on its own
// beside the floor (Pocket Level gives the map and the rest; Biome gives its stone and enemies, e.g. a later biome as
// a taste of what's to come). The pocket gets a copy of the arch in a corner as the way back; stepping through it
// returns the player to where they went in and takes the pocket down.
public class PortalArch : MonoBehaviour
{
    public enum Mode { Enter = 0, Return = 1 }
    // When the way back shows in the pocket. Serialized by integer: append only.
    public enum WayBack { AtStart = 0, WhenRoomCleared = 1 }
    public Mode mode = Mode.Enter;
    [Tooltip("At Start: the way back stands in the pocket from the moment you arrive. When Room Cleared: it appears once every enemy in the pocket is dead.")]
    public WayBack wayBack = WayBack.AtStart;
    [Tooltip("The room template the portal leads to (any DungeonRoomTemplate prefab, e.g. the Skeleton Party room).")]
    public GameObject room;
    [Tooltip("The level the pocket is built from: a small authored map whose room 0 takes Room (Levels/Pockets/Pocket).")]
    public TableLevelData pocketLevel;
    [Tooltip("The pocket's biome (stone, enemies, light). Empty: the pocket level's own.")]
    public DungeonBiome biome;
    [Tooltip("The shimmer's size: the archway's opening, width and height.")]
    public Vector2 opening = new(2f, 2.8f);
    public Color shimmer = new(.35f, .65f, 1f, 1f);
    [Min(0f)] public float lightIntensity = 3f;
    [Tooltip("How far from the floor it is built, in metres along the floor's right (kept clear of the table and the room).")]
    public float pocketDistance = 400f;
    public DungeonMaster.Line enterLine = new() { text = "Go on. See what's on the other side." };
    [Tooltip("When Room Cleared: said as the way back appears.")]
    public DungeonMaster.Line wayBackLine = new() { text = "There. Your way out." };

    static DungeonGenerator activePocket;
    static TableLevelData activeLevel;
    static Vector3 cameFrom;
    static Quaternion cameFromFacing;
    static PortalArch entered;

    Material material;
    Light glow;
    bool busy;

    void Start()
    {
        // A Return gate is only ever made by an Enter portal, in its pocket.
        if (mode == Mode.Return && activePocket == null) { gameObject.SetActive(false); return; }
        BuildShimmer();
    }

    // A thin glowing sheet filling the arch, a soft blue light, a trigger to walk through.
    void BuildShimmer()
    {
        var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
        sheet.name = "Shimmer";
        Destroy(sheet.GetComponent<Collider>());
        sheet.transform.SetParent(transform, false);
        sheet.transform.localPosition = Vector3.up * opening.y * .5f;
        sheet.transform.localScale = new Vector3(opening.x, opening.y, 1f);
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetFloat("_Surface", 1f); material.SetFloat("_Blend", 1f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = 3000;
        var renderer = sheet.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var lightGo = new GameObject("Portal light");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = Vector3.up * opening.y * .5f + Vector3.forward * .4f;
        glow = lightGo.AddComponent<Light>();
        glow.type = LightType.Point; glow.color = shimmer; glow.range = 5f; glow.intensity = lightIntensity;
        var trigger = gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = Vector3.up * opening.y * .5f;
        trigger.size = new Vector3(opening.x, opening.y, .6f);
    }

    void Update()
    {
        if (material == null) return;
        // A slow shimmer: brightness and colour drift.
        float t = Time.time;
        float pulse = .55f + .25f * Mathf.Sin(t * 2.3f) + .1f * Mathf.Sin(t * 5.1f);
        var c = Color.Lerp(shimmer, Color.white, .15f + .1f * Mathf.Sin(t * 1.7f));
        c.a = pulse * .6f;
        material.SetColor("_BaseColor", c);
        if (glow != null) glow.intensity = lightIntensity * (.8f + .3f * Mathf.Sin(t * 2.3f));
    }

    void OnTriggerEnter(Collider other)
    {
        if (busy) return;
        var player = other.GetComponentInParent<Player>();
        if (player == null || player.kind != PlayerKind.Table || !player.IsAlive) return;
        StartCoroutine(mode == Mode.Enter ? Enter(player) : Return(player));
    }

    IEnumerator Enter(Player player)
    {
        if (room == null || pocketLevel == null || activePocket != null) yield break;
        busy = true;
        var floor = GetComponentInParent<DungeonGenerator>();
        cameFrom = transform.position + transform.forward * 1.4f;
        cameFromFacing = Quaternion.LookRotation(transform.forward);
        entered = this;
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeToBlack(.35f);
        yield return new WaitForSeconds(.4f);

        // A runtime copy of the pocket level with this portal's room (and biome) in it.
        activeLevel = Instantiate(pocketLevel);
        activeLevel.name = pocketLevel.name + ": " + room.name;
        if (activeLevel.authoredRooms != null && activeLevel.authoredRooms.Length > 0) activeLevel.authoredRooms[0].template = room;
        if (biome != null && activeLevel.stages != null && activeLevel.stages.Length > 0) activeLevel.stages[0].biome = biome;

        var go = new GameObject("Pocket: " + room.name);
        go.transform.position = (floor != null ? floor.transform.position : transform.position) + Vector3.right * pocketDistance;
        activePocket = go.AddComponent<DungeonGenerator>();
        int seed = floor != null ? floor.GetHashCode() ^ 7919 : Random.Range(1, 99999);
        activePocket.Build(activeLevel, seed, floor != null ? floor.FloorNumber : 1);
        if (wayBack == WayBack.AtStart) PlaceWayBack(activePocket);
        else StartCoroutine(WayBackWhenCleared(activePocket));

        player.Warp(activePocket.SpawnPoint, activePocket.SpawnRotation);
        if (player.Look != null) player.Look.SetYaw(activePocket.SpawnRotation.eulerAngles.y);
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeFromBlack(.5f);
        if (enterLine != null && !string.IsNullOrEmpty(enterLine.text)) DungeonMaster.Say(enterLine);
        busy = false;
    }

    // A copy of this arch as the way back. With a side room off the pocket's room (room 1), it stands at that room's
    // far side facing the way in; otherwise in a corner of the pocket's room, facing its middle (never on the paths
    // between the room's doors and its middle).
    void PlaceWayBack(DungeonGenerator pocket)
    {
        if (pocket.Layout == null || pocket.Layout.rooms.Count == 0) return;
        Vector3 at, face;
        if (pocket.Layout.rooms.Count > 1)
        {
            var side = pocket.Layout.rooms[1];
            Vector2 from = pocket.Layout.RoomCenter(0), mid = side.center - Vector2.one * .5f;
            var away = mid - from;
            var cell = Mathf.Abs(away.x) >= Mathf.Abs(away.y)
                ? new Vector2Int(away.x > 0 ? side.xMax - 1 : side.xMin, Mathf.FloorToInt(mid.y + .5f))
                : new Vector2Int(Mathf.FloorToInt(mid.x + .5f), away.y > 0 ? side.yMax - 1 : side.yMin);
            at = pocket.Cell(cell);
            face = pocket.Cell(Vector2Int.RoundToInt(from)) - at;
            face = Mathf.Abs(face.x) >= Mathf.Abs(face.z) ? new Vector3(Mathf.Sign(face.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(face.z));
        }
        else
        {
            var rect = pocket.Layout.rooms[0];
            at = pocket.Cell(new Vector2Int(rect.xMin, rect.yMin));
            face = pocket.Cell(pocket.Layout.RoomCenter(0)) - at; face.y = 0f;
        }
        // Built from this arch's own pieces before its shimmer existed: copy, then strip what Start added here.
        var copy = Instantiate(gameObject, at, face.sqrMagnitude > .01f ? Quaternion.LookRotation(face) : Quaternion.identity, pocket.transform);
        copy.name = "Way back";
        var gate = copy.GetComponent<PortalArch>();
        gate.mode = Mode.Return;
        foreach (Transform child in copy.transform) if (child.name == "Shimmer" || child.name == "Portal light") Destroy(child.gameObject);
        foreach (var box in copy.GetComponents<BoxCollider>()) if (box.isTrigger) Destroy(box);
    }

    // The way back appears with a puff once nothing in the pocket is left alive.
    IEnumerator WayBackWhenCleared(DungeonGenerator pocket)
    {
        yield return new WaitForSeconds(1f);
        while (pocket != null && activePocket == pocket)
        {
            bool anyone = false;
            foreach (var enemy in pocket.GetComponentsInChildren<EnemyBrain>())
                if (enemy != null && enemy.Character != null && enemy.Character.IsAlive) { anyone = true; break; }
            if (!anyone) break;
            yield return new WaitForSeconds(.5f);
        }
        if (pocket == null || activePocket != pocket) yield break;
        yield return new WaitForSeconds(1f);
        PlaceWayBack(pocket);
        if (wayBackLine != null && !string.IsNullOrEmpty(wayBackLine.text)) DungeonMaster.Say(wayBackLine);
    }

    IEnumerator Return(Player player)
    {
        if (activePocket == null) yield break;
        busy = true;
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeToBlack(.35f);
        yield return new WaitForSeconds(.4f);
        player.Warp(cameFrom, cameFromFacing);
        if (player.Look != null) player.Look.SetYaw(cameFromFacing.eulerAngles.y);
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeFromBlack(.5f);
        // The pocket goes once the player is out (this gate goes with it).
        Clear();
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
        // The floor this portal stood on is gone (the next floor, a death): its pocket goes too.
        if (entered == this) { entered = null; Clear(); }
    }

    public static void Clear()
    {
        if (activePocket != null) Destroy(activePocket.gameObject);
        if (activeLevel != null) Destroy(activeLevel);
        activePocket = null; activeLevel = null;
    }
}
