using UnityEngine;

// Discrete, eight-pose-per-second animation of an original voxel creature.
// Combat timing remains owned by EnemyBrain; this only presents its state.
public class CryptMiteAnimation : MonoBehaviour
{
    public Transform shell, jaw;
    public Transform[] legs;
    EnemyBrain brain;
    Character character;
    float nextPose, stateStart, hurtUntil;
    EnemyState previous;
    void Start()
    {
        brain=GetComponent<EnemyBrain>(); character=GetComponent<Character>();
        character.Damaged+=Hurt;
    }
    void Hurt(DamageInfo _) { hurtUntil=Time.time+.25f; }
    void OnDestroy() { if(character!=null) character.Damaged-=Hurt; }
    void LateUpdate()
    {
        if(brain==null || shell==null) return;
        if(brain.State!=previous) { previous=brain.State;stateStart=Time.time; }
        if(Time.time<nextPose) return;
        nextPose=Time.time+.125f;
        float t=Time.time-stateStart;
        if(!character.IsAlive)
        {
            shell.localPosition=new Vector3(0,.22f,0); shell.localRotation=Quaternion.Euler(0,0,72);
            if(jaw!=null) jaw.localRotation=Quaternion.Euler(45,0,0);
            foreach(var leg in legs) if(leg!=null) leg.localRotation=Quaternion.Euler(0,0,55);
            return;
        }
        bool attack=brain.State==EnemyState.Attack;
        bool moving=brain.Motor!=null && brain.Motor.Velocity.sqrMagnitude>.05f;
        float phase=Mathf.Floor(Time.time*8)*Mathf.PI*.5f;
        float bob=moving ? Mathf.Abs(Mathf.Sin(phase))*.055f : 0;
        float lunge=attack ? (t<.3f ? -.12f : t<.55f ? .3f : 0) : 0;
        shell.localPosition=new Vector3(0,.6f+bob+(attack && t<.3f ? -.12f:0),lunge);
        shell.localRotation=Quaternion.Euler(Time.time<hurtUntil ? -18:0,0,moving ? Mathf.Sin(phase)*5:0);
        if(jaw!=null) jaw.localRotation=Quaternion.Euler(attack && t<.5f ? 32:0,0,0);
        for(int i=0;i<legs.Length;i++) if(legs[i]!=null)
            legs[i].localRotation=Quaternion.Euler(0,moving ? Mathf.Sin(phase+i*Mathf.PI)*24:0,(i%2==0 ? -1:1)*(attack ? 20:8));
    }
}
