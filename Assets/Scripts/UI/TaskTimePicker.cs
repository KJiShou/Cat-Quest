using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TaskTimePicker : MonoBehaviour
{
    public Button timeButton;
    public TMP_Text timeLabel;
    public GameObject overlay;
    public RectTransform popup;
    public TimeWheelColumn hourWheel;
    public TimeWheelColumn minuteWheel;
    public Button confirm;
    public Button cancel;
    public Button dismiss;
    public int hour = 8;
    public int minute;
    public System.TimeSpan SelectedTime => new System.TimeSpan(hour, minute, 0);

    private void Awake()
    {
        timeButton.onClick.AddListener(Open);
        confirm.onClick.AddListener(Confirm);
        cancel.onClick.AddListener(Close);
        dismiss.onClick.AddListener(Close);
        RefreshLabel();
    }

    public void Open()
    {
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        hourWheel.SetValue(hour);
        minuteWheel.SetValue(minute);
        var canvas = timeButton.GetComponentInParent<Canvas>().rootCanvas;
        var overlayRect = (RectTransform)overlay.transform;
        var corners = new Vector3[4];
        timeButton.GetComponent<RectTransform>().GetWorldCorners(corners);
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRect, RectTransformUtility.WorldToScreenPoint(camera, corners[0]), camera, out var point);
        var bounds = overlayRect.rect;
        float x = Mathf.Clamp(point.x, bounds.xMin + 12, bounds.xMax - popup.rect.width - 12);
        float y = point.y - 8;
        if (y - popup.rect.height < bounds.yMin + 12) y = point.y + timeButton.GetComponent<RectTransform>().rect.height + popup.rect.height + 8;
        y = Mathf.Clamp(y, bounds.yMin + popup.rect.height + 12, bounds.yMax - 12);
        popup.localPosition = new Vector3(x, y, 0);
    }

    private void Confirm()
    {
        hour = hourWheel.Value;
        minute = minuteWheel.Value;
        RefreshLabel();
        Close();
    }

    private void RefreshLabel() => timeLabel.text = hour.ToString("00") + ":" + minute.ToString("00");
    private void Close() => overlay.SetActive(false);
    private void OnDisable() { if (overlay) Close(); }
}
