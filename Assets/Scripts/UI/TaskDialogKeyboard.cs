using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Input only; TaskConfirmDialog keeps ownership of the existing popup and actions.
[DefaultExecutionOrder(-100)]
public sealed class TaskDialogKeyboard : MonoBehaviour
{
    Button[] controls;
    GameObject previousSelection;
    Action dismiss;
    public void Open(Button[] buttons, Action close)
    {
        if (controls == null)
            previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        controls = buttons; dismiss = close;
        for (int i = 0; i < controls.Length; i++)
        {
            var navigation = new Navigation { mode = Navigation.Mode.Explicit };
            navigation.selectOnLeft = navigation.selectOnUp = controls[(i + controls.Length - 1) % controls.Length];
            navigation.selectOnRight = navigation.selectOnDown = controls[(i + 1) % controls.Length];
            controls[i].navigation = navigation;
        }
        Focus(controls[0].gameObject);
    }
    static void Focus(GameObject target) { if (EventSystem.current) EventSystem.current.SetSelectedGameObject(target); }
    void Update()
    {
        if (controls == null || !EventSystem.current) return;
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { dismiss?.Invoke(); return; }
        var selected = EventSystem.current.currentSelectedGameObject;
        int index = -1;
        for (int i = 0; i < controls.Length; i++)
            if (controls[i] && controls[i].gameObject == selected) { index = i; break; }
        if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
        {
            int direction = keyboard.shiftKey.isPressed ? -1 : 1;
            index = index < 0 ? 0 : (index + direction + controls.Length) % controls.Length;
            Focus(controls[index].gameObject);
        }
        else if (index < 0) Focus(controls[0].gameObject);
    }
    void OnDisable()
    {
        controls = null; dismiss = null;
        var events = EventSystem.current;
        if (events && (!events.currentSelectedGameObject || events.currentSelectedGameObject.transform.IsChildOf(transform)))
            Focus(previousSelection && previousSelection.activeInHierarchy ? previousSelection : null);
        previousSelection = null;
    }
}
