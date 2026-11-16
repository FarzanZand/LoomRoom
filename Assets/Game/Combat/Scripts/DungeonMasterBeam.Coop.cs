using System.Collections;
using UnityEngine;

// Co-op: the beam as the others see it when the Dungeon Master pours it at a teammate (BossStageEncounter.WatchBeam).
// The same warning, length, layers, sparks and bursts, ending on the teammate's shield while their machine says they
// block (else on their chest), drawn thinner (it is seen from the side, not end-on); no damage, strain or shake here:
// those are on the teammate's own machine.
public partial class DungeonMasterBeam
{
    [Header("Co-op")]
    [Range(.1f, 1f), Tooltip("Width of the beam poured at a teammate, against the one at you (it is seen from the side).")]
    public float watchedWidth = .45f;

    // Every width drawn is scaled by this (the watched beam's).
    float widthScale = 1f;

    public IEnumerator FireAt(Character mate, System.Func<bool> blocking, System.Func<bool> alive, BossFight fight)
    {
        if (Firing || mate == null || fight == null) yield break;
        Firing = true; cancelled = false; widthScale = watchedWidth;
        var local = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        view = local != null && local.CameraRig != null ? local.CameraRig.Camera : null;
        if (AudioManager.HasInstance && AudioManager.Instance.SfxGroup != null) sound.outputAudioMixerGroup = AudioManager.Instance.SfxGroup;
        charge = null;
        if (handEffect != null)
        {
            charge = Instantiate(handEffect, Palm(), Quaternion.identity);
            charge.transform.localScale = Vector3.one * handEffectScale;
        }
        var colour = fight.beamColor;
        var hot = Color.Lerp(colour, Color.white, .65f) * 1.4f;
        var sparkMain = sparks.main; sparkMain.startColor = Color.Lerp(colour, Color.white, .35f);
        var sprayMain = spray.main; sprayMain.startColor = Color.Lerp(colour, Color.white, .5f);
        palm.color = shieldLight.color = Normalised(colour);
        palm.range = palmLightRange; shieldLight.range = endLightRange;
        if (fight.beamChargeSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(fight.beamChargeSound, mate.transform.position, fight.beamVolume);

        palm.enabled = true;
        for (float t = 0; t < fight.beamWarning; t += Time.deltaTime)
        {
            if (cancelled || !alive()) break;
            var origin = Palm();
            var aim = MateEnd(mate, origin, false);
            if (hand != null) hand.Aim(aim);
            float u = t / Mathf.Max(.01f, fight.beamWarning);
            if (charge != null) { charge.transform.position = origin; charge.SetCharge(u); }
            palm.transform.position = origin; palm.intensity = palmLight * .6f * u * (1f + .2f * Mathf.Sin(t * 30f));
            if (u > .45f && Mathf.Repeat(t, .28f) < .12f) Bolt(boltA, origin, aim, warningScreen * (1f + 2f * u), colour * (.5f + .5f * u), t);
            else boltA.enabled = false;
            boltB.enabled = core.enabled = body.enabled = glow.enabled = false;
            yield return null;
        }

        if (fight.beamLoopSound != null) { sound.clip = fight.beamLoopSound; sound.volume = fight.beamVolume; sound.Play(); }
        sparks.Play(); spray.Play();
        flare.gameObject.SetActive(true); shieldLight.enabled = true;
        float impact = impactInterval;
        for (float t = 0; t < fight.beamSeconds && !cancelled && alive(); t += Time.deltaTime)
        {
            var origin = Palm();
            // A pillar between his palm and the teammate takes the stream here too, and glows; whether it breaks is
            // the teammate's machine's to say (BossStageEncounter.PillarBrokeElsewhere).
            bool covered = CoverAt(mate.transform.position + Vector3.up * 1.6f, origin, out var coverPoint, out var coverCollider);
            if (covered) BossCoverPillar.BeamOn(coverCollider, coverPoint, colour);
            bool held = covered || blocking();
            var end = covered ? coverPoint : MateEnd(mate, origin, held);
            if (hand != null) hand.Aim(end);
            if (charge != null) { charge.transform.position = origin; charge.SetCharge(1f); }
            float swell = Mathf.SmoothStep(0, 1, t / .18f) * (1f + .1f * Mathf.Sin(t * 26f)) * (t < .12f ? 1.6f - 5f * t : 1f);
            Pour(origin, end, swell, colour, hot, t);
            AimSparks(origin, end, held);
            Flare(end, swell * (held ? .6f : .45f), held ? Color.Lerp(colour, hot, .4f) : Color.Lerp(colour, new Color(1.6f, .35f, .2f), .5f), t);
            palm.transform.position = origin; palm.intensity = palmLight * (1f + .15f * Mathf.Sin(t * 31f));
            shieldLight.transform.position = end + (origin - end).normalized * .3f;
            shieldLight.intensity = endLight * 2f * swell;
            impact += Time.deltaTime;
            if (impact >= impactInterval && impactEffect != null)
            {
                impact = 0;
                var burst = end + (origin - end).normalized * Mathf.Min(impactOut, 1f);
                Instantiate(impactEffect, burst, Quaternion.LookRotation(origin - end));
                if (held && blockSparks != null) Destroy(Instantiate(blockSparks, burst, Quaternion.LookRotation(origin - end)), 2f);
            }
            State = $"at {mate.name} blocking {held}";
            yield return null;
        }

        sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        spray.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        boltA.enabled = boltB.enabled = false;
        for (float t = 0; t < .25f && !cancelled; t += Time.deltaTime)
        {
            float f = 1f - t / .25f;
            var origin = Palm(); var end = MateEnd(mate, origin, true);
            Pour(origin, end, f * f, colour * f, hot * f, t, false);
            Flare(end, f * .6f, hot * f, t);
            palm.intensity = palmLight * f; shieldLight.intensity = endLight * f;
            yield return null;
        }
        Clear();
        widthScale = 1f;
    }

    // On the teammate's raised shield (in front of their chest, toward him) or their chest.
    // Cover, from a teammate's eyes as this machine shows them.
    bool CoverAt(Vector3 eye, Vector3 origin, out Vector3 point, out Collider cover)
    {
        point = default; cover = null;
        var dir = eye - origin;
        float length = dir.magnitude, nearest = float.MaxValue;
        if (length < .1f) return false;
        foreach (var hit in Physics.RaycastAll(origin, dir / length, length - .3f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance < length * .5f || hit.distance >= nearest) continue;
            if (hit.collider.GetComponentInParent<Character>() != null || hit.transform.IsChildOf(transform)) continue;
            if (InvisibleWall.Is(hit.collider) || BossCoverPillar.IsRubble(hit.collider)) continue;
            nearest = hit.distance; point = hit.point; cover = hit.collider;
        }
        return nearest < float.MaxValue;
    }

    static Vector3 MateEnd(Character mate, Vector3 origin, bool shield)
    {
        var chest = mate.transform.position + Vector3.up * 1.25f;
        var toward = origin - chest; toward.y = 0;
        toward = toward.sqrMagnitude > .0001f ? toward.normalized : mate.transform.forward;
        return shield ? chest + toward * .55f + Vector3.up * .1f : chest + toward * .15f;
    }
}
