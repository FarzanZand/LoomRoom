using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;

// A boss fight on the stage in front of the Dungeon Master (DungeonStage, run by the milestone's BossFight). Added by the
// generator. The stage stands empty until the player walks to its middle; then the music starts, the stair is shut,
// the DM says his line, steps up to the table, leans over and sets the boss down on the stage with his hand, and after
// a moment it attacks. During the fight the DM fires his beam and does whatever else the fight's events ask (by
// seconds, beats of the music or the boss's health) and he only talks about the fight. When the boss falls the stair
// opens, the way on appears on the stage and the DM straightens up again.
public partial class BossStageEncounter : MonoBehaviour
{
    DungeonGenerator dungeon;
    DungeonMilestone milestone;
    BossFight fight;
    DungeonExit exit;
    Character boss;
    EnemyBrain brain;
    Vector3 bossHome;   // where he set the boss down: it kneels there during his beam
    DungeonMasterHand hand;
    DungeonMasterBeam beam;
    DungeonMasterSeat seat;
    readonly List<GameObject> stairGates = new();
    readonly HashSet<BossEvent> done = new();
    readonly Dictionary<BossEvent, float> nextBeat = new();
    float fightStart = -1f, musicStart = -1f, lastComment = -100f;
    // The boss's beam dance (BossFight.beamDance), played straight on its animator while it lasts.
    PlayableGraph danceGraph;
    bool started, finished, halfSaid, lowSaid, playerLowSaid, darkened, phaseTwo;
    // The player fell in the fight: it is put away, but a revive picks it up again (OnRevived).
    bool lost;
    BossStageLights stageLights;
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
        // The floor is being built behind the loading screen: the fight's music and sounds are loaded now (a long track
        // decompressed when it first plays stalls the game for a moment), and the painting changes, unseen.
        foreach (var clip in new[] { fight != null ? fight.music : null, milestone.introSting, fight != null ? fight.landSound : null,
                     fight != null ? fight.beamChargeSound : null, fight != null ? fight.beamLoopSound : null, fight != null ? fight.beamBlockedSound : null })
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        if (fight != null && fight.painting != null && RoomPainting.BehindDungeonMaster != null)
        {
            painting = RoomPainting.BehindDungeonMaster;
            painting.Show(fight.painting);
        }
    }

    void OnDestroy()
    {
        StopDance();
        if (Current == this) Current = null;
        if (RunManager.HasInstance) RunManager.Instance.Revived -= OnRevived;
        if (boss != null) boss.Died -= OnBossDied;
        if (started && !finished) Cleanup(false, floorGoing: true);
        else if (introPushed) EndIntro();
        // The floor is going (the next floor loading, or back to the room after a death): the painting's own again.
        if (painting != null) painting.Restore();
        DestroyRoomLights();
    }

    // ── The room's own lights for the fight (BossFight > Room) ────────
    RoomPainting painting;
    Light dmLight, paintingLight;

    Light RoomLight(string name, Color colour, float angle)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot; l.spotAngle = angle; l.innerSpotAngle = angle * .5f; l.range = 40f; l.intensity = 0f;
        l.color = colour; l.shadows = LightShadows.None;
        // Room renderers only: the miniature dungeon keeps its own darkness.
        FirstPersonLighting.SetLayers(l, LightingManager.RoomLayer);
        return l;
    }

    void DarkenRoom()
    {
        if (fight == null || !fight.changeRoomLighting || LightingManager.Instance == null) return;
        var look = dungeon.LevelData.DungeonLighting(dungeon.FloorNumber);
        look.roomDark = LightingManager.Darkness(fight.roomLighting);
        look.roomShade = new Color(fight.roomTint.r, fight.roomTint.g, fight.roomTint.b, 1f);
        LightingManager.Instance.BlendToMood(look, fight.darkenSeconds);
        darkened = true;
    }

    void LightRoom()
    {
        if (fight == null) return;
        if (fight.dungeonMasterLight > 0f && dmLight == null) dmLight = RoomLight("Boss fight: Dungeon Master light", fight.dungeonMasterLightColor, 55f);
        if (fight.paintingLight > 0f && paintingLight == null && RoomPainting.BehindDungeonMaster != null) paintingLight = RoomLight("Boss fight: painting light", fight.paintingLightColor, 45f);
    }

    // Each frame: on his face from the stage's side (he moves and leans), and on the painting from the room; faded in
    // with the room's darkening and out when the fight is over.
    void UpdateRoomLights()
    {
        float fade = fight != null && fight.darkenSeconds > 0f ? Time.deltaTime / fight.darkenSeconds : 1f;
        var head = hand != null && hand.GetComponent<Animator>() is { isHuman: true } a ? a.GetBoneTransform(HumanBodyBones.Head) : null;
        if (dmLight != null)
        {
            dmLight.intensity = Mathf.MoveTowards(dmLight.intensity, finished ? 0f : fight.dungeonMasterLight, fight.dungeonMasterLight * fade);
            if (head != null && dungeon != null)
            {
                var out_ = dungeon.StageCentre - head.position; out_.y = 0; out_ = out_.sqrMagnitude > .01f ? out_.normalized : Vector3.forward;
                // From below, out over the stage: lit like a face over a fire, his eyes left to glow.
                dmLight.transform.position = head.position + out_ * 10f - Vector3.up * 8f;
                dmLight.transform.LookAt(head.position);
            }
        }
        if (paintingLight != null && RoomPainting.BehindDungeonMaster is { } art)
        {
            paintingLight.intensity = Mathf.MoveTowards(paintingLight.intensity, finished ? 0f : fight.paintingLight, fight.paintingLight * fade);
            var from = head != null ? head.position : art.Centre + Vector3.down;
            paintingLight.transform.position = art.Centre + art.Facing(from) * 16f + Vector3.up * 5f;
            paintingLight.transform.LookAt(art.Centre);
        }
        if (finished && (dmLight == null || dmLight.intensity <= 0f) && (paintingLight == null || paintingLight.intensity <= 0f)) DestroyRoomLights();
    }

    void DestroyRoomLights()
    {
        if (dmLight != null) Destroy(dmLight.gameObject);
        if (paintingLight != null) Destroy(paintingLight.gameObject);
        dmLight = paintingLight = null;
    }

    // The fight starts once the player is up on the stage and well onto it: most of its top, not just the middle
    // (DungeonStage.startRadius is the least it reaches). The stair up is lower, so it doesn't count.
    float StartReach
    {
        get
        {
            float reach = dungeon.Stage.startRadius;
            if (dungeon.StageRoom >= 0 && dungeon.StageRoom < dungeon.Layout.rooms.Count)
            {
                var r = dungeon.Layout.rooms[dungeon.StageRoom];
                reach = Mathf.Max(reach, Mathf.Min(r.width, r.height) * dungeon.LevelData.cellSize * .5f * .8f);
            }
            return reach;
        }
    }

    void Update()
    {
        if (dmLight != null || paintingLight != null) UpdateRoomLights();
        if (finished || dungeon == null || dungeon.Stage == null) return;
        if (!PlayerManager.HasInstance || PlayerManager.Instance.ActiveKind != PlayerKind.Table) return;
        var player = PlayerManager.Instance.Active;
        if (!started)
        {
            if (player == null || !player.IsAlive) return;
            var centre = dungeon.StageCentre;
            var flat = player.transform.position - centre; flat.y = 0;
            if (flat.magnitude <= StartReach && Mathf.Abs(player.transform.position.y - centre.y) < 1.2f) StartCoroutine(Open(player));
            return;
        }
        // A death while he is still setting the boss down waits for the opening to finish, so a revive finds the fight set.
        if (player != null && !player.IsAlive) { if (fightStart >= 0f) Defeat(); return; }
        if (Fighting) RunEvents(player);
    }

    // ── Opening ───────────────────────────────────────────────────────

    IEnumerator Open(Player player)
    {
        started = true; Current = this;
        if (RunManager.HasInstance) { RunManager.Instance.Revived -= OnRevived; RunManager.Instance.Revived += OnRevived; }
        stageLights = dungeon.Stage != null ? dungeon.Stage.GetComponent<BossStageLights>() : null;
        DungeonMasterRemarks.Hold = true;
        BossBlessing.Grant(false);
        FindDungeonMaster();
        ShutStair(true);
        // With an intro (BossFight > Intro: a spotlight, dialogue, the boss's name) the floor's music fades out now and
        // the fight's starts with the name; without one the fight's music starts now.
        bool talk = fight != null && fight.HasIntroDialogue && PortraitDialogueUI.Current != null;
        var card = fight == null ? BossIntroCard.None
            : fight.introCard == BossIntroCard.BossTitle && BossTitleUI.Current != null ? BossIntroCard.BossTitle
            : fight.introCard == BossIntroCard.Versus && VersusScreenUI.Current != null ? BossIntroCard.Versus : BossIntroCard.None;
        bool intro = talk || card != BossIntroCard.None;
        if (intro && AudioManager.HasInstance) AudioManager.Instance.StopMusic(fight.enterMusicFade);
        if (!intro && fight != null && fight.music != null && AudioManager.HasInstance)
        {
            AudioManager.Instance.CrossfadeMusic(fight.music, true, fight.musicFade, fight.musicVolume);
            musicStart = Time.time;
        }
        if (milestone.introSting != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(milestone.introSting, milestone.stingVolume);
        // The room takes the fight's look (Dark: the stage and his eyes are all that is lit), and its own lights come up.
        DarkenRoom();
        LightRoom();
        if (fight != null && !intro) Say(fight.openingLine, true);
        var spot = BossSpot();
        if (seat != null) seat.Focus(StandFor(spot), spot);
        if (hand != null) hand.Watch(spot, true);
        yield return new WaitForSeconds(fight != null ? fight.reachDelay : 1f);
        // He reaches for the boss only once he stands at the table (his shoulder is where the reach is planned from).
        for (float t = 0; seat != null && !seat.Arrived && t < 4f; t += Time.deltaTime) yield return null;
        yield return new WaitForSeconds(.4f);

        boss = dungeon.SpawnStageBoss(spot, Quaternion.LookRotation(Flat(player.transform.position - spot)));
        if (boss == null) { Debug.LogWarning("Boss stage: the boss could not be placed.", this); Finish(true); yield break; }
        brain = boss.GetComponent<EnemyBrain>();
        if (brain != null) brain.Wary = true;
        boss.Died += OnBossDied;
        ShowSetup();
        yield return Carry(boss, spot, fight != null ? fight.placeSeconds : 2f);
        if (boss != null) bossHome = boss.transform.position;
        SpawnBackup();
        // Said once the boss is down and the room is dark, where it can be read.
        BossBlessing.Announce();
        if (intro)
        {
            yield return Intro(player, talk, card);
            if (boss == null || !boss.IsAlive) yield break;
        }
        else
        {
            yield return new WaitForSeconds(fight != null ? fight.wakeDelay : 1f);
            if (boss == null || !boss.IsAlive) yield break;
        }

        if (BossBarUI.HasInstance) BossBarUI.Instance.Show(boss, milestone.title, fight != null ? fight.bossName : null);
        if (!string.IsNullOrWhiteSpace(milestone.introMessage)) MessageLog.Post(milestone.Intro(boss, fight != null ? fight.bossName : null), MessageKind.Warning);
        if (fight != null && !intro) Say(fight.fightLine, true);
        if (stageLights != null) stageLights.Follow(boss.transform);
        if (brain != null) { brain.enabled = true; brain.Alert(); }
        if (hand != null) hand.Watch(player.transform.position, false);
        fightStart = Time.time;
    }

    // The intro (BossFight > Intro), the boss set down and idle in the quiet: a moment, the spotlight snaps on over it
    // with its sound, a moment more, the dialogue, then the card (the boss's name typed out as the fight's music starts,
    // or the versus screen, the music starting as it clears), and the battle begins. The player stands still and looks
    // at the boss throughout.
    IEnumerator Intro(Player player, bool talk, BossIntroCard card)
    {
        if (GameManager.HasInstance) { GameManager.Instance.Push(GameState.Dialogue); introPushed = true; }
        GameObject spotlight = null;
        try
        {
            if (player != null && player.Look != null && boss != null) StartCoroutine(CameraEase.LookAt(player.Look, boss.transform.position + Vector3.up * 1.5f, .6f));
            yield return new WaitForSeconds(fight.spotlightDelay);
            if (boss == null || !boss.IsAlive) yield break;
            if (fight.spotlight != null)
            {
                spotlight = Instantiate(fight.spotlight, boss.transform.position, Quaternion.identity, dungeon.transform);
                spotlight.name = "Boss spotlight";
            }
            if (fight.spotlightSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(fight.spotlightSound, fight.spotlightVolume);
            yield return new WaitForSeconds(fight.dialogueDelay);
            if (talk) yield return PortraitDialogueUI.Current.Play(fight.introLines, boss, fight.bossName);
            if (boss == null || !boss.IsAlive) yield break;
            string shownName = !string.IsNullOrWhiteSpace(fight.bossName) ? fight.bossName : boss.TheNameCap;
            if (card == BossIntroCard.BossTitle) yield return BossTitleUI.Current.Play(shownName, milestone.title, fight.title, StartFightMusic);
            else
            {
                if (card == BossIntroCard.Versus) yield return VersusScreenUI.Current.Play(player, boss, shownName, milestone.title, fight.versus);
                StartFightMusic();
            }
        }
        finally
        {
            EndIntro();
            if (spotlight != null) StartCoroutine(Dim(spotlight, fight.spotlightFadeOut));
        }
    }

    // The intro's Dialogue state, popped once by whoever ends it: the intro itself, or a fight cancelled in the middle of
    // it (Leave level, a death). Unity never runs the finally of a coroutine stopped by StopAllCoroutines or by its
    // object going, so a cancelled intro left the state pushed and the HUD hidden for good; its screens are put away too.
    bool introPushed;

    void EndIntro()
    {
        if (introPushed && GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dialogue);
        introPushed = false;
        if (PortraitDialogueUI.Current != null && PortraitDialogueUI.Current.Open && !PortraitDialogueUI.Current.Barking) PortraitDialogueUI.Current.Abort();
        if (BossTitleUI.Current != null) BossTitleUI.Current.Abort();
        if (VersusScreenUI.Current != null) VersusScreenUI.Current.Abort();
    }

    void StartFightMusic()
    {
        if (fight.music == null || !AudioManager.HasInstance) return;
        AudioManager.Instance.CrossfadeMusic(fight.music, true, fight.fightMusicFade, fight.musicVolume);
        musicStart = Time.time;
    }

    // The spotlight goes out as the battle begins: its lights fade, then it is gone.
    IEnumerator Dim(GameObject spotlight, float seconds)
    {
        var lights = spotlight.GetComponentsInChildren<Light>();
        var from = System.Array.ConvertAll(lights, l => l.intensity);
        for (float t = 0f; t < seconds && spotlight != null; t += Time.deltaTime)
        {
            for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].intensity = from[i] * (1f - t / seconds);
            yield return null;
        }
        if (spotlight != null) Destroy(spotlight);
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
        // Nothing of it shows or can be aimed at until it is in his hand (its weapon is put in its hand after it spawns).
        var colliders = figure.GetComponentsInChildren<Collider>(true);
        var solid = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++) { solid[i] = colliders[i].enabled; colliders[i].enabled = false; }
        var kinematic = new bool[bodies.Length];
        for (int i = 0; i < bodies.Length; i++) { kinematic[i] = bodies[i].isKinematic; bodies[i].isKinematic = true; }
        // Set down: as he lets go, or at once on the spot if the fight is put away while it is still in his hand
        // (Cleanup stops this coroutine; an add left there stayed frozen in the air, out of reach).
        bool down = false;
        System.Action<bool> setDown = null;
        setDown = cut =>
        {
            if (down) return;
            down = true;
            inHand.Remove(setDown);
            if (figure == null) return;
            if (cut) figure.transform.position = spot;
            for (int i = 0; i < bodies.Length; i++) if (bodies[i] != null) bodies[i].isKinematic = kinematic[i];
            for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = solid[i];
            if (motor != null) motor.enabled = true;
            if (agent != null) { agent.enabled = true; agent.Warp(spot); if (agent.isOnNavMesh) { agent.ResetPath(); agent.velocity = Vector3.zero; } }
            if (figureBrain != null && figure != boss) { figureBrain.enabled = true; figureBrain.Alert(); }
        };
        inHand.Add(setDown);
        if (hand != null) { yield return hand.Place(figure.transform, spot, seconds, TableTop(), () => Landed(figure, hand.LastPlacedAt)); spot = hand.LastPlacedAt; }
        else { figure.transform.position = spot; Landed(figure, spot); }
        setDown(false);
    }

    // Figures he is carrying right now (Carry): set down at once if the fight is put away mid-carry.
    readonly List<System.Action<bool>> inHand = new();

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
        // The music's beat on the stage's fires, and the second phase.
        if (stageLights != null) stageLights.Beat(musicStart >= 0f ? Beat : -1f);
        if (!phaseTwo && fight.phaseTwoHealth > 0f && health <= fight.phaseTwoHealth)
        {
            phaseTwo = true; halfSaid = true;
            if (stageLights != null) stageLights.PhaseTwo(fight != null ? fight.phaseTwoDisco : null);
            PhaseTwoShow();
            Shake(fight.phaseTwoShake);
        }
        ShowTick(health, player);
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
        // He stays leaning over the table while he casts (the user does not want him standing up for it).
        // As he starts to charge, the boss goes back to its place and kneels to him until the beam is over.
        System.Func<bool> kneel = () => Kneel(true);
        if (brain != null && fight.beamKneel)
        {
            brain.MovementOverride = kneel;
            for (float t = 0; t < fight.beamKneelLead && !Kneeling; t += Time.deltaTime) yield return null;
        }
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
        if (brain != null && fight.beamGiveWay > 0f && !fight.beamKneel) brain.MovementOverride = giveWay;
        yield return beam.Fire(player, fight, ok => held = ok);
        if (brain != null && brain.MovementOverride == giveWay) brain.MovementOverride = null;
        if (brain != null && brain.MovementOverride == kneel) { brain.MovementOverride = null; Kneel(false); if (boss != null && boss.IsAlive) brain.Alert(); }
        if (held == true) { Comment(fight.beamBlocked, true); if (fight.beamBlockedSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(fight.beamBlockedSound, fight.beamVolume); }
        else if (held == false) Comment(fight.beamHit, true);
        beamRun = null;
    }

    // The boss during the beam: back to the spot he set it down on, then facing the table and kneeling (a swing already
    // under way finishes first). Kneel(false) gets it up again.
    bool Kneel(bool on)
    {
        if (boss == null || brain == null) return false;
        var anim = boss.Animator;
        bool hasKneel = anim != null && Character.HasParameter(anim, "Kneel", AnimatorControllerParameterType.Bool);
        if (!on || !boss.IsAlive) { if (hasKneel) anim.SetBool("Kneel", false); StopDance(); brain.Motor.ResetSpeed(); return false; }
        var toHome = bossHome - boss.transform.position; toHome.y = 0;
        // Close enough is home: the player or the stage's pillars can stand in the way of the exact spot.
        if (toHome.magnitude > 1.8f || toHome.magnitude > .5f && !brain.Motor.ReachedDestination(.6f))
        {
            if (hasKneel) anim.SetBool("Kneel", false);
            brain.Motor.SetSpeed(brain.Data != null && brain.Data.Behaviour.useChaseSpeed ? brain.Data.Behaviour.chaseSpeed : 3.5f);
            brain.Motor.MoveTo(bossHome);
            return true;
        }
        brain.Motor.Stop();
        var dm = TableManager.HasInstance && TableManager.Instance.DM != null ? TableManager.Instance.DM.transform.position : boss.transform.position + boss.transform.forward;
        // A dancing boss shows off to the player instead of kneeling to the DM.
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        var look = fight.beamDance != null && player != null ? player.transform.position : dm;
        // Turned here, every frame, so nothing else steering the boss (a hit, a spell's aim) turns it back.
        brain.Motor.LookAt(look, 300f);
        brain.Motor.Face(look, 300f);
        if (!brain.Motor.IsFacing(look, 12f)) return true;
        if (fight.beamDance != null) StartDance(anim);
        else if (hasKneel) anim.SetBool("Kneel", true);
        return true;
    }

    // The dance has the intro's spotlight back on over it (with its sound) for as long as it lasts.
    GameObject danceSpot;

    void StartDance(Animator anim)
    {
        if (danceGraph.IsValid() || anim == null) return;
        var clip = AnimationPlayableUtilities.PlayClip(anim, fight.beamDance, out danceGraph);
        clip.SetSpeed(fight.beamDanceSpeed);
        if (fight.spotlight != null && danceSpot == null)
        {
            danceSpot = Instantiate(fight.spotlight, boss.transform.position, Quaternion.identity, dungeon.transform);
            danceSpot.name = "Boss spotlight (dance)";
        }
        if (fight.spotlight != null && fight.spotlightSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(fight.spotlightSound, fight.spotlightVolume);
    }

    void StopDance()
    {
        if (danceGraph.IsValid()) danceGraph.Destroy();
        if (danceSpot != null) { if (isActiveAndEnabled) StartCoroutine(Dim(danceSpot, fight.spotlightFadeOut)); else Destroy(danceSpot); danceSpot = null; }
    }

    bool Kneeling => danceGraph.IsValid() || boss != null && boss.Animator != null && Character.HasParameter(boss.Animator, "Kneel", AnimatorControllerParameterType.Bool) && boss.Animator.GetBool("Kneel");

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
        StopDance();
        // Also after the player fell (lost): a boss that dies then still opens the way on.
        if (finished && !lost) return;
        lost = false;
        Comment(fight != null ? fight.victory : null, true);
        if (RunManager.HasInstance) RunManager.Instance.NoteBossSlain();
        MessageLog.Post($"{(fight != null && !string.IsNullOrWhiteSpace(fight.bossName) ? fight.bossName : boss.TheNameCap)} is dead. The way on is open.", MessageKind.Good);
        Finish(true);
        StartCoroutine(DefeatScene());
    }

    void Defeat()
    {
        if (finished) return;
        Comment(fight != null ? fight.defeat : null, true);
        Finish(false);
        lost = true;
        lostAt = Time.time;
    }

    float lostAt = -1f;

    // The fight's clocks after the time the player was down: the seconds pick up where they stopped, and beats of the
    // music that played on meanwhile are skipped (an event every N beats would otherwise fire once a frame until it
    // caught up: volleys and adds all at once).
    void CatchUpClocks()
    {
        if (lostAt >= 0f && fightStart >= 0f) fightStart += Time.time - lostAt;
        lostAt = -1f;
        if (fight == null || musicStart < 0f) return;
        float now = Beat;
        foreach (var e in fight.events)
        {
            if (e == null) continue;
            if (e.when == BossEvent.When.AtBeat && e.startBeat <= now) done.Add(e);
            else if (e.when == BossEvent.When.EveryBeats && nextBeat.TryGetValue(e, out float at) && at <= now)
            {
                float step = Mathf.Max(1f, e.beats);
                nextBeat[e] = at + Mathf.Ceil((now - at + .001f) / step) * step;
            }
        }
    }

    // Back on their feet after falling here (RunManager.Revived): the fight goes on where it was. The room goes dark
    // again, the stair shuts, the DM comes back to the table and the boss turns on the player; its health, the fight's
    // clock and the events already done carry on.
    void OnRevived(bool counted)
    {
        if (!lost) return;
        if (boss != null && !boss.IsAlive) { OnBossDied(); return; }
        lost = false;
        if (boss == null) { Finish(true); return; }
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        finished = false; Current = this;
        DungeonMasterRemarks.Hold = true;
        BossBlessing.Grant();
        ShutStair(true);
        // The fight's music, if something else took over meanwhile; its beats then count from the new start.
        if (fight != null && fight.music != null && AudioManager.HasInstance && AudioManager.Instance.CurrentMusic != fight.music)
        {
            AudioManager.Instance.CrossfadeMusic(fight.music, true, fight.musicFade, fight.musicVolume);
            musicStart = Time.time;
            nextBeat.Clear();
        }
        CatchUpClocks();
        DarkenRoom();
        LightRoom();
        if (BossBarUI.HasInstance) BossBarUI.Instance.Show(boss, milestone.title, fight != null ? fight.bossName : null);
        if (stageLights != null) { stageLights.Follow(boss.transform); if (phaseTwo) stageLights.PhaseTwo(fight != null ? fight.phaseTwoDisco : null); }
        if (seat != null) seat.Focus(StandFor(bossHome), bossHome);
        // A beam cut short by the death can leave the boss kneeling or keeping away.
        if (brain != null) { brain.MovementOverride = null; Kneel(false); brain.Alert(); }
        if (hand != null && player != null) hand.Watch(player.transform.position, false);
    }

    void Finish(bool won)
    {
        finished = true;
        Cleanup(won);
        if (won && dungeon != null && boss != null) StartCoroutine(BossChest(boss.transform.position));
        // The way on rises on the stage, after the boss's last pose (it would come up in front of it).
        if (won && exit != null)
        {
            if (fight != null && fight.DefeatSeconds > 0f) StartCoroutine(OpenExitAfter(fight.DefeatSeconds + .4f));
            else OpenExit();
        }
    }

    IEnumerator OpenExitAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        OpenExit();
    }

    void OpenExit()
    {
        if (exit == null) return;
        exit.Sealed = false;
        exit.gameObject.SetActive(true);
        if (AudioManager.HasInstance && milestone.introSting != null) AudioManager.Instance.PlaySFX(milestone.introSting, exit.transform.position, .5f);
    }

    // The boss chest, a moment after the kill (the body is thrown first), facing the player.
    IEnumerator BossChest(Vector3 at)
    {
        yield return new WaitForSeconds(1.2f + (fight != null ? fight.DefeatSeconds : 0f));
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        // Clear of the exit stairs that rise in the middle of the stage.
        if (exit != null)
        {
            var away = at - exit.transform.position; away.y = 0;
            if (away.magnitude < 3.5f) at = exit.transform.position + (away.sqrMagnitude > .01f ? away.normalized : Vector3.right) * 3.5f;
        }
        dungeon.SpawnBossChest(at, player != null ? player.transform.position : StageCentreOr(at));
    }

    Vector3 StageCentreOr(Vector3 at) => dungeon != null ? dungeon.StageCentre : at;

    // floorGoing: the floor is being destroyed (left mid fight, or back to the room): the room's light is the loader's to
    // put back, so the dungeon's mood is not blended in over it.
    void Cleanup(bool won, bool floorGoing = false)
    {
        if (stageLights != null) stageLights.Calm();
        ShowEnd(won);
        BossBlessing.Clear();
        StopAllCoroutines(); beamRun = null;
        if (introPushed) EndIntro();
        if (beam != null) beam.Stop();
        foreach (var v in volleys) if (v != null) v.Stop();
        if (hand != null) hand.Release();
        for (int i = inHand.Count - 1; i >= 0; i--) if (i < inHand.Count) inHand[i](true);
        if (seat != null) seat.Unfocus();
        ShutStair(false);
        DungeonMasterRemarks.Hold = false;
        // Won with a defeat scene, the room stays dark for it (the spotlight on the last pose) and comes back after the burst.
        if (darkened && !floorGoing && LightingManager.Instance != null && dungeon != null)
        {
            if (won && fight != null && fight.DefeatSeconds > 0f) StartCoroutine(RoomBackAfter(fight.DefeatSeconds + .3f));
            else LightingManager.Instance.BlendToMood(dungeon.LevelData.DungeonLighting(dungeon.FloorNumber), 2.5f);
        }
        darkened = false;
        if (BossBarUI.HasInstance) BossBarUI.Instance.Hide();
        // A lost fight keeps listening: a revive picks it up again, and a boss that dies meanwhile still opens the way.
        if (won && boss != null) boss.Died -= OnBossDied;
        if (won && AudioManager.HasInstance && dungeon != null)
        {
            // A sting cuts the fight's music dead (the record scratching to a stop); the floor's comes back after.
            if (fight != null && fight.deathSting != null && !floorGoing)
            {
                AudioManager.Instance.StopMusic(.05f);
                AudioManager.Instance.PlaySFX2D(fight.deathSting, fight.deathStingVolume);
                StartCoroutine(FloorMusicAfter(fight.musicBackAfter));
            }
            else FloorMusic();
        }
    }

    IEnumerator RoomBackAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (LightingManager.Instance != null && dungeon != null) LightingManager.Instance.BlendToMood(dungeon.LevelData.DungeonLighting(dungeon.FloorNumber), 2.5f);
    }

    void FloorMusic()
    {
        var music = dungeon.LevelData.MusicFor(dungeon.FloorNumber, out float volume);
        if (music != null) AudioManager.Instance.CrossfadeMusic(music, dungeon.LevelData.loopMusic, 3f, volume);
        else AudioManager.Instance.StopMusic(3f);
    }

    IEnumerator FloorMusicAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (dungeon != null && AudioManager.HasInstance) FloorMusic();
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
