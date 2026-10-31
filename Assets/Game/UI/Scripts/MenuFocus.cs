using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Where the keyboard or a pad is in a menu. The plain grey buttons only lighten a little when selected, so that a
// button clicked with the mouse doesn't stay lit; while the keys or a pad drive the menu the selected one is tinted gold
// instead, and it goes back to plain as soon as the mouse moves or clicks. Menus call Tint every frame they are open.
public static class MenuFocus
{
    static readonly Color Gold = new(1f, .84f, .42f);
    static bool keyed;
    static readonly Dictionary<Selectable, Color> plain = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { keyed = false; plain.Clear(); }

    // True once a key or a pad button is pressed, false again when the mouse moves or clicks.
    public static bool Keyed
    {
        get
        {
            var mouse = Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 4f || mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) keyed = false;
            else if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) keyed = true;
            else if (Gamepad.current != null && (Gamepad.current.dpad.ReadValue() != Vector2.zero || Gamepad.current.leftStick.ReadValue().sqrMagnitude > .25f || Gamepad.current.buttonSouth.wasPressedThisFrame)) keyed = true;
            return keyed;
        }
    }

    public static void Tint(IList<Selectable> selectables)
    {
        bool on = Keyed;
        for (int i = 0; i < selectables.Count; i++)
        {
            var s = selectables[i];
            if (s == null || s.transition != Selectable.Transition.ColorTint) continue;
            if (!plain.TryGetValue(s, out var own)) plain[s] = own = s.colors.selectedColor;
            var want = on ? Gold : own;
            if (s.colors.selectedColor == want) continue;
            var colours = s.colors;
            colours.selectedColor = want;
            s.colors = colours;
        }
    }

    // Up and down walk the list in order (the last wraps to the first); left and right are left to each control (a
    // slider's value), or move to `side` when given.
    public static void Chain(IList<Selectable> order)
    {
        var live = new List<Selectable>();
        foreach (var s in order) if (s != null && s.gameObject.activeInHierarchy && s.interactable) live.Add(s);
        for (int i = 0; i < live.Count; i++)
        {
            var nav = live[i].navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = live[(i - 1 + live.Count) % live.Count];
            nav.selectOnDown = live[(i + 1) % live.Count];
            nav.selectOnLeft = live[i] is Slider ? null : nav.selectOnLeft;
            nav.selectOnRight = live[i] is Slider ? null : nav.selectOnRight;
            live[i].navigation = nav;
        }
    }

    // Each control's neighbours are the nearest of the group in each direction on screen (within 60 degrees, else 85), so
    // keys and a pad move about a screen of rows and cells and never leave it for buttons elsewhere. Controls that can't
    // be used are left out (none can be reached).
    public static void LinkNearest(IList<Selectable> group)
    {
        for (int i = 0; i < group.Count; i++)
        {
            var s = group[i];
            if (s == null) continue;
            var nav = s.navigation;
            nav.mode = Navigation.Mode.Explicit;
            bool live = s.gameObject.activeInHierarchy && s.IsInteractable();
            nav.selectOnUp = live ? Nearest(group, s, Vector2.up) : null;
            nav.selectOnDown = live ? Nearest(group, s, Vector2.down) : null;
            nav.selectOnLeft = live && !(s is Slider) ? Nearest(group, s, Vector2.left) : null;
            nav.selectOnRight = live && !(s is Slider) ? Nearest(group, s, Vector2.right) : null;
            s.navigation = nav;
        }
    }

    // Straight ahead first; with nothing there, anything on that side (Leave, low and far right of the last ware).
    static Selectable Nearest(IList<Selectable> group, Selectable from, Vector2 way) => Nearest(group, from, way, 60f) ?? Nearest(group, from, way, 85f);

    static Selectable Nearest(IList<Selectable> group, Selectable from, Vector2 way, float cone)
    {
        Vector2 here = Centre(from);
        Selectable best = null; float bestScore = float.MaxValue;
        for (int i = 0; i < group.Count; i++)
        {
            var s = group[i];
            if (s == null || s == from || !s.gameObject.activeInHierarchy || !s.IsInteractable()) continue;
            var to = Centre(s) - here;
            float along = Vector2.Dot(to, way);
            if (along <= 1f || Vector2.Angle(to, way) > cone) continue;
            float score = along + (to - way * along).magnitude * 2f;   // straight on before off to the side
            if (score < bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    static readonly Vector3[] corners = new Vector3[4];
    static Vector2 Centre(Selectable s)
    {
        ((RectTransform)s.transform).GetWorldCorners(corners);
        return (corners[0] + corners[2]) * .5f;
    }

    // A selection inside a scrolled list is scrolled into view.
    public static void Follow(ScrollRect list, GameObject selected)
    {
        if (list == null || selected == null || list.content == null || !selected.transform.IsChildOf(list.content)) return;
        var view = list.viewport != null ? list.viewport : (RectTransform)list.transform;
        var item = (RectTransform)selected.transform;
        var corners = new Vector3[4];
        item.GetWorldCorners(corners);
        float top = view.InverseTransformPoint(corners[1]).y, bottom = view.InverseTransformPoint(corners[0]).y;
        float viewTop = view.rect.yMax, viewBottom = view.rect.yMin;
        float shift = top > viewTop ? top - viewTop : bottom < viewBottom ? bottom - viewBottom : 0f;
        if (Mathf.Abs(shift) < .5f) return;
        var p = list.content.anchoredPosition;
        list.content.anchoredPosition = new Vector2(p.x, p.y - shift);
    }
}
