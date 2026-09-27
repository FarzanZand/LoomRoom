using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

// Pull to open linked gates, show or hide objects, or fire a designer hook. Wire targets
// inside a room template (a hidden vault behind a gate, a bridge that appears).
public class DungeonLever : MonoBehaviour, IInteractable
{
    [Tooltip("Rotated when pulled.")]
    public Transform handle;
    public Vector3 pulledRotation = new Vector3(0, 0, -60);
    [Min(.05f)] public float pullSeconds = .35f;
    [Tooltip("Can only be pulled once.")]
    public bool oneShot = true;
    public DungeonDoor[] opensDoors = new DungeonDoor[0];
    [Tooltip("Toggled active/inactive on each pull.")]
    public GameObject[] toggles = new GameObject[0];
    public AudioClip pullClip;
    [Range(0, 1)] public float pullVolume = .9f;
    [TextArea] public string message = "Somewhere, stone grinds against stone.";
    [Tooltip("Designer hook. Receives the lever's GameObject.")]
    public UnityEvent<GameObject> onPulled;

    bool pulled;
    Quaternion restRotation;

    void Awake()
    {
        if (handle != null) restRotation = handle.localRotation;
    }

    public string Prompt => pulled && oneShot ? "The lever is stuck" : "Pull the lever";
    public bool CanInteract(Character who) => who is Player && !(pulled && oneShot);

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        pulled = !pulled || oneShot;
        if (handle != null)
            handle.DOLocalRotateQuaternion(pulled ? restRotation * Quaternion.Euler(pulledRotation) : restRotation, pullSeconds)
                .SetEase(Ease.OutBack).SetLink(gameObject);
        if (AudioManager.HasInstance && pullClip != null) AudioManager.Instance.PlaySFX(pullClip, transform.position, pullVolume, .05f);
        foreach (var door in opensDoors) if (door != null && !door.IsOpen) door.Open();
        foreach (var go in toggles) if (go != null) go.SetActive(!go.activeSelf);
        if (!string.IsNullOrWhiteSpace(message)) MessageLog.Post(message.Trim(), MessageKind.Info);
        onPulled?.Invoke(gameObject);
    }
}
