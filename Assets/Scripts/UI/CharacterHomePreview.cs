using UnityEngine;
using UnityEngine.UI;

// Render the scene character inside the shared dashboard panel without stretching it.
[ExecuteAlways, RequireComponent(typeof(RawImage))]
public sealed class CharacterHomePreview : MonoBehaviour
{
    public Camera previewCamera;
    public SpriteRenderer character;
    private RawImage view;
    private RenderTexture texture;
    private RenderTexture originalTarget;
    private Texture originalTexture;
    private Vector3 centerOffset;
    private Vector2 halfSize;

    private void OnEnable()
    {
        view = GetComponent<RawImage>();
        view.raycastTarget = false;
        if (!previewCamera || !character) return;
        originalTarget = previewCamera.targetTexture;
        originalTexture = view.texture;
        centerOffset = character.bounds.center - character.transform.position;
        halfSize = character.bounds.extents;
    }

    private void LateUpdate()
    {
        if (!previewCamera || !character || !view) return;
        var rect = view.rectTransform.rect;
        if (rect.width <= 0 || rect.height <= 0) return;
        float aspect = rect.width / rect.height;
        int height = 720;
        int width = Mathf.Clamp(Mathf.RoundToInt(height * aspect), 64, 2048);
        if (!texture || texture.width != width)
        {
            ReleaseTexture();
            texture = new RenderTexture(width, height, 24) { name = "Character Home Preview" };
            texture.Create();
            previewCamera.targetTexture = texture;
            view.texture = texture;
        }
        previewCamera.orthographic = true;
        previewCamera.aspect = aspect;
        previewCamera.orthographicSize = Mathf.Max(1f, halfSize.y / .55f, halfSize.x / (aspect * .55f));
        var center = character.transform.position + centerOffset;
        previewCamera.transform.position = new Vector3(center.x, center.y, previewCamera.transform.position.z);
    }

    private void OnDisable()
    {
        if (previewCamera) previewCamera.targetTexture = originalTarget;
        if (view) view.texture = originalTexture;
        ReleaseTexture();
    }

    private void ReleaseTexture()
    {
        if (!texture) return;
        texture.Release();
        if (Application.isPlaying) Destroy(texture);
        else DestroyImmediate(texture);
        texture = null;
    }
}
