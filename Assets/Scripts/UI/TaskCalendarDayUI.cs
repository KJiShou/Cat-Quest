using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TaskCalendarDayUI : MonoBehaviour
{
    public Button dateButton, moreButton, dailyButton;
    public TMP_Text dateLabel, moreLabel, dailyLabel, dailyCount;
    public Image todayMark, selection;
    public Button[] taskButtons;
    public TMP_Text[] taskLabels;
    public DateTime Date { get; private set; }
    public void Bind(TaskCalendarUI owner, DateTime date, CalendarTaskDay data, int month, int cellHeight)
    {
        Date = date;
        dateButton.onClick.RemoveAllListeners(); dateButton.onClick.AddListener(() => owner.SelectDate(date));
        dateLabel.text = date.Day.ToString();
        bool today = date == owner.Service.LocalNow.Date;
        dateLabel.color = today ? Color.white : date.Month == month ? TaskUIBuilder.Ink : TaskUIBuilder.ScheduleInk;
        todayMark.gameObject.SetActive(today);
        selection.gameObject.SetActive(date == owner.SelectedDate);
        var todos = data.items.FindAll(i => !i.view.task.daily);
        var daily = data.items.FindAll(i => i.view.task.daily);
        bool hasFooter = daily.Count > 0 || data.missed > 0;
        const int headerHeight = 36, rowHeight = 23;
        int availableHeight = cellHeight - headerHeight - (hasFooter ? 25 : 2);
        int slots = Mathf.Clamp(availableHeight / rowHeight, 0, taskButtons.Length);
        int shown = Mathf.Min(slots, todos.Count);
        for (int i = 0; i < taskButtons.Length; i++)
        {
            taskButtons[i].gameObject.SetActive(i < shown);
            if (i >= shown) continue;
            var item = todos[i];
            taskLabels[i].text = (item.view.Completed ? "✓ " : "") + item.view.task.title;
            taskLabels[i].color = item.view.Completed ? TaskUIBuilder.ScheduleInk : owner.Service.IsOverdue(item.view) ? TaskUIBuilder.Overdue : TaskUIBuilder.Ink;
            taskButtons[i].onClick.RemoveAllListeners(); taskButtons[i].onClick.AddListener(() => owner.OpenItem(item));
            owner.BindTaskContext(taskButtons[i].gameObject, item);
        }
        moreButton.gameObject.SetActive(todos.Count > shown);
        moreLabel.text = "+" + (todos.Count - shown) + " more";
        moreButton.onClick.RemoveAllListeners(); moreButton.onClick.AddListener(() => owner.SelectDate(date));
        dailyButton.gameObject.SetActive(hasFooter);
        dailyLabel.color = data.missed > 0 ? new Color32(143, 36, 56, 255)
            : today ? TaskUIBuilder.Ink : new Color32(76, 98, 122, 255);
        var countBadge = (RectTransform)dailyCount.transform.parent;
        bool showCount = hasFooter && date >= owner.Service.LocalNow.Date;
        countBadge.gameObject.SetActive(showCount);
        if (showCount)
        {
            dailyLabel.text = "Daily";
            dailyCount.text = today ? daily.FindAll(i => i.view.Completed).Count + "/" + daily.Count : daily.Count.ToString();
            float width = Mathf.Max(24, dailyCount.GetPreferredValues(dailyCount.text).x + 10);
            countBadge.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            dailyLabel.rectTransform.offsetMax = new Vector2(-width - 8, 0);
        }
        else
        {
            dailyLabel.rectTransform.offsetMax = new Vector2(-8, 0);
            dailyLabel.text = (daily.Count > 0 ? daily.Count + " done" : "") + (data.missed > 0 ? (daily.Count > 0 ? " · " : "") + data.missed + " missed" : "");
        }
        dailyButton.onClick.RemoveAllListeners(); dailyButton.onClick.AddListener(() => owner.SelectDate(date, true));
        // Date header and Daily footer stay fixed; only the number of title previews varies.
        for (int i = 0; i < taskButtons.Length; i++) Place(taskButtons[i].transform as RectTransform, headerHeight + i * rowHeight, rowHeight);
        var moreRect = (RectTransform)moreButton.transform;
        Place(moreRect, 4, rowHeight);
        moreRect.offsetMin = new Vector2(52, moreRect.offsetMin.y);
    }
    static void Place(RectTransform rect, float top, float height)
    {
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1);
        rect.offsetMin = new Vector2(6, -top - height); rect.offsetMax = new Vector2(-6, -top);
    }
}
