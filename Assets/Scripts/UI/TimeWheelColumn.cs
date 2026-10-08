using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class TimeWheelColumn : MonoBehaviour, IBeginDragHandler, IEndDragHandler, IScrollHandler
{
    public ScrollRect scroll;
    public TMP_Text[] numbers;
    public float rowHeight = 44;
    public int minimumValue;
    public int Value { get; private set; }
    private bool dragging;
    private float lastInput;

    public void SetValue(int value)
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        scroll.StopMovement();
        Value = Mathf.Clamp(value, minimumValue, minimumValue + numbers.Length - 1);
        scroll.content.anchoredPosition = new Vector2(0, (Value - minimumValue) * rowHeight);
        UpdateAppearance();
    }

    public void OnBeginDrag(PointerEventData data) { dragging = true; }
    public void OnEndDrag(PointerEventData data) { dragging = false; lastInput = Time.unscaledTime; }
    public void OnScroll(PointerEventData data) { lastInput = Time.unscaledTime; }
    private void OnDisable() { dragging = false; }

    private void LateUpdate()
    {
        float position = scroll.content.anchoredPosition.y;
        Value = minimumValue + Mathf.Clamp(Mathf.RoundToInt(position / rowHeight), 0, numbers.Length - 1);
        if (!dragging && Time.unscaledTime - lastInput > .12f && Mathf.Abs(scroll.velocity.y) < 40)
        {
            scroll.StopMovement();
            float target = (Value - minimumValue) * rowHeight;
            float snapped = Mathf.Lerp(position, target, 1 - Mathf.Exp(-20 * Time.unscaledDeltaTime));
            if (Mathf.Abs(snapped - target) < .05f) snapped = target;
            scroll.content.anchoredPosition = new Vector2(0, snapped);
        }
        UpdateAppearance();
    }

    private void UpdateAppearance()
    {
        float position = scroll.content.anchoredPosition.y / rowHeight;
        for (int i = 0; i < numbers.Length; i++)
        {
            float distance = Mathf.Abs(i - position);
            float emphasis = Mathf.Clamp01(1 - distance / 2.6f);
            Color color = i + minimumValue == Value ? new Color32(0, 137, 245, 255) : new Color32(103, 125, 155, 255);
            color.a = Mathf.Lerp(.12f, 1, emphasis);
            numbers[i].color = color;
            numbers[i].fontSize = Mathf.Lerp(20, 28, emphasis);
        }
    }
}
