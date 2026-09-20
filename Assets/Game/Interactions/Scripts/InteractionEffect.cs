using System;
using Sirenix.OdinInspector;
using UnityEngine;

// What an interaction does when it fires. Mirrors the item system's EffectEntry: the
// common cases are configured inline with no script; Custom routes to an
// InteractionAction ScriptableObject for anything richer.
public enum InteractionEffectType
{
    GiveItem          = 0,
    PlayAudio         = 1,
    SetFlag           = 2,
    StartConversation = 3,
    Heal              = 4,
    Custom            = 99,
}

[Serializable]
public class InteractionEffect
{
    [ValueDropdown("TypeOptions")]
    public InteractionEffectType type = InteractionEffectType.GiveItem;

    static readonly ValueDropdownList<InteractionEffectType> TypeOptions = new()
    {
        { "Loot/Give Item",              InteractionEffectType.GiveItem },
        { "Feedback/Play Audio",         InteractionEffectType.PlayAudio },
        { "Progression/Set Flag",        InteractionEffectType.SetFlag },
        { "Dialogue/Start Conversation", InteractionEffectType.StartConversation },
        { "Vitals/Heal",                 InteractionEffectType.Heal },
        { "Custom (script asset)",       InteractionEffectType.Custom },
    };

    [ShowIf("@type == InteractionEffectType.GiveItem")]
    public ItemData item;
    [ShowIf("@type == InteractionEffectType.GiveItem"), Min(1)]
    public int count = 1;

    [ShowIf("@type == InteractionEffectType.PlayAudio")]
    public AudioData audio;

    [ShowIf("@type == InteractionEffectType.SetFlag")]
    public string flag;
    [ShowIf("@type == InteractionEffectType.SetFlag")]
    public int flagValue = 1;

    [ShowIf("@type == InteractionEffectType.StartConversation")]
    [Tooltip("Conversation title in the Dialogue Database.")]
    public string conversation;

    [ShowIf("@type == InteractionEffectType.Heal")]
    public float amount = 10f;

    [ShowIf("@type == InteractionEffectType.Custom")]
    public InteractionAction custom;

    // Returns false if the effect could not run (e.g. inventory full) so a consumable
    // interactable can refuse to consume itself.
    public bool Execute(InteractionContext ctx)
    {
        switch (type)
        {
            case InteractionEffectType.GiveItem:
                if (item == null || !(ctx.Who is Player p) || !InventoryManager.HasInstance) return false;
                return InventoryManager.Instance.Pickup(item, p, count);

            case InteractionEffectType.PlayAudio:
                if (audio != null && AudioManager.HasInstance)
                    AudioManager.Instance.PlaySFXData(audio, ctx.Trigger != null ? ctx.Trigger.transform.position : ctx.Who.transform.position);
                return true;

            case InteractionEffectType.SetFlag:
                if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(flag, flagValue);
                return true;

            case InteractionEffectType.StartConversation:
                return DialogueBridge.StartConversation(conversation, ctx.Who != null ? ctx.Who.transform : null,
                                                        ctx.Trigger != null ? ctx.Trigger.transform : null);

            case InteractionEffectType.Heal:
                ctx.Who?.Stats?.Heal(amount);
                return true;

            case InteractionEffectType.Custom:
                return custom != null && custom.Execute(ctx);
        }
        return true;
    }
}

public struct InteractionContext
{
    public Character           Who;
    public InteractableTrigger Trigger;
}

// Hook for interaction behaviour too rich for the inline effect enum. Concrete
// actions are ScriptableObjects placed under Assets/Game. Return true on
// success so the trigger can gate its consume on the outcome.
public abstract class InteractionAction : ScriptableObject
{
    public abstract bool Execute(InteractionContext ctx);
}
