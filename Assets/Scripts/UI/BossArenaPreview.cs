using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public sealed class BossArenaPreview : MonoBehaviour
{
    public GameObject stagePrefab;
    public GameObject oldForestStagePrefab, lavaDungeonStagePrefab;
    [SerializeField] string stageResource, oldForestStageResource, lavaDungeonStageResource;
    public MasterBossAura masterAuraPrefab;
    MasterBossAura masterAura;
    public MasterBossAura MasterAura => masterAura;
    public void ShowMasterAura(Transform boss,Transform cat,int bossId,bool visible)
    {
        if(!visible){if(masterAura)masterAura.gameObject.SetActive(false);return;}
        if(!boss||!cat||!stage||!masterAuraPrefab)return;
        if(!masterAura)masterAura=Instantiate(masterAuraPrefab,stage.transform);
        masterAura.gameObject.SetActive(true);masterAura.Bind(boss,cat,bossId);
    }
    public Texture previewImage;
    public Transform Stage => stage ? stage.transform : null;
    public Camera StageCamera => stageCamera;
    GameObject stage;
    Camera stageCamera;
    SpriteRenderer cat;
    RawImage view;
    RenderTexture target;
    PreviewPixels.ResizeGate resizeGate;
    GameObject activePrefab;
    ResourceRequest loading;
    int requestedStage = 1, loadedStage;
    public bool IsLoading => loading != null;
    public bool IsReady => stage && loadedStage == requestedStage && !IsLoading && LoadError == null;
    public string LoadError { get; private set; }
    public event System.Action StageChanged;

    public void SelectStage(int id)
    {
        CancelLoading();requestedStage = id;LoadError = null;
        var prefab = id == 2 ? oldForestStagePrefab : id == 3 ? lavaDungeonStagePrefab : stagePrefab;
        string path = id == 2 ? oldForestStageResource : id == 3 ? lavaDungeonStageResource : stageResource;
        if (!prefab && string.IsNullOrEmpty(path)) { prefab = stagePrefab; path = stageResource; }
        if (prefab) { ShowStage(prefab); return; }
        if (stage) stage.SetActive(false);
        ReleaseTarget();
        if (string.IsNullOrEmpty(path)) { FailLoading(); return; }
        if (!isActiveAndEnabled) return;
        loading = Resources.LoadAsync<StagePrefabResource>(path);
        loading.completed += StageLoaded;
        StageChanged?.Invoke();
    }
    void StageLoaded(AsyncOperation operation)
    {
        if (operation != loading || !isActiveAndEnabled) return;
        loading.completed -= StageLoaded;
        var entry = loading.asset as StagePrefabResource;loading = null;
        if (!entry || !entry.prefab) { FailLoading(); return; }
        if (requestedStage == 2) oldForestStagePrefab = entry.prefab;
        else if (requestedStage == 3) lavaDungeonStagePrefab = entry.prefab;
        else stagePrefab = entry.prefab;
        ShowStage(entry.prefab);
    }
    void FailLoading()
    {
        LoadError = "Could not load this arena. Retry to continue. No ticket was used.";
        StageChanged?.Invoke();
    }
    void CancelLoading()
    {
        if (loading != null) loading.completed -= StageLoaded;
        loading = null;
    }
    void ShowStage(GameObject prefab)
    {
        loadedStage = requestedStage;
        if (stage && activePrefab == prefab) { stage.SetActive(isActiveAndEnabled);UpdateTarget();StageChanged?.Invoke();return; }
        ReleaseTarget();
        masterAura = null;
        if (stage) { stage.SetActive(false); Destroy(stage); }
        stage = null; stageCamera = null; cat = null;
        activePrefab = prefab;
        if (!prefab) return;
        stage = Instantiate(prefab, new Vector3(20000, 0, 0), Quaternion.identity);
        foreach(var demo in stage.GetComponentsInChildren<MonsterCharacterAnimation>(true)) demo.enabled=false;
        stageCamera = stage.GetComponentInChildren<Camera>(true);
        cat = stage.transform.Find("Ninja Cat")?.GetComponentInChildren<SpriteRenderer>();
        stage.SetActive(isActiveAndEnabled);
        UpdateTarget();
        StageChanged?.Invoke();
    }

    void OnEnable()
    {
        view = GetComponent<RawImage>();
        view.raycastTarget = false;
        if (!stage || loadedStage != requestedStage || LoadError != null) SelectStage(requestedStage);
        if (IsReady) stage.SetActive(true);
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
        if (!stageCamera || !view || !IsReady) return;
        // Keep the battle stage and simulation alive; only suspend its invisible camera.
        if (WindowsAppWindow.IsMinimized) { stageCamera.enabled = false; return; }
        var size = view.rectTransform.rect.size;
        if (size.x <= 0 || size.y <= 0) return;
        float aspect = size.x / size.y;
        var pixels = PreviewPixels.For(view, 900);
        if (resizeGate.Ready(target,pixels))
        {
            ReleaseTarget();
            target = new RenderTexture(pixels.x, pixels.y, 24) { name = "Boss Arena View" };
            target.Create();
        }
        if (view.texture != target) view.texture = target;
        if (stageCamera.targetTexture != target) stageCamera.targetTexture = target;
        if (!Mathf.Approximately(stageCamera.aspect, aspect)) stageCamera.aspect = aspect;
        // Preserve the authored scene camera projection, FOV and orthographic size.
        if (!stageCamera.enabled) stageCamera.enabled = true;
    }
    void OnDisable()
    {
        CancelLoading();
        if (stageCamera) stageCamera.enabled = false;
        if (stage) stage.SetActive(false);
        ReleaseTarget();
    }
    void OnDestroy()
    {
        CancelLoading();
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
