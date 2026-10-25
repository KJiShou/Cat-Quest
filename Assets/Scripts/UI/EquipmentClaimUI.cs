using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Authored claim selector; the equipment service remains the reward authority.
public sealed class EquipmentClaimUI : MonoBehaviour
{
    public Slider progress;
    public TMP_Text progressValue, status;
    public Image[] chests, itemIcons;
    public Sprite closedChest, openChest;
    public TMP_Text[] milestoneLabels, captions, effects;
    public GameObject[] ownedMarks;
    public Button[] buttons;
    public ChestRewardParticles[] particles;
    public int Boss { get; private set; }

    void Awake(){for(int i=0;i<particles.Length;i++)particles[i].Bind(chests[i].rectTransform);}

    public void Show(PlayerData data,int boss,bool unlocked)
    {
        Boss=boss;
        var mastery=data.bossProgress.Single(x=>x.bossId==boss);
        progress.SetValueWithoutNotify(Mathf.Min(mastery.wins,15));
        progressValue.text=$"{Mathf.Min(mastery.wins,15)} / 15 wins";
        status.text=!unlocked?"Claim a Daily Reward to unlock equipment.":mastery.pendingClaims>0?$"Click a chest to collect · {mastery.pendingClaims} available":mastery.claimedComponents>=3?"Set collected!":"Win Boss battles to unlock a reward.";
        for(int i=0;i<3;i++){
            bool reached=mastery.wins>=(i+1)*5;
            var def=EquipmentRules.Catalog.Single(x=>x.Tier==boss&&(int)x.Slot==i);
            bool claimed=data.equipment.Any(x=>x.id==def.Id);
            chests[i].sprite=reached?openChest:closedChest;
            chests[i].enabled=!claimed;
            chests[i].color=reached?Color.white:new Color(1,1,1,.5f);
            milestoneLabels[i].text=$"{(i+1)*5} wins";
            particles[i].gameObject.SetActive(unlocked&&reached&&!claimed&&mastery.pendingClaims>0);
            bool owned=claimed;
            captions[i].text=EquipmentRules.SlotName(def.Slot);
            effects[i].text=EquipmentRules.Effect(def,1);
            ownedMarks[i].SetActive(owned);buttons[i].interactable=unlocked&&reached&&!owned&&mastery.pendingClaims>0;
            var tint=boss==1?new Color32(144,218,255,255):boss==2?new Color32(216,182,255,255):new Color32(255,224,134,255);
            buttons[i].GetComponent<Image>().color=tint;
            var icon=itemIcons[i].rectTransform;icon.anchorMin=owned?new Vector2(.18f,.20f):new Vector2(.59f,.05f);icon.anchorMax=owned?new Vector2(.82f,.87f):new Vector2(.96f,.42f);icon.offsetMin=icon.offsetMax=Vector2.zero;
        }
    }
}
