using System.Collections;
using UnityEngine;

public class TableIntroController : MonoBehaviour
{
    public AudioClip tableIntroAudio;
    
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
        yield break;
    }
}
