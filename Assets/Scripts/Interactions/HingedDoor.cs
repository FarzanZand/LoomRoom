using UnityEngine;

// Uses the same targeting, prompt and interaction flow as pickups and the table.
// Author the hinge at the edge of the leaf; scale the prefab uniformly for either game world.
public class HingedDoor : MonoBehaviour, IInteractable
{
    [SerializeField] Transform hinge;
    [SerializeField] BoxCollider leafCollider;
    [SerializeField] InteractableTrigger interactionTrigger;
    [SerializeField] string doorName = "door";
    [SerializeField, Range(-170,170)] float openAngle = 100;
    [SerializeField, Min(1)] float degreesPerSecond = 140;
    [SerializeField] bool startsOpen;
    [SerializeField] LayerMask obstructionMask = ~0;
    [SerializeField] InteractionEffect openingEffect = new() { type = InteractionEffectType.PlayAudio };
    [SerializeField] InteractionEffect closingEffect = new() { type = InteractionEffectType.PlayAudio };

    readonly Collider[] overlaps = new Collider[64];
    Quaternion closedRotation;
    float angle;
    bool targetOpen;
    bool moving;
    Character lastInteractor;

    public string Prompt => (targetOpen ? "Close " : "Open ") + doorName;
    public bool IsOpen => targetOpen;
    public bool IsMoving => moving;
    public float CurrentAngle => angle;
    public Transform Hinge => hinge;

    void Awake()
    {
        if (!hinge || !leafCollider || !interactionTrigger) { Debug.LogError("Door requires hinge, leaf collider and interaction trigger.", this); enabled=false; return; }
        closedRotation=hinge.localRotation;
        targetOpen=startsOpen;
        angle=startsOpen ? openAngle : 0;
        ApplyPose(angle);
    }

    public bool CanInteract(Character who) => isActiveAndEnabled && hinge && leafCollider;
    public void Interact(Character who) { if(CanInteract(who)) SetOpen(!targetOpen,who); }

    public void SetOpen(bool open, Character who = null)
    {
        if(!enabled || !hinge || targetOpen==open && moving) return;
        if(targetOpen==open && Mathf.Approximately(angle,open ? openAngle : 0)) return;
        targetOpen=open;
        lastInteractor=who;
        moving=true;
        if(open) openingEffect?.Execute(new InteractionContext { Who=who, Trigger=interactionTrigger });
    }

    void Update()
    {
        if(!moving) return;
        float target=targetOpen ? openAngle : 0;
        float remaining=Mathf.Min(degreesPerSecond*Time.deltaTime,Mathf.Abs(target-angle));
        // Small angular substeps prevent tunnelling through a player on a long frame.
        while(remaining>0.0001f)
        {
            float step=Mathf.Min(remaining,2f);
            float next=Mathf.MoveTowards(angle,target,step);
            if(IsObstructed(next)) { moving=false; return; }
            angle=next; ApplyPose(angle); remaining-=step;
        }
        if(Mathf.Abs(angle-target)<.01f)
        {
            angle=target; ApplyPose(angle); moving=false;
            if(!targetOpen) closingEffect?.Execute(new InteractionContext { Who=lastInteractor, Trigger=interactionTrigger });
        }
    }

    void ApplyPose(float value) => hinge.localRotation=closedRotation*Quaternion.Euler(0,value,0);

    bool IsObstructed(float candidateAngle)
    {
        Quaternion rotation=hinge.parent.rotation*closedRotation*Quaternion.Euler(0,candidateAngle,0);
        Vector3 scale=hinge.lossyScale;
        scale=new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
        Vector3 center=hinge.position+rotation*Vector3.Scale(leafCollider.center,scale);
        Vector3 half=Vector3.Scale(leafCollider.size*.5f,scale);
        // A small surface tolerance keeps the authored hinge and closed jamb from self-blocking.
        half=Vector3.Max(half-Vector3.one*(.006f*Mathf.Max(scale.x,scale.y,scale.z)),Vector3.one*.001f);
        int count=Physics.OverlapBoxNonAlloc(center,half,overlaps,rotation,obstructionMask,QueryTriggerInteraction.Ignore);
        if(count==overlaps.Length) return true;
        for(int i=0;i<count;i++)
            if(overlaps[i] && !overlaps[i].transform.IsChildOf(transform)) return true;
        return false;
    }
}
