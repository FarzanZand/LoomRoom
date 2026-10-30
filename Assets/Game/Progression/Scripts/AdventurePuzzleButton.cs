using System.Collections;
using UnityEngine;

// One symbol tile of the room puzzle. Pressing it gives it a quick push (it dips and swells back); a tile pressed in
// the right order stays lit green, a wrong one flashes red and every lit tile goes dark again (AdventureRoomPuzzle.Press
// drives it). The tint is a property block on the tile's own renderers, so the material stays as made.
public class AdventurePuzzleButton : MonoBehaviour, IInteractable
{
    public AdventureRoomPuzzle puzzle;
    public int symbol;
    public Color litColour = new(.35f, 1f, .45f, 1f);
    public Color wrongColour = new(1f, .3f, .25f, 1f);

    public string Prompt => puzzle.Solved ? puzzle.SolvedText : puzzle.TileReady ? AdventureRoomPuzzle.PlacePrompt : "Press the " + puzzle.symbols[symbol];
    public bool CanInteract(Character who) => who is Player player && player.kind == PlayerKind.Room && puzzle.Revealed && !puzzle.Solved;
    public void Interact(Character who) { if (!CanInteract(who)) return; if (puzzle.TileReady) puzzle.Interact(who); else puzzle.Press(symbol); }

    Renderer[] renderers;
    MaterialPropertyBlock block;
    Vector3 restScale, restPosition;
    float tint;            // 0 plain, 1 fully coloured
    Color tintColour;
    bool lit;
    Coroutine push, fade;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), Color_ = Shader.PropertyToID("_Color"), Emission = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        block = new MaterialPropertyBlock();
        restScale = transform.localScale;
        restPosition = transform.localPosition;
    }

    // The press itself: down a little and swelling back past its size, a quarter of a second.
    public void Pulse()
    {
        if (push != null) StopCoroutine(push);
        push = StartCoroutine(Push());
    }

    IEnumerator Push()
    {
        const float seconds = .28f;
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            float k = t / seconds;
            float swell = Mathf.Sin(k * Mathf.PI);
            transform.localScale = restScale * (1f + .18f * swell);
            var down = transform.parent != null ? transform.parent.InverseTransformVector(Vector3.up) : Vector3.up;
            transform.localPosition = restPosition - down * (.25f * Mathf.Sin(Mathf.Min(1f, k * 2f) * Mathf.PI));
            yield return null;
        }
        transform.localScale = restScale;
        transform.localPosition = restPosition;
        push = null;
    }

    // Right in the order: lit green until the puzzle resets or is solved.
    public void Light(bool on)
    {
        lit = on;
        FadeTo(on ? litColour : tintColour, on ? 1f : 0f, on ? .15f : .35f);
    }

    // Wrong: a red flash, then dark.
    public void Wrong()
    {
        lit = false;
        if (fade != null) StopCoroutine(fade);
        fade = StartCoroutine(Flash());
    }

    IEnumerator Flash()
    {
        tintColour = wrongColour;
        for (int i = 0; i < 2; i++)
        {
            yield return Fade(wrongColour, 1f, .08f);
            yield return Fade(wrongColour, .25f, .12f);
        }
        yield return Fade(wrongColour, 0f, .3f);
        fade = null;
    }

    void FadeTo(Color colour, float target, float seconds)
    {
        if (fade != null) StopCoroutine(fade);
        fade = StartCoroutine(FadeRoutine(colour, target, seconds));
    }

    IEnumerator FadeRoutine(Color colour, float target, float seconds) { yield return Fade(colour, target, seconds); fade = null; }

    IEnumerator Fade(Color colour, float target, float seconds)
    {
        tintColour = colour;
        float from = tint;
        for (float t = 0; t < seconds; t += Time.deltaTime) { Apply(Mathf.Lerp(from, target, t / seconds)); yield return null; }
        Apply(target);
    }

    // Lit tiles breathe a little, so they read as on.
    void Update()
    {
        if (lit && fade == null) Apply(.85f + .15f * Mathf.Sin(Time.time * 4f));
    }

    void Apply(float amount)
    {
        tint = amount;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (amount <= 0f) { r.SetPropertyBlock(null); continue; }
            r.GetPropertyBlock(block);
            var shared = r.sharedMaterial;
            Color baseColour = shared != null && shared.HasProperty(BaseColor) ? shared.GetColor(BaseColor) : shared != null && shared.HasProperty(Color_) ? shared.GetColor(Color_) : Color.white;
            var c = Color.Lerp(baseColour, tintColour, amount);
            block.SetColor(BaseColor, c);
            block.SetColor(Color_, c);
            block.SetColor(Emission, tintColour * (amount * 1.6f));
            r.SetPropertyBlock(block);
        }
    }
}
