using TMPro;
using UnityEngine;
using UnityEngine.UI;

// References into the table menu prefab (UI/Prefabs/Adventure/Table adventures). TableLevelMenu
// fills it: one AdventureOptionUI card per level, the class panel, and the footer buttons.
public class TableAdventureMenuView : MonoBehaviour
{
    public CanvasGroup group;
    public RectTransform panel;
    public TMP_Text heading;
    public Transform options;
    public AdventureOptionUI optionPrefab;
    public AdventureClassSelectionUI classPanel;
    public Button resume;
    public TMP_Text resumeDetail;
    public Button returnToRoom;
}
