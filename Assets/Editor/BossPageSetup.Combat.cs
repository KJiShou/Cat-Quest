using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static partial class BossPageSetup
{
    const string Monsters="Assets/Packages/2D SD Monster Pack/";
    static void ConfigureCombat(BossPageUI binding,RectTransform arena,RectTransform challenge,RectTransform bossHealth)
    {
        var actors=arena.gameObject.AddComponent<BossBattleActors>();binding.actors=actors;
        actors.catController=Controller("CatCombat","Assets/Packages/Character/Spine 2D Ninja Cats Character Set/Characters/Animations/Ninja/",new[]{"ninja-idle","ninja-run","ninja-slash","ninja-die"});
        actors.bossController=Controller("BossCombat",Monsters+"Animations/Bigguy/",new[]{"idle","walk","attack-smash","die"});
        actors.orcPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Monsters+"Prefabs/Orc.prefab");
        actors.demonPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Monsters+"Prefabs/Demon.prefab");
        binding.bossBar=bossHealth.GetComponent<Slider>();
        binding.bossTitle=bossHealth.Find("TextFrame/Text_Title").GetComponent<TMP_Text>();
        binding.bossValue=bossHealth.Find("FillArea/Text_Value").GetComponent<TMP_Text>();
        binding.action=challenge.gameObject.AddComponent<Button>();binding.action.targetGraphic=challenge.GetComponent<Image>();
        binding.actionLabel=challenge.GetComponentInChildren<TMP_Text>();
        // Prototype on the main Challenge/Stop button only.
        var buttonBackground=challenge.GetComponent<Image>();
        var buttonVisual=Panel(challenge,"ButtonVisual",0,0,1,1,Color.white);
        var visualImage=buttonVisual.GetComponent<Image>();
        visualImage.sprite=buttonBackground.sprite;visualImage.type=buttonBackground.type;
        visualImage.pixelsPerUnitMultiplier=buttonBackground.pixelsPerUnitMultiplier;
        buttonBackground.color=Color.clear;
        binding.actionLabel.transform.SetParent(buttonVisual,false);
        var shine=Panel(buttonVisual,"HoverShine",0,0,1,1,new Color(1,1,1,0)).GetComponent<Image>();
        shine.sprite=visualImage.sprite;shine.type=visualImage.type;
        shine.pixelsPerUnitMultiplier=visualImage.pixelsPerUnitMultiplier;
        binding.actionLabel.transform.SetAsLastSibling();
        binding.action.transition=Selectable.Transition.None;
        var juice=challenge.gameObject.AddComponent<JuicyButton>();juice.visual=buttonVisual;juice.shine=shine;
        binding.stageButton=Control(arena,"Stages","Stage",.025f,.87f,.16f,.96f);
        binding.message=Label(arena,"",19,.25f,.145f,.75f,.25f,Color.white);binding.message.alignment=TextAlignmentOptions.Center;binding.message.textWrappingMode=TextWrappingModes.Normal;
        binding.catDamage=DamageText(arena,"CatDamage");binding.bossDamage=DamageText(arena,"BossDamage");
        var overlay=Panel(arena,"EntryConfirmation",0,0,1,1,new Color(0,.08f,.16f,.45f));binding.confirmation=overlay.gameObject;
        var modal=Panel(overlay,"Dialog",.29f,.27f,.71f,.73f,Color.white);
        binding.confirmText=Label(modal,"",22,.06f,.31f,.94f,.94f,TaskUIBuilder.Ink);binding.confirmText.alignment=TextAlignmentOptions.Center;binding.confirmText.textWrappingMode=TextWrappingModes.Normal;
        binding.confirm=Control(modal,"Confirm","Confirm",.06f,.08f,.48f,.26f);
        binding.cancel=Control(modal,"Cancel","Cancel",.52f,.08f,.94f,.26f,true);
        overlay.gameObject.SetActive(false);
        var picker=Panel(arena,"StageSelection",0,0,1,1,new Color32(240,248,255,255));binding.stagePicker=picker.gameObject;
        Label(picker,"Choose Stage",28,.04f,.85f,.7f,.97f,TaskUIBuilder.Blue);
        binding.closeStages=Control(picker,"CloseStages","Back",.82f,.87f,.96f,.96f);
        var reference=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Stage.prefab");
        var picture=reference.transform.Find("ScrollRect/Content/StageList/StagePIcture");
        binding.stageChoices=new Button[3];binding.stageCaptions=new TMP_Text[3];binding.stageLocks=new GameObject[3];
        binding.masteryBars=new Slider[3];binding.masteryValues=new TMP_Text[3];
        string[] names={"Cyclope","Orc","Demon"};
        for(int i=0;i<3;i++) {
            var card=Panel(picker,"Stage"+(i+1),.035f+i*.325f,.20f,.315f+i*.325f,.80f,Color.white);
            var frame=(RectTransform)UnityEngine.Object.Instantiate(picture,card);frame.name="BossPortrait";
            frame.anchorMin=new Vector2(.06f,.31f);frame.anchorMax=new Vector2(.94f,.96f);frame.offsetMin=frame.offsetMax=Vector2.zero;frame.localScale=Vector3.one;
            var aspect=frame.gameObject.AddComponent<AspectRatioFitter>();aspect.aspectMode=AspectRatioFitter.AspectMode.FitInParent;aspect.aspectRatio=1;
            // Constrain the fitted hexagon to its own portrait area, above the caption.
            var area=Rect(card,"PortraitArea",.06f,.38f,.94f,.96f);frame.SetParent(area,false);TaskUIBuilder.Stretch(frame);
            var mask=(RectTransform)frame.Find("PictureFrmae_(Mask)");TaskUIBuilder.Stretch(mask,7,7,7,7);
            var photo=mask.Find("PictureSample").GetComponent<Image>();TaskUIBuilder.Stretch(photo.rectTransform);
            photo.sprite=Portrait(names[i]);photo.preserveAspect=true;photo.color=Color.white;
            var shade=Rect(mask,"LockedOverlay",0,0,1,1);
            ui.Image(shade,new Color(0,0,0,.55f)).raycastTarget=false;
            LockIcon(shade,96);
            binding.stageLocks[i]=shade.gameObject;shade.gameObject.SetActive(i>0);
            binding.stageCaptions[i]=Label(card,"Stage "+(i+1),20,.04f,.20f,.96f,.37f,TaskUIBuilder.Ink);
            binding.stageCaptions[i].textWrappingMode=TextWrappingModes.Normal;binding.stageCaptions[i].alignment=TextAlignmentOptions.Center;
            var progress=Panel(card,"EquipmentProgress",.08f,.055f,.92f,.185f,new Color32(235,247,255,255));
            var badge=Panel(progress,"ArmorBadge",.015f,.08f,.22f,.92f,new Color32(198,236,255,255));
            var icon=Rect(badge,"ArmorIcon",.06f,.06f,.94f,.94f);
            var armor=ui.Image(icon,Color.white,AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/ResourcesData/Sprites/Components/Icon_ItemIcons/512/Itemicon_Equipment_Armor.Png"));
            armor.type=Image.Type.Simple;armor.preserveAspect=true;
            binding.masteryValues[i]=Label(progress,"0/5",20,.27f,.47f,.95f,.96f,TaskUIBuilder.Blue);
            binding.masteryValues[i].alignment=TextAlignmentOptions.Center;
            var track=Panel(progress,"Track",.27f,.14f,.95f,.39f,new Color32(200,223,242,255));
            var fill=Panel(track,"Fill",0,0,1,1,TaskUIBuilder.Blue);
            var bar=track.gameObject.AddComponent<Slider>();bar.fillRect=fill;bar.minValue=0;bar.maxValue=5;bar.wholeNumbers=true;bar.interactable=false;bar.transition=Selectable.Transition.None;bar.SetValueWithoutNotify(0);
            binding.masteryBars[i]=bar;
            binding.stageChoices[i]=card.gameObject.AddComponent<Button>();binding.stageChoices[i].targetGraphic=card.GetComponent<Image>();
        }
        binding.masterChoice=Control(picker,"Master","Master",.36f,.055f,.64f,.15f);
        binding.masterCaption=binding.masterChoice.GetComponentInChildren<TMP_Text>();
        var masterLock=LockIcon(binding.masterChoice.transform,34);
        masterLock.anchorMin=masterLock.anchorMax=new Vector2(.08f,.5f);
        binding.masterLock=masterLock.gameObject;
        binding.masterCaption.rectTransform.offsetMin=new Vector2(48,4);
        picker.gameObject.SetActive(false);
        ConfigureResults(binding,arena);
    }
    static void ConfigureResults(BossPageUI binding,Transform parent)
    {
        binding.resultPanels=new GameObject[2];binding.resultValues=new TMP_Text[2];binding.resultDetails=new TMP_Text[2];binding.resultContinue=new Button[2];
        string[] names={"Victory","Defeat"};
        for(int i=0;i<2;i++) {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Play_Result_"+names[i]+"_Common.prefab");
            var panel=UnityEngine.Object.Instantiate(source,parent);panel.name=names[i]+"Result";
            var rect=(RectTransform)panel.transform;TaskUIBuilder.Stretch(rect);rect.localScale=Vector3.one;
            var popup=(RectTransform)rect.Find("Popup");popup.anchoredPosition=new Vector2(0,40);popup.localScale=Vector3.one*.65f;
            var dim=rect.Find("ScreenDim").GetComponent<Image>();dim.color=new Color(0,.025f,.07f,.72f);
            var reward=(RectTransform)popup.Find("Frame/TextFrame");reward.sizeDelta=new Vector2(430,86);
            reward.Find("Icon_Trophy").gameObject.SetActive(false);
            var value=reward.Find("Text_Value").GetComponent<TMP_Text>();TaskUIBuilder.Stretch(value.rectTransform,12,12);
            value.text="";value.fontSize=30;value.enableAutoSizing=true;value.fontSizeMin=18;value.fontSizeMax=30;value.alignment=TextAlignmentOptions.Center;
            binding.resultValues[i]=value;
            var detail=Label(rect,"",20,.2f,.23f,.8f,.30f,Color.white);detail.alignment=TextAlignmentOptions.Center;
            binding.resultDetails[i]=detail;
            binding.resultContinue[i]=Control(rect,"Continue","Continue",.36f,.105f,.64f,.195f);
            binding.resultPanels[i]=panel;panel.SetActive(false);
        }
    }
    static RectTransform LockIcon(Transform parent,float size)
    {
        var rect=Rect(parent,"Lock",.5f,.5f,.5f,.5f);rect.sizeDelta=Vector2.one*size;
        var image=ui.Image(rect,Color.white,AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/ResourcesData/Sprites/Components/Icon_ItemIcons/128/Itemicon_Lock.Png"));
        image.type=Image.Type.Simple;image.preserveAspect=true;image.raycastTarget=false;return rect;
    }
    static TMP_Text DamageText(Transform arena,string name)
    {
        var label=Label(arena,"",30,.5f,.5f,.5f,.5f,Color.white);label.name=name;label.rectTransform.sizeDelta=new Vector2(140,60);label.alignment=TextAlignmentOptions.Center;
        label.fontStyle=FontStyles.Bold;label.gameObject.SetActive(false);return label;
    }
    static Button Control(Transform parent,string name,string caption,float x0,float y0,float x1,float y1,bool red=false)
    {
        var rect=Panel(parent,name,x0,y0,x1,y1,Color.white);var image=rect.GetComponent<Image>();
        image.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(red?FocusPageSetup.ActionBackground.Replace("_Blue.png","_Red.png"):FocusPageSetup.ActionBackground);image.pixelsPerUnitMultiplier=1;
        var b=rect.gameObject.AddComponent<Button>();b.targetGraphic=image;
        Label(rect,caption,21,.04f,.06f,.96f,.94f,Color.white).alignment=TextAlignmentOptions.Center;return b;
    }
    static RuntimeAnimatorController Controller(string name,string source,string[] clips)
    {
        string path=Folder+"/"+name+".controller";
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if(controller)return controller;
        controller=AnimatorController.CreateAnimatorControllerAtPath(path);
        var machine=controller.layers[0].stateMachine;
        string[] states={"Idle","Run","Attack","Die"};
        for(int i=0;i<clips.Length;i++) {
            var clip=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<AnimationClip>(source+clips[i]+".anim"));clip.name=clips[i];clip.events=Array.Empty<AnimationEvent>();
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=i!=3;AnimationUtility.SetAnimationClipSettings(clip,settings);
            AssetDatabase.AddObjectToAsset(clip,controller);
            var state=machine.AddState(states[i]);state.motion=clip;if(i==0)machine.defaultState=state;
        }
        EditorUtility.SetDirty(controller);return controller;
    }
    static Sprite Portrait(string name)
    {
        string path="Assets/Generated/Previews/BossPortrait_"+name+".png";
        if(!File.Exists(path)) {
            var scene=EditorSceneManager.NewPreviewScene();RenderTexture rt=null;Texture2D texture=null;
            var previous=RenderTexture.active;
            try {
                var obj=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Monsters+"Prefabs/"+name+".prefab"));SceneManager.MoveGameObjectToScene(obj,scene);
                obj.GetComponentInChildren<Animator>().Update(0);
                foreach(var renderer in obj.GetComponentsInChildren<SpriteRenderer>())renderer.sharedMaterial=AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
                var renderers=obj.GetComponentsInChildren<SpriteRenderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                var cameraObject=new GameObject("Portrait Camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var cam=cameraObject.AddComponent<Camera>();cam.scene=scene;cam.orthographic=true;cam.orthographicSize=Mathf.Max(bounds.extents.y,bounds.extents.x)*1.15f;cam.aspect=1;
                cam.transform.position=new Vector3(bounds.center.x,bounds.center.y,-20);cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.88f,.96f,1,1);
                rt=new RenderTexture(512,512,24);cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
                texture=new Texture2D(512,512,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,512,512),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
            }finally{RenderTexture.active=previous;if(rt){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}if(texture)UnityEngine.Object.DestroyImmediate(texture);EditorSceneManager.ClosePreviewScene(scene);}
            AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
