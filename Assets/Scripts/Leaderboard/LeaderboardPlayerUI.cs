using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Read-only view of the exact published snapshot displayed by a ranking row.
public sealed class LeaderboardPlayerUI : MonoBehaviour, IPointerClickHandler
{
    public RectTransform card;
    public Button close;
    public TMP_Text playerName, subtitle, publication;
    public TMP_Text[] metrics, equipmentNames, equipmentDetails;
    public Image[] equipmentIcons, equipmentFrames;
    public Sprite[] slotIcons;
    public LootArt lootArt;
    public CharacterHomePreview characterPreview;
    public TMP_Text[] equipmentLevels;
    public Button[] informationControls;
    public Image rankIcon;
    public Sprite[] metricSprites;
    public EquipmentItemDetailUI equipmentHover;
    public Sprite[] statIcons;
    AccountAggregate publishedPlayer;
    LeaderboardEquipmentHover hoverSource;
    GameObject previousSelection;
    void Awake() { close.onClick.AddListener(Close); }
    void OnDestroy() { if (close) close.onClick.RemoveListener(Close); }
    public void Show(AccountAggregate player, long rank, LeaderboardKind kind, GameObject origin)
    {
        HideEquipment(); publishedPlayer=player;
        previousSelection = origin;
        playerName.text = player.playerName;
        subtitle.text = $"Lv. {player.catLevel:N0}  ·  #{rank:N0}";
        rankIcon.sprite=metricSprites[(int)kind];rankIcon.color=kind==LeaderboardKind.Power?new Color32(20,45,77,255):Color.white;
        for (int i = 0; i < metrics.Length; i++) metrics[i].text = LeaderboardQuery.Display(player, (LeaderboardKind)i);
        publication.text = player.HasEquipment ? "" : "Equipment not shared yet";
        for (int i = 0; i < 3; i++)
        {
            var slot = (EquipmentSlot)i; var definition = EquipmentRules.Find(player.EquipmentId(slot));
            int level = player.EquipmentLevel(slot);
            informationControls[i+5].GetComponent<ControlHint>().enabled=definition==null;
            equipmentLevels[i].text = definition == null ? (player.HasEquipment ? "—" : "?") : "Lv. " + level.ToString("N0");
            equipmentNames[i].text = !player.HasEquipment ? "Not shared yet" : definition == null ? "No equipment" : definition.Name;
            equipmentDetails[i].text = definition == null ? EquipmentRules.SlotName(slot) :
                $"{EquipmentRules.SlotName(slot)} · {(definition.IsLoot ? LootRules.Rarity(definition.Tier) : EquipmentRules.BossName(definition.Tier) + " Set")}\nLevel {level:N0} · {EquipmentRules.Effect(definition, level)}" +
                (EquipmentRules.Ability(definition) == EquipmentAbility.None ? "" : "\n" + EquipmentRules.AbilityDescription(definition));
            equipmentIcons[i].sprite = definition != null && definition.IsLoot && lootArt ? lootArt.Icon(definition.Id) : slotIcons[i];
            equipmentIcons[i].color = definition == null ? new Color(.65f,.7f,.78f) : Color.white;
            equipmentFrames[i].color = definition == null ? new Color(.91f,.94f,.98f) : LootShopUI.Tint(definition.Tier);
        }
        CharacterEquipmentAura.SelectEquipment(player.weaponId, player.bodyId, player.headId, out int auraTier, out int auraPieces);
        characterPreview.SetRemoteEquipment(auraTier, auraPieces);
        gameObject.SetActive(true); transform.SetAsLastSibling(); close.Select();
    }
    public void Close() { gameObject.SetActive(false); }
    public void ShowEquipment(LeaderboardEquipmentHover source,Vector2 position)
    {
        if(!isActiveAndEnabled||publishedPlayer==null||!publishedPlayer.HasEquipment)return;
        var definition=EquipmentRules.Find(publishedPlayer.EquipmentId(source.slot));
        if(definition==null){HideEquipment();return;}
        hoverSource=source;
        var view=equipmentHover;
        int level=publishedPlayer.EquipmentLevel(source.slot),index=(int)source.slot;
        view.SetHoverPreview(true,position);
        view.itemName.text=definition.Name;
        view.tier.text=definition.IsLoot?LootRules.Rarity(definition.Tier):EquipmentRules.BossName(definition.Tier).ToUpperInvariant()+" SET";
        view.level.text=level.ToString();view.itemIcon.sprite=equipmentIcons[index].sprite;
        view.currentLabel.text=index==0?"Attack":index==1?"Defence":"Speed";
        view.currentValue.text=EquipmentRules.Effect(definition,level);
        view.nextValue.text=EquipmentRules.Effect(definition,level+1);
        view.currentIcon.sprite=view.nextIcon.sprite=statIcons[index+1];
        int pieces=0;
        for(int i=0;i<3;i++){
            var worn=EquipmentRules.Find(publishedPlayer.EquipmentId((EquipmentSlot)i));
            if(worn!=null&&!worn.IsLoot&&worn.Tier==definition.Tier)pieces++;
        }
        view.ShowSet(definition.Tier,pieces);view.ShowAbility(definition);
        view.setInfo.text=definition.IsLoot?"":EquipmentRules.BossName(definition.Tier)+" set";
        if(definition.IsLoot)view.setProgress.text="";
        foreach(var bonus in view.setBonusCards)bonus.gameObject.SetActive(!definition.IsLoot);
        foreach(var pip in view.setProgressPips)pip.gameObject.SetActive(!definition.IsLoot);
        view.close.gameObject.SetActive(false);view.equip.gameObject.SetActive(false);
        view.upgrade.gameObject.SetActive(false);view.upgradeCaption.SetActive(false);view.status.text="";
        view.gameObject.SetActive(true);view.transform.SetAsLastSibling();
    }
    public void MoveEquipment(LeaderboardEquipmentHover source,Vector2 position)
    {if(hoverSource==source)equipmentHover.MoveHoverPreview(position);}
    public void HideEquipment(LeaderboardEquipmentHover source=null)
    {
        if(source&&source!=hoverSource)return;
        hoverSource=null;if(equipmentHover)equipmentHover.gameObject.SetActive(false);
    }
    void OnApplicationFocus(bool focused){if(!focused)HideEquipment();}
    void OnDisable()
    {
        HideEquipment();publishedPlayer=null;
        if (EventSystem.current && previousSelection && previousSelection.activeInHierarchy)
            EventSystem.current.SetSelectedGameObject(previousSelection);
        previousSelection = null;
    }
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || e.dragging) return;
        if (!RectTransformUtility.RectangleContainsScreenPoint(card, e.pressPosition, e.pressEventCamera) &&
            !RectTransformUtility.RectangleContainsScreenPoint(card, e.position, e.pressEventCamera)) Close();
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
        else if (Input.GetKeyDown(KeyCode.Tab) && EventSystem.current)
        {
            var selected = EventSystem.current.currentSelectedGameObject;
            int index = Array.FindIndex(informationControls, b => b.gameObject == selected);
            int direction = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
            informationControls[(index + direction + informationControls.Length) % informationControls.Length].Select();
        }
    }
    void LateUpdate()
    {
        var bounds = ((RectTransform)transform).rect;
        card.localScale = Vector3.one * Mathf.Max(0, Mathf.Min(1, (bounds.width - 32) / card.rect.width, (bounds.height - 32) / card.rect.height));
    }
}
