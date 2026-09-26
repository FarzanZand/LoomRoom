using UnityEngine;

// Decoration in a room template that the generator may remove if it would block a walkway.
public class DungeonTemplatePiece : MonoBehaviour
{
    [Tooltip("Remove this object when it lands on the centre cross or a doorway approach.")]
    public bool removeIfBlocking = true;
}
