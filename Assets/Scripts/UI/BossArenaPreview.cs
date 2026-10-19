using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public sealed class BossArenaPreview : MonoBehaviour
{
    public GameObject stagePrefab;
    public Texture previewImage;
    public Transform Stage => stage ? stage.transform : null;
    public Camera StageCamera => stageCamera;
    GameObject stage;
    Camera stageCamera;
    SpriteRenderer cat;
    RawImage view;
    RenderTexture target;

    void OnEnable()
    {
        view = GetComponent<RawImage>();
        view.raycastTarget = false;
        if (!stage && stagePrefab)
        {
            stage = Instantiate(stagePrefab, new Vector3(20000, 0, 0), Quaternion.identity);
            foreach(var demo in stage.GetComponentsInChildren<MonsterCharacterAnimation>(true)) demo.enabled=false;
            stageCamera = stage.GetComponentInChildren<Camera>(true);
            cat = stage.transform.Find("Ninja Cat")?.GetComponentInChildren<SpriteRenderer>();
        }
        if (stage) stage.SetActive(true);
        UpdateTarget();
    }
    public bool TryGetCatHeadViewport(out Vector3 point)
    {
        point=default;
        if(!cat || !stageCamera) return false;
        // The original ninja frames include transparent padding above the ears.
        var bounds=cat.bounds;
        point=stageCamera.WorldToViewportPoint(bounds.center+Vector3.up*bounds.extents.y*.45f);
        return true;
    }
    void LateUpdate() => UpdateTarget();
    void UpdateTarget()
    {
        if (!stageCamera || !view) return;
        var size = view.rectTransform.rect.size;
        if (size.x <= 0 || size.y <= 0) return;
        float aspect = size.x / size.y;
        int width = Mathf.Clamp(Mathf.RoundToInt(900 * aspect), 128, 2048);
        if (!target || target.width != width)
        {
            ReleaseTarget();
            target = new RenderTexture(width, 900, 24) { name = "Boss Arena View" };
            target.Create();
        }
        view.texture = target;
        stageCamera.targetTexture = target;
        stageCamera.aspect = aspect;
        // Preserve the authored scene camera projection, FOV and orthographic size.
        stageCamera.enabled = true;
    }
    void OnDisable()
    {
        if (stageCamera) stageCamera.enabled = false;
        if (stage) stage.SetActive(false);
        ReleaseTarget();
    }
    void OnDestroy()
    {
        ReleaseTarget();
        if (stage) Destroy(stage);
    }
    void ReleaseTarget()
    {
        if (stageCamera) stageCamera.targetTexture = null;
        if (view) view.texture = previewImage;
        if (!target) return;
        target.Release(); Destroy(target); target = null;
    }
}
