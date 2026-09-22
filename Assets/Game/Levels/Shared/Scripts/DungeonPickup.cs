using UnityEngine;

public static class DungeonPickup
{
    public static WorldItem Spawn(ItemData item, Vector3 position, Transform parent)
    {
        if (item == null || !InventoryManager.HasInstance) return null;
        // Use the shared pickup path, so inventory, prompts, audio and hotbar remain consistent.
        var pickup = InventoryManager.Instance.Spawn(item, position + Vector3.up * .35f);
        if (pickup == null) return null;
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
        return pickup;
    }
}
