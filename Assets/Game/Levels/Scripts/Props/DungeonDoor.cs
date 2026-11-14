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
    // The open leaf is solid too (the user, 2026-11-14: enemies walked and hit through open doors): a box on the hinge
    // round the leaf's own mesh and a navmesh cut of the same size, made at run time. Both stay off while it swings, so
    // it never pushes, traps or launches what it passes through, and come on once it has stopped and nothing stands
    // where it rests (a living character; loose pieces there are left out of its collisions instead); until then the spot
    // is looked at again now and then. A closed door is the barrier above.
    [Tooltip("Swing: the open leaf blocks walking, blows, sight, spells and arrows once it has stopped and its spot is clear.")]
    public bool solidWhenOpen = true;
    [Min(.02f), Tooltip("Swing: the least thickness of the open leaf's collider (local units).")] public float leafThickness = .1f;
    BoxCollider leafCollider; NavMeshObstacle leafObstacle; bool leafSettled; float nextClearCheck;
    static readonly Collider[] leafOverlaps = new Collider[64];
    public bool LeafSolid => leafSettled;
    [Min(.1f)] public float liftHeight=2.8f;
    [Min(.1f)] public float openingSpeed=4f;
    [Min(1)] public float health=25;
    public AudioClip openAudio;
    [Min(0), Tooltip("How far the sound of it opening carries (a sleeper hears a share of it).")] public float openNoise=7f;
    Vector3 closedPosition;
    bool open;
    public bool IsOpen=>open;
    // The moment it starts to open (board-game levels lay out what lies behind it).
    public event System.Action<DungeonDoor> Opened;
    public string Prompt=>open ? "Door open" : "Open dungeon door";
    public bool CanInteract(Character who)=>!open && who!=null && who.IsAlive && (who is Player || HasAccess(who));
    static bool HasAccess(Character who) { var access=who.GetComponent<DungeonDoorAccess>(); return access!=null && access.isActiveAndEnabled; }
    Quaternion closedRotation, swingTarget;
    // The leaf as authored (its handle away from the hinge): mirrored when the hinge goes to the other side, so the
    // handle stays on the far edge.
    Vector3 leafMeshScale; float authoredSide = -1f;
    void Awake(){closedPosition=leaf.localPosition;closedRotation=leaf.localRotation;swingTarget=closedRotation*Quaternion.Euler(0,swingDegrees,0);
        if(leafMesh!=null)leafMeshScale=leafMesh.localScale; if(Mathf.Abs(closedPosition.x)>.001f)authoredSide=Mathf.Sign(closedPosition.x);}
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
    // The collider and cut fitted round the leaf's meshes where they hang now (the hinge has just changed side), off
    // until the swing is over.
    void PrepareLeaf()
    {
        if (!solidWhenOpen) return;
        bool any = false; var bounds = new Bounds();
        foreach (var filter in leafMesh.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            var local = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = leaf.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
            }
        }
        if (!any) return;
        if (leafCollider == null)
        {
            leafCollider = leaf.gameObject.AddComponent<BoxCollider>();
            leafObstacle = leaf.gameObject.AddComponent<NavMeshObstacle>();
            leafObstacle.shape = NavMeshObstacleShape.Box; leafObstacle.carving = true; leafObstacle.carveOnlyStationary = true;
        }
        // A little short at its ends and top, so it never reaches into the jambs or the lintel.
        var size = new Vector3(Mathf.Max(.1f, bounds.size.x - .06f), Mathf.Max(.1f, bounds.size.y - .04f), Mathf.Max(leafThickness, bounds.size.z));
        leafCollider.size = size; leafCollider.center = bounds.center; leafCollider.enabled = false;
        leafObstacle.size = size; leafObstacle.center = bounds.center; leafObstacle.enabled = false;
        leafSettled = false; nextClearCheck = 0f;
    }

    // Nothing the leaf would push or shut in where it rests: no living character (with room for a navmesh agent's
    // radius round it, which the cut would take away). Loose pieces there (a body's parts, a dropped item, debris) don't
    // hold it up: the leaf is set down not touching them (Physics.IgnoreCollision, those colliders only), so none is
    // pushed or thrown, and it still stops everything else.
    bool LeafSpotClear()
    {
        var centre = leaf.TransformPoint(leafCollider.center);
        var half = Vector3.Scale(leafCollider.size * .5f, leaf.lossyScale);
        half = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z)) + new Vector3(.5f, 0f, .5f);
        int count = Physics.OverlapBoxNonAlloc(centre, half, leafOverlaps, leaf.rotation, ~0, QueryTriggerInteraction.Ignore);
        if (count == leafOverlaps.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var c = leafOverlaps[i];
            if (c == leafCollider || c == barrier) continue;
            if (c is CharacterController) return false;
            var owner = c.GetComponentInParent<Character>();
            if (owner != null && owner.IsAlive) return false;
        }
        for (int i = 0; i < count; i++)
        {
            var c = leafOverlaps[i];
            if (c != leafCollider && c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) Physics.IgnoreCollision(leafCollider, c, true);
        }
        return true;
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
            leafMesh.localScale=new Vector3(side==authoredSide ? leafMeshScale.x : -leafMeshScale.x,leafMeshScale.y,leafMeshScale.z);
            // With the hinge on their left, turning the same way always takes the leaf away from them.
            swingTarget=closedRotation*Quaternion.Euler(0,-Mathf.Abs(swingDegrees),0);
            PrepareLeaf();
        }
        open=true;Opened?.Invoke(this);NoiseEvents.Report(transform.position,openNoise);if(openAudio!=null && AudioManager.HasInstance)AudioManager.Instance.PlaySFX(openAudio,transform.position);}
    void Update(){
        if(!open)return;
        if(opening==Opening.Swing)
        {
            var target=swingTarget;
            leaf.localRotation=Quaternion.RotateTowards(leaf.localRotation,target,swingSpeed*Time.deltaTime);
            if(Quaternion.Angle(leaf.localRotation,target)<Mathf.Abs(swingDegrees)*.3f){barrier.enabled=false;obstacle.enabled=false;}
            if(leafCollider!=null && !leafSettled && Time.time>=nextClearCheck && Quaternion.Angle(leaf.localRotation,target)<.5f)
            {
                nextClearCheck=Time.time+.25f;
                if(LeafSpotClear()){leafCollider.enabled=true;leafObstacle.enabled=true;leafSettled=true;}
            }
            return;
        }
        leaf.localPosition=Vector3.MoveTowards(leaf.localPosition,closedPosition+Vector3.up*liftHeight,openingSpeed*Time.deltaTime);
        if(leaf.localPosition.y-closedPosition.y>=liftHeight*.95f){barrier.enabled=false;obstacle.enabled=false;}
    }
}
