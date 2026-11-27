using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(TaskDialogKeyboard))]
public sealed class EquipmentBulkUpgradeUI : MonoBehaviour
{
    [SerializeField] RectTransform card;
    [SerializeField] TMP_Text itemName, fromLevel, toLevel, statName, statChange, quantity, price, status;
    [SerializeField] Image itemIcon, itemFrame, statIcon;
    [SerializeField] Button decrease, increase, cancel, close, confirm;
    [SerializeField] Slider slider;

    TaskDialogKeyboard keyboard;
    SqlitePlayerService service;
    EquipmentDefinition item;
    Action<bool> closed;
    string requestId;
    int originalLevel, targetLevel;
    bool saving, purchased;

    void Awake()
    {
        keyboard = GetComponent<TaskDialogKeyboard>();
        slider.onValueChanged.AddListener(value => SelectLevel(originalLevel + Mathf.RoundToInt(value)));
        decrease.onClick.AddListener(() => SelectLevel(targetLevel - 1));
        increase.onClick.AddListener(() => SelectLevel(targetLevel + 1));
        cancel.onClick.AddListener(Dismiss);
        close.onClick.AddListener(Dismiss);
        confirm.onClick.AddListener(Confirm);
    }

    public bool Open(SqlitePlayerService player, EquipmentDefinition definition, int level, Sprite icon, Sprite attributeIcon, Action<bool> onClosed)
    {
        if (gameObject.activeSelf || saving || player.MaxEquipmentUpgradeLevel(definition.Id, level) <= level) return false;

        service = player;
        item = definition;
        closed = onClosed;
        originalLevel = level;
        targetLevel = level + 1;
        requestId = Guid.NewGuid().ToString("N");
        itemName.text = definition.Name;
        itemIcon.sprite = icon;
        itemFrame.color = LootShopUI.Tint(item.Tier);
        statIcon.sprite = attributeIcon;
        statName.text = item.Slot == EquipmentSlot.Weapon ? "ATK" : item.Slot == EquipmentSlot.Body ? "DEF" : "ASPD";
        gameObject.SetActive(true);
        Refresh();
        return true;
    }

    void OnEnable() => OnRectTransformDimensionsChange();

    void OnRectTransformDimensionsChange()
    {
        if (!card) return;
        Vector2 available = ((RectTransform)transform).rect.size - new Vector2(40, 40);
        card.localScale = Vector3.one * Mathf.Clamp(Mathf.Min(available.x / card.rect.width, available.y / (card.rect.height + 120)), 0, .72f);
    }

    void SelectLevel(int value)
    {
        if (saving || !isActiveAndEnabled) return;
        value = Mathf.Clamp(value, originalLevel + 1, Mathf.Max(originalLevel + 1, service.MaxEquipmentUpgradeLevel(item.Id, originalLevel)));
        if (value == targetLevel) return;
        targetLevel = value;
        requestId = Guid.NewGuid().ToString("N");
        Refresh();
    }

    void Refresh(string error = null)
    {
        int maximum = service.MaxEquipmentUpgradeLevel(item.Id, originalLevel);
        int maxLevels = Mathf.Max(1, maximum - originalLevel);
        if (slider.value > maxLevels) slider.SetValueWithoutNotify(maxLevels);
        slider.maxValue = maxLevels;
        slider.SetValueWithoutNotify(targetLevel - originalLevel);
        slider.interactable = !saving && maximum > originalLevel + 1;
        decrease.interactable = !saving && targetLevel > originalLevel + 1;
        increase.interactable = !saving && targetLevel < maximum;
        confirm.interactable = !saving && targetLevel <= maximum;
        cancel.interactable = close.interactable = !saving;
        fromLevel.text = originalLevel.ToString();
        toLevel.text = targetLevel.ToString();
        statChange.text = $"{EquipmentRules.Bonus(item, originalLevel):0.##} → {EquipmentRules.Bonus(item, targetLevel):0.##}";
        quantity.text = (targetLevel - originalLevel).ToString();
        price.text = EquipmentRules.UpgradeCost(originalLevel, targetLevel).ToString();
        status.text = error ?? "";
        if (saving) return;

        var events = EventSystem.current;
        var focus = events ? events.currentSelectedGameObject : null;
        Button[] buttons = new[] { cancel, decrease, increase, confirm, close }.Where(button => button.interactable).ToArray();
        keyboard.Open(buttons, Dismiss);
        if (focus && buttons.Any(button => button.gameObject == focus)) events.SetSelectedGameObject(focus);
        else if (focus == increase.gameObject && decrease.interactable) decrease.Select();
        else if (focus == decrease.gameObject && increase.interactable) increase.Select();
    }

    void Dismiss()
    {
        if (!saving) gameObject.SetActive(false);
    }

    async void Confirm()
    {
        if (saving || !isActiveAndEnabled || !confirm.interactable) return;
        saving = true;
        Refresh();
        var result = await service.UpgradeEquipmentAsync(item.Id, originalLevel, targetLevel, requestId);
        if (!this) return;
        saving = false;
        if (!isActiveAndEnabled) return;
        if (result.Succeeded)
        {
            purchased = true;
            gameObject.SetActive(false);
        }
        else Refresh(result.Message);
    }

    void OnDisable()
    {
        var callback = closed;
        bool success = purchased;
        closed = null;
        purchased = false;
        callback?.Invoke(success);
    }
}
