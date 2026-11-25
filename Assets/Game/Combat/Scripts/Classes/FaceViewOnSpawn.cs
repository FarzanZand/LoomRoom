using UnityEngine;

// A burst that turns to face the view as it appears: its own flat (x right, y up) is then the picture's plane, so
// particles thrown out of a circle or an arc in that flat spread across the screen, never at the eye and never away
// into the wall behind the hit. On the Pianist's note bursts (Characters/Classes/Pianist/Effects), which are spawned
// with no rotation where a blow landed and throw their notes to the left and right of it. It turns once, as it is
// switched on; the notes then fly on in the world. Each player's own game turns its own copy to its own view.
public class FaceViewOnSpawn : MonoBehaviour
{
    void OnEnable()
    {
        var view = Camera.main;
        if (view != null) transform.rotation = view.transform.rotation;
    }
}
