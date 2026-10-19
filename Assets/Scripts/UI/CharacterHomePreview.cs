using UnityEngine;
using UnityEngine.UI;

// Render the scene character inside the shared dashboard panel without stretching it.
[ExecuteAlways, RequireComponent(typeof(RawImage))]
public sealed class CharacterHomePreview : MonoBehaviour
{
    public Camera previewCamera;
    public SpriteRenderer character;
    public GameObject characterPrefab;
    public bool showSceneBackground;
    [Min(.1f)] public float backgroundOrthographicSize = 6.7f;
    private GameObject ownedStage;
    private Image placeholder;
    private RawImage view;
    private RenderTexture texture;
    private RenderTexture originalTarget;
    private Texture originalTexture;
    private Vector2 halfSize;
    private bool initialized;

    private void OnEnable()
    {
        view = GetComponent<RawImage>();
        view.raycastTarget = false;
        SyncLayout();
        placeholder = transform.parent ? transform.parent.Find("Image")?.GetComponent<Image>() : null;
        if (ownedStage) ownedStage.SetActive(true);
        if ((!previewCamera || !character) && characterPrefab && Application.isPlaying) CreatePreview();
        view.enabled = previewCamera && character;
        if (placeholder) placeholder.enabled = !view.enabled;
        if (!previewCamera || !character) return;
        if (!initialized)
        {
            originalTarget = previewCamera.targetTexture;
            originalTexture = view.texture;
            initialized = true;
        }
        halfSize = character.bounds.extents;
    }

    private void CreatePreview()
    {
        ownedStage = new GameObject("Dashboard Character Preview");
        ownedStage.transform.position = new Vector3(-10000, 10000, 0);
        var model = Instantiate(characterPrefab, ownedStage.transform);
        model.transform.localPosition = Vector3.zero;
        character = model.GetComponentInChildren<SpriteRenderer>();
        var animator = model.GetComponentInChildren<Animator>();
        if (animator) { animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Play("ninja-idle", 0, 0); animator.Update(0); }
        var cameraObject = new GameObject("Dashboard Preview Camera");
        cameraObject.transform.SetParent(ownedStage.transform, false);
        cameraObject.transform.localPosition = new Vector3(0, 0, -10);
        previewCamera = cameraObject.AddComponent<Camera>();
        previewCamera.enabled = false;
        previewCamera.orthographic = true;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = Color.clear;
        previewCamera.nearClipPlane = .1f;
        previewCamera.farClipPlane = 30;
    }

    private void SyncLayout()
    {
        // The shared dashboard's character area also positions the scene's live preview.
        var placeholder = transform.parent ? transform.parent.Find("Image") as RectTransform : null;
        if (placeholder)
        {
            var rect = view.rectTransform;
            rect.anchorMin = placeholder.anchorMin; rect.anchorMax = placeholder.anchorMax;
            rect.pivot = placeholder.pivot; rect.offsetMin = placeholder.offsetMin; rect.offsetMax = placeholder.offsetMax;
        }
        else if (transform.parent && transform.parent.Find("EquipmentStage") is RectTransform stage)
        {
            // CharacterHome replaces the prefab's sprite Image with this live preview.
            var rect = view.rectTransform;
            rect.anchorMin = stage.anchorMin + new Vector2(.02f, .02f);
            rect.anchorMax = stage.anchorMax - new Vector2(.02f, .01f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }

    private void LateUpdate()
    {
        if (!previewCamera || !character || !view) return;
        SyncLayout();
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
        }
        previewCamera.targetTexture = texture;
        if (ownedStage) previewCamera.enabled = true;
        view.texture = texture;
        previewCamera.orthographic = true;
        previewCamera.aspect = aspect;
        halfSize = character.bounds.extents;
        float fill = ownedStage ? .8f : .55f;
        previewCamera.orthographicSize = showSceneBackground && !ownedStage ? backgroundOrthographicSize : Mathf.Max(ownedStage ? .1f : 1f,
            halfSize.y / fill, halfSize.x / (aspect * fill));
        var center = character.bounds.center;
        previewCamera.transform.position = new Vector3(center.x, center.y, previewCamera.transform.position.z);
    }

    private void OnDisable()
    {
        if (ownedStage) ownedStage.SetActive(false);
        if (previewCamera) previewCamera.targetTexture = originalTarget;
        if (view) view.texture = originalTexture;
        // Keep the same target across page switches; recreating it disrupts the preview's rendering state.
        if (!Application.isPlaying) ReleaseTexture();
    }

    private void OnDestroy()
    {
        if (previewCamera) previewCamera.targetTexture = originalTarget;
        if (view) view.texture = originalTexture;
        ReleaseTexture();
        if (ownedStage) Destroy(ownedStage);
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
