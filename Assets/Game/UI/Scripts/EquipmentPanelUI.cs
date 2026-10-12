using TMPro;
using UnityEngine;

public class EquipmentPanelUI : MonoBehaviour
{
    public EquipmentSlotUI[] slots;
    public TMP_Text health,mana,stamina,damage,armor,food;
    [Tooltip("Optional: name, class and level (Diablo's header).")] public TMP_Text identity;
    [Tooltip("Optional: Strength, Dexterity, Constitution, Intelligence, Perception, Charisma, one per line.")] public TMP_Text attributes;
    [Tooltip("The skills with their ranks; hovering one shows what it does per point and now.")] public AdventureSkillRowUI[] skills;
    Player player;
    public void Bind(Player value){player=value;foreach(var slot in slots)if(slot!=null)slot.Bind(player);Refresh();}
    static readonly (StatType stat,string label)[] Attributes={(StatType.Strength,"STRENGTH"),(StatType.Dexterity,"DEXTERITY"),(StatType.Constitution,"CONSTITUTION"),(StatType.Intelligence,"INTELLIGENCE"),(StatType.Perception,"PERCEPTION"),(StatType.Charisma,"CHARISMA")};
    public void Refresh(){foreach(var slot in slots)if(slot!=null)slot.Refresh();shownKey=null;Update();}
    // Every number the labels show, rounded as shown; the strings are rebuilt only when one changes.
    long[] shownKey;
    readonly long[] key=new long[18+AdventureSkills.Count];
    bool Same(){if(shownKey==null)return false;for(int i=0;i<key.Length;i++)if(shownKey[i]!=key[i])return false;return true;}
    static long Tenths(float v)=>(long)System.Math.Round(v*10,System.MidpointRounding.AwayFromZero);
    void Update(){
        if(player==null || player.Stats==null)return;
        var s=player.Stats;
        key[0]=Mathf.CeilToInt(s.CurrentHealth);key[1]=Tenths(s.MaxHealth);key[2]=Mathf.CeilToInt(s.CurrentStamina);key[3]=Tenths(s.MaxStamina);
        key[4]=Mathf.CeilToInt(s.CurrentMana);key[5]=Tenths(s.MaxMana);key[6]=Tenths(s.GetFinal(StatType.AttackDamage));
        key[7]=Tenths(s.ArmorWithoutShield);key[8]=Tenths(s.BlockArmor);key[9]=Tenths(s.FoodRemaining);key[10]=Tenths(s.FoodHealingPerSecond);
        for(int i=0;i<Attributes.Length;i++)key[11+i]=Tenths(s.GetFinal(Attributes[i].stat));
        var progress=player.GetComponent<AdventurerProgress>();key[17]=progress!=null?progress.Level:0;
        for(int i=0;i<AdventureSkills.Count;i++)key[18+i]=progress!=null?progress.Ranks[i]:0;
        if(Same())return;
        shownKey??=new long[key.Length];System.Array.Copy(key,shownKey,key.Length);
        if(health!=null)health.text=$"HEALTH  {Mathf.CeilToInt(s.CurrentHealth)} / {s.MaxHealth:0}";
        if(stamina!=null)stamina.text=$"STAMINA  {Mathf.CeilToInt(s.CurrentStamina)} / {s.MaxStamina:0}";
        if(mana!=null)mana.text=$"MANA  {Mathf.CeilToInt(s.CurrentMana)} / {s.MaxMana:0}";
        if(damage!=null)damage.text=$"DAMAGE  {s.GetFinal(StatType.AttackDamage):0.#}";
        // Shield armor counts only on a block: shown apart so the main number is what always applies.
        if(armor!=null){float block=s.BlockArmor;armor.text=$"ARMOR  {s.ArmorWithoutShield:0.#}"+(block>0?$"  <color={ItemData.DimColor}>(+{block:0.#} BLOCK)</color>":"");}
        if(identity!=null){var p=player.GetComponent<AdventurerProgress>();string cls=p!=null && p.selectedClass!=null?p.selectedClass.displayName:"Adventurer";identity.text=$"{cls.ToUpperInvariant()}\n<color={ItemData.DimColor}>LEVEL {(p!=null?p.Level:1)}</color>";}
        if(attributes!=null){var sb=new System.Text.StringBuilder();foreach(var (stat,label) in Attributes)sb.Append(label).Append("<pos=62%>").Append(s.GetFinal(stat).ToString("0")).Append('\n');attributes.text=sb.ToString();}
        if(skills!=null && progress!=null)foreach(var row in skills)if(row!=null)row.Refresh(progress,default);
        if(food!=null)food.text=s.FoodRemaining>0?$"NOURISHED   +{s.FoodHealingPerSecond:0.#} HP/s\n{s.FoodRemaining:0}s remaining":"";
    }
}
