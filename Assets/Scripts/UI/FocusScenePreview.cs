using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public sealed class FocusScenePreview : MonoBehaviour
{
    public GameObject stagePrefab;
    public Camera PreviewCamera { get; private set; }
    public GameObject Stage { get; private set; }
    public Vector2 framingOffset;
    Vector3 cameraHome;
    RawImage view;
    RenderTexture texture;
    public int targetHeight = 720;
    public float minimumOrthographicSize = 10, horizontalHalfWidth = 13;
    public void Bind(IFocusPresentation source)
    {
        if (!Stage) return;
        Stage.GetComponentInChildren<FocusTrainingBattle>().Presentation = source;
        Stage.GetComponentInChildren<FocusFishSpectators>().Presentation = source;
    }

    void OnEnable()
    {
        view = GetComponent<RawImage>();
        view.raycastTarget = false;
        if (!Stage && stagePrefab)
        {
            Stage = Instantiate(stagePrefab, new Vector3(10000, 10000, 0), Quaternion.identity);
            Stage.name = "Focus Page Stage";
            PreviewCamera = Stage.GetComponentInChildren<Camera>(true);
            cameraHome = PreviewCamera.transform.position;
            Stage.GetComponentInChildren<FocusFishSpectators>().viewportCamera = PreviewCamera;
        }
        if (Stage) Stage.SetActive(true);
        UpdateTarget();
    }

    void LateUpdate() => UpdateTarget();
    void UpdateTarget()
    {
        if (!PreviewCamera || !view) return;
        var size = view.rectTransform.rect.size;
        if (size.x <= 0 || size.y <= 0) return;
        float aspect = size.x / size.y;
        int height = targetHeight, width = Mathf.Clamp(Mathf.RoundToInt(height * aspect), 64, 2048);
        if (!texture || texture.width != width)
        {
            ReleaseTexture();
            texture = new RenderTexture(width, height, 24) { name = "Focus Training View" };
            texture.Create();
        }
        view.texture = texture;
        PreviewCamera.transform.position = cameraHome + new Vector3(framingOffset.x, framingOffset.y, 0);
        PreviewCamera.targetTexture = texture;
        PreviewCamera.aspect = aspect;
        PreviewCamera.orthographicSize = Mathf.Max(minimumOrthographicSize, horizontalHalfWidth / aspect);
        PreviewCamera.enabled = true;
    }

    void OnDisable()
    {
        if (PreviewCamera) PreviewCamera.enabled = false;
        if (Stage) Stage.SetActive(false);
    }
    void OnDestroy()
    {
        if (Stage) Destroy(Stage);
        ReleaseTexture();
    }
    void ReleaseTexture()
    {
        if (PreviewCamera) PreviewCamera.targetTexture = null;
        if (view) view.texture = null;
        if (!texture) return;
        texture.Release(); Destroy(texture); texture = null;
    }
}
