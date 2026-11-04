using TMPro;
using UnityEngine;

public class EquipmentPanelUI : MonoBehaviour
{
    public EquipmentSlotUI[] slots;
    public TMP_Text health,mana,stamina,damage,armor,food;
    [Tooltip("Optional: name, class and level (the panel's header).")] public TMP_Text identity;
    [Tooltip("Optional: a thin line under the level, filled with the progress to the next level (stretched in its frame, like the skill rows).")] public UnityEngine.UI.Image levelProgress;
    [Tooltip("Optional: Strength, Dexterity, Constitution and Intelligence, one per line.")] public TMP_Text attributes;
    [Tooltip("The skills with their ranks; hovering one shows what it does per point and now.")] public AdventureSkillRowUI[] skills;
    [Tooltip("Optional: the line of gear bonuses (critical chance, kill heal...) under armor; hovering it lists them all.")] public GearBonusesUI gearBonuses;
    [Tooltip("Strength, Dexterity, Constitution, Intelligence: the sheet's scores plus this run's points, with a + while a level's point is left.")] public AttributeCellUI[] attributeCells;
    [Tooltip("The attributes' header; it says how many points are left to put in.")] public TMP_Text attributesTitle;
    Player player;
    const string ValueColumn="<pos=34%>";
    static string Line(string label,string value)=>$"<color={ItemData.DimColor}>{label}</color>{ValueColumn}{value}";
    // A status bar colour lifted for text on the dark panel.
    // The bar's colour made bright enough to read on the dark panel, keeping its hue (blending toward white turned the
    // dark stamina green into tan).
    static string Status(Color c){Color.RGBToHSV(c,out float h,out float s,out float v);return "#"+ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(h,s*.8f,Mathf.Max(v,.85f)));}
    // The held weapon's spread makes damage a range (5-9).
    string DamageText(CharacterStats s){var held=player.Equipment!=null?player.Equipment.Get(EquipmentSlot.RightHand):null;int spread=held!=null && held.IsWeapon?held.damageSpread:0;float dmg=s.GetFinal(StatType.AttackDamage);return spread>0?$"{Mathf.Max(1f,dmg-spread):0}-{dmg+spread:0}":$"{dmg:0.#}";}

    // A stat line's hover tooltip (StatTooltipUI): the player's own numbers, one a line, the name dim and the number in
    // colour. No sentences on how things work.
    public bool Explain(StatTooltipUI.Kind kind,out string title,out string subtitle,out string body)
    {
        title=subtitle=body="";
        if(player==null || player.Stats==null)return false;
        var s=player.Stats;
        switch(kind)
        {
            case StatTooltipUI.Kind.Health:
            {
                title="Health";subtitle=$"{Mathf.CeilToInt(s.CurrentHealth)} / {s.MaxHealth:0}";
                var b=new System.Text.StringBuilder();
                float killHeal=s.GetFinal(StatType.KillHeal);
                if(killHeal>0)Row(b,"Per kill",$"+{killHeal:0.#}");
                if(s.FoodRemaining>0)Row(b,$"Food, {s.FoodRemaining:0}s",$"+{s.FoodHealingPerSecond:0.#}/s");
                body=b.ToString();
                return true;
            }
            case StatTooltipUI.Kind.Stamina:
            {
                title="Stamina";subtitle=$"{Mathf.CeilToInt(s.CurrentStamina)} / {s.MaxStamina:0}";
                var b=new System.Text.StringBuilder();
                Row(b,"Regeneration",$"+{s.GetFinal(StatType.StaminaRegen):0.#}/s");
                if(CombatManager.HasInstance)
                {
                    var cm=CombatManager.Instance;
                    if(cm.dash!=null && cm.dash.enabled && (!ProgressionManager.HasInstance || ProgressionManager.Instance.Unlocked(cm.dash.ability,player.GetComponent<AdventurerProgress>())))Row(b,"Dash",$"-{cm.dash.staminaCost:0.#}",ItemData.BadColor);
                    Row(b,"Blocked hit",$"-{cm.blockStaminaCost:0.#}",ItemData.BadColor);
                }
                body=b.ToString();
                return true;
            }
            case StatTooltipUI.Kind.Mana:
            {
                title="Mana";subtitle=$"{Mathf.CeilToInt(s.CurrentMana)} / {s.MaxMana:0}";
                var b=new System.Text.StringBuilder();
                Row(b,"Regeneration",$"+{(s.HasStat(StatType.ManaRegen)?s.GetFinal(StatType.ManaRegen):0f):0.#}/s");
                body=b.ToString();
                return true;
            }
            case StatTooltipUI.Kind.Damage:
            {
                float crit=Mathf.Min(CombatManager.HasInstance?CombatManager.Instance.critChanceCap:.6f,(CombatManager.HasInstance?CombatManager.Instance.critChance:.08f)+s.GetFinal(StatType.CriticalChance));
                title="Damage";subtitle=DamageText(s);
                var b=new System.Text.StringBuilder();
                Row(b,"Critical chance",$"{crit*100f:0.#}%");
                float backstab=s.GetFinal(StatType.BackstabDamage),heavy=s.GetFinal(StatType.HeavyDamage);
                if(backstab>0)Row(b,"Backstab damage",$"+{backstab*100f:0.#}%");
                if(heavy>0)Row(b,"Heavy attack damage",$"+{heavy*100f:0.#}%");
                body=b.ToString();
                return true;
            }
            case StatTooltipUI.Kind.Strength: return ExplainAttribute(MetaAttribute.Strength,out title,out subtitle,out body);
            case StatTooltipUI.Kind.Dexterity: return ExplainAttribute(MetaAttribute.Dexterity,out title,out subtitle,out body);
            case StatTooltipUI.Kind.Constitution: return ExplainAttribute(MetaAttribute.Constitution,out title,out subtitle,out body);
            case StatTooltipUI.Kind.Intelligence: return ExplainAttribute(MetaAttribute.Intelligence,out title,out subtitle,out body);
            case StatTooltipUI.Kind.Armor:
            {
                float per=CombatManager.ArmorPerPoint;
                float armorNow=s.ArmorWithoutShield,block=s.BlockArmor;
                title="Armour";subtitle=block>0?$"{armorNow:0.#}  (+{block:0.#} blocking)":$"{armorNow:0.#}";
                var b=new System.Text.StringBuilder();
                Row(b,"Per hit",$"-{armorNow*per:0.#}");
                if(block>0)Row(b,"Blocked hit",$"-{(armorNow+block)*per:0.#}");
                body=b.ToString();
                return true;
            }
        }
        return false;
    }

    // One tooltip line: the name dim on the left, the number in colour in a column.
    static void Row(System.Text.StringBuilder b,string label,string number,string colour=ItemData.GoodColor)
    {
        if(b.Length>0)b.Append('\n');
        b.Append($"<color={ItemData.DimColor}>{label}</color><pos=62%><color={colour}>{number}</color>");
    }

    // An attribute: each of its gains on a line, what one point gives and what the score gives now (from the track,
    // as the sheet and the level-up cards say it).
    bool ExplainAttribute(MetaAttribute a,out string title,out string subtitle,out string body)
    {
        title=subtitle=body="";
        var meta=ProgressionManager.HasInstance?ProgressionManager.Instance:null;var t=meta!=null?meta.Track(a):null;
        if(t==null)return false;
        var progress=player.GetComponent<AdventurerProgress>();
        int score=progress!=null?progress.Score(a):meta.AttributeScore(a),above=score-t.baseScore;
        int fromRun=progress!=null && (int)a<progress.AttributePoints.Length?progress.AttributePoints[(int)a]:0;
        title=t.displayName;subtitle=fromRun>0?$"{score}  (+{fromRun} this run)":score.ToString();
        string dim=ItemData.DimColor;
        var b=new System.Text.StringBuilder($"<color={dim}><pos=62%>Point<pos=84%>Now</color>");
        void Gain(float per,string unit)
        {
            if(per==0 || string.IsNullOrEmpty(unit))return;
            unit=unit.Trim();bool percent=unit.StartsWith("%"),perSecond=unit.EndsWith("/s");
            string name=unit.TrimStart('%').Trim();
            if(perSecond)name=name.Substring(0,name.Length-2)+" regeneration";
            if(name.Length==0)return;
            string suffix=percent?"%":perSecond?"/s":"";
            b.Append('\n').Append($"<color={dim}>{char.ToUpperInvariant(name[0])}{name.Substring(1)}</color>")
             .Append($"<pos=62%>{Amount(per,suffix)}<pos=84%>{Amount(per*above,suffix)}");
        }
        Gain(t.bonusPerPoint,t.unit);Gain(t.secondBonusPerPoint,t.secondUnit);Gain(t.thirdBonusPerPoint,t.thirdUnit);
        body=b.ToString();
        return true;
    }

    // A gain in colour: green above nothing, red below, dim at nothing.
    static string Amount(float v,string suffix)
    {
        if(Mathf.Abs(v)<.001f)return $"<color={ItemData.DimColor}>0</color>";
        string n=Mathf.Approximately(v,Mathf.Round(v))?Mathf.Abs(Mathf.RoundToInt(v)).ToString():Mathf.Abs(v).ToString("0.##");
        return v>0?$"<color={ItemData.GoodColor}>+{n}{suffix}</color>":$"<color={ItemData.BadColor}>-{n}{suffix}</color>";
    }

    public void Bind(Player value){player=value;foreach(var slot in slots)if(slot!=null)slot.Bind(player);Refresh();}
    static readonly (StatType stat,string label)[] Attributes={(StatType.Strength,"STRENGTH"),(StatType.Dexterity,"DEXTERITY"),(StatType.Constitution,"CONSTITUTION"),(StatType.Intelligence,"INTELLIGENCE")};
    public void Refresh(){foreach(var slot in slots)if(slot!=null)slot.Refresh();shownKey=null;Update();}
    // Every number the labels show, rounded as shown; the strings are rebuilt only when one changes.
    long[] shownKey;
    readonly long[] key=new long[18+AdventureSkills.Count*2+2+AdventurerProgress.AttributeCount+1];
    bool Same(){if(shownKey==null)return false;for(int i=0;i<key.Length;i++)if(shownKey[i]!=key[i])return false;return true;}
    static long Tenths(float v)=>(long)System.Math.Round(v*10,System.MidpointRounding.AwayFromZero);
    void Update(){
        if(player==null || player.Stats==null)return;
        var s=player.Stats;
        key[0]=Mathf.CeilToInt(s.CurrentHealth);key[1]=Tenths(s.MaxHealth);key[2]=Mathf.CeilToInt(s.CurrentStamina);key[3]=Tenths(s.MaxStamina);
        key[4]=Mathf.CeilToInt(s.CurrentMana);key[5]=Tenths(s.MaxMana);key[6]=Tenths(s.GetFinal(StatType.AttackDamage));
        key[7]=Tenths(s.ArmorWithoutShield);key[8]=Tenths(s.BlockArmor);key[9]=Tenths(s.FoodRemaining);key[10]=Tenths(s.FoodHealingPerSecond);
        for(int i=0;i<Attributes.Length;i++)key[11+i]=Tenths(s.GetFinal(Attributes[i].stat));
        var progress=player.GetComponent<AdventurerProgress>();key[17]=progress!=null?progress.Level*1000+(long)(progress.NextLevelXp>0?progress.Experience/progress.NextLevelXp*200:0):0;
        for(int i=0;i<AdventureSkills.Count;i++){key[18+i]=progress!=null?progress.Ranks[i]:0;key[18+AdventureSkills.Count+i]=progress!=null?(long)(progress.SkillProgress((AdventureSkill)i)*200):0;}
        // Gear: anything equipped changes this (the bonuses line and the damage range follow it).
        long gearHash=0;if(player.Equipment!=null)foreach(EquipmentSlot slot in System.Enum.GetValues(typeof(EquipmentSlot))){var it=player.Equipment.Get(slot);gearHash=gearHash*31+(it!=null?it.GetHashCode():0);}
        int at=18+AdventureSkills.Count*2;
        for(int i=0;i<AdventurerProgress.AttributeCount;i++)key[at+i]=progress!=null?progress.Score((MetaAttribute)i)*100+progress.AttributePoints[i]:0;
        key[at+AdventurerProgress.AttributeCount]=progress!=null?progress.UnspentPoints*2+(progress.InRun?1:0):0;
        key[key.Length-2]=gearHash;key[key.Length-1]=Tenths(s.GetFinal(StatType.CriticalChance)*100+s.GetFinal(StatType.KillHeal)+s.GetFinal(StatType.LightRadius));
        if(Same())return;
        shownKey??=new long[key.Length];System.Array.Copy(key,shownKey,key.Length);
        // Label dim on the left, the number in a column in its bar's colour (hover a line for what it does, StatTooltipUI).
        if(health!=null)health.text=Line("HEALTH",$"<color={Status(UIManager.HasInstance?UIManager.Instance.health:Color.red)}>{Mathf.CeilToInt(s.CurrentHealth)}</color><color={ItemData.DimColor}> / {s.MaxHealth:0}</color>");
        if(stamina!=null)stamina.text=Line("STAMINA",$"<color={Status(UIManager.HasInstance?UIManager.Instance.stamina:Color.green)}>{Mathf.CeilToInt(s.CurrentStamina)}</color><color={ItemData.DimColor}> / {s.MaxStamina:0}</color>");
        if(mana!=null)mana.text=Line("MANA",$"<color={Status(UIManager.HasInstance?UIManager.Instance.mana:Color.blue)}>{Mathf.CeilToInt(s.CurrentMana)}</color><color={ItemData.DimColor}> / {s.MaxMana:0}</color>");
        if(damage!=null)damage.text=Line("DAMAGE",$"<color={ItemData.TextColor}>{DamageText(s)}</color>");
        // Shield armor counts only on a block: shown apart so the main number is what always applies.
        if(armor!=null){float block=s.BlockArmor;armor.text=Line("ARMOUR",$"<color={ItemData.TextColor}>{s.ArmorWithoutShield:0.#}</color>"+(block>0?$"<color={ItemData.DimColor}>   +{block:0.#} blocking</color>":""));}
        if(identity!=null){var p=player.GetComponent<AdventurerProgress>();string cls=p!=null && p.selectedClass!=null?p.selectedClass.displayName:"Adventurer";identity.text=$"{cls.ToUpperInvariant()}\n<color={ItemData.DimColor}>LEVEL {(p!=null?p.Level:1)}</color>";}
        if(attributes!=null){var sb=new System.Text.StringBuilder();foreach(var (stat,label) in Attributes)sb.Append(label).Append("<pos=62%>").Append(s.GetFinal(stat).ToString("0")).Append('\n');attributes.text=sb.ToString();}
        if(gearBonuses!=null)gearBonuses.Refresh(player.Equipment);
        if(skills!=null && progress!=null)foreach(var row in skills)if(row!=null)row.Refresh(progress,default);
        if(levelProgress!=null && progress!=null){UIBar.Set(levelProgress,progress.NextLevelXp>0?progress.Experience/progress.NextLevelXp:0);levelProgress.color=UIManager.HasInstance?Color.Lerp(UIManager.Instance.experience,Color.white,.35f):levelProgress.color;}
        if(attributeCells!=null)foreach(var cell in attributeCells)if(cell!=null)cell.Refresh(progress);
        if(attributesTitle!=null){int left=progress!=null && progress.InRun?progress.UnspentPoints:0;bool cards=ProgressionManager.CardsOnLevelUp;attributesTitle.text="ATTRIBUTES"+(left>0?$"<pos=55%><size=80%><color=#F2C75C>{left} {(cards?"LEVEL UP":"POINT")}{(left>1?"S":"")} {(cards?"WAITING":"TO SPEND")}</color></size>":"");}
        if(food!=null)food.text=s.FoodRemaining>0?$"NOURISHED   +{s.FoodHealingPerSecond:0.#} HP/s\n{s.FoodRemaining:0}s remaining":"";
    }
}
