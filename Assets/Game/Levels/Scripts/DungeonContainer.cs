using UnityEngine;
using DG.Tweening;

public class DungeonContainer : MonoBehaviour, IDamageable, IInteractable
{
    public bool chest;
    public LootSource loot;
    public int seed;
    [Min(1)] public int floorNumber=1;
    public ItemData guaranteedItem;
    [Tooltip("Always in it, one of each, thrown out with the rest (a boss chest's Chest Items).")]
    public System.Collections.Generic.List<ItemData> guaranteedItems = new();
    [Min(0), Tooltip("Gold on top of what the loot rolls (a boss chest's Bonus Gold).")]
    public int bonusGold;
    public Material debrisMaterial;
    public AudioClip openAudio;
    // AudioManager SFX Library key every chest opens with (its Open Audio is the fallback).
    public const string StandardOpenKey = "standardChestOpen";
    [Range(0,1)] public float openVolume = 1f;
    public Transform lid;
    [Tooltip("The lid's opening, an editable clip (legacy, curves under the chest's root: the lid swings up with a small bounce). Without one the lid turns to Lid Open Rotation in code.")]
    public AnimationClip lidClip;
    public Vector3 lidOpenRotation = new Vector3(-105,0,0);
    [Min(.05f)] public float lidOpenSeconds = .45f;
    [Header("Tier")]
    [Tooltip("What the prompt calls it: \"Open <name>\".")]
    public string chestName = "supply chest";
    [Tooltip("A short sting over the creak as it opens (wood, iron and gold chests each have their own).")]
    public AudioClip stingAudio;
    [Range(0,1)] public float stingVolume = .7f;
    [Min(0), Tooltip("Extra item rolls (an iron chest: 1).")]
    public int extraRolls;
    [Tooltip("The starting room's chest: exactly one piece of gear rolled from its loot plus its guaranteed items (a potion), no spoils (set by the generator).")]
    public bool startingKit;
    [Min(0), Tooltip("Floors deeper a bonus roll comes from (a gold chest: one item as if from 3 floors down). 0 = none.")]
    public int rareRollFloors;
    bool opened;
    float health = 10;
    public string Prompt => chest ? $"Open {chestName}" : "Break supply barrel";
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

    // A chest or barrel opening (a trapped chest springs its spikes).
    public event System.Action<DungeonContainer> Opened;

    void Open()
    {
        if (opened) return;
        opened = true;
        Opened?.Invoke(this);
        // A chest opens with the SFX Library's StandardOpenKey when it has one (each tier's sting comes after), else its own clip.
        if(AudioManager.HasInstance && (!chest || AudioManager.Instance.PlayFirstSFX(transform.position,StandardOpenKey)==null) && openAudio!=null)
            AudioManager.Instance.PlaySFX(openAudio,transform.position,openVolume,.06f);
        NoiseEvents.Report(transform.position,7);
        var rng = new System.Random(seed);
        if(!chest || lid==null)foreach(var collider in GetComponentsInChildren<Collider>())collider.enabled=false;
        var dropPosition=transform.position+(chest && lid!=null ? transform.forward*.9f : Vector3.zero);
        if(guaranteedItem!=null)DungeonPickup.Spawn(guaranteedItem,dropPosition,transform.parent);
        var source=chest ? DungeonLootSource.Chest:DungeonLootSource.Barrel;
        var (drops,gold)=DungeonLootDrop.Roll(loot,rng,floorNumber,source);
        if(startingKit)
        {
            // One piece of gear, rolled until one comes out; the potion comes with the guaranteed items below.
            var gear=new System.Collections.Generic.List<LootSource.Drop>();
            for(int i=0;gear.Count==0 && loot!=null && i<24;i++)
                foreach(var d in i==0 ? drops : loot.RollDrops(rng,floorNumber,source))
                    if(d.item!=null && d.item.IsGear){gear.Add(new LootSource.Drop(d.item,1));break;}
            drops=gear;
        }
        // A chest is never empty: reroll the items a few times, then fall back to a little gold.
        for(int i=0;chest && drops.Count==0 && guaranteedItem==null && loot!=null && i<4;i++) drops=loot.RollDrops(rng,floorNumber,source);
        // Better chests: more rolls, and one roll from deeper floors (its best item only).
        if(chest && loot!=null && !startingKit)
        {
            for(int i=0;i<extraRolls;i++) drops.AddRange(loot.RollDrops(rng,floorNumber,source));
            if(rareRollFloors>0)
            {
                var rare=loot.RollDrops(rng,floorNumber+rareRollFloors,source);
                for(int i=0;rare.Count==0 && i<4;i++) rare=loot.RollDrops(rng,floorNumber+rareRollFloors,source);
                if(rare.Count>0){ rare.Sort((a,b)=>(b.item!=null?b.item.tier:0).CompareTo(a.item!=null?a.item.tier:0)); drops.Add(rare[0]); }
            }
        }
        if(guaranteedItems!=null)foreach(var item in guaranteedItems)if(item!=null)drops.Add(new LootSource.Drop(item,1));
        // Spoils for the tally (ProgressionManager): chests only, at their own chance.
        if(chest && !startingKit && ProgressionManager.HasInstance){var spoils=ProgressionManager.Instance.RollSpoils(seed,floorNumber,true);if(spoils!=null)drops.Add(new LootSource.Drop(spoils,1));}
        gold+=bonusGold;
        if(chest && drops.Count==0 && guaranteedItem==null && gold<=0) gold=3+rng.Next(0,3)+floorNumber*2;
        // A chest glows gold as it opens and its loot jumps out.
        var mouth=transform.position+Vector3.up*.55f;
        DungeonLootDrop.Spill(drops,gold,dropPosition,transform.parent,chest ? mouth : (Vector3?)null);
        if(chest)Glow(mouth);
        bool chime=InventoryManager.HasInstance && InventoryManager.Instance.chestOpenChime;
        if(chest && chime && stingAudio!=null && AudioManager.HasInstance) DOVirtual.DelayedCall(.12f,()=>AudioManager.Instance.PlaySFX(stingAudio,transform.position,stingVolume,.02f)).SetLink(gameObject);
        if(chest && lid!=null)
        {
            if(lidClip!=null)
            {
                var player=GetComponent<Animation>(); if(player==null)player=gameObject.AddComponent<Animation>();
                player.playAutomatically=false;
                if(player.GetClip(lidClip.name)==null)player.AddClip(lidClip,lidClip.name);
                player.Play(lidClip.name);
            }
            else lid.DOLocalRotate(lidOpenRotation,lidOpenSeconds).SetEase(Ease.OutCubic).SetLink(gameObject);
            return;
        }
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
