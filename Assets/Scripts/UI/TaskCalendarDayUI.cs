using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TaskCalendarDayUI : MonoBehaviour
{
    public Button dateButton, moreButton, dailyButton;
    public TMP_Text dateLabel, moreLabel, dailyLabel;
    public Image todayMark, selection;
    public Button[] taskButtons;
    public TMP_Text[] taskLabels;
    public DateTime Date { get; private set; }
    public void Bind(TaskCalendarUI owner, DateTime date, CalendarTaskDay data, int month, int slots)
    {
        Date = date;
        dateButton.onClick.RemoveAllListeners(); dateButton.onClick.AddListener(() => owner.SelectDate(date));
        dateLabel.text = date.Day.ToString();
        bool today = date == owner.Service.LocalNow.Date;
        dateLabel.color = today ? Color.white : date.Month == month ? TaskUIBuilder.Ink : TaskUIBuilder.ScheduleInk;
        todayMark.gameObject.SetActive(today);
        selection.gameObject.SetActive(date == owner.SelectedDate);
        var todos = data.items.FindAll(i => !i.view.task.daily);
        int shown = Mathf.Min(slots, todos.Count);
        for (int i = 0; i < taskButtons.Length; i++)
        {
            taskButtons[i].gameObject.SetActive(i < shown);
            if (i >= shown) continue;
            var item = todos[i];
            taskLabels[i].text = (item.view.Completed ? "✓ " : "") + item.view.task.title;
            taskLabels[i].color = item.view.Completed ? TaskUIBuilder.ScheduleInk : owner.Service.IsOverdue(item.view) ? TaskUIBuilder.Overdue : TaskUIBuilder.Ink;
            taskButtons[i].onClick.RemoveAllListeners(); taskButtons[i].onClick.AddListener(() => owner.OpenItem(item));
        }
        moreButton.gameObject.SetActive(todos.Count > shown);
        moreLabel.text = "+" + (todos.Count - shown) + " more";
        moreButton.onClick.RemoveAllListeners(); moreButton.onClick.AddListener(() => owner.SelectDate(date));
        var daily = data.items.FindAll(i => i.view.task.daily);
        dailyButton.gameObject.SetActive(daily.Count > 0 || data.missed > 0);
        dailyLabel.text = date > owner.Service.LocalNow.Date ? "Daily · " + daily.Count
            : date == owner.Service.LocalNow.Date ? "Daily · " + daily.FindAll(i => i.view.Completed).Count + "/" + daily.Count
            : "Daily · " + daily.Count + (data.missed > 0 ? " · " + data.missed + " missed" : "");
        dailyButton.onClick.RemoveAllListeners(); dailyButton.onClick.AddListener(() => owner.SelectDate(date, true));
        // Date header and Daily footer stay fixed; only the number of title previews varies.
        for (int i = 0; i < taskButtons.Length; i++) Place(taskButtons[i].transform as RectTransform, 36 + i * 23, 23);
        Place(moreButton.transform as RectTransform, 36 + shown * 23, 23);
    }
    static void Place(RectTransform rect, float top, float height)
    {
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1);
        rect.offsetMin = new Vector2(6, -top - height); rect.offsetMax = new Vector2(-6, -top);
    }
}
