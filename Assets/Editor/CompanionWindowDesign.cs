using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public static class CompanionWindowDesign
{
    static readonly Color Ink = new Color32(29, 51, 70, 255);
    public static void Build(TMP_FontAsset font, Sprite panel)
    {
        var root = new GameObject("CompanionWindow", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CompanionWindowUI));
        try
        {
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var ui = new TaskUIBuilder(font, panel);var controller = root.GetComponent<CompanionWindowUI>();
            var window = TaskUIBuilder.Rect(root.transform,"Window");TaskUIBuilder.Stretch(window);
            window.gameObject.AddComponent<CompanionRoundedMask>().raycastTarget=false;window.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            controller.sceneFrame = TaskUIBuilder.Rect(window,"TrainingScene");controller.sceneFrame.gameObject.AddComponent<RawImage>();
            controller.preview=controller.sceneFrame.gameObject.AddComponent<FocusScenePreview>();
            controller.preview.stagePrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Focus/FocusPageStage.prefab");
            controller.preview.targetHeight=540;controller.preview.minimumOrthographicSize=6.8f;controller.preview.framingOffset=new Vector2(0,-1.3f);controller.preview.horizontalHalfWidth=8;
            controller.header=TaskUIBuilder.Rect(window,"Header");ui.Image(controller.header,new Color32(238,247,239,255));
            controller.title=Label(ui,controller.header,"Player",15);
            controller.title.enableAutoSizing=true;controller.title.fontSizeMin=10;controller.title.fontSizeMax=15;
            controller.dragHandle=TaskUIBuilder.Rect(controller.header,"DragHandle");ui.Image(controller.dragHandle,new Color(1,1,1,.001f));controller.dragHandle.gameObject.AddComponent<CompanionWindowHandle>();
            var popup=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Lobby_Popup_User_Info.prefab");
            var sourceClose=System.Array.Find(popup.GetComponentsInChildren<Button>(true),b=>b.name=="Button_Close");
            if(!sourceClose)throw new System.Exception("Source popup Close button is missing.");
            controller.close=Object.Instantiate(sourceClose,controller.header);controller.close.name="CloseWidget";
            controller.close.onClick=new Button.ButtonClickedEvent();
            var closeIcon=controller.close.transform.Find("Icon_Close").GetComponent<RectTransform>();
            closeIcon.anchoredPosition=Vector2.zero;closeIcon.sizeDelta=Vector2.one*(44f/90f*30f);
            controller.pinnedSprite=LoadWindowIcon("pin");
            controller.unpinnedSprite=LoadWindowIcon("pin-off");
            controller.pin=SpriteButton(ui,controller.header,"PinWindow",controller.pinnedSprite);
            controller.pinIcon=controller.pin.transform.Find("Icon").GetComponent<Image>();
            controller.pinIcon.color=new Color32(29,104,119,255);
            controller.minimize=SpriteButton(ui,controller.header,"MinimizeWindow",LoadWindowIcon("minus"));
            controller.timerBacking=TaskUIBuilder.Rect(window,"TimerBacking");ui.Image(controller.timerBacking,new Color32(245,252,244,195),panel).raycastTarget=false;
            controller.timer=Label(ui,controller.timerBacking,"25:00",46);TaskUIBuilder.Stretch(controller.timer.rectTransform);controller.timer.alignment=TextAlignmentOptions.Center;controller.timer.overflowMode=TextOverflowModes.Overflow;
            controller.controls=TaskUIBuilder.Rect(window,"SessionAction");controller.controlFade=controller.controls.gameObject.AddComponent<CanvasGroup>();controller.controlFade.alpha=0;controller.controlFade.blocksRaycasts=false;
            controller.start=Action(ui,controller.controls,"Start","Start focus");controller.end=Action(ui,controller.controls,"End","End focus early");controller.rest=Action(ui,controller.controls,"Rest","Finish rest");controller.retry=Action(ui,controller.controls,"Retry","Retry save");
            // Invisible hit areas on all four edges and corners, with OS resize cursors.
            for(int edge=1;edge<=8;edge++) {
                var r=TaskUIBuilder.Rect(window,"WindowEdge"+edge);ui.Image(r,new Color(1,1,1,.001f));var handle=r.gameObject.AddComponent<CompanionWindowHandle>();handle.resize=true;handle.resizeEdge=edge;
                controller.edges[edge-1]=r;
            }
            new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule)).transform.SetParent(root.transform,false);
            controller.ApplyLayout(false,false);PrefabUtility.SaveAsPrefabAsset(root,CompanionPageSetup.PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }
    static Sprite LoadWindowIcon(string name)
    {
        string path="Assets/Generated/Icons/WindowControls/"+name+".png";
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
        importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    static Button SpriteButton(TaskUIBuilder ui,Transform parent,string name,Sprite sprite)
    {
        var button=ui.Button(parent,name,"",color:new Color(1,1,1,.001f));
        var rect=TaskUIBuilder.Rect(button.transform,"Icon");
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=Vector2.one*19;
        var image=ui.Image(rect,Ink,sprite);image.type=Image.Type.Simple;image.preserveAspect=true;image.raycastTarget=false;
        return button;
    }
    static RectTransform Icon(Transform parent,string name,CompanionHeaderIcon.Icon kind)
    {
        var rect=TaskUIBuilder.Rect(parent,name);var graphic=rect.gameObject.AddComponent<CompanionHeaderIcon>();graphic.icon=kind;graphic.color=Ink;graphic.raycastTarget=false;return rect;
    }
    static TMP_Text Label(TaskUIBuilder ui,Transform parent,string text,int size)
    {
        var label=ui.Text(parent,text,size,Ink);label.enableAutoSizing=false;label.textWrappingMode=TextWrappingModes.NoWrap;label.overflowMode=TextOverflowModes.Ellipsis;return label;
    }
    static Button Action(TaskUIBuilder ui,Transform parent,string name,string caption)
    {
        var button=ui.Button(parent,name,caption,color:new Color32(223,245,238,245));var label=button.GetComponentInChildren<TMP_Text>();label.color=Ink;label.fontSize=15;label.enableAutoSizing=false;label.textWrappingMode=TextWrappingModes.NoWrap;return button;
    }
    static Button WindowButton(TaskUIBuilder ui,Transform parent,string name,CompanionHeaderIcon.Icon kind)
    {
        var button=ui.Button(parent,name,"",color:new Color(1,1,1,.001f));var icon=Icon(button.transform,"Icon",kind);TaskUIBuilder.Stretch(icon);return button;
    }
}
