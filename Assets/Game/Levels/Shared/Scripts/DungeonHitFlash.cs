using UnityEngine;

// Preserve renderer property blocks so a hit never leaves a material permanently tinted.
public class DungeonHitFlash : MonoBehaviour
{
    Character character;
    Renderer[] renderers;
    MaterialPropertyBlock[] originals;
    MaterialPropertyBlock flash;
    float until;
    bool flashing;
    void Awake()
    {
        flash=new MaterialPropertyBlock();
        character=GetComponent<Character>();renderers=GetComponentsInChildren<Renderer>();
        originals=new MaterialPropertyBlock[renderers.Length];
        for(int i=0;i<renderers.Length;i++)originals[i]=new MaterialPropertyBlock();
        character.Damaged+=Hurt;
    }
    void Hurt(DamageInfo info)
    {
        if(info.Amount<=0 || info.Blocked)return;
        for(int i=0;i<renderers.Length;i++)
        {
            if(renderers[i]==null)continue;
            if(!flashing)renderers[i].GetPropertyBlock(originals[i]);
            renderers[i].GetPropertyBlock(flash);
            flash.SetColor("_BaseColor",new Color(2.4f,1.9f,1.45f,1));flash.SetColor("_Color",new Color(2.4f,1.9f,1.45f,1));
            renderers[i].SetPropertyBlock(flash);
        }
        flashing=true;until=Time.time+.1f;
    }
    void Update(){if(flashing && Time.time>=until)Restore();}
    void Restore(){if(!flashing)return;for(int i=0;i<renderers.Length;i++)if(renderers[i]!=null)renderers[i].SetPropertyBlock(originals[i]);flashing=false;}
    void OnDisable()=>Restore();
    void OnDestroy(){Restore();if(character!=null)character.Damaged-=Hurt;}
}
