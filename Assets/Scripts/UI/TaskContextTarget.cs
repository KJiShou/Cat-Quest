using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Rebound with recycled rows; the owning task presenter supplies the current identity.
public sealed class TaskContextTarget : MonoBehaviour, IPointerClickHandler
{
    Action<Vector2> open;
    public static void Bind(GameObject target, Action<Vector2> action)
    {
        var input = target.GetComponent<TaskContextTarget>() ?? target.AddComponent<TaskContextTarget>();
        input.open = action;
    }
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Right) open?.Invoke(e.position);
    }
    void Update()
    {
        if (open == null || !EventSystem.current || EventSystem.current.currentSelectedGameObject != gameObject) return;
        var keyboard = Keyboard.current;
        if (keyboard == null || (!keyboard.contextMenuKey.wasPressedThisFrame && !(keyboard.f10Key.wasPressedThisFrame && keyboard.shiftKey.isPressed))) return;
        var canvas = GetComponentInParent<Canvas>().rootCanvas;
        var rect = (RectTransform)transform;
        open(RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
            rect.TransformPoint(rect.rect.center)));
    }
}
