using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Architecture merged into one mesh per material per square of ground, so culling still works by area; colliders stay
// on their original objects. At build the squares are small (8 cells): the table reveal lifts each chunk as one piece
// and hides the start room's near wall chunk by chunk. Once the floor is in play (Coarsen, from TableLevelLoader after
// the reveal) they are merged again into big squares (32 cells): measured on Dungeon1 floor 1, about 8% fewer draw
// calls; one mesh per room was worse (each room holds many wall and floor variants). A chunk made only of floor tiles
// casts no shadow: nothing lies below a floor to shade (another ~7%).
public class DungeonStaticGeometry : MonoBehaviour
{
    readonly List<Mesh> ownedMeshes=new();
    public const string FloorTileName="Flagstone";
    public void Combine(Transform root,float chunkSize)
    {
        var groups=new Dictionary<(Material,int,int),List<MeshFilter>>();
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>()) {
            var renderer=filter.GetComponent<MeshRenderer>();
            if(renderer==null || !renderer.enabled || filter.sharedMesh==null || !filter.sharedMesh.isReadable || filter.sharedMesh.subMeshCount!=1 || renderer.sharedMaterials.Length!=1 || renderer.sharedMaterial==null)continue;
            if(filter.GetComponentInParent<Animator>()!=null || filter.GetComponentInParent<Rigidbody>()!=null || filter.GetComponentInParent<DungeonContainer>()!=null
               || filter.GetComponentInParent<DungeonDestructible>()!=null)continue;   // a merged breakable would stay drawn once broken
            var p=root.InverseTransformPoint(filter.transform.position);
            var key=(renderer.sharedMaterial,Mathf.FloorToInt(p.x/chunkSize),Mathf.FloorToInt(p.z/chunkSize));
            if(!groups.TryGetValue(key,out var list)){list=new List<MeshFilter>();groups.Add(key,list);}list.Add(filter);
        }
        foreach(var group in groups) {
            if(group.Value.Count<2)continue;
            var instances=new CombineInstance[group.Value.Count];
            for(int i=0;i<instances.Length;i++){var f=group.Value[i];instances[i]=new CombineInstance{mesh=f.sharedMesh,transform=root.worldToLocalMatrix*f.transform.localToWorldMatrix};}
            var mesh=new Mesh{name="Dungeon static chunk",indexFormat=IndexFormat.UInt32};
            mesh.CombineMeshes(instances,true,true);ownedMeshes.Add(mesh);
            var go=new GameObject("Static chunk",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root,false);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=group.Key.Item1;
            if(group.Value.TrueForAll(f=>f.name==FloorTileName))go.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
            foreach(var f in group.Value)f.GetComponent<MeshRenderer>().enabled=false;
        }
    }
    // The build's chunks under root merged again into squares of chunkSize (same material, shadow mode and rendering
    // layers); the small chunks are switched off and kept, like the pieces they were made of.
    public void Coarsen(Transform root,float chunkSize)
    {
        if(root==null)return;
        var groups=new Dictionary<(Material,ShadowCastingMode,uint,int,int),List<MeshRenderer>>();
        foreach(Transform child in root)
        {
            if(child.name!="Static chunk" || !child.TryGetComponent<MeshRenderer>(out var renderer) || !renderer.enabled || renderer.forceRenderingOff)continue;
            // The mesh's own bounds: a hidden root (ceilings seen from the room) gives its renderers empty bounds.
            var mesh=child.GetComponent<MeshFilter>().sharedMesh;if(mesh==null)continue;
            var p=root.InverseTransformPoint(child.TransformPoint(mesh.bounds.center));
            var key=(renderer.sharedMaterial,renderer.shadowCastingMode,renderer.renderingLayerMask,Mathf.FloorToInt(p.x/chunkSize),Mathf.FloorToInt(p.z/chunkSize));
            if(!groups.TryGetValue(key,out var list)){list=new List<MeshRenderer>();groups.Add(key,list);}list.Add(renderer);
        }
        foreach(var group in groups)
        {
            if(group.Value.Count<2)continue;
            var instances=new CombineInstance[group.Value.Count];
            for(int i=0;i<instances.Length;i++){var r=group.Value[i];instances[i]=new CombineInstance{mesh=r.GetComponent<MeshFilter>().sharedMesh,transform=root.worldToLocalMatrix*r.transform.localToWorldMatrix};}
            var mesh=new Mesh{name="Dungeon static chunk",indexFormat=IndexFormat.UInt32};
            mesh.CombineMeshes(instances,true,true);ownedMeshes.Add(mesh);
            var go=new GameObject("Static chunk",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root,false);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            var merged=go.GetComponent<MeshRenderer>();merged.sharedMaterial=group.Key.Item1;merged.shadowCastingMode=group.Key.Item2;merged.renderingLayerMask=group.Key.Item3;
            foreach(var r in group.Value)r.enabled=false;
        }
    }
    void OnDestroy(){foreach(var mesh in ownedMeshes)if(mesh!=null)Destroy(mesh);}
}
