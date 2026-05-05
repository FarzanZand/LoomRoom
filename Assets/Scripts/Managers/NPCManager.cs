using Sirenix.OdinInspector;
using UnityEngine;

public class NPCManager : Singleton<NPCManager>
{
    public Animator DMAnimator;
    public GameObject DMGameObject;

    [Button] 
    public void TestAnimation()
    {
        DMAnimator.SetTrigger("ReachOut");
    }
    
    
}
