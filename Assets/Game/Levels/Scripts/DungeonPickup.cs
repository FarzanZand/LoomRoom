using DG.Tweening;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public static class DungeonPickup
{
    // popFrom: the items jump out of there (a chest's mouth) and land where they would lie.
    public static void SpawnDrops(IReadOnlyList<LootSource.Drop> drops, Vector3 origin, Transform parent, Vector3? popFrom = null)
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
                var item=Spawn(drop.item,target,parent,count);remaining-=count;
                if(item!=null && popFrom.HasValue) PopOut(item.transform,popFrom.Value,index);
            }
        }
    }
    // From the source to where it settled, in an arc, one item after another.
    // It leaves upright and tips over onto the floor on the way down.
    static void PopOut(Transform item, Vector3 from, int order)
    {
        Vector3 landing = item.position;
        Quaternion lying = item.rotation;
        item.position = from;
        item.rotation = Quaternion.Euler(0f, lying.eulerAngles.y, 0f);
        item.DOJump(landing, .7f + .15f * (order % 3), 1, .55f).SetDelay(.08f * order).SetEase(Ease.Linear).SetLink(item.gameObject);
        item.DORotateQuaternion(lying, .5f).SetDelay(.08f * order + .05f).SetEase(Ease.InQuad).SetLink(item.gameObject);
    }

    // Kinematic loot never falls, so it is put down as it would land: anything taller than it is wide (a sword, a
    // mace, a shield on its rim) lies on its side, and every item gets a random heading.
    static void LayFlat(WorldItem pickup)
    {
        var t = pickup.transform;
        float yaw = Random.Range(0f, 360f);
        t.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (!DungeonFeatureUtility.RendererBounds(pickup.GetComponentsInChildren<Renderer>(), out var upright)) return;
        if (upright.size.y > Mathf.Max(upright.size.x, upright.size.z) * 1.15f)
            t.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(90f, 0f, Random.Range(-20f, 20f));
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
        if (DungeonFeatureUtility.RendererBounds(pickup.GetComponentsInChildren<Renderer>(), out var bounds))
        {
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (longest > .9f) pickup.transform.localScale *= .9f / longest;
        }
        var body = pickup.GetComponent<Rigidbody>();
        if (body != null) { body.isKinematic = true; body.useGravity = false; }
        LayFlat(pickup);
        SettleOnFloor(pickup);
        CoopWorld.Spawned(pickup.gameObject);
        return pickup;
    }

    // Kinematic loot doesn't fall, so place it resting on the floor below it. Only fixed geometry
    // counts: a dying enemy, the player, other loot, a chest or flying debris can move or vanish
    // and would leave the item hanging in the air.
    static void SettleOnFloor(WorldItem pickup)
    {
        if (!DungeonFeatureUtility.RendererBounds(pickup.GetComponentsInChildren<Renderer>(), out var bounds)) return;
        var from = new Vector3(bounds.center.x, bounds.max.y + .5f, bounds.center.z);
        var hits = Physics.RaycastAll(from, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        foreach (var hit in hits)
        {
            if (hit.transform.IsChildOf(pickup.transform) || !IsFloor(hit.collider)) continue;
            pickup.transform.position += Vector3.up * (hit.point.y + .02f - bounds.min.y);
            return;
        }
        if (NavMesh.SamplePosition(bounds.center, out var floor, 3f, NavMesh.AllAreas))
            pickup.transform.position += Vector3.up * (floor.position.y + .02f - bounds.min.y);
    }

    static bool IsFloor(Collider c) =>
        c.attachedRigidbody == null && c.GetComponentInParent<Character>() == null &&
        c.GetComponentInParent<CharacterController>() == null && c.GetComponentInParent<WorldItem>() == null &&
        c.GetComponentInParent<CoinPickup>() == null && c.GetComponentInParent<DungeonContainer>() == null &&
        c.GetComponentInParent<DungeonCorpse>() == null;
}
