using DG.Tweening;
using UnityEngine;

// DOTween starts with room for 200 tweens and 50 sequences and grows when they run out, writing a warning to the console
// each time (a floor's barrels and crates breaking at once filled the sequences: their debris timers are
// DOVirtual.DelayedCall, a sequence each). It starts here with room enough, before any scene loads.
static class TweenSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        // Init returns null when DOTween is already running (domain reload off: the next Play in the editor), so the
        // capacity is set on its own after it.
        DOTween.Init();
        DOTween.SetTweensCapacity(300, 150);
    }
}
