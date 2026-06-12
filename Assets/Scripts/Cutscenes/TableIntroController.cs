using System.Collections;
using UnityEngine;

public class TableIntroController : MonoBehaviour
{
    public AudioClip tableIntroAudio;
    public Animator NPCanimator;
    public float musicBPM;
    protected float delayBetweenBeats;
    public GameObject object1;
    public GameObject object2;
    public GameObject object3;
    public GameObject object4;
    public GameObject object5;
    public GameObject object6;
    public GameObject object7;
    public GameObject object8;
    public GameObject weaponObject;
    
    public void PlayTableIntro()
    {
        StartCoroutine(PlayTableIntroRoutine());
    }

    private IEnumerator PlayTableIntroRoutine()
    {
            delayBetweenBeats = 60 / musicBPM;
            ProgressionManager.Instance.tableEntered = true;
            ScreenManager.Instance.FadeInOut(2f, 3f, 5f);
        
            yield return new WaitForSeconds(delayBetweenBeats*8);
            PlayerManager.Instance.SwapToPlayer(PlayerManager.ActivePlayer.TablePlayer);
            PlayerManager.Instance.SetControlsFrozen(true);
            AudioManager.Instance.PlayMusic(tableIntroAudio);
            yield return new WaitForSeconds(0.2f);
            
            yield return new WaitForSeconds(delayBetweenBeats*12);
            PlayerManager.Instance.SetControlsFrozen(false);
            yield return new WaitForSeconds(delayBetweenBeats*4f);            
            NPCanimator.SetTrigger("Point");
            yield return new WaitForSeconds(delayBetweenBeats*2f);
            
            //Spawn object 1
            object1.SetActive(true);
            AudioManager.Instance.PlaySFX2D("poof");
            yield return new WaitForSeconds(delayBetweenBeats*4f);
            object5.SetActive(true);
            object5.GetComponent<Animator>().SetTrigger("Wave");
            
            //Spawn object 2
            object2.SetActive(true);
            AudioManager.Instance.PlaySFX2D("poof");

            // Spawn object 3 waving npc
            yield return new WaitForSeconds(delayBetweenBeats*4f);
            object3.SetActive(true);
            object3.GetComponent<NPCController>().SnapRotationTowardsPlayer();
            AudioManager.Instance.PlaySFX2D("poof");
            yield return new WaitForSeconds(delayBetweenBeats*1f);
            object3.GetComponent<Animator>().SetTrigger("Wave");
            
            //Spawn object 5 NPC Stall
            yield return new WaitForSeconds(delayBetweenBeats*3f);
            object4.SetActive(true);
            AudioManager.Instance.PlaySFX2D("poof");
            
            //Trigger DM and light
            NPCanimator.SetTrigger("ReachOut");
            yield return new WaitForSeconds(delayBetweenBeats*2f);
            WorldManager.Instance.FadeDirectionalLight(1.4f, 3f);
            WorldManager.Instance.FadeDirectionalLightColor(Color.white, 3f);
            
            //Weapon spawn
            yield return new WaitForSeconds(delayBetweenBeats*8f);
            weaponObject.SetActive(true);
            var weaponController = weaponObject.GetComponentInChildren<ObjectController>(true);
            if (weaponController != null) weaponController.StartMove();
            else Debug.LogWarning("TableIntroController: no ObjectController found on weaponObject or its children.", weaponObject);
        yield break;
    }
}
