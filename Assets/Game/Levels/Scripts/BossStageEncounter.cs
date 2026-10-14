using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// A boss fight on the stage in front of the Dungeon Master (DungeonStage, run by the milestone's BossFight). Added by the
// generator. The stage stands empty until the player walks to its middle; then the music starts, the stair is shut,
// the DM says his line, steps up to the table, leans over and sets the boss down on the stage with his hand, and after
// a moment it attacks. During the fight the DM fires his beam and does whatever else the fight's events ask (by
// seconds, beats of the music or the boss's health) and he only talks about the fight. When the boss falls the stair
// opens, the way on appears on the stage and the DM straightens up again.
public class BossStageEncounter : MonoBehaviour
{
    DungeonGenerator dungeon;
    DungeonMilestone milestone;
    BossFight fight;
    DungeonExit exit;
    Character boss;
    EnemyBrain brain;
    DungeonMasterHand hand;
    DungeonMasterBeam beam;
    DungeonMasterSeat seat;
    readonly List<GameObject> stairGates = new();
    readonly HashSet<BossEvent> done = new();
    readonly Dictionary<BossEvent, float> nextBeat = new();
    float fightStart = -1f, musicStart = -1f, lastComment = -100f;
    bool started, finished, halfSaid, lowSaid, playerLowSaid, darkened;
    Coroutine beamRun;

    // The fight in progress (a debug view and tests read it).
    public static BossStageEncounter Current { get; private set; }
    public Character Boss => boss;
    public bool Started => started;
    public bool Fighting => fightStart >= 0f && !finished;
    public float FightTime => fightStart >= 0f ? Time.time - fightStart : 0f;
    // Beats of the fight's music since its first beat (from when the music started).
    public float Beat => musicStart >= 0f && fight != null ? (Time.time - musicStart - fight.firstBeat) / fight.SecondsPerBeat : 0f;

    public void Initialize(DungeonGenerator generator, DungeonMilestone data, DungeonExit stageExit)
    {
        dungeon = generator; milestone = data; fight = data.fight; exit = stageExit;
        if (exit != null && milestone.sealExit) exit.Sealed = true;
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
        if (started && !finished) Cleanup(false);
    }

    void Update()
    {
        if (finished || dungeon == null || dungeon.Stage == null) return;
        if (!PlayerManager.HasInstance || PlayerManager.Instance.ActiveKind != PlayerKind.Table) return;
        var player = PlayerManager.Instance.Active;
        if (!started)
        {
            if (player == null || !player.IsAlive) return;
            var centre = dungeon.StageCentre;
            var flat = player.transform.position - centre; flat.y = 0;
            if (flat.magnitude <= dungeon.Stage.startRadius && Mathf.Abs(player.transform.position.y - centre.y) < 1.2f) StartCoroutine(Open(player));
            return;
        }
        if (player != null && !player.IsAlive) { Defeat(); return; }
        if (Fighting) RunEvents(player);
    }

    // ── Opening ───────────────────────────────────────────────────────

