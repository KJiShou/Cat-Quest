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
    GameObject activePrefab;

    public void SelectStage(int id)
    {
        GameObject prefab;
        if (id == 2) prefab = oldForestStagePrefab = StagePrefabResource.Resolve(oldForestStagePrefab, oldForestStageResource);
        else if (id == 3) prefab = lavaDungeonStagePrefab = StagePrefabResource.Resolve(lavaDungeonStagePrefab, lavaDungeonStageResource);
        else prefab = null;
        if (!prefab) prefab = stagePrefab = StagePrefabResource.Resolve(stagePrefab, stageResource);
        if (stage && activePrefab == prefab) return;
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
    }

    void OnEnable()
    {
        view = GetComponent<RawImage>();
        view.raycastTarget = false;
        if (!stage) SelectStage(1);
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
        var pixels = PreviewPixels.For(view, 900);
        if (!target || target.width != pixels.x || target.height != pixels.y)
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
