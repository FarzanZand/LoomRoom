using UnityEngine;

// Add to intelligent enemies that can operate gates. Creatures without it cannot.
[RequireComponent(typeof(EnemyBrain))]
public class DungeonDoorAccess : MonoBehaviour
{
    [Min(.5f)] public float reach=1.8f;
    EnemyBrain brain;
    float nextCheck;
    readonly Collider[] nearby=new Collider[24];
    void Awake()=>brain=GetComponent<EnemyBrain>();
    void Update()
    {
        if(Time.time<nextCheck || brain==null || !brain.enabled || !brain.Character.IsAlive)return;
        nextCheck=Time.time+.3f;
        if(brain.State!=EnemyState.Chase && brain.State!=EnemyState.Investigate)return;
        var origin=transform.position+Vector3.up;
        int count=Physics.OverlapSphereNonAlloc(origin,reach,nearby,~0,QueryTriggerInteraction.Collide);
        for(int i=0;i<count;i++) {
            var door=nearby[i].GetComponentInParent<DungeonDoor>();
            if(door==null || door.IsOpen)continue;
            if(Physics.Linecast(origin,door.transform.position+Vector3.up,out var hit,~0,QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<DungeonDoor>()!=door)continue;
            door.Interact(brain.Character);break;
        }
    }
}
