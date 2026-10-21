using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Both character cards display the same authoritative equipped statistics.
public sealed class EquipmentStatsUI : MonoBehaviour
{
    public TMP_Text totalPower;
    public TMP_Text[] values;
    public Image[] fills;
    TaskService service;
    void OnEnable(){service=GameDataManager.EnsureInstance().Tasks;service.Changed+=Refresh;Refresh();}
    void OnDisable(){if(service!=null)service.Changed-=Refresh;}
    public void Refresh()
    {
        var stats=service.EquipmentStats;var final=stats.Final;
        totalPower.text=$"{final.Power:N0}";
        double[] numbers={final.hp,final.atk,final.def,final.aspd};
        double[] scales={stats.Base.hp*1.2,stats.Base.atk+12,stats.Base.def+12,2};
        for(int i=0;i<4;i++){
            values[i].text=i==3?$"{numbers[i]:0.00}/s":$"{numbers[i]:0.##}";
            fills[i].rectTransform.anchorMax=new Vector2(Mathf.Clamp01((float)(numbers[i]/scales[i])),1);
        }
    }
}
