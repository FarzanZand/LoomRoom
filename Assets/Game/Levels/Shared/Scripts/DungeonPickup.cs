using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public static class DungeonPickup
{
    public static void SpawnDrops(IReadOnlyList<LootSource.Drop> drops, Vector3 origin, Transform parent)
    {
        int index=0;
        foreach(var drop in drops) {
            if(drop.item==null)continue;
            int remaining=drop.quantity;
            while(remaining>0) {
                int count=Mathf.Min(remaining,Mathf.Max(1,drop.item.maxStackSize));
                float angle=index++*2.399963f;
                var target=origin+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(.35f+.12f*Mathf.Sqrt(index));
                if(NavMesh.SamplePosition(origin,out var anchor,2,NavMesh.AllAreas)) {
                    target=anchor.position;
                    var desired=anchor.position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*.7f;
                    if(!NavMesh.Raycast(anchor.position,desired,out var edge,NavMesh.AllAreas))target=desired;
                    else target=Vector3.Lerp(anchor.position,edge.position,.7f);
                }
                Spawn(drop.item,target,parent,count);remaining-=count;
            }
        }
    }
    public static WorldItem Spawn(ItemData item, Vector3 position, Transform parent, int count=1)
    {
        if (item == null || !InventoryManager.HasInstance) return null;
        // Use the shared pickup path, so inventory, prompts, audio and hotbar remain consistent.
        var pickup = InventoryManager.Instance.Spawn(item, position + Vector3.up * .35f);
        if (pickup == null) return null;
        pickup.Init(item,count);
        pickup.transform.SetParent(parent, true);
        // Existing pickup meshes vary in size; constrain floor loot without changing held equipment.
        var renderers = pickup.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (longest > .9f) pickup.transform.localScale *= .9f / longest;
        }
        var body = pickup.GetComponent<Rigidbody>();
        if (body != null) { body.isKinematic = true; body.useGravity = false; }
        SettleOnFloor(pickup);
        return pickup;
    }

    // Kinematic loot doesn't fall, so place it resting on whatever is below it.
    static void SettleOnFloor(WorldItem pickup)
    {
        var renderers = pickup.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        var from = new Vector3(bounds.center.x, bounds.max.y + .5f, bounds.center.z);
        var hits = Physics.RaycastAll(from, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        foreach (var hit in hits)
        {
            if (hit.transform.IsChildOf(pickup.transform)) continue;
            pickup.transform.position += Vector3.up * (hit.point.y + .02f - bounds.min.y);
            return;
        }
    }
}
