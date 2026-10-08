using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Small spatial chunks preserve culling; colliders stay on their original objects.
public class DungeonStaticGeometry : MonoBehaviour
{
    readonly List<Mesh> ownedMeshes=new();
    public void Combine(Transform root,float chunkSize)
    {
        var groups=new Dictionary<(Material,int,int),List<MeshFilter>>();
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>()) {
            var renderer=filter.GetComponent<MeshRenderer>();
            if(renderer==null || !renderer.enabled || filter.sharedMesh==null || !filter.sharedMesh.isReadable || filter.sharedMesh.subMeshCount!=1 || renderer.sharedMaterials.Length!=1 || renderer.sharedMaterial==null)continue;
            if(filter.GetComponentInParent<Animator>()!=null || filter.GetComponentInParent<Rigidbody>()!=null || filter.GetComponentInParent<DungeonContainer>()!=null)continue;
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
            foreach(var f in group.Value)f.GetComponent<MeshRenderer>().enabled=false;
        }
    }
    void OnDestroy(){foreach(var mesh in ownedMeshes)if(mesh!=null)Destroy(mesh);}
}
