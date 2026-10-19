using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static partial class BossPageSetup
{
    public const string SourceScene = "Assets/Scenes/BossBattle1.unity";
    const string Folder = "Assets/Prefabs/Boss";
    const string StagePath = Folder + "/ForestBossStage.prefab";
    const string Poster = "Assets/Generated/Previews/ForestBossArena.png";
    static TaskUIBuilder ui;
    static readonly Color Ink = TaskUIBuilder.Blue;
    static BossPageSetup()
    {
        EditorApplication.update += Poll;
        EditorSceneManager.sceneSaved += scene => {
            if (scene.path == SourceScene) EditorApplication.delayCall += RefreshScene;
        };
    }
    [MenuItem("Tools/Cat Quest/Refresh Boss Scene")]
    public static void RefreshScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        BuildStage();
        AssetDatabase.SaveAssets();
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists("Temp/refresh-boss-scene.request"))
        {
            File.Delete("Temp/refresh-boss-scene.request");
            try { RefreshScene(); File.WriteAllText("Temp/refresh-boss-scene.result", "PASS: BossBattle1 scene synchronized."); }
            catch (Exception e) { File.WriteAllText("Temp/refresh-boss-scene.result",e.ToString());Debug.LogException(e); }
        }
        if (!File.Exists("Temp/setup-boss-page.request")) return;
        File.Delete("Temp/setup-boss-page.request");
        try { Build(); File.WriteAllText("Temp/setup-boss-page.result", "PASS: Forest Boss page and animated stage saved."); }
        catch (Exception e) { File.WriteAllText("Temp/setup-boss-page.result", e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("Tools/Cat Quest/Build Forest Boss Preview")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder);
        if (!File.Exists(StagePath)) BuildStage();
        var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Canvas.prefab");
        try
        {
            var nav = root.GetComponentInChildren<MainMenuNavigation>(true);
            var page = nav.pages[(int)MainMenuPage.Boss].transform;
            foreach (Transform child in page.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            foreach (var layout in page.GetComponents<LayoutGroup>()) UnityEngine.Object.DestroyImmediate(layout);
            ui = new TaskUIBuilder(nav.taskSystem.font, nav.taskSystem.roundedSprite);
            var binding = page.GetComponent<BossPageUI>() ?? page.gameObject.AddComponent<BossPageUI>();
            var surface=Panel(page,"PageBackground",0,0,1,1,Color.white);
            surface.offsetMin=new Vector2(24,24);surface.offsetMax=new Vector2(-24,-24);
            var arena=Rect(surface,"Arena",0,0,1,1);
            arena.offsetMin=new Vector2(4,12);arena.offsetMax=new Vector2(-4,-4);
            ui.Image(arena,Color.white,ui.Rounded);arena.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            var viewRect=Rect(arena,"ForestView",0,0,1,1);
            var view=viewRect.gameObject.AddComponent<RawImage>();
            var preview=viewRect.gameObject.AddComponent<BossArenaPreview>();
            preview.stagePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            preview.previewImage=AssetDatabase.LoadAssetAtPath<Texture2D>(Poster);view.texture=preview.previewImage;
            var play=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Play_Type_03.prefab").transform;
            var bossHealth=CopyHealth(play.Find("Slider22"),arena,"BossHealth",.5f);
            bossHealth.anchorMin=bossHealth.anchorMax=new Vector2(.5f,1);bossHealth.anchoredPosition=new Vector2(0,-52);
            bossHealth.Find("TextFrame/Text_Title").GetComponent<TMP_Text>().text="Cyclope";
            bossHealth.Find("FillArea/Text_Value").GetComponent<TMP_Text>().text="100%";
            var catHealth=CopyHealth(play.Find("Character_Group/Character/Slider"),arena,"CatHealth",.45f);
            catHealth.anchorMin=catHealth.anchorMax=new Vector2(.3f,.2f);catHealth.anchoredPosition=Vector2.zero;
            var healthFeedback=catHealth.gameObject.AddComponent<PlayerHealthFeedback>();
            healthFeedback.neutralFill=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/ResourcesData/Sprites/Components/Slider/Slider_Custom/Slider01~06_White_Fill.png");
            var bossFeedback=bossHealth.gameObject.AddComponent<PlayerHealthFeedback>();
            bossFeedback.neutralFill=healthFeedback.neutralFill;
            bossFeedback.useColorStates=false;bossFeedback.healthy=new Color32(255,75,99,255);
            binding.arena=preview;binding.catHealth=catHealth;
            var challenge=Panel(arena,"ChallengePreview",.36f,.035f,.64f,.13f,Color.white);
            var actionImage=challenge.GetComponent<Image>();
            actionImage.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(FocusPageSetup.ActionBackground);
            actionImage.type=Image.Type.Sliced;actionImage.pixelsPerUnitMultiplier=1;
            Label(challenge,"Challenge",24,.04f,.1f,.96f,.9f,Color.white).alignment=TextAlignmentOptions.Center;
            ConfigureCombat(binding,arena,challenge,bossHealth);
            // Only controls intercept clicks.
            foreach(var graphic in page.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
            foreach(var button in page.GetComponentsInChildren<Button>(true))if(button.targetGraphic)button.targetGraphic.raycastTarget=true;
            binding.confirmation.GetComponent<Image>().raycastTarget=true;
            binding.stagePicker.GetComponent<Image>().raycastTarget=true;
            foreach(var panel in binding.resultPanels)panel.transform.Find("ScreenDim").GetComponent<Image>().raycastTarget=true;
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/Canvas.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
    }
    static RectTransform CopyHealth(Transform source,Transform parent,string name,float scale)
    {
        var clone=UnityEngine.Object.Instantiate(source.gameObject,parent);clone.name=name;clone.SetActive(true);
        var rect=(RectTransform)clone.transform;rect.localScale=Vector3.one*scale;
        var slider=clone.GetComponent<Slider>();slider.interactable=false;slider.transition=Selectable.Transition.None;
        slider.onValueChanged=new Slider.SliderEvent();slider.minValue=0;slider.maxValue=1;slider.SetValueWithoutNotify(1);
        foreach(var graphic in clone.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
        return rect;
    }
    static RectTransform Rect(Transform p,string name,float x0,float y0,float x1,float y1)
    {
        var r=TaskUIBuilder.Rect(p,name);r.anchorMin=new Vector2(x0,y0);r.anchorMax=new Vector2(x1,y1);r.offsetMin=r.offsetMax=Vector2.zero;return r;
    }
    static RectTransform Panel(Transform p,string name,float x0,float y0,float x1,float y1,Color color)
    {
        var r=Rect(p,name,x0,y0,x1,y1);ui.Image(r,color,ui.Rounded).raycastTarget=false;return r;
    }
    static TMP_Text Label(Transform p,string text,float size,float x0,float y0,float x1,float y1,Color color)
    {
        var t=ui.Text(p,text,size,color);var r=t.rectTransform;r.anchorMin=new Vector2(x0,y0);r.anchorMax=new Vector2(x1,y1);r.offsetMin=r.offsetMax=Vector2.zero;
        t.enableAutoSizing=true;t.fontSizeMin=size*.7f;t.fontSizeMax=size;t.textWrappingMode=TextWrappingModes.NoWrap;t.overflowMode=TextOverflowModes.Ellipsis;t.raycastTarget=false;return t;
    }
    static void BuildStage()
    {
        Directory.CreateDirectory(Folder);
        var scene = EditorSceneManager.OpenPreviewScene(SourceScene);
        GameObject root = null;
        try
        {
            var sources = scene.GetRootGameObjects();
            root = new GameObject("BossBattle1 Stage");
            SceneManager.MoveGameObjectToScene(root, scene);
            foreach (var source in sources)
            {
                var clone = UnityEngine.Object.Instantiate(source, root.transform, false);
                clone.name = source.name;
                source.SetActive(false);
            }
            var cameras = root.GetComponentsInChildren<Camera>(true);
            var camera = cameras.FirstOrDefault(c => c.CompareTag("MainCamera")) ?? cameras.FirstOrDefault();
            if (!camera) throw new InvalidOperationException("BossBattle1 needs a preview camera.");
            foreach (var c in cameras) { c.enabled = false; c.tag = "Untagged"; }
            camera.name = "Arena Camera";
            // Use the composition saved by the scene author, not the asset demo's camera tour.
            var tour = camera.GetComponent<Animator>();
            if (tour) tour.enabled = false;
            foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                if (animator.enabled) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var actors = new[] { root.transform.Find("Ninja Cat"), root.transform.Find("Cyclope") };
            var renderers = actors.Where(a=>a).SelectMany(a=>a.GetComponentsInChildren<SpriteRenderer>()).ToArray();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach(var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                camera.aspect = 1.5f;
                if (camera.WorldToViewportPoint(bounds.min).x < .04f || camera.WorldToViewportPoint(bounds.max).x > .96f)
                {
                    var position = camera.transform.position;
                    position.x = bounds.center.x;
                    camera.transform.position = position;
                }
            }
            PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            var rt = new RenderTexture(1200,800,24);
            var previous = RenderTexture.active;
            var texture = new Texture2D(1200,800,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=rt;camera.aspect=1.5f;camera.Render();
                RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1200,800),0,0);texture.Apply();
                File.WriteAllBytes(Poster,texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=null;RenderTexture.active=previous;rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(Poster,ImportAssetOptions.ForceUpdate);
        }
        finally
        {
            if(root) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