    IEnumerator Open(Player player)
    {
        started = true; Current = this;
        DungeonMasterRemarks.Hold = true;
        FindDungeonMaster();
        ShutStair(true);
        if (fight != null && fight.music != null && AudioManager.HasInstance)
        {
            AudioManager.Instance.CrossfadeMusic(fight.music, true, fight.musicFade, fight.musicVolume);
            musicStart = Time.time;
        }
        if (milestone.introSting != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(milestone.introSting, milestone.stingVolume);
        // The room goes dark: the stage and his eyes are all that is lit.
        if (fight != null && fight.darkRoom && LightingManager.Instance != null)
        {
            var dark = dungeon.LevelData.DungeonLighting(dungeon.FloorNumber);
            dark.roomDark = 1f;
            LightingManager.Instance.BlendToMood(dark, fight.darkenSeconds);
            darkened = true;
        }
        if (fight != null) Say(fight.openingLine, true);
        var spot = BossSpot();
        if (seat != null) seat.Focus(StandFor(spot), spot);
        if (hand != null) hand.Watch(spot, true);
        yield return new WaitForSeconds(fight != null ? fight.reachDelay : 1f);

        boss = dungeon.SpawnStageBoss(spot, Quaternion.LookRotation(Flat(player.transform.position - spot)));
        if (boss == null) { Debug.LogWarning("Boss stage: the boss could not be placed.", this); Finish(true); yield break; }
        brain = boss.GetComponent<EnemyBrain>();
        if (brain != null) brain.Wary = true;
        boss.Died += OnBossDied;
        yield return Carry(boss, spot, fight != null ? fight.placeSeconds : 2f);
        yield return new WaitForSeconds(fight != null ? fight.wakeDelay : 1f);
        if (boss == null || !boss.IsAlive) yield break;

        if (BossBarUI.HasInstance) BossBarUI.Instance.Show(boss, milestone.title);
        if (!string.IsNullOrWhiteSpace(milestone.introMessage)) MessageLog.Post(milestone.introMessage.Trim(), MessageKind.Warning);
        if (fight != null) Say(fight.fightLine, true);
        if (brain != null) { brain.enabled = true; brain.Alert(); }
        if (hand != null) hand.Watch(player.transform.position, false);
        fightStart = Time.time;
    }

    // The DM carries an enemy down onto the stage and lets go: its brain and agent wait in his hand.
    IEnumerator Carry(Character figure, Vector3 spot, float seconds)
    {
        var figureBrain = figure.GetComponent<EnemyBrain>();
        var agent = figure.GetComponent<NavMeshAgent>();
        // Everything that would hold it to the floor waits while it is in his hand.
        var motor = figure.GetComponent<EnemyMotor>();
        var bodies = figure.GetComponentsInChildren<Rigidbody>();
        if (figureBrain != null) figureBrain.enabled = false;
        if (agent != null) agent.enabled = false;
        if (motor != null) motor.enabled = false;
        var kinematic = new bool[bodies.Length];
        for (int i = 0; i < bodies.Length; i++) { kinematic[i] = bodies[i].isKinematic; bodies[i].isKinematic = true; }
        if (hand != null) yield return hand.Place(figure.transform, spot, seconds, TableTop(), () => Landed(figure, spot));
        else { figure.transform.position = spot; Landed(figure, spot); }
        for (int i = 0; i < bodies.Length; i++) if (bodies[i] != null) bodies[i].isKinematic = kinematic[i];
        if (motor != null) motor.enabled = true;
        if (agent != null) { agent.enabled = true; agent.Warp(spot); }
        if (figureBrain != null && figure != boss) { figureBrain.enabled = true; figureBrain.Alert(); }
    }

    float TableTop() => TableManager.HasInstance && TableManager.Instance.GetComponent<TableCut>() is { } cut && cut.Bounds.size != Vector3.zero ? cut.Bounds.max.y : dungeon.transform.position.y;

    void Landed(Character figure, Vector3 spot)
    {
        // A puff of the table's dust where it lands.
        var dust = WorldManager.HasInstance && WorldManager.Instance.tableLevelReveal != null ? WorldManager.Instance.tableLevelReveal.dustPrefab : null;
        if (dust != null)
        {
            var puff = Instantiate(dust, spot + Vector3.up * .1f, Quaternion.identity);
            var shape = puff.shape; shape.scale = new Vector3(2.5f, 2.5f, .1f);
            var emission = puff.emission; emission.rateOverTimeMultiplier = 0f; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
            var main = puff.main; main.loop = false;
            puff.Play();
            Destroy(puff.gameObject, 4f);
        }
        if (fight != null && fight.landSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(fight.landSound, spot, fight.landVolume);
        Shake(.6f);
    }

    // ── During the fight ──────────────────────────────────────────────

    void RunEvents(Player player)
    {
        if (fight == null || boss == null) return;
        float t = FightTime, beat = Beat, health = boss.Stats != null ? boss.Stats.CurrentHealth / Mathf.Max(1f, boss.Stats.MaxHealth) : 1f;
        foreach (var e in fight.events)
        {
            if (e == null || !e.enabled) continue;
            switch (e.when)
            {
                case BossEvent.When.AtSeconds: if (!done.Contains(e) && t >= e.seconds) { done.Add(e); Do(e, player); } break;
                case BossEvent.When.AtBeat: if (!done.Contains(e) && BeatClock(t) >= e.startBeat) { done.Add(e); Do(e, player); } break;
                case BossEvent.When.AtBossHealth: if (!done.Contains(e) && health <= e.health) { done.Add(e); Do(e, player); } break;
                case BossEvent.When.EveryBeats:
                    if (!nextBeat.TryGetValue(e, out float at)) at = nextBeat[e] = e.startBeat;
                    if (BeatClock(t) >= at) { nextBeat[e] = at + Mathf.Max(1f, e.beats); Do(e, player); }
                    break;
            }
        }
        // His comments on how it goes.
        if (!halfSaid && health <= .5f) { halfSaid = true; Comment(fight.bossHalf); }
        if (!lowSaid && health <= .2f) { lowSaid = true; Comment(fight.bossLow); }
        if (!playerLowSaid && player != null && player.Stats != null && player.Stats.CurrentHealth <= player.Stats.MaxHealth * .3f) { playerLowSaid = true; Comment(fight.playerLow); }
        _ = beat;
    }

    // Beats since the fight began when there is no music of the fight's own, else the music's beat.
    float BeatClock(float seconds) => musicStart >= 0f ? Beat : seconds / fight.SecondsPerBeat;

    // Runs the first event of a kind now, out of its turn (tests and the debug view).
    public void Trigger(BossEvent.What what)
    {
        var e = fight != null ? fight.events.Find(x => x != null && x.what == what) : null;
        if (e != null && Fighting) Do(e, PlayerManager.Instance.Active);
    }

    void Do(BossEvent e, Player player)
    {
        switch (e.what)
        {
            case BossEvent.What.Beam: if (beamRun == null && beam != null && player != null) beamRun = StartCoroutine(Beam(player)); break;
            case BossEvent.What.Say: Say(e.line, true); break;
            case BossEvent.What.Music:
                if (e.music != null && AudioManager.HasInstance) { AudioManager.Instance.CrossfadeMusic(e.music, true, fight.musicFade, e.volume); musicStart = Time.time; }
                break;
            case BossEvent.What.Spawn: if (e.enemy != null) StartCoroutine(SpawnAdds(e.enemy, e.count)); break;
            case BossEvent.What.Shake: Shake(.5f * e.shake); break;
            case BossEvent.What.Volley: if (player != null && e.spell != null) StartCoroutine(Volley(e, player)); break;
        }
    }

    IEnumerator Beam(Player player)
    {
        // On the next beat of the music, so it lands with it.
        if (fight.beamOnBeat && musicStart >= 0f)
        {
            float wait = (Mathf.Ceil(Beat) - Beat) * fight.SecondsPerBeat;
            if (wait > .02f) yield return new WaitForSeconds(wait);
        }
        Comment(fight.beamWarned, true);
        bool? held = null;
        // The boss gives way while the DM pours: it backs off and waits, so the player can face him and hold.
        System.Func<bool> giveWay = () => GiveWay(player);
        if (brain != null && fight.beamGiveWay > 0f) brain.MovementOverride = giveWay;
        yield return beam.Fire(player, fight, ok => held = ok);
        if (brain != null && brain.MovementOverride == giveWay) brain.MovementOverride = null;
        if (held == true) { Comment(fight.beamBlocked, true); if (fight.beamBlockedSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(fight.beamBlockedSound, fight.beamVolume); }
        else if (held == false) Comment(fight.beamHit, true);
        beamRun = null;
    }

    // The boss during the beam: back to a few metres from the player, facing them, no attacks (a swing already under
    // way finishes first: EnemyBrain only asks while it isn't swinging).
    bool GiveWay(Player player)
    {
        if (boss == null || !boss.IsAlive || brain == null || player == null) return false;
        var away = boss.transform.position - player.transform.position; away.y = 0;
        if (away.sqrMagnitude < .01f) away = -player.transform.forward;
        if (away.magnitude < fight.beamGiveWay)
        {
            var speed = brain.Data != null && brain.Data.Behaviour.useChaseSpeed ? brain.Data.Behaviour.chaseSpeed : 3f;
            brain.Motor.Strafe(away.normalized, speed * .8f);
        }
        else brain.Motor.Stop();
        brain.Motor.LookAt(player.transform.position, 360f);
        return true;
    }

    // His fireball volley: from his left hand, raised toward the player while the balls appear and hang.
    readonly List<FireballVolley> volleys = new();
    IEnumerator Volley(BossEvent e, Player player)
    {
        while (beamRun != null) yield return null;
        bool done = false;
        if (hand != null) hand.Aim(player.transform.position + Vector3.up);
        Comment(fight.volleyWarned);
        // Out of his raised palm, sent out over the stage to hang between him and the player, a little above their
        // eyes, where they can be seen coming.
        System.Func<Vector3> palm = () => hand != null && hand.LeftHand != null ? hand.LeftHand.position : dungeon.StageCentre + Vector3.up * 4f;
        var eye = player.transform.position + Vector3.up * 1.6f;
        var row = eye + Flat(palm() - eye) * fight.volleyDistance + Vector3.up * .4f;
        var v = FireballVolley.Cast(e.spell, null, palm, player, e.damage, e.volley, player, "the Dungeon Master", () => done = true, row);
        if (v != null) volleys.Add(v);
        float raised = e.volley.count * e.volley.spawnInterval + e.volley.riseSeconds;
        for (float t = 0; t < raised && !done && Fighting; t += Time.deltaTime) { if (hand != null) hand.Aim(player.transform.position + Vector3.up); yield return null; }
        if (hand != null && beamRun == null) hand.StopAiming();
    }

    IEnumerator SpawnAdds(GameObject prefab, int count)
    {
        for (int i = 0; i < count && Fighting; i++)
        {
            while (hand != null && hand.Busy) yield return null;
            var spot = dungeon.StageCentre + Random.insideUnitSphere.normalized * dungeon.LevelData.cellSize * 2f; spot.y = dungeon.StageCentre.y;
            if (NavMesh.SamplePosition(spot, out var hit, 2f, NavMesh.AllAreas)) spot = hit.position;
            var go = Instantiate(prefab, spot, Quaternion.identity, dungeon.transform);
            var added = go.GetComponent<Character>();
            if (added != null) yield return Carry(added, spot, fight.placeSeconds * .7f);
        }
    }

    // ── The end ───────────────────────────────────────────────────────

    void OnBossDied()
    {
        if (finished) return;
        Comment(fight != null ? fight.victory : null, true);
        if (RunManager.HasInstance) RunManager.Instance.NoteBossSlain();
        MessageLog.Post($"The {boss.DisplayName} falls! The way on is open.", MessageKind.Good);
        Finish(true);
    }

    void Defeat()
    {
        if (finished) return;
        Comment(fight != null ? fight.defeat : null, true);
        Finish(false);
    }

    void Finish(bool won)
    {
        finished = true;
        Cleanup(won);
        if (won && exit != null)
        {
            exit.Sealed = false;
            exit.gameObject.SetActive(true);
            if (AudioManager.HasInstance && milestone.introSting != null) AudioManager.Instance.PlaySFX(milestone.introSting, exit.transform.position, .5f);
        }
    }

    void Cleanup(bool won)
    {
        StopAllCoroutines(); beamRun = null;
        if (beam != null) beam.Stop();
        foreach (var v in volleys) if (v != null) v.Stop();
        if (hand != null) hand.Release();
        if (seat != null) seat.Unfocus();
        ShutStair(false);
        DungeonMasterRemarks.Hold = false;
        if (darkened && LightingManager.Instance != null && dungeon != null) LightingManager.Instance.BlendToMood(dungeon.LevelData.DungeonLighting(dungeon.FloorNumber), 2.5f);
        darkened = false;
        if (BossBarUI.HasInstance) BossBarUI.Instance.Hide();
        if (boss != null) boss.Died -= OnBossDied;
        if (won && AudioManager.HasInstance && dungeon != null)
        {
            var music = dungeon.LevelData.MusicFor(dungeon.FloorNumber, out float volume);
            if (music != null) AudioManager.Instance.CrossfadeMusic(music, dungeon.LevelData.loopMusic, 3f, volume);
            else AudioManager.Instance.StopMusic(3f);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────

    void FindDungeonMaster()
    {
        var dm = TableManager.HasInstance ? TableManager.Instance.DM : null;
        hand = dm != null ? dm.GetComponentInChildren<DungeonMasterHand>(true) : null;
        beam = dm != null ? dm.GetComponentInChildren<DungeonMasterBeam>(true) : null;
        seat = FindAnyObjectByType<DungeonMasterSeat>();
    }

    Vector3 BossSpot()
    {
        var spot = dungeon.StageBossSpot();
        return NavMesh.SamplePosition(spot, out var hit, 2f, NavMesh.AllAreas) ? hit.position : spot;
    }

    // He stands at the table's edge straight out from the spot (leanBack behind it, leaning over), so his reach covers it.
    Vector3 StandFor(Vector3 spot)
    {
        var dm = TableManager.HasInstance && TableManager.Instance.DM != null ? TableManager.Instance.DM.transform.position : spot + Vector3.right * 30f;
        var box = TableManager.HasInstance && TableManager.Instance.GetComponent<TableCut>() is { } cut && cut.Bounds.size != Vector3.zero ? cut.Bounds
            : TableManager.HasInstance ? TableManager.Instance.GetComponent<Collider>().bounds : new Bounds(spot, Vector3.one);
        var away = Flat(dm - spot);
        // From the spot out to the table's edge along that line, then a step back from it.
        float toEdge = 0f;
        for (float d = 0; d < 200f; d += .5f) { var p = spot + away * d; if (p.x < box.min.x || p.x > box.max.x || p.z < box.min.z || p.z > box.max.z) { toEdge = d; break; } }
        var at = spot + away * (toEdge + (seat != null ? seat.leanBack : 1.2f));
        return new Vector3(at.x, dm.y, at.z);
    }

    // The stair gets an invisible wall at its foot while the fight is on.
    void ShutStair(bool shut)
    {
        foreach (var g in stairGates) if (g != null) Destroy(g);
        stairGates.Clear();
        if (!shut || dungeon == null || dungeon.StageRoom < 0) return;
        var layout = dungeon.Layout;
        foreach (var c in layout.RoomCells(dungeon.StageRoom))
            foreach (var d in DungeonLayout.Neighbours)
            {
                var o = c + d;
                if (!layout.InBounds(o) || !layout.floor[o.x, o.y] || layout.InRoom(dungeon.StageRoom, o)) continue;
                var g = new GameObject("Stage stair shut");
                g.transform.SetParent(dungeon.transform, false);
                g.transform.position = dungeon.Cell(c) + new Vector3(d.x, 0, d.y) * dungeon.LevelData.cellSize * .5f + Vector3.up * 1.5f;
                var box = g.AddComponent<BoxCollider>();
                float s = dungeon.LevelData.cellSize;
                box.size = d.x != 0 ? new Vector3(.2f, 3f, s) : new Vector3(s, 3f, .2f);
                stairGates.Add(g);
            }
    }

    void Comment(List<DungeonMaster.Line> lines, bool always = false)
    {
        if (fight == null || lines == null || lines.Count == 0) return;
        if (!always && Time.time - lastComment < fight.commentGap) return;
        Say(lines[Random.Range(0, lines.Count)], true);
    }

    void Say(DungeonMaster.Line line, bool force)
    {
        if (line == null || string.IsNullOrWhiteSpace(line.text)) return;
        lastComment = Time.time;
        DungeonMaster.Say(line);
    }

    Unity.Cinemachine.CinemachineImpulseSource impulse;
    void Shake(float force)
    {
        if (impulse == null) impulse = gameObject.AddComponent<Unity.Cinemachine.CinemachineImpulseSource>();
        impulse.GenerateImpulseWithForce(force);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > .0001f ? v.normalized : Vector3.forward; }
}
