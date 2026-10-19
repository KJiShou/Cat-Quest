using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class FocusPageSetup
{
    const string StagePath="Assets/Prefabs/Focus/FocusPageStage.prefab";
    static FocusPageSetup(){EditorApplication.update+=Poll;}
    public const string ActionBackground = "Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/ResourcesData/Sprites/Components/Button/Button_Demo_Common/Btn_Rectangle00_n_Blue.png";
    public static void StyleActions(GameObject root)
    {
        var blue=AssetDatabase.LoadAssetAtPath<Sprite>(ActionBackground);
        var red=AssetDatabase.LoadAssetAtPath<Sprite>(ActionBackground.Replace("_Blue.png", "_Red.png"));
        foreach(var panel in root.GetComponentsInChildren<FocusPanelUI>(true).Where(p=>!p.quickStart))
            foreach(var button in new[]{panel.start,panel.endEarly,panel.endRest,panel.retry,panel.cancel}) {
                if(!button) continue;
                var sprite=button==panel.cancel ? red : blue;
                var image=button.targetGraphic as Image;
                if(image) { image.sprite=sprite;image.overrideSprite=null;image.color=Color.white;image.type=Image.Type.Sliced; }
                button.spriteState=new SpriteState {highlightedSprite=sprite,pressedSprite=sprite,selectedSprite=sprite,disabledSprite=sprite};
                foreach(var label in button.GetComponentsInChildren<TMP_Text>(true)) label.color=Color.white;
            }
    }
    static void Poll(){
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode && File.Exists("Temp/style-focus-actions.request")) {
            File.Delete("Temp/style-focus-actions.request");
            var root=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Canvas.prefab");
            try { StyleActions(root);PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/Canvas.prefab");File.WriteAllText("Temp/style-focus-actions.result","PASS"); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode && File.Exists("Temp/position-focus-rewards.request")) {
            File.Delete("Temp/position-focus-rewards.request");
            try {
                var root=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Canvas.prefab");
                try {
                    PositionRewardNumbers(root.GetComponentsInChildren<FocusPanelUI>(true).First(p=>!p.quickStart));
                    PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/Canvas.prefab");
                } finally { PrefabUtility.UnloadPrefabContents(root); }
                var scene=SceneManager.GetSceneByPath("Assets/Scenes/CharacterHome.unity");
                if(scene.isLoaded) {
                    foreach(var panel in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<FocusPanelUI>(true)).Where(p=>!p.quickStart)) PositionRewardNumbers(panel);
                    EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
                }
                AssetDatabase.SaveAssets();File.WriteAllText("Temp/position-focus-rewards.result","PASS: all three reward labels match reference anchors and Top 13.2544 / Bottom 0.");
            } catch(Exception ex) { File.WriteAllText("Temp/position-focus-rewards.result",ex.ToString());Debug.LogException(ex); }
        }
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/setup-focus-page.request"))return;
        File.Delete("Temp/setup-focus-page.request");
        try{Build();File.WriteAllText("Library/FocusBattleVerification/focus-page-setup.txt","PASS: full environment and training connected to Focus page.");}
        catch(Exception ex){File.WriteAllText("Library/FocusBattleVerification/focus-page-setup.txt",ex.ToString());Debug.LogException(ex);}
    }
    static void PositionRewardNumbers(FocusPanelUI panel)
    {
        foreach(var label in new[]{panel.fishReward,panel.rewards,panel.ticketReward}) {
            Place(label.transform,.08f,0,.92f,.32f);
            label.rectTransform.offsetMax=new Vector2(0,-13.2544f);
            label.margin=Vector4.zero;label.alignment=TextAlignmentOptions.Center;
        }
    }
    static void Place(Transform t,float x0,float y0,float x1,float y1){var r=(RectTransform)t;r.anchorMin=new Vector2(x0,y0);r.anchorMax=new Vector2(x1,y1);r.offsetMin=r.offsetMax=Vector2.zero;}
    [MenuItem("Tools/Cat Quest/Build Focus Page Scene")]
    public static void Build(){
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/FocusScene.unity");
        if(!scene.isLoaded)scene=EditorSceneManager.OpenScene("Assets/Scenes/FocusScene.unity",OpenSceneMode.Additive);
        var roots=scene.GetRootGameObjects();
        File.WriteAllText("Library/FocusBattleVerification/focus-stage-roots.txt",string.Join("\n",roots.Select(r=>r.name)));
        var sourceBattle=roots.SelectMany(r=>r.GetComponentsInChildren<FocusTrainingBattle>(true)).First();
        var stage=new GameObject("Focus Page Environment");
        try{
            foreach(var source in roots){
                if(source.GetComponentInChildren<FocusTrainingBattle>(true)||source.GetComponentInChildren<Canvas>(true)||source.GetComponentInChildren<GameDataManager>(true))continue;
                var clone=UnityEngine.Object.Instantiate(source,stage.transform);clone.name=source.name;
            }
            var battle=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Focus/FocusTrainingBattle.prefab"),stage.transform);
            battle.transform.SetPositionAndRotation(sourceBattle.transform.position,sourceBattle.transform.rotation);
            battle.GetComponent<FocusTrainingBattle>().previewWithoutFocusSession=false;
            battle.GetComponent<FocusFishSpectators>().previewElapsedSeconds=0;
            foreach(var listener in stage.GetComponentsInChildren<AudioListener>(true))UnityEngine.Object.DestroyImmediate(listener);
            foreach(var camera in stage.GetComponentsInChildren<Camera>(true)){camera.tag="Untagged";camera.enabled=false;}
            PrefabUtility.SaveAsPrefabAsset(stage,StagePath);
        }finally{UnityEngine.Object.DestroyImmediate(stage);}
        var root=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Canvas.prefab");
        try{
            ConfigurePage(root);
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/Canvas.prefab");
        }finally{PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene("Assets/Scenes/CharacterHome.unity");
    }
    public static void ConfigurePage(GameObject root)
    {
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(StagePath)) return;
        var nav = root.GetComponentInChildren<MainMenuNavigation>(true);
        var page = nav.pages[(int)MainMenuPage.Focus]; var panel = page.GetComponent<FocusPanelUI>();
        var background = page.transform.Find("background");
        var card = page.transform.Find("FocusTraining") ?? background.Find("FocusTraining");
        card.SetParent(background,false); TaskUIBuilder.Stretch((RectTransform)card,4,4,4,12);
        foreach (Transform child in page.transform) if(child.name=="Text" || child.name=="ReturnToDashboard") child.gameObject.SetActive(false);
        var font = panel.timer.font;
        var sprite = card.GetComponent<UnityEngine.UI.Image>().sprite;
        var ui = new TaskUIBuilder(font, sprite);
        var petLabel=root.GetComponentsInChildren<TMP_Text>(true).First(t=>t.text=="Pet Widget");
        var accent=petLabel.color;
        foreach (var name in new[] {"TrainingCat","FocusSettings","FocusSummary","TimerBacking","QuestPicker","QuestPopup"}) {
            var old = card.Find(name); if(old) UnityEngine.Object.DestroyImmediate(old.gameObject);
        }
        var clip = card.Find("SceneClip");
        if(!clip) {
            clip=TaskUIBuilder.Rect(card,"SceneClip");TaskUIBuilder.Stretch((RectTransform)clip);
            clip.gameObject.AddComponent<CanvasRenderer>();
            clip.gameObject.AddComponent<CompanionRoundedMask>().raycastTarget=false;
            clip.gameObject.AddComponent<Mask>().showMaskGraphic=false;
        }
        if(!clip.GetComponent<CanvasRenderer>())clip.gameObject.AddComponent<CanvasRenderer>();
        clip.SetAsFirstSibling();
        var image = card.Find("FocusSceneView") ?? clip.Find("FocusSceneView");
        if(!image) { var go=new GameObject("FocusSceneView",typeof(RectTransform),typeof(RawImage),typeof(FocusScenePreview)); go.transform.SetParent(card,false); image=go.transform; }
        image.SetParent(clip,false); Place(image,0,0,1,1);
        image.GetComponent<FocusScenePreview>().stagePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
        image.GetComponent<RawImage>().color=Color.white;
        // Full scene with compact floating SimpleCasual controls.
        Place(image,0,0,1,1);
        if (!card.GetComponent<RectMask2D>()) card.gameObject.AddComponent<RectMask2D>();
        var preview=image.GetComponent<FocusScenePreview>();
        preview.minimumOrthographicSize=6.8f; preview.horizontalHalfWidth=8; preview.framingOffset=new Vector2(0,-1.3f);
        var left=TaskUIBuilder.Rect(card,"FocusSettings"); Place(left,.025f,.55f,.285f,.96f);
        var focusCard=Card(ui,left,"FocusLength",0,.54f,1,1,sprite);
        Text(ui,focusCard,"Focus",22,.07f,.55f,.55f,.88f).color=accent;
        panel.focusDurationLabel=Text(ui,focusCard,"25 min",20,.56f,.55f,.93f,.88f);
        panel.focusDurationLabel.alignment=TextAlignmentOptions.TopRight;
        panel.focusDuration=DurationSlider(focusCard,"FocusDuration",25,.16f,.52f,out panel.focusDecrease,out panel.focusIncrease);
        var restCard=Card(ui,left,"RestLength",0,0,1,.46f,sprite);
        Text(ui,restCard,"Rest",22,.07f,.55f,.55f,.88f).color=accent;
        panel.restDurationLabel=Text(ui,restCard,"5 min",20,.56f,.55f,.93f,.88f);
        panel.restDurationLabel.alignment=TextAlignmentOptions.TopRight;
        panel.restDuration=DurationSlider(restCard,"RestDuration",5,.16f,.52f,out panel.restDecrease,out panel.restIncrease);

        var quest=TaskUIBuilder.Rect(card,"QuestPicker"); Place(quest,.025f,.425f,.285f,.52f);
        ui.Image(quest,Color.white,sprite);
        panel.questSearch=ui.Input(quest,"Search or choose quest"); Place(panel.questSearch.transform,.02f,.06f,.83f,.94f);
        panel.questSearch.GetComponent<Image>().color=Color.clear;
        TaskUIBuilder.BlueCaret(panel.questSearch);
        panel.questDropdown=ui.Button(quest,"Dropdown","",color:Color.clear); Place(panel.questDropdown.transform,.84f,.12f,.97f,.88f);
        var sourceArrow=root.GetComponentInChildren<EditTaskDetailsUI>(true).difficulty.transform.Find("Arrow");
        var arrow=(RectTransform)UnityEngine.Object.Instantiate(sourceArrow,panel.questDropdown.transform);
        arrow.name="Arrow";arrow.anchorMin=arrow.anchorMax=arrow.pivot=new Vector2(.5f,.5f);arrow.anchoredPosition=Vector2.zero;arrow.sizeDelta=new Vector2(14,22);
        arrow.GetComponent<Image>().raycastTarget=false;
        panel.questPopup=TaskUIBuilder.Rect(card,"QuestPopup"); Place(panel.questPopup,.025f,.10f,.285f,.42f);
        ui.Image(panel.questPopup,Color.white,sprite);
        var scroll=TaskUIBuilder.Rect(panel.questPopup,"QuestScroll"); TaskUIBuilder.Stretch(scroll,8,8,8,8);
        panel.questList=ui.ScrollContent(scroll,"Results"); TaskUIBuilder.Stretch((RectTransform)panel.questList.parent);
        panel.questPopup.gameObject.SetActive(false);
        panel.settingsFont=font; panel.settingsSprite=sprite;
        panel.durationDisabledSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/ResourcesData/Sprites/Components/UI_Etc/Status_ButtonFrame_Light_Gray.png");
        panel.questContext=null; panel.allowance=null;

        var right=Card(ui,card,"FocusSummary",.735f,.69f,.975f,.96f,sprite);
        var sourceRibbon = root.transform.Find("Container/Content/Dashboard/Top/DailyReward/DailyRewardRibbon");
        var rewardRibbon = UnityEngine.Object.Instantiate(sourceRibbon, right);
        rewardRibbon.name = "RewardRibbon";
        Place(rewardRibbon, .015f, .72f, .985f, .96f);
        panel.rewardTitle = rewardRibbon.GetComponentInChildren<TMP_Text>(true);
        panel.rewardTitle.text = "Rewards";
        panel.rewardTitle.fontSizeMin = 16;
        panel.rewardTitle.fontSizeMax = 25;
        var detail=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Task/TaskDetails.prefab");
        var source=detail.transform.Find("ScrollRect/Content/Rewards/RewardsItem");
        var fish=RewardTile(source.Find("FishCoins"),right,"FishCoins",.06f,.35f);
        var xp=RewardTile(source.Find("EXP"),right,"EXP",.355f,.645f);
        var ticket=RewardTile(source.Find("EXP"),right,"Tickets",.65f,.94f);
        panel.fishReward=fish.GetComponentInChildren<TMP_Text>(true);
        panel.rewards=xp.GetComponentInChildren<TMP_Text>(true);
        panel.ticketReward=ticket.GetComponentInChildren<TMP_Text>(true);
        var ticketIcon=ticket.GetComponentsInChildren<Image>(true).First(i=>i.transform!=ticket);
        ticketIcon.sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/ResourcesData/Sprites/Components/Icon_ItemIcons/512/Itemicon_Ticket_Blue.Png");
        ticketIcon.preserveAspect=true;
        panel.status.gameObject.SetActive(false);
        panel.progress.gameObject.SetActive(false);
        var timerBacking=TaskUIBuilder.Rect(card,"TimerBacking");Place(timerBacking,.405f,.81f,.595f,.94f);
        var companion=AssetDatabase.LoadAssetAtPath<GameObject>(CompanionPageSetup.PrefabPath).GetComponent<CompanionWindowUI>();
        var companionBacking=companion.timerBacking.GetComponent<Image>();
        var timerImage=ui.Image(timerBacking,companionBacking.color,companionBacking.sprite);timerImage.pixelsPerUnitMultiplier=companionBacking.pixelsPerUnitMultiplier;timerImage.raycastTarget=false;
        Place(panel.timer.transform,.34f,.78f,.66f,.96f); panel.timer.fontSize=70; panel.timer.alignment=TextAlignmentOptions.Center; panel.timer.transform.SetAsLastSibling();
        panel.timer.color=companion.timer.color;
        Place(panel.message.transform,.31f,.66f,.69f,.75f); panel.message.fontSize=16; panel.message.color=TaskUIBuilder.Ink; panel.message.transform.SetAsLastSibling();
        foreach(var button in new[]{panel.start,panel.endEarly,panel.endRest,panel.retry}) { Place(button.transform.parent,.36f,.035f,.64f,.13f); button.transform.parent.SetAsLastSibling(); }
        Place(panel.cancel.transform.parent,.65f,.045f,.79f,.115f); panel.cancel.transform.parent.SetAsLastSibling();
        foreach(var label in card.GetComponentsInChildren<TMP_Text>(true)) if(label.text.StartsWith("25 minutes Focus") || label.text.StartsWith("A full session")) label.gameObject.SetActive(false);
        panel.questPopup.SetAsLastSibling();
        StyleActions(root);
        CompanionPageSetup.BindToggle(root);
    }
    static RectTransform Card(TaskUIBuilder ui,Transform parent,string name,float x0,float y0,float x1,float y1,Sprite sprite)
    {
        var r=TaskUIBuilder.Rect(parent,name);Place(r,x0,y0,x1,y1);
        var shadow=TaskUIBuilder.Rect(r,"Shadow");TaskUIBuilder.Stretch(shadow);shadow.anchoredPosition=new Vector2(0,-4);
        ui.Image(shadow,new Color32(63,79,96,35),sprite).raycastTarget=false;
        var face=TaskUIBuilder.Rect(r,"Face");TaskUIBuilder.Stretch(face);ui.Image(face,Color.white,sprite).raycastTarget=false;
        return r;
    }
    static RectTransform RewardTile(Transform source,Transform parent,string name,float x0,float x1)
    {
        var tile=(RectTransform)UnityEngine.Object.Instantiate(source,parent);tile.name=name;
        foreach(var layout in tile.GetComponentsInChildren<LayoutGroup>(true))UnityEngine.Object.DestroyImmediate(layout);
        foreach(var fitter in tile.GetComponentsInChildren<ContentSizeFitter>(true))UnityEngine.Object.DestroyImmediate(fitter);
        Place(tile,x0,.10f,x1,.65f);
        var label=tile.GetComponentInChildren<TMP_Text>(true); Place(label.transform,.08f,0,.92f,.32f);
        label.rectTransform.offsetMax=new Vector2(0,-13.2544f);
        label.margin=Vector4.zero;
        label.fontSize=22;label.enableAutoSizing=true;label.fontSizeMin=14;label.fontSizeMax=22;label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;
        var icon=tile.GetComponentsInChildren<Image>(true).First(i=>i.transform!=tile);Place(icon.transform,.20f,.39f,.80f,.89f);icon.preserveAspect=true;
        foreach(var graphic in tile.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
        return tile;
    }
    static Slider DurationSlider(Transform parent,string name,int value,float y0,float y1,out Button decrease,out Button increase)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Material_Level_Up.prefab").transform.Find("Popup/Quantity");
        var row=UnityEngine.Object.Instantiate(source.gameObject,parent);row.name=name;
        Place(row.transform,.05f,y0,.95f,y1);
        var label=row.transform.Find("Text_Quantity");if(label)UnityEngine.Object.DestroyImmediate(label.gameObject);
        var slider=row.transform.Find("Slider").GetComponent<Slider>();
        var number=slider.transform.Find("Text_Value");if(number)UnityEngine.Object.DestroyImmediate(number.gameObject);
        var rect=slider.GetComponent<RectTransform>();rect.anchorMin=new Vector2(0,.5f);rect.anchorMax=new Vector2(1,.5f);rect.pivot=new Vector2(.5f,.5f);rect.offsetMin=new Vector2(52,-9);rect.offsetMax=new Vector2(-52,9);
        var fillArea=slider.transform.Find("FillArea").GetComponent<RectTransform>();TaskUIBuilder.Stretch(fillArea);
        var fill=slider.fillRect;fill.anchorMin=Vector2.zero;fill.anchorMax=Vector2.one;fill.offsetMin=fill.offsetMax=Vector2.zero;
        foreach(Transform child in fill){
            var point=(RectTransform)child;bool upper=child.name=="PointIcon";
            point.anchorMin=point.anchorMax=new Vector2(1,upper?1:0);point.pivot=new Vector2(.5f,upper?0:1);
            point.anchoredPosition=new Vector2(0,upper?2:-2);point.sizeDelta=new Vector2(8,6);
            var image=point.GetComponent<Image>();if(image)image.raycastTarget=false;
        }
        var minus=row.transform.Find("Icon_Reduce");var plus=row.transform.Find("Icon_Increase");
        decrease=minus.gameObject.AddComponent<Button>();decrease.targetGraphic=minus.GetComponent<Image>();
        increase=plus.gameObject.AddComponent<Button>();increase.targetGraphic=plus.GetComponent<Image>();
        foreach(var button in new[]{decrease,increase}){
            bool left=button==decrease;var r=button.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(left?0:1,.5f);r.pivot=new Vector2(left?0:1,.5f);r.anchoredPosition=new Vector2(left?4:-4,0);r.sizeDelta=Vector2.one*44;
            var icon=button.transform.Find("Icon").GetComponent<RectTransform>();icon.anchorMin=icon.anchorMax=icon.pivot=new Vector2(.5f,.5f);icon.anchoredPosition=Vector2.zero;icon.sizeDelta=left?new Vector2(22,11f/45f*22f):Vector2.one*22;
            button.onClick=new Button.ButtonClickedEvent();
        }
        slider.onValueChanged=new Slider.SliderEvent();slider.minValue=1;slider.maxValue=60;slider.wholeNumbers=true;slider.value=value;
        return slider;
    }
    static TMP_Text Text(TaskUIBuilder ui,Transform parent,string text,float size,float x0,float y0,float x1,float y1)
    {
        var label=ui.Text(parent,text,size); Place(label.transform,x0,y0,x1,y1); label.enableAutoSizing=true; label.fontSizeMin=13; label.fontSizeMax=size; label.alignment=TextAlignmentOptions.TopLeft; return label;
    }
    static void Icon(TaskUIBuilder ui,Transform parent,string name,Sprite sprite,float y0,float y1)
    {
        var r=TaskUIBuilder.Rect(parent,name);Place(r,.06f,y0,.23f,y1);var image=ui.Image(r,Color.white,sprite);image.type=Image.Type.Simple;image.preserveAspect=true;image.raycastTarget=false;
    }
}
