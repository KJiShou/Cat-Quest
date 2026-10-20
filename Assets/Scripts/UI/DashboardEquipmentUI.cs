using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed class DashboardEquipmentUI : MonoBehaviour
{
    public MainMenuNavigation navigation;
    public Button[] slots;
    public Image[] icons;
    public Texture2D[] gradeSheets;
    RawImage[] effects;
    TaskService service;
    void Awake(){
        effects=new RawImage[slots.Length];
        for(int i=0;i<slots.Length;i++){
            var rect=TaskUIBuilder.Rect(slots[i].transform,"GradeEffect");rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,.5f);
            effects[i]=rect.gameObject.AddComponent<RawImage>();effects[i].raycastTarget=false;effects[i].color=new Color(1,1,1,.8f);rect.gameObject.AddComponent<EquipmentGradeEffect>();rect.SetAsFirstSibling();rect.gameObject.SetActive(false);
        }
        service=GameDataManager.EnsureInstance().Tasks;for(int i=0;i<slots.Length;i++){int index=i;slots[i].onClick.AddListener(()=>{if(navigation.ShowPage(MainMenuPage.CatHome))navigation.pages[4].GetComponent<EquipmentPageUI>().OpenSlot((EquipmentSlot)index);});}}
    void OnEnable(){if(service!=null){service.Changed+=Refresh;Refresh();}}
    void OnDisable(){if(service!=null)service.Changed-=Refresh;}
    void LateUpdate(){if(effects==null)return;for(int i=0;i<effects.Length;i++){var r=(RectTransform)slots[i].transform;float side=Mathf.Min(r.rect.width,r.rect.height)*1.12f;effects[i].rectTransform.sizeDelta=new Vector2(side,side);}}
    void Refresh(){
        var d=service.Snapshot;
        for(int i=0;i<slots.Length;i++){
            string id=EquipmentRules.Equipped(d,(EquipmentSlot)i);bool owned=d.equipment.Any(x=>x.id==id);
            slots[i].interactable=service.EquipmentUnlocked;icons[i].color=owned?Color.white:new Color(1,1,1,.3f);
            int tier=owned?EquipmentRules.Find(id).Tier:0;
            bool show=tier>0&&gradeSheets!=null&&gradeSheets.Length>=tier&&gradeSheets[tier-1];
            effects[i].gameObject.SetActive(show);if(show)effects[i].texture=gradeSheets[tier-1];
        }
    }
}
