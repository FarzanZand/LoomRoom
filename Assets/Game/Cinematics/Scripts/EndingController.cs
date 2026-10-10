using System;
using System.Collections;
using UnityEngine;

// Winning a run. Back in the room under the black: daylight, the Dungeon Master's chair empty, the
// bedroom door open onto the apartment. Walking out fades to the end card; then the room is put back
// as it was and the player wakes in bed again. Started by the run recap through WorldManager.ending.
public class EndingController : MonoBehaviour
{
    [Tooltip("Hidden for the ending morning.")]
    [SerializeField] GameObject dungeonMaster;
    [Tooltip("Lighting for the ending morning.")]
    [SerializeField] SceneMood mood;
    [Tooltip("Walking this close to it (across the floor) ends the game.")]
    [SerializeField] Transform exit;
    [SerializeField, Min(1)] float exitRadius = 18f;
    [Tooltip("\"The end\", on the screen fade's canvas so it shows over the black.")]
    [SerializeField] CanvasGroup endCard;
    [SerializeField, Min(0)] float fadeSeconds = 2.5f, cardSeconds = 5f;

    public const string WinsFlag = "story.wins";

    public bool Running { get; private set; }
    // After the end card, as the player wakes into the usual night again.
    public event Action Finished;

    LightingManager lighting;
    LightingManager.Snapshot night;
    bool leaving;
    bool ownsCutscene; // pushed GameState.Cutscene in Leave and not popped yet

    void Awake()
    {
        if (endCard != null) { endCard.alpha = 0f; endCard.gameObject.SetActive(false); }
    }

    // Cut short (scene change, disabled mid-ending): never leave the Cutscene state pushed.
    void OnDisable()
    {
        if (!ownsCutscene) return;
        ownsCutscene = false;
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Cutscene);
    }

    // From the victory recap.
    public void Play()
    {
        var loader = TableManager.HasInstance ? TableManager.Instance.GetComponent<TableLevelLoader>() : null;
        if (loader != null) StartCoroutine(TableManager.ReturnToRoom(loader, Begin, context: this));
        else Begin();
    }

    void Begin()
    {
        Running = true; leaving = false;
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.AddToFlag(WinsFlag, 1);
        if (dungeonMaster != null) dungeonMaster.SetActive(false);
        if (lighting == null) lighting = LightingManager.Instance;
        if (lighting != null) { night = lighting.Capture(); if (mood != null) lighting.BlendToMood(mood, 0f); }
        if (WorldManager.HasInstance)
        {
            WorldManager.Instance.SetRoomExit(RoomExitMode.LockOneDoor);
            if (WorldManager.Instance.apartmentDoor != null) WorldManager.Instance.apartmentDoor.SetOpen(true);
            if (WorldManager.Instance.wakeUpCutscene != null) WorldManager.Instance.wakeUpCutscene.Play(false);
        }
    }

    void Update()
    {
        if (!Running || leaving || exit == null || !PlayerManager.HasInstance || PlayerManager.Instance.ActiveKind != PlayerKind.Room) return;
        if (!GameManager.HasInstance || GameManager.Instance.State != GameState.Explore) return;
        var p = PlayerManager.Instance.Active.transform.position;
        bool atExit = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(exit.position.x, exit.position.z)) < exitRadius;
        // Fallen out of the world somehow: end it anyway rather than leave the player falling.
        bool lost = p.y < exit.position.y - 50f;
        if (atExit || lost) StartCoroutine(Leave());
    }

    IEnumerator Leave()
    {
        leaving = true;
        if (GameManager.HasInstance) { GameManager.Instance.Push(GameState.Cutscene); ownsCutscene = true; }
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeToBlack(fadeSeconds);
        if (AudioManager.HasInstance) AudioManager.Instance.StopMusic(fadeSeconds);
        yield return new WaitForSecondsRealtime(fadeSeconds + .5f);

        if (endCard != null)
        {
            endCard.gameObject.SetActive(true);
            endCard.alpha = 0f;
            yield return ScreenManager.FadeGroup(endCard, 1f, 1f);
            yield return new WaitForSecondsRealtime(cardSeconds);
            yield return ScreenManager.FadeGroup(endCard, 0f, 1f);
            endCard.gameObject.SetActive(false);
        }

        if (WorldManager.HasInstance)
        {
            if (WorldManager.Instance.apartmentDoor != null) WorldManager.Instance.apartmentDoor.SetOpen(false);
            WorldManager.Instance.SetRoomExit(RoomExitMode.LoopRooms);
        }
        if (lighting != null) lighting.Restore(night, 0f);
        if (dungeonMaster != null) dungeonMaster.SetActive(true);
        Running = false;
        yield return new WaitForSecondsRealtime(1f);
        if (ownsCutscene)
        {
            ownsCutscene = false;
            if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Cutscene);
        }
        if (WorldManager.HasInstance && WorldManager.Instance.wakeUpCutscene != null) WorldManager.Instance.wakeUpCutscene.Play(false);
        Finished?.Invoke();
    }
}
