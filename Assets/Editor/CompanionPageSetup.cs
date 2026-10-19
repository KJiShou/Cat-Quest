using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[InitializeOnLoad] public static class CompanionPageSetup
{
    public const string PrefabPath="Assets/Prefabs/Focus/CompanionWindow.prefab";
    public const string BootstrapPath="Assets/Scenes/CatQuestBootstrap.unity";
    static CompanionPageSetup() { EditorApplication.update+=Poll; }
    static void Poll()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if(File.Exists("Temp/setup-focus-sliders.request"))
        {
            File.Delete("Temp/setup-focus-sliders.request");
            var root=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Canvas.prefab");
            try { FocusPageSetup.ConfigurePage(root);PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/Canvas.prefab");AssetDatabase.SaveAssets();File.WriteAllText("Temp/setup-focus-sliders.result","PASS"); }
            catch(Exception e){File.WriteAllText("Temp/setup-focus-sliders.result",e.ToString());Debug.LogException(e);}
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        if(File.Exists("Temp/redesign-companion.request"))
        {
            File.Delete("Temp/redesign-companion.request");
            try {
                BuildCompanionOnly(); File.WriteAllText("Temp/redesign-companion.result","PASS");
            } catch(Exception e) { File.WriteAllText("Temp/redesign-companion.result",e.ToString());Debug.LogException(e); }
        }
        if(File.Exists("Temp/setup-focus-companion.request"))
        {
            File.Delete("Temp/setup-focus-companion.request");
            try { Build(); File.WriteAllText("Temp/setup-focus-companion.result","PASS"); }
            catch(Exception e) { File.WriteAllText("Temp/setup-focus-companion.result",e.ToString()); Debug.LogException(e); }
        }
        if(File.Exists("Temp/build-focus-windows.request"))
        {
            File.Delete("Temp/build-focus-windows.request");
            try {
                Directory.CreateDirectory("Builds/Windows");
                var report=BuildPipeline.BuildPlayer(EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),"Builds/Windows/CatQuest.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
                File.WriteAllText("Temp/build-focus-windows.result",report.summary.result+"\n"+report.summary.totalErrors+" errors\n"+report.summary.totalSize+" bytes");
            } catch(Exception e) { File.WriteAllText("Temp/build-focus-windows.result",e.ToString()); }
        }
    }
    [MenuItem("Tools/Cat Quest/Build Companion Window")]
    public static void BuildCompanionOnly()
    {
        var canvas=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Canvas.prefab");
        var panel=canvas.GetComponentsInChildren<FocusPanelUI>(true).First(p=>!p.quickStart);
        CompanionWindowDesign.Build(panel.timer.font,panel.settingsSprite);
        AssetDatabase.SaveAssets();
    }
    public static void BindToggle(GameObject root)
    {
        var label=root.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t=>t.text=="Pet Widget");
        if(!label) return;
        var toggle=label.GetComponentInParent<Toggle>();
        if(!toggle) toggle=label.transform.parent.GetComponentInChildren<Toggle>(true);
        if(!toggle)
        {
            var icon=label.transform.parent.Find("Icon_Gem");
            if(!icon) throw new Exception("Pet Widget switch artwork is missing.");
            toggle=icon.gameObject.AddComponent<Toggle>(); toggle.targetGraphic=icon.GetComponent<Image>();
        }
        var binding=toggle.GetComponent<PetWidgetToggle>() ?? toggle.gameObject.AddComponent<PetWidgetToggle>();
        binding.notice=root.GetComponentsInChildren<FocusPanelUI>(true).First(p=>!p.quickStart).message;
        binding.frame=toggle.GetComponent<Image>();
        binding.handle=toggle.transform.childCount>0?toggle.transform.GetChild(0).GetComponent<RectTransform>():null;
        const string sprites="Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/ResourcesData/Sprites/Components/UI_Etc/";
        binding.onSprite=AssetDatabase.LoadAssetAtPath<Sprite>(sprites+"Toggle_Switch_Frame_White.png");
        binding.onColor=label.color;
        binding.offSprite=AssetDatabase.LoadAssetAtPath<Sprite>(sprites+"Toggle_Switch_Frame_Off_Light.png");
        toggle.onValueChanged=new Toggle.ToggleEvent(); toggle.SetIsOnWithoutNotify(false);
    }
    [MenuItem("Tools/Cat Quest/Build Focus and Companion UI")]
    public static void Build()
    {
        var stage=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Focus/FocusPageStage.prefab");
        try {
            var fish=stage.GetComponentInChildren<FocusFishSpectators>(true);
            fish.firstRowCenter=new Vector2(-2,-5.8f);fish.spawnAreaSize=new Vector2(14,1.5f);fish.minimumSeparation=.9f;
            PrefabUtility.SaveAsPrefabAsset(stage,"Assets/Prefabs/Focus/FocusPageStage.prefab");
        } finally { PrefabUtility.UnloadPrefabContents(stage); }
        var root=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Canvas.prefab");
        TMP_FontAsset font; Sprite sprite;
        try {
            FocusPageSetup.ConfigurePage(root);
            var panel=root.GetComponentsInChildren<FocusPanelUI>(true).First(p=>!p.quickStart);
            font=panel.timer.font; sprite=panel.settingsSprite;
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/Canvas.prefab");
        } finally { PrefabUtility.UnloadPrefabContents(root); }
        CompanionWindowDesign.Build(font,sprite);
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var bootstrap=new GameObject("CatQuestBootstrap"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(bootstrap,scene);
        bootstrap.AddComponent<CatQuestBootstrap>().companionPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        EditorSceneManager.SaveScene(scene,BootstrapPath);EditorSceneManager.CloseScene(scene,true);
        var scenes=EditorBuildSettings.scenes.Where(s=>s.path!=BootstrapPath).ToList();scenes.Insert(0,new EditorBuildSettingsScene(BootstrapPath,true));EditorBuildSettings.scenes=scenes.ToArray();
        PlayerSettings.runInBackground=true;PlayerSettings.resizableWindow=true;
        AssetDatabase.SaveAssets(); EditorSceneManager.OpenScene("Assets/Scenes/CharacterHome.unity");
    }
}
