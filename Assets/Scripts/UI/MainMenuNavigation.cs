using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public enum MainMenuPage { Dashboard, Tasks, Focus, Boss, CatHome, Leaderboard }

// Left navigation switches page containers while keeping the shared header and sidebar alive.
public sealed class MainMenuNavigation : MonoBehaviour
{
    public Button[] buttons;
    public GameObject[] pages;
    public MainMenuPage initialPage=MainMenuPage.Tasks;
    public TaskSystemUI taskSystem;
    public TMP_Text fishCoins, tickets;
    public MainMenuPage CurrentPage { get; private set; }
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
        ShowPage(initialPage); RefreshHeader();
    }
    public bool ShowPage(MainMenuPage page)
    {
        int index=(int)page;
        if(index<0 || index>=pages.Length || !pages[index]) return false;
        if(taskSystem.gameObject.activeInHierarchy && taskSystem.Service!=null && page!=MainMenuPage.Tasks)
            if(!taskSystem.PrepareToLeavePage()) return false;
        service?.RefreshDate();
        CurrentPage=page;
        for(int i=0;i<pages.Length;i++) {
            pages[i].SetActive(i==index);
            ((SidebarNavigationButton)buttons[i]).SetPageSelected(i==index);
        }
        if(taskSystem.Service!=null && page==MainMenuPage.Tasks) taskSystem.Refresh();
        RefreshHeader(); return true;
    }
    private void Update()
    {
        // Daily state still changes at midnight while a different page is visible.
        if(service!=null && Time.unscaledTime>=nextClockCheck) { nextClockCheck=Time.unscaledTime+1; service.RefreshDate(); }
    }
    private void RefreshHeader()
    {
        if(service==null) return;
        var data=service.Snapshot; fishCoins.text=data.fishCoins.ToString(); tickets.text=data.tickets.ToString();
    }
    private void OnApplicationFocus(bool focus) { if(focus && service!=null) { service.RefreshDate(); RefreshHeader(); } }
    private void OnDestroy()
    {
        if(service!=null) service.Changed-=RefreshHeader;
        if(listeners!=null) for(int i=0;i<listeners.Length;i++)
            if(buttons[i]) buttons[i].onClick.RemoveListener(listeners[i]);
    }
}
