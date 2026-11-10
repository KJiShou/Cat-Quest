using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public sealed class FocusScenePreview : MonoBehaviour
{
    public GameObject stagePrefab;
    [SerializeField] string stageResource;
    FocusTrainingBattle battle;
    FocusFishSpectators fish;
    ResourceRequest loading;
    IFocusPresentation presentation;
    bool showWhenReady;
    public Camera PreviewCamera { get; private set; }
    public GameObject Stage { get; private set; }
    public Vector2 framingOffset;
    Vector3 cameraHome;
    RawImage view;
    RenderTexture texture;
    PreviewPixels.ResizeGate resizeGate;
    CharacterEquipmentAura equipmentAura;
    public int targetHeight = 720;
    public float minimumOrthographicSize = 10, horizontalHalfWidth = 13;
    public void Bind(IFocusPresentation source)
    {
        presentation = source;
        if (!Stage) return;
        battle.Presentation = source;
        fish.Presentation = source;
        if (source is CompanionState state && equipmentAura)
            equipmentAura.SetRemote(state.connected ? state.equipmentAuraTier : 0, state.connected ? state.equipmentAuraPieces : 0);
    }

    void OnEnable()
    {
        view = GetComponent<RawImage>();
        view.raycastTarget = false;
        if (!Stage && !stagePrefab && !string.IsNullOrEmpty(stageResource)) {
            showWhenReady = view.enabled;
            view.enabled = false;
            loading = Resources.LoadAsync<StagePrefabResource>(stageResource);
            loading.completed += StageLoaded;
            return;
        }
        ShowStage();
    }
    void StageLoaded(AsyncOperation operation)
    {
        if (operation != loading) return;
        loading.completed -= StageLoaded;
        var entry = loading.asset as StagePrefabResource;
        loading = null;
        if (!entry || !entry.prefab) { Debug.LogError("Missing Focus stage " + stageResource); return; }
        stagePrefab = entry.prefab;
        view.enabled = showWhenReady;
        ShowStage();
    }
    void ShowStage()
    {
        if (!Stage && stagePrefab)
        {
            Stage = Instantiate(stagePrefab, new Vector3(10000, 10000, 0), Quaternion.identity);
            Stage.name = "Focus Page Stage";
            equipmentAura = Stage.GetComponentInChildren<CharacterEquipmentAura>(true);
            PreviewCamera = Stage.GetComponentInChildren<Camera>(true);
            cameraHome = PreviewCamera.transform.position;
            battle = Stage.GetComponentInChildren<FocusTrainingBattle>();
            fish = Stage.GetComponentInChildren<FocusFishSpectators>();
            fish.viewportCamera = PreviewCamera;
        }
        if (Stage) { Stage.SetActive(true); Bind(presentation); }
        UpdateTarget();
    }

    void LateUpdate() => UpdateTarget();
    void UpdateTarget()
    {
        if (!PreviewCamera || !view) return;
        // Rendering may stop while the native window is minimized; Focus and IPC keep running.
        if (WindowsAppWindow.IsMinimized) { PreviewCamera.enabled = false; return; }
        var size = view.rectTransform.rect.size;
        if (size.x <= 0 || size.y <= 0) return;
        float aspect = size.x / size.y;
        var pixels = PreviewPixels.For(view, targetHeight);
        if (resizeGate.Ready(texture,pixels))
        {
            ReleaseTexture();
            texture = new RenderTexture(pixels.x, pixels.y, 24) { name = "Focus Training View" };
            texture.Create();
        }
        if (view.texture != texture) view.texture = texture;
        PreviewCamera.transform.position = cameraHome + new Vector3(framingOffset.x, framingOffset.y, 0);
        if (PreviewCamera.targetTexture != texture) PreviewCamera.targetTexture = texture;
        if (!Mathf.Approximately(PreviewCamera.aspect, aspect)) PreviewCamera.aspect = aspect;
        PreviewCamera.orthographicSize = Mathf.Max(minimumOrthographicSize, horizontalHalfWidth / aspect);
        PreviewCamera.enabled = true;
    }

    void OnDisable()
    {
        if (loading != null) {
            loading.completed -= StageLoaded; loading = null;
            view.enabled = showWhenReady;
        }
        if (PreviewCamera) PreviewCamera.enabled = false;
        if (Stage) Stage.SetActive(false);
        ReleaseTexture();
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
