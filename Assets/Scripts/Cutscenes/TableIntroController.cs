using System.Collections;
using UnityEngine;

public class TableIntroController : MonoBehaviour
{
    public AudioClip tableIntroAudio;
    public Animator NPCanimator;
    public GameObject object1;
    public GameObject object2;
    public GameObject object3;
    public GameObject object4;
    public GameObject object5;
    public GameObject object6;
    public GameObject object7;
    public GameObject object8;
    
    public void PlayTableIntro()
    {
        StartCoroutine(PlayTableIntroRoutine());
    }

    private IEnumerator PlayTableIntroRoutine()
    {

            ProgressionManager.Instance.tableEntered = true;
            ScreenManager.Instance.FadeInOut(2f, 3f, 5f);
        
            yield return new WaitForSeconds(4f);
            PlayerManager.Instance.SwapToPlayer(PlayerManager.ActivePlayer.TablePlayer);
            PlayerManager.Instance.SetControlsFrozen(true);
            AudioManager.Instance.PlayMusic(tableIntroAudio);
            
            yield return new WaitForSeconds(7f);
            PlayerManager.Instance.SetControlsFrozen(false);
            yield return new WaitForSeconds(3f);
            object1.SetActive(true);
            yield return new WaitForSeconds(2f);
            object2.SetActive(true);
            yield return new WaitForSeconds(2f);
            object3.SetActive(true);
            yield return new WaitForSeconds(2f);
            NPCanimator.SetTrigger("Point");
        yield break;
    }
}
