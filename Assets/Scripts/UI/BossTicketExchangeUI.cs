using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(TaskDialogKeyboard))]
public sealed class BossTicketExchangeUI : MonoBehaviour
{
    [SerializeField] RectTransform card;
    [SerializeField] TMP_Text unitPrice, balance, status, price, quantityText;
    [SerializeField] Button buy, cancel, decrease, increase, maximum;

    TaskDialogKeyboard keyboard;
    SqlitePlayerService service;
    Button opener;
    Action<int> closed;
    string previousExchangeId, requestId;
    int quantity;
    bool saving, purchased;

    void Awake()
    {
        keyboard = GetComponent<TaskDialogKeyboard>();
        buy.onClick.AddListener(Buy);
        cancel.onClick.AddListener(Dismiss);
        unitPrice.text = SqlitePlayerService.BossTicketFishCost.ToString();
        decrease.onClick.AddListener(() => SetQuantity(quantity - 1));
        increase.onClick.AddListener(() => SetQuantity(quantity + 1));
        maximum.onClick.AddListener(() => SetQuantity(service.MaxBossTicketExchange));
    }

    public bool Open(SqlitePlayerService player, Button source, Action<int> onClosed = null)
    {
        if (gameObject.activeSelf || saving || !player.IsReady) return false;

        service = player;
        opener = source;
        closed = onClosed;
        previousExchangeId = service.LastTicketExchangeId;
        requestId = Guid.NewGuid().ToString("N");
        quantity = 1;
        gameObject.SetActive(true);
        RefreshControls(restoreSelection: false);
        return true;
    }

    void OnRectTransformDimensionsChange()
    {
        if (!card) return;
        Vector2 available = ((RectTransform)transform).rect.size - new Vector2(32, 32);
        Vector2 size = card.rect.size;
        card.localScale = Vector3.one * Mathf.Clamp01(Mathf.Min(available.x / size.x, available.y / size.y));
    }

    void OnEnable()
    {
        OnRectTransformDimensionsChange();
        if (service != null) service.Changed += BalanceChanged;
    }

    void BalanceChanged()
    {
        if (!saving) RefreshControls();
    }

    void SetQuantity(int value)
    {
        if (saving) return;
        value = Mathf.Clamp(value, 1, Mathf.Max(1, service.MaxBossTicketExchange));
        if (value == quantity) return;
        quantity = value;
        requestId = Guid.NewGuid().ToString("N");
        RefreshControls();
    }

    void RefreshControls(string message = null, bool restoreSelection = true)
    {
        string error = service.BossTicketExchangeError(quantity);
        balance.text = service.FishCoins.ToString();
        status.text = message ?? error ?? "";
        quantityText.text = quantity.ToString();
        price.text = ((long)quantity * SqlitePlayerService.BossTicketFishCost).ToString();
        decrease.interactable = !saving && quantity > 1;
        increase.interactable = maximum.interactable = !saving && quantity < service.MaxBossTicketExchange;
        buy.interactable = !saving && service.IsReady && error == null;
        cancel.interactable = !saving;
        if (saving) return;

        var events = EventSystem.current;
        var selected = restoreSelection && events ? events.currentSelectedGameObject : null;
        var controls = new[] { cancel, decrease, increase, maximum, buy }.Where(button => button.interactable).ToArray();
        keyboard.Open(controls, Dismiss);
        if (selected && controls.Any(button => button.gameObject == selected)) events.SetSelectedGameObject(selected);
        else if (selected == increase.gameObject || selected == maximum.gameObject) decrease.Select();
        else if (selected == decrease.gameObject && increase.IsInteractable()) increase.Select();
    }

    void Dismiss()
    {
        if (!saving) gameObject.SetActive(false);
    }

    async void Buy()
    {
        if (saving || !isActiveAndEnabled || !buy.interactable) return;
        saving = true;
        RefreshControls();
        var result = await service.ExchangeBossTicketAsync(quantity, previousExchangeId, requestId);
        if (!this) return;

        saving = false;
        if (!isActiveAndEnabled) return;
        if (result.Succeeded)
        {
            purchased = true;
            gameObject.SetActive(false);
            return;
        }

        RefreshControls(result.Message);
    }

    void OnDisable()
    {
        var callback = closed;
        var source = opener;
        if (service != null) service.Changed -= BalanceChanged;
        closed = null;
        opener = null;
        callback?.Invoke(purchased ? quantity : 0);
        purchased = false;
        if (source && source.isActiveAndEnabled && source.IsInteractable()) source.Select();
    }
}
