using UnityEngine;

// On an event room's own enemy (the Duellist, the Card sharp, the Sleeping giant): it does nothing and notices no one
// from the moment it exists (EnemyBrain.Entranced, no close awareness) until its event takes it over and lets it go
// (DuelEvent's salute, WagerEvent's blow, SleepingGiantEvent's sleep). Without it the enemy would come for a player who
// starts in its room before the event has found it.
[RequireComponent(typeof(EnemyBrain))]
public class EventActor : MonoBehaviour
{
    void Awake() => Hold();
    void OnEnable() => Hold();
    void Start() => Hold();

    bool released;
    public void Release() => released = true;

    void Hold()
    {
        if (released || !TryGetComponent<EnemyBrain>(out var brain)) return;
        brain.Entranced = true;
        if (brain.Perception != null) brain.Perception.NoCloseAwareness = true;
    }
}
