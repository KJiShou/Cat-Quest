using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Uses the OS cursor: no extra Canvas, raycast target, click handler or feedback owner.
public sealed class AppCursor : MonoBehaviour
{
    AppCursorSettings settings;
    Texture2D appliedTexture;
    Vector2 appliedHotspot;
    bool applied, focused = true;
    readonly List<RaycastResult> hits = new List<RaycastResult>(16);
    EventSystem pointerEventSystem;
    PointerEventData pointer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        // Companion owns native drag/resize cursors in its separate process.
        if (CompanionProtocol.HasArgument("--companion")) return;
        var config = Resources.Load<AppCursorSettings>("AppCursorSettings");
        if (!config) return;
        var host = new GameObject("App Cursor");
        DontDestroyOnLoad(host);
        host.AddComponent<AppCursor>().settings = config;
    }

    void LateUpdate()
    {
        var mouse = Mouse.current;
        var position = mouse == null ? Vector2.zero : mouse.position.ReadValue();
        bool inside = position.x >= 0 && position.y >= 0 &&
            position.x < Screen.width && position.y < Screen.height;
        if (!settings || !focused || mouse == null || !inside || Cursor.lockState != CursorLockMode.None)
        {
            ResetCursor();
            return;
        }
        bool down = mouse.leftButton.isPressed;
        bool overButton = settings.buttonHover && IsOverControl(position);
        var texture = down && settings.pressed ? settings.pressed :
            overButton ? settings.buttonHover : settings.normal;
        var hotspot = settings.HotspotFor(texture, down, overButton);
        if (applied && appliedTexture == texture && appliedHotspot == hotspot) return;
        Cursor.SetCursor(texture, hotspot, CursorMode.Auto);
        appliedTexture = texture;
        appliedHotspot = hotspot;
        applied = true;
    }

    bool IsOverControl(Vector2 position)
    {
        var eventSystem = EventSystem.current;
        if (!eventSystem || !eventSystem.isActiveAndEnabled) return false;
        if (pointer == null || pointerEventSystem != eventSystem)
        {
            pointerEventSystem = eventSystem;
            pointer = new PointerEventData(eventSystem);
        }
        pointer.Reset();
        pointer.position = position;
        hits.Clear();
        eventSystem.RaycastAll(pointer, hits);
        // Sliders handle pointer-down/drag rather than pointer-click. Resolve the
        // first pointer-down handler just as UI input does. Never look through
        // a blocking graphic or a child control to a button behind it.
        var target = hits.Count > 0 ? hits[0].gameObject : null;
        hits.Clear();
        var handler = target ? ExecuteEvents.GetEventHandler<IPointerDownHandler>(target) : null;
        var control = handler ? handler.GetComponent<Selectable>() : null;
        if (!control || !control.isActiveAndEnabled || !control.IsInteractable()) return false;
        if (control is TMPro.TMP_InputField text) return !text.readOnly;
        if (control is InputField legacyText) return !legacyText.readOnly;
        return control is Button || control is Slider || control is Toggle || control is Dropdown || control is TMPro.TMP_Dropdown;
    }

    void OnApplicationFocus(bool hasFocus)
    {
        focused = hasFocus;
        if (!hasFocus) ResetCursor();
    }

    void OnDisable() { ResetCursor(); }
    void OnApplicationQuit() { ResetCursor(); }

    void ResetCursor()
    {
        if (!applied) return;
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        appliedTexture = null;
        applied = false;
    }
}
