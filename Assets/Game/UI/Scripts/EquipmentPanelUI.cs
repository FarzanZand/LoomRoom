using TMPro;
using UnityEngine;

public class EquipmentPanelUI : MonoBehaviour
{
    public EquipmentSlotUI[] slots;
    public TMP_Text health,mana,stamina,damage,armor,food;
    Player player;
    public void Bind(Player value){player=value;foreach(var slot in slots)if(slot!=null)slot.Bind(player);Refresh();}
    public void Refresh(){foreach(var slot in slots)if(slot!=null)slot.Refresh();Update();}
    void Update(){
        if(player==null || player.Stats==null)return;
        var s=player.Stats;
        if(health!=null)health.text=$"HEALTH  {Mathf.CeilToInt(s.CurrentHealth)} / {s.MaxHealth:0}";
        if(stamina!=null)stamina.text=$"STAMINA  {Mathf.CeilToInt(s.CurrentStamina)} / {s.MaxStamina:0}";
        if(mana!=null)mana.text=$"MANA  {Mathf.CeilToInt(s.CurrentMana)} / {s.MaxMana:0}";
        if(damage!=null)damage.text=$"DAMAGE  {s.GetFinal(StatType.AttackDamage):0.#}";
        // Shield armor counts only on a block: shown apart so the main number is what always applies.
        if(armor!=null){float block=s.BlockArmor;armor.text=$"ARMOR  {s.ArmorWithoutShield:0.#}"+(block>0?$"  <color={ItemData.DimColor}>(+{block:0.#} BLOCK)</color>":"");}
        if(food!=null)food.text=s.FoodRemaining>0?$"NOURISHED   +{s.FoodHealingPerSecond:0.#} HP/s\n{s.FoodRemaining:0}s remaining":"";
    }
}
