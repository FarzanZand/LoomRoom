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
            object1.SetActive(true);
            AudioManager.Instance.PlaySFX2D("poof");
            yield return new WaitForSeconds(delayBetweenBeats*4f);
            object2.SetActive(true);
            AudioManager.Instance.PlaySFX2D("poof");

            yield return new WaitForSeconds(delayBetweenBeats*4f);
            object3.SetActive(true);
            AudioManager.Instance.PlaySFX2D("poof");
            yield return new WaitForSeconds(delayBetweenBeats*4f);
            object4.SetActive(true);
            AudioManager.Instance.PlaySFX2D("poof");
            NPCanimator.SetTrigger("ReachOut");
            yield return new WaitForSeconds(delayBetweenBeats*2f);
            WorldManager.Instance.FadeDirectionalLight(WorldManager.Instance.directionalLight.intensity, 1.4f, 3f);
            WorldManager.Instance.FadeDirectionalLightColor(Color.white, 3f);
            
            
            yield return new WaitForSeconds(delayBetweenBeats*4f);
            weaponObject.SetActive(true);
            weaponObject.GetComponent<ObjectController>().StartMove();
        yield break;
    }
}
