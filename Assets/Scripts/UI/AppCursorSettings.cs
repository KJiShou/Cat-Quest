using UnityEngine;

[CreateAssetMenu(menuName = "Cat Quest/Cursor Settings")]
public sealed class AppCursorSettings : ScriptableObject
{
    [Tooltip("Import as Cursor, with Read/Write enabled. Leave empty for the system cursor.")]
    public Texture2D normal;
    [Tooltip("Pixel coordinates from the TOP LEFT of the imported texture; place on the fingertip.")]
    public Vector2 hotspot = new Vector2(2, 2);
    [Tooltip("Optional cursor over enabled, interactable UI controls, including buttons and sliders. Empty keeps Normal.")]
    public Texture2D buttonHover;
    public Vector2 buttonHoverHotspot = new Vector2(9, 3);
    [Tooltip("Optional left-button-down artwork. Empty keeps the normal cursor.")]
    public Texture2D pressed;
    public Vector2 pressedHotspot = new Vector2(2, 2);

    public Vector2 HotspotFor(Texture2D texture, bool isPressed, bool isOverButton = false)
    {
        if (!texture) return Vector2.zero;
        var point = isPressed && pressed ? pressedHotspot :
            isOverButton && buttonHover ? buttonHoverHotspot : hotspot;
        return new Vector2(Mathf.Clamp(point.x, 0, texture.width - 1),
            Mathf.Clamp(point.y, 0, texture.height - 1));
    }
}
