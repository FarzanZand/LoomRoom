using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;

// The fight's show (BossFight > Phase two, Backup, Defeat), for a boss that puts one on: in the second phase the boss's
// own line in the portrait box, the dance floor lighting up on the beat and a disco ball lowered on its chain; the backup
// dancers that come out behind it and keep it going until they are broken up; and its last moment, a spin and a pose
// before it bursts. Each part is off while its field on the fight is empty.
public partial class BossStageEncounter
{
    BossDanceFloor danceFloor;
    BossDiscoBall discoBall;
    bool backupCalled, backupDanced;
    readonly List<(EnemyBrain brain, PlayableGraph graph)> backup = new();

    // As the boss is set down: its burst waits for the defeat scene.
    void ShowSetup()
    {
        if (fight == null || boss == null) return;
        // Its stage name in every line about it (the kill, the killer, the feed).
        if (!string.IsNullOrWhiteSpace(fight.bossName)) boss.nameOverride = fight.bossName;
        if (fight.defeatPose != null && boss.TryGetComponent<DeathBurst>(out var burst)) burst.HoldSeconds = fight.DefeatSeconds;
        // The killing blow (Damaged comes before Died, so before the kill's experience): a level-up waits for the pose.
        if (fight.defeatPose != null)
            boss.Damaged += _ =>
            {
                if (boss == null || boss.IsAlive) return;
                LevelUpUI.HoldUntil = Time.time + fight.DefeatSeconds + .5f;
                // And the HUD steps aside for it, so nothing sits over the pose.
                HudCanvas.HideUntil = Time.time + fight.DefeatSeconds + .3f;
            };
    }

    // The second phase begins: the boss says its line (else the DM his), the floor lights up, the ball comes down.
    void PhaseTwoShow()
    {
        if (!string.IsNullOrWhiteSpace(fight.phaseTwoBossLine) && PortraitDialogueUI.Current != null)
            PortraitDialogueUI.Current.Bark(fight.phaseTwoBossLine, boss, fight.bossName);
        else Say(fight.phaseTwoLine, true);
        if (fight.danceFloor != null && danceFloor == null)
            danceFloor = BossDanceFloor.Build(dungeon, fight.danceFloor, fight.phaseTwoDisco);
        if (fight.discoBall != null && discoBall == null)
        {
            // Off the middle toward the Dungeon Master, where it hangs in view behind the boss (over the middle it was
            // always above the top of the screen).
            var at = dungeon.StageCentre;
            var dm = TableManager.HasInstance && TableManager.Instance.DM != null ? TableManager.Instance.DM.transform.position : at;
            var toDm = dm - at; toDm.y = 0f;
            if (toDm.sqrMagnitude > .01f) at += toDm.normalized * 2.5f;
            var go = Instantiate(fight.discoBall, at, Quaternion.identity, dungeon.transform);
            discoBall = go.GetComponent<BossDiscoBall>();
            if (discoBall != null) discoBall.Lower(at, fight.phaseTwoDisco);
        }
    }

    // Every frame of the fight: the beat on the floor and the ball, the backup dancers' call and their hold on the boss.
    void ShowTick(float health, Player player)
    {
        float beat = musicStart >= 0f ? Beat : -1f;
        if (danceFloor != null) danceFloor.Beat(beat);
        if (discoBall != null) discoBall.Beat(beat);
        if (fight.backupDancer != null && !backupCalled && health <= fight.backupAtHealth && (hand == null || !hand.Busy))
        {
            backupCalled = true;
            CallBackup(player);
        }
        // While any of them still dances the boss gets its health back.
        bool dancing = false;
        for (int i = backup.Count - 1; i >= 0; i--)
        {
            var (b, graph) = backup[i];
            if (b == null || b.State == EnemyState.Dead) { if (graph.IsValid()) graph.Destroy(); backup.RemoveAt(i); continue; }
            if (b.Entranced) dancing = true;
        }
        if (dancing && boss.Stats != null) boss.Stats.Heal(boss.Stats.MaxHealth * fight.backupHealPerSecond * Time.deltaTime);
        // The last of them broken up: the boss minds.
        if (dancing) backupDanced = true;
        else if (backupDanced)
        {
            backupDanced = false;
            if (!string.IsNullOrWhiteSpace(fight.backupBrokenLine) && PortraitDialogueUI.Current != null) PortraitDialogueUI.Current.Bark(fight.backupBrokenLine, boss, fight.bossName);
        }
    }

    // Out they come behind the boss in a puff each, in a row facing the player, dancing; one blow breaks a dancer's
    // dance and it fights.
    void CallBackup(Player player)
    {
        var toBoss = boss.transform.position - (player != null ? player.transform.position : StageCentreOr(boss.transform.position)); toBoss.y = 0f;
        var back = toBoss.sqrMagnitude > .01f ? toBoss.normalized : -boss.transform.forward;
        var side = Vector3.Cross(Vector3.up, back);
        for (int i = 0; i < fight.backupCount; i++)
        {
            float across = fight.backupCount > 1 ? (i / (fight.backupCount - 1f) - .5f) * 3.2f : 0f;
            var spot = boss.transform.position + back * 2.2f + side * across;
            // On the stage, on its floor: out past its edge or into a pillar is pulled back to the nearest walkable ground.
            if (NavMesh.SamplePosition(spot, out var hit, 2.5f, NavMesh.AllAreas)) spot = hit.position;
            var go = Instantiate(fight.backupDancer, spot, Quaternion.LookRotation(-back), dungeon.transform);
            go.name = fight.backupDancer.name + " (backup)";
            if (dungeon.LevelData.ambushSpawnPuff != null) Destroy(Instantiate(dungeon.LevelData.ambushSpawnPuff, spot, Quaternion.identity, dungeon.transform), 4f);
            var brain = go.GetComponent<EnemyBrain>();
            if (brain == null) continue;
            StartCoroutine(StartDancer(brain, i));
        }
        if (!string.IsNullOrWhiteSpace(fight.backupBossLine) && PortraitDialogueUI.Current != null)
            PortraitDialogueUI.Current.Bark(fight.backupBossLine, boss, fight.bossName);
        Say(fight.backupLine, true);
    }

