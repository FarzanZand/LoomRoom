using System.Collections.Generic;

public class MenuManager : Singleton<MenuManager>
{
    private readonly HashSet<string> openMenus = new();

    public bool AnyMenuOpen => openMenus.Count > 0;

    public void OpenMenu(string id)  => openMenus.Add(id);
    public void CloseMenu(string id) => openMenus.Remove(id);
}
