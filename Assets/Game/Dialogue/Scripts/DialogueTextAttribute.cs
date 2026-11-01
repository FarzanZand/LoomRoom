using System;

// Marks a text field as dialogue for Asset Studio's Dialogue view when its name doesn't say so (fields named lines,
// line, or ending in Lines or Line are found without it): an answer button's label, say. A category files the field
// there instead of under its owner's (a place's rumour is the tavern keeper's to say, so it shows under Tavern), and
// a switch (a bool field on the same object) lists it only while that is on (only places that are rumours).
[AttributeUsage(AttributeTargets.Field)]
public class DialogueTextAttribute : Attribute
{
    public readonly string category, onlyIf;
    public DialogueTextAttribute(string category = null, string onlyIf = null) { this.category = category; this.onlyIf = onlyIf; }
}
