using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Render the scene character inside the shared dashboard panel without stretching it.
[ExecuteAlways, RequireComponent(typeof(RawImage))]
public sealed class CharacterHomePreview : MonoBehaviour
{
    public Camera previewCamera;
    public SpriteRenderer character;
    public GameObject characterPrefab;
    public GameObject environmentPrefab;
    public string idleAnimation = "ninja-idle";
    public bool fixedFraming;
    [Min(.1f)] public float framingZoom = 1;
    public Vector2 framingOffset;
    public Vector3 stageOffset;
    public Material characterMaterial;
    // Optional remote presentation. Existing home/dashboard previews remain local.
    public bool remoteEquipment;
    int remoteTier, remotePieces;
    public CharacterEquipmentAura EquipmentAura => character ? character.GetComponentInChildren<CharacterEquipmentAura>(true) : null;
    public void SetRemoteEquipment(int tier, int pieces)
    {
        remoteEquipment = true; remoteTier = tier; remotePieces = pieces;
        if (EquipmentAura) EquipmentAura.SetRemote(tier, pieces);
    }
    private Vector3 fixedCenter;
    public Animator CharacterAnimator => character ? character.GetComponent<Animator>() : null;
    public bool showSceneBackground;
    [Min(.1f)] public float backgroundOrthographicSize = 6.7f;
    private GameObject ownedStage;
    private Image placeholder;
    private RectTransform layoutImage, layoutStage;
    private RawImage view;
    private RenderTexture texture;
    private RenderTexture originalTarget;
    private Texture originalTexture;
    private Vector2 halfSize;
    private bool initialized;
    private PreviewPixels.ResizeGate resizeGate;
    // Dashboard and Cat Home share the authored forest camera. Disabled pages keep
    // their binding so its original state is captured once, not from another page.
    private sealed class CameraBinding
    {
        public Camera camera;
        public bool originalEnabled;
        public RenderTexture originalTarget;
        public readonly HashSet<CharacterHomePreview> users = new HashSet<CharacterHomePreview>();
        public readonly HashSet<CharacterHomePreview> activeUsers = new HashSet<CharacterHomePreview>();
    }
    private static readonly Dictionary<Camera, CameraBinding> sharedCameras = new Dictionary<Camera, CameraBinding>();
    private CameraBinding cameraBinding;

    private void OnEnable()
    {
        view = GetComponent<RawImage>();
        view.raycastTarget = false;
        layoutImage = transform.parent ? transform.parent.Find("Image") as RectTransform : null;
        layoutStage = transform.parent ? transform.parent.Find("EquipmentStage") as RectTransform : null;
        SyncLayout();
        placeholder = layoutImage ? layoutImage.GetComponent<Image>() : null;
        if (ownedStage) ownedStage.SetActive(true);
        if ((!previewCamera || !character) && characterPrefab && Application.isPlaying) CreatePreview();
        if (remoteEquipment && EquipmentAura) EquipmentAura.SetRemote(remoteTier, remotePieces);
        view.enabled = previewCamera && character;
        if (placeholder) placeholder.enabled = !view.enabled;
        if (!previewCamera || !character) return;
        if (Application.isPlaying && !ownedStage) BindCamera();
        if (!initialized)
        {
            originalTarget = cameraBinding != null ? cameraBinding.originalTarget : previewCamera.targetTexture;
            originalTexture = view.texture;
            initialized = true;
        }
        if (!fixedFraming || halfSize == Vector2.zero) { halfSize = character.bounds.extents; fixedCenter = character.bounds.center; }
    }

    private void BindCamera()
    {
        if (cameraBinding != null && cameraBinding.camera != previewCamera) UnbindCamera();
        if (cameraBinding == null)
        {
            if (!sharedCameras.TryGetValue(previewCamera, out cameraBinding))
            {
                cameraBinding = new CameraBinding { camera = previewCamera, originalEnabled = previewCamera.enabled, originalTarget = previewCamera.targetTexture };
                sharedCameras.Add(previewCamera, cameraBinding);
            }
            cameraBinding.users.Add(this);
        }
        cameraBinding.activeUsers.Add(this);
        RefreshCamera(cameraBinding);
    }

    private static void RefreshCamera(CameraBinding binding, CharacterHomePreview releasing = null)
    {
        if (!binding.camera) return;
        if (binding.activeUsers.Count == 0) binding.camera.targetTexture = binding.originalTarget;
        else if (releasing && binding.camera.targetTexture == releasing.texture)
        {
            // An outgoing page must not leave the other active consumer on its target.
            binding.camera.targetTexture = binding.originalTarget;
            foreach (var owner in binding.activeUsers)
                if (owner && owner.texture) { binding.camera.targetTexture = owner.texture; break; }
        }
        binding.camera.enabled = binding.originalEnabled && binding.activeUsers.Count > 0 && !WindowsAppWindow.IsMinimized;
    }

