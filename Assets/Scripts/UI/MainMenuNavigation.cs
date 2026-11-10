using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public enum MainMenuPage { Dashboard, Tasks, Focus, Boss, CatHome, Leaderboard, Chat }

// Left navigation switches page containers while keeping the shared header and sidebar alive.
public sealed class MainMenuNavigation : MonoBehaviour
{
    public Button[] buttons;
    public GameObject[] pages;
    public MainMenuPage initialPage=MainMenuPage.Tasks;
    public TaskSystemUI taskSystem;
    public TMP_Text fishCoins, tickets;
    public MainMenuPage CurrentPage { get; private set; }
    public event System.Action<MainMenuPage> PageChanged;
    public event System.Action<string> AccessDenied;
    public bool CanOpen(MainMenuPage page)
    {
        var tasks = service ?? GameDataManager.EnsureInstance().Tasks;
        if (page == MainMenuPage.Focus)
            return tasks.CanAccess(ProgressionFeature.Focus) || FocusRunner.EnsureInstance().Controller.Phase is FocusPhase.Focusing or FocusPhase.Saving or FocusPhase.Resting;
        if (page == MainMenuPage.Boss)
            return tasks.CanAccess(ProgressionFeature.Boss) || tasks.HasActiveBattle;
        return page != MainMenuPage.Leaderboard || tasks.CanAccess(ProgressionFeature.Leaderboard);
    }
    private TaskService service;
    private UnityAction[] listeners;
    private float nextClockCheck;
    private void Start()
    {
        service=GameDataManager.EnsureInstance().Tasks;
        service.Changed+=RefreshHeader;
        listeners=new UnityAction[buttons.Length];
        for(int i=0;i<buttons.Length;i++) {
            int index=i; listeners[i]=()=>ShowPage((MainMenuPage)index);
            buttons[i].onClick.AddListener(listeners[i]);
        }
        ShowPage(CanOpen(initialPage) ? initialPage : MainMenuPage.Dashboard); RefreshHeader();
    }
    public bool ShowPage(MainMenuPage page)
    {
        int index=(int)page;
        if(index<0 || index>=pages.Length || !pages[index]) return false;
        if (!CanOpen(page)) { AccessDenied?.Invoke("Unlocks at Lv." + (page == MainMenuPage.Focus ? 2 : 5)); return false; }
        if(taskSystem.gameObject.activeInHierarchy && taskSystem.Service!=null && page!=MainMenuPage.Tasks)
            if(!taskSystem.PrepareToLeavePage(()=>ShowPage(page))) return false;
        if(CurrentPage==MainMenuPage.Boss && page!=MainMenuPage.Boss && pages[(int)MainMenuPage.Boss].activeInHierarchy)
        {
            var boss=pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>();
            if(boss && !boss.PrepareToLeavePage(()=>ShowPage(page))) return false;
        }
        if(CurrentPage==MainMenuPage.Dashboard && page!=MainMenuPage.Dashboard && pages[0].activeInHierarchy)
        {
            var dashboard=pages[0].GetComponentInChildren<DashboardTasksUI>();
            if(dashboard && !dashboard.PrepareToLeavePage()) return false;
        }
        service?.RefreshDate();
        CurrentPage=page;
        for(int i=0;i<pages.Length;i++) {
            pages[i].SetActive(i==index);
            ((SidebarNavigationButton)buttons[i]).SetPageSelected(i==index);
        }
        RefreshHeader(); PageChanged?.Invoke(page); return true;
    }
    private void Update()
    {
        // Daily state still changes at midnight while a different page is visible.
        if(service!=null && Time.unscaledTime>=nextClockCheck) { nextClockCheck=Time.unscaledTime+1; service.RefreshDate(); }
    }
    private void RefreshHeader()
    {
        if(service==null) return;
        fishCoins.text=service.FishCoins.ToString(); tickets.text=service.Tickets.ToString();
    }
    private void OnApplicationFocus(bool focus) { if(focus && service!=null) { service.RefreshDate(); RefreshHeader(); } }
    private void OnDestroy()
    {
        if(service!=null) service.Changed-=RefreshHeader;
        if(listeners!=null) for(int i=0;i<listeners.Length;i++)
            if(buttons[i]) buttons[i].onClick.RemoveListener(listeners[i]);
    }
}
