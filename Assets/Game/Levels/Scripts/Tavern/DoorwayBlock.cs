using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// On the Tavern door: once the floor is in play, its doorway is cut out of the NavMesh, so no enemy walks through it,
// open or shut (the player is not on the NavMesh). Off while the floor is built: the generator checks that every room
// can be walked to.
public class DoorwayBlock : MonoBehaviour
{
    public NavMeshObstacle obstacle;

    IEnumerator Start()
    {
        if (obstacle == null) yield break;
        obstacle.enabled = false;
        yield return null;
        while (TableManager.Loader != null && TableManager.Loader.Busy) yield return null;
        obstacle.enabled = true;
    }
}
