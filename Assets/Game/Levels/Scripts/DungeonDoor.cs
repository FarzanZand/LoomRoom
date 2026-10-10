using UnityEngine;
using UnityEngine.AI;

// A dungeon door: a leaf that swings open on its hinge or, as a gate, lifts into the ceiling.
// Swinging, it always opens away from whoever opens it, hinged on their left: the hinge and the
// leaf are moved to that side just before it turns.
public class DungeonDoor : MonoBehaviour, IInteractable, IDamageable
{
    public enum Opening { Lift = 0, Swing = 1 }
    [Tooltip("Lift: the leaf rises into the ceiling. Swing: it turns on its hinge (the leaf's pivot).")]
    public Opening opening = Opening.Lift;
    [Tooltip("What moves: the gate, or the hinge the door leaf hangs on.")]
    public Transform leaf;
    [Tooltip("Swing: the door leaf under the hinge; it is moved to the far side of the hinge when the hinge changes side.")]
    public Transform leafMesh;
    [Tooltip("Swing: half the doorway's width (local units): where the hinge sits, left or right of the middle.")]
    public float halfWidth = 1f;
    [Tooltip("Swing: degrees the door turns open, away from whoever opens it.")]
    public float swingDegrees = 100f;
    [Min(1), Tooltip("Swing: degrees a second.")] public float swingSpeed = 170f;
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
    // The moment it starts to open (board-game levels lay out what lies behind it).
    public event System.Action<DungeonDoor> Opened;
    public string Prompt=>open ? "Door open" : "Open dungeon door";
    public bool CanInteract(Character who)=>!open && who!=null && who.IsAlive && (who is Player || HasAccess(who));
    static bool HasAccess(Character who) { var access=who.GetComponent<DungeonDoorAccess>(); return access!=null && access.isActiveAndEnabled; }
    Quaternion closedRotation, swingTarget;
    void Awake(){closedPosition=leaf.localPosition;closedRotation=leaf.localRotation;swingTarget=closedRotation*Quaternion.Euler(0,swingDegrees,0);}
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
    public void Interact(Character who){if(CanInteract(who))Open(who.transform.position);}
    public void TakeDamage(DamageInfo info){if(open)return;health-=Mathf.Max(0,info.Amount);if(health<=0)Open(info.Source!=null ? info.Source.transform.position : (Vector3?)null);}
    public void Open() => Open(null);
    // from: where the opener stands. The hinge goes to their left and the door swings away from them.
    public void Open(Vector3? from){
        if(open)return;
        if(opening==Opening.Swing && leafMesh!=null)
        {
            bool front=!from.HasValue || Vector3.Dot(from.Value-transform.position,transform.forward)<0f;
            // Seen from in front (behind the door's forward) their left is local -x; from behind it is +x.
            float side=front ? -1f : 1f;
            leaf.localPosition=new Vector3(side*halfWidth,closedPosition.y,closedPosition.z);
            leafMesh.localPosition=new Vector3(-side*halfWidth,leafMesh.localPosition.y,leafMesh.localPosition.z);
            // With the hinge on their left, turning the same way always takes the leaf away from them.
            swingTarget=closedRotation*Quaternion.Euler(0,-Mathf.Abs(swingDegrees),0);
        }
        open=true;Opened?.Invoke(this);NoiseEvents.Report(transform.position,7);if(openAudio!=null && AudioManager.HasInstance)AudioManager.Instance.PlaySFX(openAudio,transform.position);}
    void Update(){
        if(!open)return;
        if(opening==Opening.Swing)
        {
            var target=swingTarget;
            leaf.localRotation=Quaternion.RotateTowards(leaf.localRotation,target,swingSpeed*Time.deltaTime);
            if(Quaternion.Angle(leaf.localRotation,target)<Mathf.Abs(swingDegrees)*.3f){barrier.enabled=false;obstacle.enabled=false;}
            return;
        }
        leaf.localPosition=Vector3.MoveTowards(leaf.localPosition,closedPosition+Vector3.up*liftHeight,openingSpeed*Time.deltaTime);
        if(leaf.localPosition.y-closedPosition.y>=liftHeight*.95f){barrier.enabled=false;obstacle.enabled=false;}
    }
}
