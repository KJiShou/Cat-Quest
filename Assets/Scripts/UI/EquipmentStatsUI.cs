using TMPro;
using UnityEngine;

// Both character cards display the same authoritative equipped statistics.
public sealed class EquipmentStatsUI : MonoBehaviour
{
    public TMP_Text totalPower;
    public TMP_Text[] values;
    public TMP_Text[] bonuses;
    TaskService service;
    void OnEnable(){service=GameDataManager.EnsureInstance().Tasks;service.Changed+=Refresh;Refresh();}
    void OnDisable(){if(service!=null)service.Changed-=Refresh;}
    public void Refresh()
    {
        var stats=service.EquipmentStats;var final=stats.Final;
        totalPower.text=$"{final.Power:N0}";
        double[] numbers={final.hp,final.atk,final.def,final.aspd};
        double[] added={stats.SetHp,stats.GearAtk+stats.SetAtk,stats.GearDef+stats.SetDef,stats.GearAspd+stats.SetAspd};
        for(int i=0;i<4;i++){
            values[i].text=i==3?$"{numbers[i]:0.00}/s":$"{numbers[i]:0.##}";
            string bonus=i==3?$"+{added[i]:0.00}/s gear":$"+{added[i]:0.##} gear";
            if(bonuses!=null && i<bonuses.Length && bonuses[i])
                bonuses[i].text=added[i]>0?bonus:"";
        }
    }
}
