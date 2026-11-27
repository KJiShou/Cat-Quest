using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent, RequireComponent(typeof(Camera))]
public sealed class CatSceneParallax : MonoBehaviour
{
    [SerializeField] Transform farLayer, nearLayer;
    Camera viewCamera;
    Vector3 farHome, nearHome, cameraHome;
    Vector2 offset;
    bool applied;

    void Awake()
    {
        viewCamera = GetComponent<Camera>();
        if (farLayer) farHome = farLayer.localPosition;
        if (nearLayer) nearHome = nearLayer.localPosition;
    }

    public void Apply(Vector3 framing)
    {
        cameraHome = framing;
        applied = true;
        if (UiMotion.Reduced || !Application.isFocused || WindowsAppWindow.IsMinimized)
        {
            ResetOffset();
            return;
        }

        if (!WindowsAppWindow.TryGetClientPointer(out var position, out var size))
        {
            var mouse = Mouse.current;
            position = mouse == null ? new Vector2(-1, -1) : mouse.position.ReadValue();
            size = new Vector2(Screen.width, Screen.height);
        }
        var target = WindowPointer(position, size);
        offset = Vector2.Lerp(offset, target, 1 - Mathf.Exp(-12 * Time.unscaledDeltaTime));
        if (target == Vector2.zero && offset.sqrMagnitude < .000001f) offset = Vector2.zero;
        var height = 2 * viewCamera.orthographicSize;
        var delta = viewCamera.transform.right * (offset.x * .015f * height * viewCamera.aspect)
            + viewCamera.transform.up * (offset.y * .01f * height);
        Place(delta);
    }

    // The client area supplies input even while the pointer is above unrelated UI.
    static Vector2 WindowPointer(Vector2 position, Vector2 size)
    {
        if (position.x < 0 || position.y < 0 || position.x >= size.x || position.y >= size.y)
            return Vector2.zero;
        return new Vector2(2 * position.x / size.x - 1, 2 * position.y / size.y - 1);
    }

    public void ResetOffset()
    {
        offset = Vector2.zero;
        if (!applied) return;
        Place(Vector3.zero);
        applied = false;
    }

    void Place(Vector3 delta)
    {
        transform.position = cameraHome + delta;
        // The actor and ground stay together. Distant scenery drifts less on screen.
        if (farLayer) farLayer.localPosition = farHome + LocalOffset(farLayer, delta * .7f);
        if (nearLayer) nearLayer.localPosition = nearHome + LocalOffset(nearLayer, delta * -.25f);
    }

    static Vector3 LocalOffset(Transform layer, Vector3 delta) => layer.parent ? layer.parent.InverseTransformVector(delta) : delta;

    void OnDisable() => ResetOffset();
    void OnApplicationFocus(bool focused) { if (!focused) ResetOffset(); }
}
