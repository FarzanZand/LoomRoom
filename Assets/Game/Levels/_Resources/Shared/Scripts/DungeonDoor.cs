using UnityEngine;
using UnityEngine.AI;

// A lifting gate keeps narrow thresholds usable without a swing obstructing the corridor.
public class DungeonDoor : MonoBehaviour, IInteractable, IDamageable
{
    public Transform leaf;
    [Tooltip("Stationary masonry above the gate. Edit its width, depth and material in the door prefab.")]
    public Transform lintel;
    [Min(1.8f), Tooltip("Height of the clear doorway; the lintel fills the remaining height to the ceiling.")]
    public float openingHeight = 2.8f;
    [Tooltip("Unscaled cube mesh used to map the lintel to the same brick size as a full wall.")]
    public Mesh lintelSourceMesh;
    Mesh fittedLintelMesh;
    public Collider barrier;
    public NavMeshObstacle obstacle;
    [Min(.1f)] public float liftHeight=2.8f;
    [Min(.1f)] public float openingSpeed=4f;
    [Min(1)] public float health=25;
    public AudioClip openAudio;
    Vector3 closedPosition;
    bool open;
    public bool IsOpen=>open;
    public string Prompt=>open ? "Door open" : "Open dungeon door";
    public bool CanInteract(Character who)=>!open && who!=null && who.IsAlive && (who is Player || who.GetComponent<DungeonDoorAccess>()?.isActiveAndEnabled==true);
    void Awake(){closedPosition=leaf.localPosition;}
    public void FitCeiling(float ceilingHeight, float wallTileSize = 0)
    {
        if (lintel == null) return;
        float bottom = Mathf.Min(openingHeight, ceilingHeight) - .025f;
        var position = lintel.localPosition;
        position.y = (bottom + ceilingHeight + .025f) * .5f;
        lintel.localPosition = position;
        var size = lintel.localScale;
        size.y = ceilingHeight + .025f - bottom;
        lintel.localScale = size;
        if (lintelSourceMesh != null)
        {
            if (fittedLintelMesh == null) fittedLintelMesh = Instantiate(lintelSourceMesh);
            var uv = lintelSourceMesh.uv;
            var normals = lintelSourceMesh.normals;
            for (int i = 0; i < uv.Length; i++)
            {
                if (Mathf.Abs(normals[i].y) > .5f) continue;
                // Crop the top of a full wall texture instead of squeezing all its brick rows here.
                float textureSize = wallTileSize > 0 ? wallTileSize : 2f;
                uv[i].x *= Mathf.Abs(normals[i].z) > .5f
                    ? size.x * transform.localScale.x / textureSize : size.z * transform.localScale.z / textureSize;
                uv[i].y = (bottom + uv[i].y * size.y) / (wallTileSize > 0 ? wallTileSize : ceilingHeight);
            }
            fittedLintelMesh.uv = uv;
            lintel.GetComponent<MeshFilter>().sharedMesh = fittedLintelMesh;
        }
    }
    void OnDestroy() { if (fittedLintelMesh != null) Destroy(fittedLintelMesh); }
    public void Interact(Character who){if(CanInteract(who))Open();}
    public void TakeDamage(DamageInfo info){if(open)return;health-=Mathf.Max(0,info.Amount);if(health<=0)Open();}
    void Open(){open=true;NoiseEvents.Report(transform.position,7);if(openAudio!=null && AudioManager.HasInstance)AudioManager.Instance.PlaySFX(openAudio,transform.position);}
    void Update(){
        if(!open)return;
        leaf.localPosition=Vector3.MoveTowards(leaf.localPosition,closedPosition+Vector3.up*liftHeight,openingSpeed*Time.deltaTime);
        if(leaf.localPosition.y-closedPosition.y>=liftHeight*.95f){barrier.enabled=false;obstacle.enabled=false;}
    }
}
