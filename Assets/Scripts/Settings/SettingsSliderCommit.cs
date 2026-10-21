using System;
using UnityEngine;
using UnityEngine.EventSystems;

// A drag previews continuously but commits only at the end of an interaction.
public sealed class SettingsSliderCommit : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IEndDragHandler, IDeselectHandler
{
    public event Action Commit;
    bool dragging, dirty;
    float lastChange;
    public void MarkChanged() { dirty = true; lastChange = Time.unscaledTime; }
    public void Flush() { if (!dirty) return; dirty = false; Commit?.Invoke(); }
    public void Clear() { dirty = false; dragging = false; }
    public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) dragging = true; }
    public void OnPointerUp(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) { dragging = false; Flush(); } }
    public void OnEndDrag(PointerEventData e) { dragging = false; Flush(); }
    public void OnDeselect(BaseEventData e) { dragging = false; Flush(); }
    void Update() { if (dirty && !dragging && Time.unscaledTime - lastChange > .3f) Flush(); }
    void OnDisable() { dragging = false; Flush(); }
    void OnApplicationFocus(bool focused) { if (!focused) { dragging = false; Flush(); } }
}
