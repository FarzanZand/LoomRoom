using UnityEngine;

// A spell that lands on the ground (SpellDefinition.groundArea): the spot is marked (optional), and after Area Delay it
// erupts, hitting everything hostile to the caster inside Area Radius. The eruption is the spell's Impact effect.
// Cast by enemies (EnemyAttackRunner) and by the player (PlayerSpellcasting). Made at run time; nothing to place.
public class GroundAreaSpell : MonoBehaviour
{
    static readonly LayerMask BlastMask = ~0;

    Character owner;
    SpellDefinition spell;
    AdventurerProgress progress;
    float power, elapsed;
    GameObject marker;
    Transform fill;

    // point: where it lands (on the ground). skills: the player's progress, null for enemies.
    public static GroundAreaSpell Cast(Character source, SpellDefinition definition, float damage, Vector3 point, AdventurerProgress skills)
    {
        var go = new GameObject(definition.name + " (ground)");
        go.transform.position = point;
        var area = go.AddComponent<GroundAreaSpell>();
        area.owner = source; area.spell = definition; area.power = damage; area.progress = skills;
        if (definition.showMarker && definition.areaMarker != null)
        {
            area.marker = Instantiate(definition.areaMarker, point + Vector3.up * .02f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), go.transform);
            area.marker.transform.localScale *= definition.areaRadius / Mathf.Max(.1f, definition.markerRadius);
            area.fill = area.marker.transform.Find("Fill");
            if (area.fill != null) area.fill.localScale = Vector3.zero;
        }
        return area;
    }

    // The ground under a point (a character's feet, a spot the crosshair found): the first surface below that isn't a
    // character, so the mark lies on the floor.
    public static Vector3 GroundBelow(Vector3 point)
    {
        var hits = Physics.RaycastAll(point + Vector3.up * 1f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
            if (hit.collider.GetComponentInParent<Character>() == null) return hit.point;
        return point;
    }

    void Update()
    {
        if (GameManager.HasInstance && !GameManager.Instance.SimulationActive) return;
        elapsed += Time.deltaTime;
        float t = spell.areaDelay > 0f ? Mathf.Clamp01(elapsed / spell.areaDelay) : 1f;
        if (fill != null) fill.localScale = Vector3.one * t;
        if (t >= 1f) Erupt();
    }

    void Erupt()
    {
        var centre = transform.position + Vector3.up * .5f;
        // Overlap a little taller than the radius, so a character standing in the circle is caught at any height in it.
        bool struck = SpellProjectile.Blast(owner, centre, spell.areaRadius, power, BlastMask);
        if (struck) progress?.Practise(spell.Skill, SkillAction.Cast);
        if (spell.impact != null) Instantiate(spell.impact, transform.position, Quaternion.identity);
        if (spell.impactSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(spell.impactSound, centre, spell.impactVolume);
        Destroy(gameObject);
    }
}
