using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed class DashboardEquipmentUI : MonoBehaviour
{
    public MainMenuNavigation navigation;
    public Button[] slots;
    public Image[] icons;
    public Texture2D[] gradeSheets;
    public Sprite[] emptySlotIcons;
    Sprite[] equippedIcons;
    Color[] emptyColors;
    TaskService service;
    void Awake(){
        equippedIcons=icons.Select(i=>i.sprite).ToArray();
        emptyColors=slots.Select(b=>b.GetComponent<Image>().color).ToArray();
        service=GameDataManager.EnsureInstance().Tasks;for(int i=0;i<slots.Length;i++){int index=i;slots[i].onClick.AddListener(()=>{if(navigation.ShowPage(MainMenuPage.CatHome))navigation.pages[4].GetComponent<EquipmentPageUI>().OpenSlot((EquipmentSlot)index);});}}
    void OnEnable(){if(service!=null){service.Changed+=Refresh;Refresh();}}
    void OnDisable(){if(service!=null)service.Changed-=Refresh;}
    void Refresh(){
        var d=service.Snapshot;
        for(int i=0;i<slots.Length;i++){
            string id=EquipmentRules.Equipped(d,(EquipmentSlot)i);bool owned=d.equipment.Any(x=>x.id==id);
            slots[i].interactable=service.EquipmentUnlocked;icons[i].sprite=owned?equippedIcons[i]:emptySlotIcons[i];icons[i].color=owned?Color.white:new Color32(161,172,198,255);
            int tier=owned?EquipmentRules.Find(id).Tier:0;
            Color grade=tier==1?new Color32(67,184,255,255):tier==2?new Color32(182,112,245,255):new Color32(255,201,65,255);
            slots[i].GetComponent<Image>().color=owned?Color.Lerp(grade,Color.white,.75f):emptyColors[i];

        }
    }
}
