using TMPro;
using UnityEngine;

public class EquipmentPanelUI : MonoBehaviour
{
    public EquipmentSlotUI[] slots;
    public TMP_Text health,mana,damage,armor,food;
    Player player;
    public void Bind(Player value){player=value;foreach(var slot in slots)if(slot!=null)slot.Bind(player);Refresh();}
    public void Refresh(){foreach(var slot in slots)if(slot!=null)slot.Refresh();Update();}
    void Update(){
        if(player==null || player.Stats==null)return;
        var s=player.Stats;
        health.text=$"HEALTH  {Mathf.CeilToInt(s.CurrentHealth)} / {s.MaxHealth:0}";
        mana.text=$"MANA  {Mathf.CeilToInt(s.CurrentMana)} / {s.MaxMana:0}";
        damage.text=$"DAMAGE  {s.GetFinal(StatType.AttackDamage):0.#}";
        armor.text=$"ARMOR  {s.GetFinal(StatType.Defense):0.#}";
        if(food!=null)food.text=s.FoodRemaining>0?$"NOURISHED   +{s.FoodHealingPerSecond:0.#} HP/s\n{s.FoodRemaining:0}s remaining":"";
    }
}
