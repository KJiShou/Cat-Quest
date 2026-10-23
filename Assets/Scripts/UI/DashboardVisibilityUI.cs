using UnityEngine;
using UnityEngine.UI;

// Visibility affects presentation only. Hidden Focus/reward cards do not stop domain services.
public sealed class DashboardVisibilityUI : MonoBehaviour
{
    public GameObject tasks, dailyRewards, focus, ranking, character, topRow, bottomRow, emptyState;
    TaskService service;
    int applied = -1;
    VerticalLayoutGroup layout;
    LayoutElement bottomSizing;
    bool originalExpand;
    float originalFlex;

    void Awake()
    {
        layout = GetComponent<VerticalLayoutGroup>();
        bottomSizing = bottomRow.GetComponent<LayoutElement>();
        originalExpand = layout.childForceExpandHeight;
        originalFlex = bottomSizing.flexibleHeight;
    }

    void OnEnable()
    {
        service = GameDataManager.EnsureInstance().Tasks;
        service.Changed += Refresh;
        applied = -1;
        Refresh();
    }
    void OnDisable()
    {
        if (service != null) service.Changed -= Refresh;
        layout.childForceExpandHeight = originalExpand;
        bottomSizing.flexibleHeight = originalFlex;
    }
    void Refresh()
    {
        var saved = service.Settings;
        int mask = (saved.hideDashboardTasks ? 1 : 0) | (saved.hideDashboardDailyRewards ? 2 : 0)
            | (saved.hideDashboardFocus ? 4 : 0) | (saved.hideDashboardRanking ? 8 : 0) | (saved.hideDashboardCharacter ? 16 : 0);
        if (mask == applied) return;
        applied = mask;
        tasks.SetActive(!saved.hideDashboardTasks);
        dailyRewards.SetActive(!saved.hideDashboardDailyRewards);
        focus.SetActive(!saved.hideDashboardFocus);
        ranking.SetActive(!saved.hideDashboardRanking);
        character.SetActive(!saved.hideDashboardCharacter);
        bool hasTop = !saved.hideDashboardTasks || !saved.hideDashboardDailyRewards || !saved.hideDashboardCharacter;
        topRow.SetActive(hasTop);
        bottomRow.SetActive(!saved.hideDashboardFocus || !saved.hideDashboardRanking);
        emptyState.SetActive(mask == 31);
        // The quick-action row keeps its authored height when no top cards remain.
        layout.childForceExpandHeight = hasTop && originalExpand;
        bottomSizing.flexibleHeight = hasTop ? originalFlex : 0;
        LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
    }
}
