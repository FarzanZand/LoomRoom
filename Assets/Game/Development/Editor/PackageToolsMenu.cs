using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Keep package-owned commands under Tools without editing Library/PackageCache.
[InitializeOnLoad]
public static class PackageToolsMenu
{
    const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly MethodInfo Add = typeof(Menu).GetMethod("AddMenuItem", StaticMembers,
        null, new[] { typeof(string), typeof(string), typeof(bool), typeof(int), typeof(Action), typeof(Func<bool>) }, null);
    static readonly MethodInfo Remove = typeof(Menu).GetMethod("RemoveMenuItem", StaticMembers,
        null, new[] { typeof(string) }, null);

    static PackageToolsMenu() => EditorApplication.delayCall += Relocate;

    static void Relocate()
    {
        if (Add == null || Remove == null)
        {
            Debug.LogWarning("Package menu relocation is unavailable in this Unity version; original menus were retained.");
            return;
        }
        var commands = new Dictionary<string, (MethodInfo method, MenuItem item)>();
        var validators = new Dictionary<string, MethodInfo>();
        foreach (var method in TypeCache.GetMethodsWithAttribute<MenuItem>())
        foreach (var item in method.GetCustomAttributes<MenuItem>())
        {
            if (!item.menuItem.StartsWith("Animation Rigging/", StringComparison.Ordinal) &&
                !item.menuItem.StartsWith("Jobs/", StringComparison.Ordinal)) continue;
            if (item.validate) validators[item.menuItem] = method;
            else commands[item.menuItem] = (method, item);
        }
        foreach (var pair in commands)
        {
            string original = pair.Key;
            string relocated = "Tools/" + original;
            var method = pair.Value.method;
            validators.TryGetValue(original, out var validator);
            Action execute = () => Invoke(method);
            Func<bool> validate = () =>
            {
                bool enabled = validator == null || (bool)Invoke(validator);
                // Burst's validators still address their old paths. Mirror their
                // settings onto the relocated checkboxes without changing Burst.
                bool? check = BurstChecked(original);
                if (check.HasValue) Menu.SetChecked(relocated, check.Value);
                return enabled;
            };
            Add.Invoke(null, new object[] { relocated, "", Menu.GetChecked(original), pair.Value.item.priority, execute, validate });
            Remove.Invoke(null, new object[] { original });
        }
    }

    static object Invoke(MethodInfo method)
    {
        var args = method.GetParameters().Length == 0 ? null : new object[] { new MenuCommand(Selection.activeObject) };
        try { return method.Invoke(null, args); }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
    }

    static bool? BurstChecked(string path)
    {
        if (!path.StartsWith("Jobs/Burst/", StringComparison.Ordinal)) return null;
        var options = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Unity.Burst.Editor.BurstEditorOptions")).FirstOrDefault(t => t != null);
        if (options == null) return null;
        bool Read(string name) => (bool)(options.GetProperty(name, StaticMembers)?.GetValue(null)
            ?? options.GetField(name, StaticMembers)?.GetValue(null) ?? false);
        switch (path.Substring("Jobs/Burst/".Length))
        {
            case "Enable Compilation": return Read("EnableBurstCompilation");
            case "Safety Checks/Off": return !Read("EnableBurstSafetyChecks") && !Read("ForceEnableBurstSafetyChecks");
            case "Safety Checks/On": return Read("EnableBurstSafetyChecks") && !Read("ForceEnableBurstSafetyChecks");
            case "Safety Checks/Force On": return Read("ForceEnableBurstSafetyChecks");
            case "Synchronous Compilation": return Read("EnableBurstCompileSynchronously");
            case "Native Debug Mode Compilation": return Read("EnableBurstDebug");
            case "Show Timings": return Read("EnableBurstTimings");
            default: return null;
        }
    }
}
