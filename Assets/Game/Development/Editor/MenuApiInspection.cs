using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;

[InitializeOnLoad]
public static class MenuApiInspection
{
    static MenuApiInspection() => EditorApplication.delayCall += () => File.WriteAllLines("Temp/MenuApi.txt", typeof(Menu).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Select(m=>m.ToString()));
}
