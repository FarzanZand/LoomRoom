using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The stage fight in co-op. The host runs it for the whole party: it starts when anyone reaches the stage's middle,
// it is lost only when no one is left alive, and the boss and its adds are the host's (guests get them as puppets).
// Each beam and fireball volley goes at a player picked at random: that player's own machine runs the real one (so
// blocking answers their own input and the damage is theirs), everyone else sees it poured or thrown at them.
public partial class BossStageEncounter
{
    void CoopUpdate()
    {
        var local = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table ? PlayerManager.Instance.Active : null;
        if (!started)
        {
            // Guests: the host's fight starts here when its word comes (FromHost).
            if (Coop.IsGuest) return;
            foreach (var c in CoopWorld.Party())
                if (OnStage(c))
                {
                    CoopWorld.BossStage(CoopEventKind.BossStageOpened, CoopWorld.SlotOf(c), 0, c.transform.position);
                    StartCoroutine(Open(c));
                    return;
                }
            return;
        }
        if (Coop.IsHost && !AnyoneAlive()) { Defeat(); return; }
        if (Fighting) RunEvents(local);
    }

    bool OnStage(Character c)
    {
        if (c == null || !c.IsAlive) return false;
        var centre = dungeon.StageCentre;
        var flat = c.transform.position - centre; flat.y = 0;
        return flat.magnitude <= dungeon.Stage.startRadius && Mathf.Abs(c.transform.position.y - centre.y) < 1.2f;
    }

    static bool AnyoneAlive() { foreach (var _ in CoopWorld.Party()) return true; return false; }

    // Guests: what the host's fight does.
    public void FromHost(CoopEventKind kind, int target, int index, Vector3 position)
    {
        if (!Coop.IsGuest || finished || dungeon == null || dungeon.Stage == null) return;
        switch (kind)
        {
            case CoopEventKind.BossStageOpened: if (!started) StartCoroutine(Open(CoopWorld.CharacterOf(target))); break;
            case CoopEventKind.BossBeam: if (started) Attack(true, target, null); break;
            case CoopEventKind.BossVolley:
                if (started && fight != null && index >= 0 && index < fight.events.Count) Attack(false, target, fight.events[index]);
                break;
        }
    }

    // Guests: the boss is the host's, arriving as its enemy; his hand goes through setting it down meanwhile.
    IEnumerator TakeHostBoss(Vector3 spot)
    {
        string name = milestone.boss != null ? milestone.boss.name : null;
        for (float t = 0; t < 8f && boss == null; t += Time.deltaTime)
        {
            foreach (var e in CoopWorld.Enemies)
                if (e != null && e.IsAlive && name != null && e.name.StartsWith(name)) { boss = e; break; }
            if (boss == null) yield return null;
        }
        if (boss == null) { Debug.LogWarning("[Coop] Boss stage: the host's boss never came.", this); yield break; }
        brain = boss.GetComponent<EnemyBrain>();
        boss.Died += OnBossDied;
        if (hand != null)
        {
            var held = new GameObject("Boss (in his hand)").transform;
            held.position = boss.transform.position;
            yield return hand.Place(held, spot, fight != null ? fight.placeSeconds : 2f, TableTop(), () => Landed(boss, spot));
            Destroy(held.gameObject);
        }
        else Landed(boss, spot);
        // Said once the boss is down, as on the host.
        BossBlessing.Announce();
        yield return new WaitForSeconds(fight != null ? fight.wakeDelay : 1f);
        if (boss == null || !boss.IsAlive) yield break;
        if (BossBarUI.HasInstance) BossBarUI.Instance.Show(boss, milestone.title);
        if (!string.IsNullOrWhiteSpace(milestone.introMessage)) MessageLog.Post(milestone.introMessage.Trim(), MessageKind.Warning);
        if (fight != null) Say(fight.fightLine, true);
        var local = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        if (hand != null && local != null) hand.Watch(local.transform.position, false);
        fightStart = Time.time;
    }

    // Host: a beam or a volley at one of the party, at random.
    void CoopAttack(BossEvent e, bool isBeam)
    {
        if (!Coop.IsHost || fight == null) return;
        if (isBeam && (beamRun != null || beam == null || beam.Firing)) return;
        var choices = new List<Character>(CoopWorld.Party());
        if (choices.Count == 0) return;
        var at = choices[Random.Range(0, choices.Count)];
        int slot = CoopWorld.SlotOf(at);
        CoopWorld.BossStage(isBeam ? CoopEventKind.BossBeam : CoopEventKind.BossVolley, slot, fight.events.IndexOf(e), Vector3.zero);
        Debug.Log($"[Coop] Boss stage: {(isBeam ? "beam" : "volley")} at {at.name} (slot {slot})");
        Attack(isBeam, slot, e);
    }

    // Every machine: the real one at this player, or the look of it at a teammate.
    void Attack(bool isBeam, int slot, BossEvent e)
    {
        var mate = CoopWorld.CharacterOf(slot);
        if (Coop.IsGuest) Debug.Log($"[Coop] Boss stage: {(isBeam ? "beam" : "volley")} at slot {slot}{(slot == Coop.LocalSlot ? " (me)" : "")}");
        if (mate == null || !mate.IsAlive) return;
        var mine = mate as Player;
        if (isBeam)
        {
            if (beamRun != null || beam == null || beam.Firing) return;
            beamRun = StartCoroutine(mine != null ? Beam(mine) : WatchBeam(mate, slot));
        }
        else if (e != null && e.spell != null) StartCoroutine(Volley(e, mate, mine != null));
    }

    // The beam at a teammate: on the same beat, the boss giving way to them (on the host), no damage here.
    IEnumerator WatchBeam(Character mate, int slot)
    {
        if (fight.beamOnBeat && musicStart >= 0f)
        {
            float wait = (Mathf.Ceil(Beat) - Beat) * fight.SecondsPerBeat;
            if (wait > .02f) yield return new WaitForSeconds(wait);
        }
        var coop = MultiplayerManager.HasInstance ? MultiplayerManager.Instance.PlayerInSlot(slot) : null;
        System.Func<bool> giveWay = () => GiveWay(mate);
        bool steer = Coop.IsHost && brain != null && fight.beamGiveWay > 0f;
        if (steer) brain.MovementOverride = giveWay;
        yield return beam.FireAt(mate, () => coop != null && coop.Current.blocking, () => mate != null && mate.IsAlive && (coop == null || coop.Current.alive), fight);
        if (steer && brain != null && brain.MovementOverride == giveWay) brain.MovementOverride = null;
        beamRun = null;
    }
}
