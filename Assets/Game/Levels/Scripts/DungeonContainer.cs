using UnityEngine;
using DG.Tweening;

public class DungeonContainer : MonoBehaviour, IDamageable, IInteractable
{
    public bool chest;
    public LootSource loot;
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
    [Tooltip("Burst of gold sparks from the chest as it opens (optional).")]
    public GameObject openBurst;
    public Color glowColor = new Color(1f,.78f,.38f);
    [Min(0)] public float glowIntensity = 6f;

    // Light spills out of the open chest, then fades.
    void Glow(Vector3 at)
    {
        if(openBurst!=null)PoolManager.SpawnOrInstantiate(openBurst,at,Quaternion.identity,2f);
        if(glowIntensity<=0)return;
        var go=new GameObject("Chest glow");go.transform.SetParent(transform,false);go.transform.position=at;
        var light=go.AddComponent<Light>();light.type=LightType.Point;light.color=glowColor;light.range=3.5f;light.intensity=0;
        DOTween.Sequence().Append(DOTween.To(()=>light.intensity,v=>light.intensity=v,glowIntensity,.25f))
            .Append(DOTween.To(()=>light.intensity,v=>light.intensity=v,0f,2.2f)).OnComplete(()=>Destroy(go)).SetLink(go);
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
        var source=chest ? DungeonLootSource.Chest:DungeonLootSource.Barrel;
        var (drops,gold)=DungeonLootDrop.Roll(loot,rng,floorNumber,source);
        // A chest is never empty: reroll the items a few times, then fall back to a little gold.
        for(int i=0;chest && drops.Count==0 && guaranteedItem==null && loot!=null && i<4;i++) drops=loot.RollDrops(rng,floorNumber,source);
        if(chest && drops.Count==0 && guaranteedItem==null && gold<=0) gold=3+rng.Next(0,3)+floorNumber*2;
        // A chest glows gold as it opens and its loot jumps out (Heathen).
        var mouth=transform.position+Vector3.up*.55f;
        DungeonLootDrop.Spill(drops,gold,dropPosition,transform.parent,chest ? mouth : (Vector3?)null);
        if(chest)Glow(mouth);
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
