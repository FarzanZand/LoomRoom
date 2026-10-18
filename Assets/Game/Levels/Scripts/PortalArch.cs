using System.Collections;
using UnityEngine;

// A stone archway with a blue shimmer in it (prefab Levels/Shared/Portal/Portal arch): put it in any room template and
// set Room to a room template; walking through takes the table player to a pocket, that one room built on its own
// beside the floor (Pocket Level gives the map and the rest; Biome gives its stone and enemies, e.g. a later biome as
// a taste of what's to come). The pocket gets a copy of the arch as the way back (in its side room, else a corner), and
// the player arrives just in front of it facing into the room; stepping through it returns the player to where they
// went in. The pocket is built with the floor (PrebuildAll, from the floor's seed), so every player in co-op has the
// same one and its enemies are indexed with the floor's; it stays up until the floor goes.
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
    [Tooltip("The shimmer's material (Levels/Shared/Portal/Portal shimmer: a small point-filtered pixel texture on the additive beam shader). Copied per arch and scrolled upward.")]
    public Material shimmerMaterial;
    [Tooltip("How fast the shimmer's texture scrolls up (texture heights a second).")]
    public float scrollSpeed = .25f;
    [Min(0f)] public float lightIntensity = 3f;
    [Tooltip("How far from the floor it is built, in metres along the floor's right (kept clear of the table and the room).")]
    public float pocketDistance = 400f;
    [Min(.5f), Tooltip("The player arrives this far in front of the pocket's way back, facing into the room.")]
    public float arriveDistance = 1.8f;
    public DungeonMaster.Line enterLine = new() { text = "Go on. See what's on the other side." };
    [Tooltip("When Room Cleared: said as the way back appears.")]
    public DungeonMaster.Line wayBackLine = new() { text = "There. Your way out." };

    // This machine's own player: where it went in.
    static Vector3 cameFrom;
    static Quaternion cameFromFacing;

    // An Enter portal's pocket (built with the floor), or a way back's own portal.
    DungeonGenerator pocket;
    TableLevelData pocketCopy;
    PortalArch owner;
    public DungeonGenerator Pocket => pocket;

    Material material;
    Light glow;
    bool busy;

    void Start()
    {
        // A Return gate is only ever made by an Enter portal, in its pocket.
        if (mode == Mode.Return && owner == null) { gameObject.SetActive(false); return; }
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
        // A referenced material (never a shader found by name: that can be stripped from a build and leave the gate
        // invisible), copied so each arch pulses on its own.
        if (shimmerMaterial == null) { Debug.LogWarning($"{name}: Portal arch has no Shimmer Material.", this); Destroy(sheet); }
        else material = new Material(shimmerMaterial);
        if (material != null)
        {
            var renderer = sheet.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
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
        c.a = pulse * .9f;
        material.SetColor("_BaseColor", c);
        // The pixel texture drifts upward, wrapping (the texture tiles vertically).
        material.SetTextureOffset("_BaseMap", new Vector2(0f, Mathf.Repeat(-t * scrollSpeed, 1f)));
        if (glow != null) glow.intensity = lightIntensity * (.8f + .3f * Mathf.Sin(t * 2.3f));
    }

    void OnTriggerEnter(Collider other) => TryUse(other);

    // The way back stands just behind where players arrive, so a blow can knock one into it: it takes a player only
    // once they have not been hit for a moment, and then still while they stand in it (walking on into it).
    void OnTriggerStay(Collider other) { if (mode == Mode.Return) TryUse(other); }

    void TryUse(Collider other)
    {
        if (busy) return;
        var player = other.GetComponentInParent<Player>();
        if (player == null || player.kind != PlayerKind.Table || !player.IsAlive) return;
        if (mode == Mode.Return && Time.time - lastHurt < hurtGrace) return;
        StartCoroutine(mode == Mode.Enter ? Enter(player) : Return(player));
    }

    [Min(0f), Tooltip("The way back takes no one hit less than this many seconds ago (a blow can knock a player into it).")]
    public float hurtGrace = 1.5f;
    static float lastHurt = -99f;
    static Character watched;
    static void OnWatchedHurt(DamageInfo info) => lastHurt = Time.time;

    // This machine's player in a pocket: its hits are watched for the way back.
    static void Watch(Player player)
    {
        var c = player != null ? player.GetComponent<Character>() : null;
        if (watched == c) return;
        if (watched != null) watched.Damaged -= OnWatchedHurt;
        watched = c;
        if (watched != null) watched.Damaged += OnWatchedHurt;
    }

    // Every Enter portal on a new floor builds its pocket now, from the floor's seed and the portal's place among the
    // floor's portals, so every machine builds the same pockets (TableLevelLoader, before co-op indexes the floor).
    public static void PrebuildAll(DungeonGenerator floor, int floorSeed)
    {
        if (floor == null) return;
        int index = 0;
        foreach (var portal in floor.GetComponentsInChildren<PortalArch>(true))
            if (portal.mode == Mode.Enter && portal.pocket == null) { index++; portal.Build(floor, unchecked(floorSeed * 486187739 + 7919 * index), index); }
    }

    // The pockets of a floor's portals (co-op indexes their enemies, chests and loot with the floor's).
    public static System.Collections.Generic.IEnumerable<DungeonGenerator> PocketsOf(DungeonGenerator floor)
    {
        if (floor == null) yield break;
        foreach (var portal in floor.GetComponentsInChildren<PortalArch>(true))
            if (portal.mode == Mode.Enter && portal.pocket != null) yield return portal.pocket;
    }

    void Build(DungeonGenerator floor, int seed, int index)
    {
        if (room == null || pocketLevel == null) return;
        // A runtime copy of the pocket level with this portal's room (and biome) in it.
        pocketCopy = Instantiate(pocketLevel);
        pocketCopy.name = pocketLevel.name + ": " + room.name;
        if (pocketCopy.authoredRooms != null && pocketCopy.authoredRooms.Length > 0) pocketCopy.authoredRooms[0].template = room;
        if (biome != null && pocketCopy.stages != null && pocketCopy.stages.Length > 0) pocketCopy.stages[0].biome = biome;

        var go = new GameObject("Pocket: " + room.name);
        // Each portal's pocket in its own place, well apart from the others.
        go.transform.position = (floor != null ? floor.transform.position : transform.position) + Vector3.right * pocketDistance + Vector3.forward * (index - 1) * 120f;
        pocket = go.AddComponent<DungeonGenerator>();
        pocket.Build(pocketCopy, seed, floor != null ? floor.FloorNumber : 1);
        if (wayBack == WayBack.AtStart) PlaceWayBack(pocket);
        else StartCoroutine(WayBackWhenCleared(pocket));
    }

    IEnumerator Enter(Player player)
    {
        // A portal set down after the floor was built (the Debug arena's) builds its pocket on first use.
        if (pocket == null) Build(GetComponentInParent<DungeonGenerator>(), Random.Range(1, 99999), 1);
        if (pocket == null) yield break;
        busy = true;
        cameFrom = transform.position + transform.forward * 1.4f;
        cameFromFacing = Quaternion.LookRotation(transform.forward);
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeToBlack(.35f);
        yield return new WaitForSeconds(.4f);

        // The player arrives just in front of the way back (where it stands or will stand), facing into the pocket's room.
        var arrive = pocket.SpawnPoint; var facing = pocket.SpawnRotation;
        if (WayBackSpot(pocket, out var gateAt, out var gateFace))
        {
            arrive = gateAt + gateFace * arriveDistance;
            if (UnityEngine.AI.NavMesh.SamplePosition(arrive, out var hit, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) arrive = hit.position;
            facing = Quaternion.LookRotation(gateFace);
        }
        // Co-op: teammates arriving one after another stand beside each other, not in one spot.
        if (Coop.Running && MultiplayerManager.HasInstance)
        {
            var beside = arrive + MultiplayerManager.Instance.SpawnOffset(facing);
            if (UnityEngine.AI.NavMesh.SamplePosition(beside, out var near, 1f, UnityEngine.AI.NavMesh.AllAreas)) arrive = near.position;
        }
        Watch(player);
        player.Warp(arrive, facing);
        if (player.Look != null) player.Look.SetYaw(facing.eulerAngles.y);
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeFromBlack(.5f);
        if (enterLine != null && !string.IsNullOrEmpty(enterLine.text)) DungeonMaster.Say(enterLine);
        busy = false;
    }

    // A copy of this arch as the way back. With a side room off the pocket's room (room 1), it stands at that room's
    // far side facing the way in; otherwise in a corner of the pocket's room, facing its middle (never on the paths
    // between the room's doors and its middle).
    void PlaceWayBack(DungeonGenerator pocket)
    {
        if (!WayBackSpot(pocket, out var at, out var face)) return;
        // Built from this arch's own pieces before its shimmer existed: copy, then strip what Start added here.
        var copy = Instantiate(gameObject, at, face.sqrMagnitude > .01f ? Quaternion.LookRotation(face) : Quaternion.identity, pocket.transform);
        copy.name = "Way back";
        var gate = copy.GetComponent<PortalArch>();
        gate.mode = Mode.Return; gate.owner = this; gate.pocket = null; gate.pocketCopy = null;
        foreach (Transform child in copy.transform) if (child.name == "Shimmer" || child.name == "Portal light") Destroy(child.gameObject);
        foreach (var box in copy.GetComponents<BoxCollider>()) if (box.isTrigger) Destroy(box);
    }

    // Where the way back stands and the way it faces (into the pocket's room).
    static bool WayBackSpot(DungeonGenerator pocket, out Vector3 at, out Vector3 face)
    {
        at = face = Vector3.zero;
        if (pocket.Layout == null || pocket.Layout.rooms.Count == 0) return false;
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
            face = face.sqrMagnitude > .01f ? face.normalized : Vector3.forward;
        }
        return true;
    }

    // The way back appears with a puff once nothing in the pocket is left alive.
    IEnumerator WayBackWhenCleared(DungeonGenerator pocket)
    {
        yield return new WaitForSeconds(1f);
        while (pocket != null && this.pocket == pocket)
        {
            bool anyone = false;
            foreach (var enemy in pocket.GetComponentsInChildren<EnemyBrain>())
                if (enemy != null && enemy.Character != null && enemy.Character.IsAlive) { anyone = true; break; }
            if (!anyone) break;
            yield return new WaitForSeconds(.5f);
        }
        if (pocket == null || this.pocket != pocket) yield break;
        yield return new WaitForSeconds(1f);
        PlaceWayBack(pocket);
        // Said only to a player inside the pocket (in co-op the others may be anywhere on the floor).
        var me = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        bool inside = me != null && Vector3.Distance(me.transform.position, pocket.transform.position) < pocketDistance * .5f;
        if (inside && wayBackLine != null && !string.IsNullOrEmpty(wayBackLine.text)) DungeonMaster.Say(wayBackLine);
    }

    IEnumerator Return(Player player)
    {
        busy = true;
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeToBlack(.35f);
        yield return new WaitForSeconds(.4f);
        // Back where this player went in, else in front of the portal. The pocket stays up (others may be in it).
        var portal = owner != null ? owner.transform : null;
        var to = cameFrom; var facing = cameFromFacing;
        if (portal != null && (to - portal.position).sqrMagnitude > 9f) { to = portal.position + portal.forward * 1.4f; facing = Quaternion.LookRotation(portal.forward); }
        player.Warp(to, facing);
        if (player.Look != null) player.Look.SetYaw(facing.eulerAngles.y);
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeFromBlack(.5f);
        busy = false;
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
        // The floor this portal stood on is gone (the next floor, a death): its pocket goes too.
        if (pocket != null) Destroy(pocket.gameObject);
        if (pocketCopy != null) Destroy(pocketCopy);
        pocket = null; pocketCopy = null;
    }
}
