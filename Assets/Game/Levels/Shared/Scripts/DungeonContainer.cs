using UnityEngine;

public class DungeonContainer : MonoBehaviour, IDamageable, IInteractable
{
    public bool chest;
    public DungeonLootTable loot;
    public int seed;
    public ItemData guaranteedItem;
    public Material debrisMaterial;
    public AudioClip openAudio;
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
        if(AudioManager.HasInstance && openAudio!=null) AudioManager.Instance.PlaySFX(openAudio,transform.position,.65f,.06f);
        NoiseEvents.Report(transform.position,7);
        var rng = new System.Random(seed);
        int count = chest ? 2 : 1;
        for (int i = 0; i < count; i++)
        {
            var item = i == 0 && guaranteedItem != null ? guaranteedItem : loot != null ? loot.Roll(rng) : null;
            DungeonPickup.Spawn(item, transform.position + new Vector3((i-.5f)*.7f, 0, -.65f), transform.parent);
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
