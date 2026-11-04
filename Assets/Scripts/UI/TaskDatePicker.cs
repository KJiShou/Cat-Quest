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
    public Button noDate;
    public TMP_Text noDateLabel;
    public bool HasDate { get; private set; } = true;
    public event Action DateCleared;
    public Button[] days;
    public int year = 2024;
    public int month = 8;
    public int day = 4;

    public event Action<DateTime> DateSelected;
    private DateTime displayedMonth;
    private DateTime firstGridDay;
    private readonly Color ink = new Color32(20, 45, 77, 255);

    public DateTime SelectedDate => new DateTime(year, month, day);
    public void LoadValue(DateTime value, bool hasDate = true)
    {
        year = value.Year; month = value.Month; day = value.Day;
        HasDate = hasDate;
        RefreshLabel();
    }

    private void Awake()
    {
        dateButton.onClick.AddListener(Open);
        if (noDate) noDate.onClick.AddListener(() =>
        {
            HasDate = false;
            RefreshLabel();
            overlay.SetActive(false);
            dateButton.Select();
            DateCleared?.Invoke();
        });
        dismiss.onClick.AddListener(() => overlay.SetActive(false));
        (dismiss.GetComponent<JuicyButton>()??dismiss.gameObject.AddComponent<JuicyButton>()).enabled=false;
        previousMonth.onClick.AddListener(() => ChangeMonth(-1));
        nextMonth.onClick.AddListener(() => ChangeMonth(1));
        for (int i = 0; i < days.Length; i++)
        {
            int index = i;
            days[i].onClick.AddListener(() => SelectDate(firstGridDay.AddDays(index)));
        }
        RefreshLabel();
    }

    void RefreshLabel() => dateLabel.text = HasDate ? SelectedDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "No date";

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
            bool inRange = (DateTime.MaxValue.Date - firstGridDay).Days >= i;
            days[i].gameObject.SetActive(inRange);
            if (!inRange) continue;
            var date = firstGridDay.AddDays(i);
            var label = days[i].GetComponentInChildren<TMP_Text>();
            label.text = date.Day.ToString();
            bool selected = HasDate && date == SelectedDate;
            days[i].GetComponent<Image>().color = selected ? new Color32(0, 156, 246, 255) : new Color32(244, 248, 255, 255);
            label.color = selected ? Color.white : date.Month == displayedMonth.Month ? ink : new Color32(167, 182, 200, 255);
        }
    }

    private void SelectDate(DateTime date)
    {
        LoadValue(date);
        overlay.SetActive(false);
        dateButton.Select();
        DateSelected?.Invoke(date);
    }

    private void OnDisable()
    {
        if (overlay) overlay.SetActive(false);
    }
}