    private void UnbindCamera()
    {
        if (cameraBinding == null) return;
        var binding = cameraBinding;
        cameraBinding = null;
        binding.activeUsers.Remove(this);
        binding.users.Remove(this);
        if (binding.users.Count == 0)
        {
            if (binding.camera) { binding.camera.targetTexture = binding.originalTarget; binding.camera.enabled = binding.originalEnabled; }
            sharedCameras.Remove(binding.camera);
        }
        else RefreshCamera(binding, this);
    }

    private void CreatePreview()
    {
        ownedStage = new GameObject("Dashboard Character Preview");
        ownedStage.transform.position = new Vector3(-10000, 10000, 0) + stageOffset;
        if (environmentPrefab)
        {
            var environment = Instantiate(environmentPrefab, ownedStage.transform, false);
            previewCamera = environment.GetComponentInChildren<Camera>();
        }
        var model = Instantiate(characterPrefab, ownedStage.transform);
        model.transform.localPosition = Vector3.zero;
        character = model.GetComponentInChildren<SpriteRenderer>();
        if (remoteEquipment && EquipmentAura) EquipmentAura.SetRemote(remoteTier, remotePieces);
        if(characterMaterial) character.sharedMaterial = characterMaterial;
        var animator = model.GetComponentInChildren<Animator>();
        if (animator) { animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; if(remoteEquipment)animator.updateMode=AnimatorUpdateMode.UnscaledTime; animator.Play(idleAnimation, 0, 0); animator.Update(0); }
        if (previewCamera) { previewCamera.enabled = false; return; }
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
        if (layoutImage)
        {
            var rect = view.rectTransform;
            rect.anchorMin = layoutImage.anchorMin; rect.anchorMax = layoutImage.anchorMax;
            rect.pivot = layoutImage.pivot; rect.offsetMin = layoutImage.offsetMin; rect.offsetMax = layoutImage.offsetMax;
        }
        else if (layoutStage)
        {
            // CharacterHome replaces the prefab's sprite Image with this live preview.
            var rect = view.rectTransform;
            rect.anchorMin = layoutStage.anchorMin + new Vector2(.02f, .02f);
            rect.anchorMax = layoutStage.anchorMax - new Vector2(.02f, .01f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }

    private void LateUpdate()
    {
        if (!previewCamera || !character || !view) return;
        if (WindowsAppWindow.IsMinimized) { previewCamera.enabled = false; return; }
        SyncLayout();
        var rect = view.rectTransform.rect;
        if (rect.width <= 0 || rect.height <= 0) return;
        float aspect = rect.width / rect.height;
        var pixels = PreviewPixels.For(view, 720);
        int width = pixels.x, height = pixels.y;
        if (resizeGate.Ready(texture,pixels))
        {
            ReleaseTexture();
            texture = new RenderTexture(width, height, 24) { name = "Character Home Preview" };
            texture.Create();
        }
        previewCamera.targetTexture = texture;
        if (ownedStage) previewCamera.enabled = true;
        else if (cameraBinding != null) RefreshCamera(cameraBinding);
        view.texture = texture;
        previewCamera.orthographic = true;
        previewCamera.aspect = aspect;
        if (!fixedFraming || halfSize == Vector2.zero) { halfSize = character.bounds.extents; fixedCenter = character.bounds.center; }
        float fill = ownedStage ? .8f : .55f;
        previewCamera.orthographicSize = showSceneBackground && (!ownedStage || environmentPrefab) ? backgroundOrthographicSize : Mathf.Max(ownedStage ? .1f : 1f,
            halfSize.y / fill, halfSize.x / (aspect * fill)) / Mathf.Max(.1f, framingZoom);
        var center = (fixedFraming ? fixedCenter : character.bounds.center) + new Vector3(framingOffset.x * halfSize.x, framingOffset.y * halfSize.y, 0);
        previewCamera.transform.position = new Vector3(center.x, center.y, previewCamera.transform.position.z);
    }

    private void OnDisable()
    {
        if (ownedStage) ownedStage.SetActive(false);
        if (cameraBinding != null) { cameraBinding.activeUsers.Remove(this); RefreshCamera(cameraBinding, this); }
        else if (initialized && previewCamera) previewCamera.targetTexture = originalTarget;
        if (view) view.texture = originalTexture;
        // Keep the same target across page switches; recreating it disrupts the preview's rendering state.
        if (!Application.isPlaying) ReleaseTexture();
    }

    private void OnDestroy()
    {
        if (cameraBinding != null) UnbindCamera();
        else if (initialized && previewCamera) previewCamera.targetTexture = originalTarget;
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
