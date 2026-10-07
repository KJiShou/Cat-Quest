using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TaskDatePicker : MonoBehaviour
{
    public Button dateButton;
    public TMP_Text dateLabel;
    public GameObject overlay;
    public RectTransform calendar;
    public TMP_Text monthLabel;
    public Button previousMonth;
    public Button nextMonth;
    public Button dismiss;
    public Button[] days;
    public int year = 2024;
    public int month = 8;
    public int day = 4;

    private DateTime displayedMonth;
    private DateTime firstGridDay;
    private readonly Color ink = new Color32(20, 45, 77, 255);

    public DateTime SelectedDate => new DateTime(year, month, day);

    private void Awake()
    {
        dateButton.onClick.AddListener(Open);
        dismiss.onClick.AddListener(() => overlay.SetActive(false));
        previousMonth.onClick.AddListener(() => ChangeMonth(-1));
        nextMonth.onClick.AddListener(() => ChangeMonth(1));
        for (int i = 0; i < days.Length; i++)
        {
            int index = i;
            days[i].onClick.AddListener(() => SelectDate(firstGridDay.AddDays(index)));
        }
        dateLabel.text = SelectedDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    public void Open()
    {
        displayedMonth = new DateTime(year, month, 1);
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        RefreshCalendar();
        Canvas.ForceUpdateCanvases();
        var canvas = dateButton.GetComponentInParent<Canvas>().rootCanvas;
        var overlayRect = (RectTransform)overlay.transform;
        var corners = new Vector3[4];
        dateButton.GetComponent<RectTransform>().GetWorldCorners(corners);
        var screen = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, corners[0]);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRect, screen, canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var point);
        var bounds = overlayRect.rect;
        float x = Mathf.Clamp(point.x, bounds.xMin + 12, bounds.xMax - calendar.rect.width - 12);
        float y = point.y - 8;
        if (y - calendar.rect.height < bounds.yMin + 12) y = point.y + dateButton.GetComponent<RectTransform>().rect.height + calendar.rect.height + 8;
        y = Mathf.Clamp(y, bounds.yMin + calendar.rect.height + 12, bounds.yMax - 12);
        calendar.localPosition = new Vector3(x, y, 0);
    }

    private void ChangeMonth(int offset)
    {
        if ((offset < 0 && displayedMonth.Year == 1 && displayedMonth.Month == 1)
            || (offset > 0 && displayedMonth.Year == 9999 && displayedMonth.Month == 12)) return;
        displayedMonth = displayedMonth.AddMonths(offset);
        RefreshCalendar();
    }

    private void RefreshCalendar()
    {
        monthLabel.text = displayedMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        int leadingDays = ((int)displayedMonth.DayOfWeek + 6) % 7;
        firstGridDay = displayedMonth.AddDays(-leadingDays);
        for (int i = 0; i < days.Length; i++)
        {
            var date = firstGridDay.AddDays(i);
            var label = days[i].GetComponentInChildren<TMP_Text>();
            label.text = date.Day.ToString();
            bool selected = date == SelectedDate;
            days[i].GetComponent<Image>().color = selected ? new Color32(0, 156, 246, 255) : new Color32(244, 248, 255, 255);
            label.color = selected ? Color.white : date.Month == displayedMonth.Month ? ink : new Color32(167, 182, 200, 255);
        }
    }

    private void SelectDate(DateTime date)
    {
        year = date.Year; month = date.Month; day = date.Day;
        dateLabel.text = date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        overlay.SetActive(false);
    }

    private void OnDisable()
    {
        if (overlay) overlay.SetActive(false);
    }
}
