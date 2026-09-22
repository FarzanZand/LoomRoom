using UnityEngine;
using DG.Tweening;

public class DungeonContainer : MonoBehaviour, IDamageable, IInteractable
{
    public bool chest;
    public DungeonLootTable loot;
    public int seed;
    [Min(1)] public int floorNumber=1;
    public ItemData guaranteedItem;
    public Material debrisMaterial;
    public AudioClip openAudio;
    [Range(0,1)] public float openVolume = 1f;
    public Transform lid;
    public Vector3 lidOpenRotation = new Vector3(-105,0,0);
    [Min(.05f)] public float lidOpenSeconds = .45f;
    bool opened;
    float health = 10;
    public string Prompt => chest ? "Open supply chest" : "Break supply barrel";
    public bool CanInteract(Character who) => !opened && who is Player;
    public void Interact(Character who) { if (chest && CanInteract(who)) Open(); }
    public void TakeDamage(DamageInfo info)
    {
        if (opened || info.Amount <= 0) return;
        health -= info.Amount;
        if (health <= 0) Open();
    }
    void Open()
    {
        if (opened) return;
        opened = true;
        if(AudioManager.HasInstance && openAudio!=null) AudioManager.Instance.PlaySFX(openAudio,transform.position,openVolume,.06f);
        NoiseEvents.Report(transform.position,7);
        var rng = new System.Random(seed);
        if(!chest || lid==null)foreach(var collider in GetComponentsInChildren<Collider>())collider.enabled=false;
        var dropPosition=transform.position+(chest && lid!=null ? transform.forward*.9f : Vector3.zero);
        if(guaranteedItem!=null)DungeonPickup.Spawn(guaranteedItem,dropPosition,transform.parent);
        if(loot!=null)DungeonPickup.SpawnDrops(loot.RollDrops(rng,floorNumber,chest ? DungeonLootSource.Chest:DungeonLootSource.Barrel),dropPosition,transform.parent);
        if(chest && lid!=null){lid.DOLocalRotate(lidOpenRotation,lidOpenSeconds).SetEase(Ease.OutCubic).SetLink(gameObject);return;}
        for (int i = 0; i < 6; i++)
        {
            var bit = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bit.name = "Broken wood"; bit.transform.SetParent(transform.parent, true);
            bit.transform.position = transform.position + Vector3.up * .5f;
            bit.transform.localScale = new Vector3(.12f, .3f, .09f);
            bit.GetComponent<Renderer>().sharedMaterial = debrisMaterial;
            var rb = bit.AddComponent<Rigidbody>();
            rb.AddForce(new Vector3((float)rng.NextDouble()-.5f, .8f, (float)rng.NextDouble()-.5f)*2, ForceMode.Impulse);
            Destroy(bit, 3);
        }
        Destroy(gameObject);
    }
}