    // A frame after it is made (its stats and brain set up): weaker, entranced and dancing, a little out of step.
    IEnumerator StartDancer(EnemyBrain brain, int index)
    {
        yield return null;
        if (brain == null || brain.Character == null) yield break;
        var character = brain.Character;
        if (character.Stats != null) character.Stats.SetHealthShare(fight.backupHealth);
        brain.Entranced = true;
        var graph = default(PlayableGraph);
        if (fight.backupDance != null && character.Animator != null)
        {
            var playable = AnimationPlayableUtilities.PlayClip(character.Animator, fight.backupDance, out graph);
            playable.SetSpeed(.95f + .07f * index);
            playable.SetTime(fight.backupDance.length * (index * .29f % 1f));
        }
        backup.Add((brain, graph));
        character.Damaged += _ => BreakUp(brain);
    }

    bool partyOver;

    void BreakUp(EnemyBrain brain)
    {
        if (partyOver) return;
        for (int i = 0; i < backup.Count; i++)
        {
            if (backup[i].brain != brain) continue;
            if (backup[i].graph.IsValid()) backup[i].graph.Destroy();
            backup[i] = (brain, default);
        }
        if (brain != null && brain.Entranced)
        {
            var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
            brain.Provoke(player != null ? player.transform.position : brain.transform.position);
        }
    }

    // The party is over (the fight won or lost): the floor and the ball go dark, and backup still standing when the boss
    // falls falls with it.
    void ShowEnd(bool won)
    {
        if (danceFloor != null) danceFloor.Off();
        if (discoBall != null) discoBall.Off();
        if (!won) return;
        partyOver = true;
        foreach (var (b, graph) in backup.ToArray())
        {
            if (graph.IsValid()) graph.Destroy();
            if (b != null && b.Character != null && b.Character.Stats != null && b.Character.IsAlive)
                b.Character.Stats.TakeDamage(new DamageInfo { Amount = 1e6f, SourceName = "the end of the party", HitPoint = b.transform.position + Vector3.up, Direction = b.transform.forward });
        }
        backup.Clear();
    }

    // The boss's last moment: a spin round to face the player, the pose held, then the burst (DeathBurst, waiting the
    // same time) in a shower of the fight's burst effect.
    IEnumerator DefeatScene()
    {
        if (fight == null || fight.defeatPose == null || boss == null) yield break;
        var anim = boss.Animator;
        var graph = default(PlayableGraph);
        if (anim != null) AnimationPlayableUtilities.PlayClip(anim, fight.defeatPose, out graph);
        // Nothing else turns or moves it now (its agent would hold its heading through the spin).
        if (boss.TryGetComponent<NavMeshAgent>(out var agent)) agent.enabled = false;
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        var body = boss.transform;
        var face = player != null ? Flat(player.transform.position - body.position) : body.forward;
        // It spins back from the player into the light (a player right in its face would see only its chest), on its stage.
        var from = body.position;
        var to = from - face * 1.3f;
        to = NavMesh.SamplePosition(to, out var ground, 1f, NavMesh.AllAreas) ? ground.position : from;
        // The intro's theatre spotlight back on, for the last pose.
        GameObject spot = null;
        if (fight.spotlight != null)
        {
            spot = Instantiate(fight.spotlight, to, Quaternion.identity, dungeon.transform);
            spot.name = "Boss spotlight (defeat)";
            if (fight.spotlightSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(fight.spotlightSound, fight.spotlightVolume * .8f);
        }
        float start = Quaternion.LookRotation(face).eulerAngles.y;
        for (float t = 0f; t < fight.defeatSpinSeconds; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / fight.defeatSpinSeconds);
            if (body == null) yield break;
            body.rotation = Quaternion.Euler(0f, start + 360f * (1f - u), 0f);
            body.position = Vector3.Lerp(from, to, u);
            yield return null;
        }
        if (body != null) { body.rotation = Quaternion.LookRotation(face); body.position = to; }
        yield return new WaitForSeconds(fight.defeatHold);
        if (body != null && fight.defeatBurst != null)
        {
            var chest = body.position + Vector3.up * 1.3f;
            var fx = Instantiate(fight.defeatBurst, chest, Quaternion.LookRotation(-face));
            fx.transform.localScale *= fight.defeatBurstScale;
        }
        if (body != null && fight.defeatBurstSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(fight.defeatBurstSound, body.position, fight.defeatBurstVolume);
        if (spot != null) StartCoroutine(Dim(spot, 1.2f));
        yield return null;
        yield return null;
        if (graph.IsValid()) graph.Destroy();
    }
}
