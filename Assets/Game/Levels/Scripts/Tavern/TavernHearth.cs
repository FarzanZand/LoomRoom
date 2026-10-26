using UnityEngine;

// The tavern's fire: sitting by it gives back a share of the table player's health, once (each floor builds its own
// tavern, so once a floor). Then the fire burns low: its lights dim and its flames shrink, and it says so when looked at.
public class TavernHearth : MonoBehaviour, IInteractable
{
    [Range(0, 1), Tooltip("Share of maximum health given back.")]
    public float heal = .2f;
    [Tooltip("Posted to the feed. {Amount} is the health given back.")]
    public string restedMessage = "You rest by the fire. +{Amount} health.";
    [Tooltip("Said by the Dungeon Master as you sit (optional).")]
    public DungeonMaster.Line restedLine = new() { text = "Warm your hands. It won't last.", mumble = true, inPerson = true };
    [Tooltip("Lights that dim once the fire has been used (its own glow).")]
    public Light[] lights = new Light[0];
    [Tooltip("Flames that shrink once it has been used.")]
    public ParticleSystem[] flames = new ParticleSystem[0];
    [Range(0, 1), Tooltip("How bright the lights stay afterwards.")]
    public float embers = .3f;
    public AudioClip restClip;

    bool used;
    public bool Used => used;

    public string Prompt => used ? "The fire has burned low" : $"Rest by the fire (+{Mathf.RoundToInt(heal * 100)}% health)";
    public bool CanInteract(Character who) => !used && DungeonFeatureUtility.TablePlayer(who) != null;

    public void Interact(Character who)
    {
        var player = DungeonFeatureUtility.TablePlayer(who);
        if (used || player == null) return;
        used = true;
        var stats = player.Stats;
        float before = stats.CurrentHealth;
        stats.Heal(stats.MaxHealth * heal);
        int gained = Mathf.RoundToInt(stats.CurrentHealth - before);
        MessageLog.Post(restedMessage.Replace("{Amount}", gained.ToString()), MessageKind.Info);
        if (restedLine != null && !restedLine.IsEmpty) DungeonMaster.Say(restedLine);
        if (restClip != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(restClip, transform.position);
        foreach (var l in lights) if (l != null) l.intensity *= embers;
        foreach (var f in flames)
            if (f != null) { var main = f.main; main.startSizeMultiplier *= .5f; var emission = f.emission; emission.rateOverTimeMultiplier *= .4f; }
    }
}
