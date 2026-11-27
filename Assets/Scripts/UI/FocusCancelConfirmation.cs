using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

// Shared, presentation-only prompt. The caller validates the captured session.
public sealed class FocusCancelConfirmation : MonoBehaviour
{
    public Button confirm, keepFocusing, backdrop;
    public RectTransform card;
    public TMP_Text heading, info;
    public GameObject rewardPreview;
    public TMP_Text previewExperience, previewFish, previewTickets, previewGoal;
    public readonly struct RewardPreview
    {
        public readonly RewardAmounts rewards;
        public readonly int goalMinutes;
        public RewardPreview(RewardAmounts rewards, int goalMinutes)
        { this.rewards = rewards; this.goalMinutes = goalMinutes; }
    }
    Action accepted, closed;
    bool dismissOnConfirm;
    TMP_Text confirmLabel, keepLabel;
    Func<RewardPreview> estimate;
    float nextEstimateAt;
    GameObject previousSelection;
    Vector2 lastSize;
    public bool IsOpen => gameObject.activeSelf;
    void Awake()
    {
        confirmLabel = confirm.GetComponentInChildren<TMP_Text>(true);
        keepLabel = keepFocusing.GetComponentInChildren<TMP_Text>(true);
        confirm.onClick.AddListener(Confirm);
        keepFocusing.onClick.AddListener(Dismiss);
        backdrop.onClick.AddListener(Dismiss);
    }
    public void Show(bool cancelSession, Action action, Func<RewardPreview> rewardEstimate = null)
    {
        if(IsOpen)return;
        heading.text = cancelSession ? "Cancel Pomodoro?" : "End Focus?";
        estimate = !cancelSession && rewardPreview ? rewardEstimate : null;
        if (rewardPreview) rewardPreview.SetActive(estimate != null);
        info.gameObject.SetActive(estimate == null);
        info.text = cancelSession ? "No rewards will be granted." : "Save your focus time and earned rewards.";
        RefreshEstimate();
        nextEstimateAt = Time.unscaledTime + 1;
        confirm.GetComponentInChildren<TMP_Text>(true).text = cancelSession ? "Cancel" : "End Focus";
        Open(action, true);
    }
    public void ShowExit(Action action, Action onClosed)
    {
        heading.text = "Exit Cat Quest?";
        if (rewardPreview) rewardPreview.SetActive(false);
        info.gameObject.SetActive(true);
        // Keep the consequence legible against the light panel.
        info.color = keepFocusing.GetComponentInChildren<TMP_Text>(true).color;
        closed = onClosed;
        Open(action, false);
    }
    public void SetExitMessage(string message, string stayText, string actionText, bool busy)
    {
        info.text = message;
        keepLabel.text = stayText;
        confirmLabel.text = actionText;
        confirm.interactable = !busy;
    }
    void Open(Action action, bool dismissAfterConfirm)
    {
        accepted=action;
        dismissOnConfirm=dismissAfterConfirm;
        previousSelection=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
        gameObject.SetActive(true);transform.SetAsLastSibling();
        if(EventSystem.current)EventSystem.current.SetSelectedGameObject(keepFocusing.gameObject);
    }
    public void Confirm()
    {
        if (!IsOpen || !confirm.interactable) return;
        var action = accepted;
        if (dismissOnConfirm) Dismiss();
        action?.Invoke();
    }
    public void Dismiss() => gameObject.SetActive(false);
    void OnDisable()
    {
        accepted = null;
        estimate = null;
        if (EventSystem.current)
        {
            var selected = EventSystem.current.currentSelectedGameObject;
            if (selected == confirm.gameObject || selected == keepFocusing.gameObject)
                EventSystem.current.SetSelectedGameObject(previousSelection && previousSelection.activeInHierarchy ? previousSelection : null);
        }
        previousSelection = null;
        var onClosed = closed;
        closed = null;
        onClosed?.Invoke();
    }
    void RefreshEstimate()
    {
        if (estimate == null) return;
        var value = estimate();
        if (previewExperience) previewExperience.text = value.rewards.experience.ToString();
        if (previewFish) previewFish.text = value.rewards.fishCoins.ToString();
        if (previewTickets) previewTickets.text = value.rewards.tickets.ToString();
        if (previewGoal) previewGoal.text = $"+{value.goalMinutes} min today";
    }
    void LateUpdate()
    {
        var available=((RectTransform)transform).rect.size;
        if(available==lastSize)return;
        lastSize=available;
        float scale=Mathf.Min(1,available.x*.9f/card.rect.width,available.y*.85f/card.rect.height);
        card.localScale=Vector3.one*scale;
        heading.fontSize=Mathf.Max(36,14/Mathf.Max(.1f,scale));info.fontSize=Mathf.Max(28,11/Mathf.Max(.1f,scale));
        confirmLabel.fontSize = keepLabel.fontSize = Mathf.Max(24,11/Mathf.Max(.1f,scale));
    }
    void Update()
    {
        if(estimate != null && Time.unscaledTime >= nextEstimateAt) { RefreshEstimate(); nextEstimateAt = Time.unscaledTime + 1; }
        if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame){Dismiss();return;}
        if(!EventSystem.current)return;
        var selected=EventSystem.current.currentSelectedGameObject;
        if(Keyboard.current!=null&&Keyboard.current.tabKey.wasPressedThisFrame)EventSystem.current.SetSelectedGameObject(selected==keepFocusing.gameObject&&confirm.interactable?confirm.gameObject:keepFocusing.gameObject);
        else if(selected!=keepFocusing.gameObject&&(selected!=confirm.gameObject||!confirm.interactable))EventSystem.current.SetSelectedGameObject(keepFocusing.gameObject);
    }
}
