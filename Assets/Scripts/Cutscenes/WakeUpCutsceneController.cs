using System.Collections;
using UnityEngine;

public class WakeUpCutsceneController : MonoBehaviour
{
    [SerializeField] private Transform roomPlayerRoot;
    [SerializeField] private Transform wakeUpPosition;
    [SerializeField] private Transform bedSidePosition;
    [SerializeField] private float wakeDuration = 4f;

    private MFPC.PlayerController playerController;
    private CharacterController characterController;

    void Start()
    {
        playerController = MFPC.PlayerController.instance;
        characterController = playerController.GetComponent<CharacterController>();

        playerController.enabled = false;
        characterController.enabled = false;

        var startRot = Quaternion.Euler(80f, wakeUpPosition.eulerAngles.y, 0f);
        roomPlayerRoot.SetPositionAndRotation(wakeUpPosition.position, startRot);

        StartCoroutine(WakeUpRoutine());
    }

    IEnumerator WakeUpRoutine()
    {
        float elapsed = 0f;
        var startRot = roomPlayerRoot.rotation;
        var endRot = Quaternion.Euler(0f, wakeUpPosition.eulerAngles.y, 0f);

        while (elapsed < wakeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / wakeDuration);
            roomPlayerRoot.rotation = Quaternion.Slerp(startRot, endRot, t);
            yield return null;
        }

        roomPlayerRoot.rotation = endRot;

        characterController.enabled = false;
        roomPlayerRoot.SetPositionAndRotation(bedSidePosition.position, bedSidePosition.rotation);
        characterController.enabled = true;
        playerController.enabled = true;
    }
}
