using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Displays committed level rewards. Closing or replaying visuals never changes player data.
public sealed class LevelUpCelebrationUI : MonoBehaviour
{
    public GameObject panel;
    public RectTransform design, badge, rays;
    public RectTransform footer, footerSource;
    public CanvasGroup fade;
    public TMP_Text level, info, fish, tickets;
    public GameObject ticketCard;
    public Button continueButton;
    public RewardFlightUI rewardFlight;
    readonly Queue<TaskService.LevelUpReward> pending = new Queue<TaskService.LevelUpReward>();
    TaskService service;
    GameObject previousSelection;
    float elapsed, queuedAt;
    Vector3 badgeScale;
    Quaternion rayRotation;
    bool showing;
    public bool IsShowing => showing;
    public int PendingCount => pending.Count;

    void OnEnable()
    {
        badgeScale=badge.localScale; rayRotation=rays.localRotation;
        service=GameDataManager.EnsureInstance().Tasks;
        service.LevelUp+=Enqueue;
        continueButton.onClick.AddListener(Dismiss);
        panel.SetActive(false); showing=false;
    }
    void OnDisable()
    {
        if(service!=null) service.LevelUp-=Enqueue;
        continueButton.onClick.RemoveListener(Dismiss);
        pending.Clear(); CloseVisual();
    }
    void Enqueue(TaskService.LevelUpReward reward)
    {
        if(pending.Count==0) queuedAt=Time.unscaledTime;
        pending.Enqueue(reward);
    }
    void Update()
    {
        var rect=(RectTransform)transform;
        float scale=Mathf.Min(rect.rect.width/2560f,rect.rect.height/1440f);
        design.localScale=Vector3.one*scale;
        // Keep the reference's lower background aligned to the fitted content,
        // while extending it to the viewport edges independently of that content.
        if(footer && footerSource) {
            var panelRect=(RectTransform)panel.transform;
            float top=panelRect.InverseTransformPoint(footerSource.TransformPoint(new Vector3(0,footerSource.rect.yMax))).y;
            footer.sizeDelta=new Vector2(0,Mathf.Max(0,top-panelRect.rect.yMin));
        }
        if(!showing) {
            if(pending.Count==0 || (rewardFlight && rewardFlight.IsAnimating && Time.unscaledTime-queuedAt<4)) return;
            var reward=pending.Dequeue();
            level.text=reward.toLevel.ToString();
            info.text=reward.toLevel==reward.fromLevel+1 ? "You reached a new level!" : $"Level {reward.fromLevel} → {reward.toLevel}";
            fish.text="+"+reward.fishCoins; tickets.text="+"+reward.tickets;
            ticketCard.SetActive(reward.tickets>0);
            previousSelection=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
            showing=true; elapsed=0; fade.alpha=0;
            badge.localScale=badgeScale*.82f;
            panel.SetActive(true); continueButton.interactable=false;
        }
        elapsed+=Time.unscaledDeltaTime;
        bool reduced=UiMotion.Reduced;
        fade.alpha=reduced?1:Mathf.Clamp01(elapsed/.22f);
        float t=Mathf.Clamp01(elapsed/.55f)-1;
        float pop=1+2.70158f*t*t*t+1.70158f*t*t;
        badge.localScale=reduced?badgeScale:badgeScale*Mathf.LerpUnclamped(.82f,1,pop);
        rays.localRotation=reduced?rayRotation:rayRotation*Quaternion.Euler(0,0,elapsed*9);
        if((reduced||elapsed>=.55f) && !continueButton.interactable) {
            continueButton.interactable=true;
            if(EventSystem.current) EventSystem.current.SetSelectedGameObject(continueButton.gameObject);
        }
        if(continueButton.interactable && Input.GetKeyDown(KeyCode.Escape)) Dismiss();
        // Keep keyboard navigation inside the modal, including after background page updates.
        if(showing && continueButton.interactable && EventSystem.current) {
            var selected=EventSystem.current.currentSelectedGameObject;
            if(!selected || !selected.transform.IsChildOf(panel.transform))
                EventSystem.current.SetSelectedGameObject(continueButton.gameObject);
        }
    }
    public void Dismiss()
    {
        if(!showing || !continueButton.interactable) return;
        CloseVisual();
    }
    void CloseVisual()
    {
        showing=false;
        if(panel) panel.SetActive(false);
        if(badge) badge.localScale=badgeScale;
        if(rays) rays.localRotation=rayRotation;
        if(fade) fade.alpha=1;
        if(EventSystem.current && previousSelection && previousSelection.activeInHierarchy)
            EventSystem.current.SetSelectedGameObject(previousSelection);
        previousSelection=null;
    }
    void OnApplicationFocus(bool focused)
    {
        if(!focused && showing) {
            elapsed=Mathf.Max(elapsed,.55f); badge.localScale=badgeScale;
        }
    }
}
