using UnityEngine;

// Template for custom item effects. Copy this file, rename the class and the menu
// path, and implement Apply. Create the asset under Assets/Game/Items/Effects, then drop it
// into an EffectEntry with type = Custom.
[CreateAssetMenu(menuName = "Items/Effects/Log Message")]
public class LogMessageEffect : ItemEffect
{
    [TextArea] public string message = "Custom effect fired";

    public override string Describe(EffectEntry entry) => message;

    public override void Apply(EffectEntry entry, EffectContext ctx)
    {
        string who = ctx.User != null ? ctx.User.DisplayName : "nobody";
        Debug.Log($"[{name}] {message} (used by {who}, item {ctx.Item?.itemName})");
    }
}
