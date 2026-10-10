using UnityEngine;

// Animator reads and writes shared by the player and enemy scripts. States are read by tag
// hash on any layer, never by name or layer index; parameters are only set when the
// controller has them (Character.HasParameter caches the lookup).
public static class AnimatorHelper
{
    // 0 for an empty tag: an empty tag would otherwise match every untagged state.
    public static int TagHash(string tag) => string.IsNullOrEmpty(tag) ? 0 : Animator.StringToHash(tag);

    public static bool Ready(Animator anim) => anim != null && anim.runtimeAnimatorController != null;

    // The state carries one of the given tag hashes (0 = unused slot, never matches).
    public static bool HasTag(AnimatorStateInfo state, int a, int b = 0, int c = 0, int d = 0)
    {
        int h = state.tagHash;
        return (a != 0 && h == a) || (b != 0 && h == b) || (c != 0 && h == c) || (d != 0 && h == d);
    }

    // True if the current or incoming state on any layer carries one of the tags.
    public static bool AnyLayerHasTag(Animator anim, int a, int b = 0, int c = 0, int d = 0)
    {
        if (!Ready(anim)) return false;
        for (int layer = 0; layer < anim.layerCount; layer++)
        {
            if (HasTag(anim.GetCurrentAnimatorStateInfo(layer), a, b, c, d)) return true;
            if (anim.IsInTransition(layer) && HasTag(anim.GetNextAnimatorStateInfo(layer), a, b, c, d)) return true;
        }
        return false;
    }

    // ── Safe parameter writes ─────────────────────────────────────────

    public static void SetFloat(Animator anim, string name, float value)
    {
        if (Character.HasParameter(anim, name, AnimatorControllerParameterType.Float)) anim.SetFloat(name, value);
    }

    public static void SetFloat(Animator anim, string name, float value, float damping)
    {
        if (Character.HasParameter(anim, name, AnimatorControllerParameterType.Float)) anim.SetFloat(name, value, damping, Time.deltaTime);
    }

    public static void SetBool(Animator anim, string name, bool value)
    {
        if (Character.HasParameter(anim, name, AnimatorControllerParameterType.Bool)) anim.SetBool(name, value);
    }

    public static void SetInteger(Animator anim, string name, int value)
    {
        if (Character.HasParameter(anim, name, AnimatorControllerParameterType.Int)) anim.SetInteger(name, value);
    }

    public static void SetTrigger(Animator anim, string name)
    {
        if (Character.HasParameter(anim, name, AnimatorControllerParameterType.Trigger)) anim.SetTrigger(name);
    }

    public static void ResetTrigger(Animator anim, string name)
    {
        if (Character.HasParameter(anim, name, AnimatorControllerParameterType.Trigger)) anim.ResetTrigger(name);
    }
}
