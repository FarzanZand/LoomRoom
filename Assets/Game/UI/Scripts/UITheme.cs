using UnityEngine;

// Built-in UI colour presets on UIManager. Serialized by integer: append only.
public enum UIThemePreset
{
    Custom = 0,         // keep whatever is set on UIManager
    CopperPlum = 1,
    Slate = 2,
    LoomyDungeons = 3,
    Bone = 4,
    Ember = 5,
    Saved = 6,          // a UITheme asset (UIManager > Save As Theme makes one)
}

// Every UIManager colour, as one value. Built-in presets are defined here; saved themes are UITheme assets.
[System.Serializable]
public class UIThemeColors
{
    [Header("Theme")]
    public Color shadow, panel;
    [Range(0, 1)] public float panelShading;
    public Color frame, frameHighlight, text, dimText;
    [Header("Status")]
    public Color health, mana, stamina, experience, gold, bossHealth, enemyHealth, recentDamage;

    public static UIThemeColors From(UIManager m) => new()
    {
        shadow = m.shadow, panel = m.panel, panelShading = m.panelShading, frame = m.frame, frameHighlight = m.frameHighlight,
        text = m.text, dimText = m.dimText, health = m.health, mana = m.mana, stamina = m.stamina, experience = m.experience,
        gold = m.gold, bossHealth = m.bossHealth, enemyHealth = m.enemyHealth, recentDamage = m.recentDamage,
    };

    public void CopyTo(UIManager m)
    {
        m.shadow = shadow; m.panel = panel; m.panelShading = panelShading; m.frame = frame; m.frameHighlight = frameHighlight;
        m.text = text; m.dimText = dimText; m.health = health; m.mana = mana; m.stamina = stamina; m.experience = experience;
        m.gold = gold; m.bossHealth = bossHealth; m.enemyHealth = enemyHealth; m.recentDamage = recentDamage;
    }

    static Color C(float r, float g, float b) => new(r, g, b);
    static Color RGB(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);

    public static UIThemeColors Builtin(UIThemePreset preset) => preset switch
    {
        UIThemePreset.CopperPlum => new()
        {
            shadow = C(.043f, .055f, .067f), panel = C(.114f, .145f, .173f), panelShading = .25f,
            frame = C(.64f, .34f, .24f), frameHighlight = C(.88f, .53f, .35f), text = C(.902f, .902f, .902f), dimText = C(.847f, .8f, .706f),
            health = C(.659f, .275f, .243f), mana = C(.267f, .439f, .624f), stamina = C(.549f, .604f, .384f), experience = C(.722f, .565f, .243f),
            gold = C(1f, .851f, .4f), bossHealth = C(.722f, .122f, .122f), enemyHealth = C(.698f, .22f, .09f), recentDamage = C(.949f, .62f, .298f),
        },
        // The colours this project used before presets existed: dark blue-grey with a slate frame.
        UIThemePreset.Slate => new()
        {
            shadow = C(.043f, .055f, .067f), panel = C(.114f, .145f, .173f), panelShading = .25f,
            frame = C(.266f, .297f, .425f), frameHighlight = C(.166f, .185f, .264f), text = C(.902f, .902f, .902f), dimText = C(.847f, .8f, .706f),
            health = C(.659f, .275f, .243f), mana = C(.267f, .439f, .624f), stamina = C(.549f, .604f, .384f), experience = C(.722f, .565f, .243f),
            gold = C(1f, .851f, .4f), bossHealth = C(.722f, .122f, .122f), enemyHealth = C(.698f, .22f, .09f), recentDamage = C(.949f, .62f, .298f),
        },
        // Matches the LoomyDungeons HUD: plum slots and bar tracks with a lit bevel, gold selection, bright
        // bars. Bar colours are set so the fill's middle row lands on the LoomyDungeons bar colours.
        UIThemePreset.LoomyDungeons => new()
        {
            shadow = RGB(20, 17, 23), panel = C(.207f, .178f, .216f), panelShading = 1f,
            frame = C(.716f, .528f, .18f), frameHighlight = RGB(240, 195, 90), text = C(.93f, .92f, .9f), dimText = C(.74f, .7f, .76f),
            health = C(1f, .28f, .28f), mana = C(.29f, .57f, 1f), stamina = C(.47f, .8f, .38f), experience = C(1f, .71f, .22f),
            gold = C(1f, .85f, .4f), bossHealth = C(.9f, .16f, .16f), enemyHealth = C(.95f, .32f, .2f), recentDamage = C(.96f, .93f, .88f),
        },
        // Pale bone and parchment: warm grey-brown panels, ivory frames.
        UIThemePreset.Bone => new()
        {
            shadow = C(.08f, .06f, .05f), panel = C(.24f, .2f, .16f), panelShading = .35f,
            frame = C(.62f, .56f, .44f), frameHighlight = C(.9f, .85f, .7f), text = C(.95f, .92f, .85f), dimText = C(.8f, .74f, .62f),
            health = C(.78f, .3f, .24f), mana = C(.33f, .5f, .7f), stamina = C(.58f, .66f, .38f), experience = C(.85f, .66f, .3f),
            gold = C(1f, .86f, .45f), bossHealth = C(.75f, .15f, .12f), enemyHealth = C(.75f, .28f, .14f), recentDamage = C(.95f, .88f, .7f),
        },
        // Embers: near-black reds with orange frames.
        UIThemePreset.Ember => new()
        {
            shadow = C(.05f, .02f, .02f), panel = C(.16f, .07f, .06f), panelShading = .4f,
            frame = C(.7f, .25f, .12f), frameHighlight = C(1f, .6f, .25f), text = C(.98f, .9f, .82f), dimText = C(.85f, .65f, .5f),
            health = C(.85f, .2f, .15f), mana = C(.35f, .45f, .8f), stamina = C(.65f, .6f, .3f), experience = C(1f, .62f, .2f),
            gold = C(1f, .8f, .35f), bossHealth = C(.8f, .1f, .08f), enemyHealth = C(.9f, .35f, .12f), recentDamage = C(1f, .8f, .55f),
        },
        _ => null,
    };
}

// A saved UI colour theme. Make one with UIManager > Save As Theme; pick it with Preset = Saved.
[CreateAssetMenu(menuName = "LoomRoom/UI Theme")]
public class UITheme : ScriptableObject
{
    public UIThemeColors colors = new();
}
