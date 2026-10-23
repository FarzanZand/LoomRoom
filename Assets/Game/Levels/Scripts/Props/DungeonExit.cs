using UnityEngine;

public class DungeonExit : MonoBehaviour, IInteractable
{
    public bool entrance;
    TableLevelLoader Loader => TableManager.Loader;
    public string Prompt => !entrance && Sealed ? "Blocked" : entrance ? "No way back" : IntroController.PracticeRunning ? "Climb out" : Loader!=null && Loader.HasNextFloor ? "Descend to floor "+(Loader.FloorNumber+1) : "Complete dungeon and return";
    // A living boss seals the way down (see DungeonBossEncounter).
    public bool Sealed { get => isSealed; set { isSealed = value; if (grate != null) grate.SetActive(value); } }
    bool isSealed;
    [HideInInspector] public GameObject grate;
    // What stands over a hidden exit until it is shown (the boss stage's block over its shaft): gone when the exit shows.
    [HideInInspector] public GameObject cover;
    // Half the width of what opens under the cover: a player or loot standing there is moved out in front of the mouth
    // (local -z), so nobody drops into the hole or is caught in the kerb as it appears.
    [HideInInspector] public float coverHalf;
    void OnEnable()
    {
        if (cover == null || !cover.activeSelf) return;
        cover.SetActive(false);
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if (player != null && Over(player.transform.position)) player.Warp(Out(1) + Vector3.up * .1f, player.transform.rotation);
        StartCoroutine(ClearLoot());
    }
    Vector3 Out(int i) => transform.position - transform.forward * (coverHalf + 1f) + transform.right * ((i % 3 - 1) * .6f) - transform.forward * (i / 3 * .5f);
    bool Over(Vector3 p) { var l = transform.InverseTransformPoint(p); return Mathf.Abs(l.x) <= coverHalf + .4f && Mathf.Abs(l.z) <= coverHalf + .4f; }
    // Loot keeps landing for a moment after the kill (it is thrown out in arcs), so the hole is swept for a few seconds.
    System.Collections.IEnumerator ClearLoot()
    {
        int n = 0;
        for (float t = 0f; t < 4f; t += .25f)
        {
            foreach (var item in FindObjectsByType<WorldItem>()) Move(item.transform);
            foreach (var coin in FindObjectsByType<CoinPickup>()) Move(coin.transform);
            yield return new WaitForSeconds(.25f);
        }
        void Move(Transform t)
        {
            if (!Over(t.position)) return;
            var to = Out(n++); to.y = transform.position.y + .1f;
            if (t.TryGetComponent<Rigidbody>(out var body) && !body.isKinematic) body.linearVelocity = Vector3.zero;
            t.position = to;
        }
    }
    public bool CanInteract(Character who) => who is Player p && p.kind==PlayerKind.Table && p.IsAlive && Loader!=null && !Loader.Busy;
    public void Interact(Character who)
    {
        if (!CanInteract(who))return;
        if(!entrance && Sealed){MessageLog.Post("The stairs are blocked until the boss is dead.",MessageKind.Warning);NotificationUI.Show("Blocked");return;}
        if(!entrance && Loader.HasNextFloor)Loader.Descend();
        else if(!entrance)Loader.CompleteRun();
        else NotificationUI.Show("No way back");
    }
    void OnTriggerEnter(Collider other)
    {
        if(entrance || Sealed || Loader==null || !Loader.HasNextFloor)return;
        var player=other.GetComponentInParent<Player>();
        if(player!=null && player.IsActive)Interact(player);
    }
}
