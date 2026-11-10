using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

[InitializeOnLoad]
public static class ButtonPagesVerification
{
    const string Flag="CatQuest.ButtonPages",Folder="Library/ButtonPagesVerification";
    static IEnumerator run;static double next;static int checkedGraphics,checkedButtons;
    static RewardScreenshotCapture capture;
    const System.Reflection.BindingFlags Hidden=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
    static EditorWindow View=>(EditorWindow)typeof(SettingsPlayVerification).GetProperty("GameView",Hidden).GetValue(null);
    static System.Reflection.PropertyInfo Size=>(System.Reflection.PropertyInfo)typeof(SettingsPlayVerification).GetProperty("SizeProperty",Hidden).GetValue(null);
    static object Group=>typeof(SettingsPlayVerification).GetMethod("GameViewGroup",Hidden).Invoke(null,null);
    static int Count(string method)=>(int)Group.GetType().GetMethod(method).Invoke(Group,null);
    static void Resolution(int width,int height)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",Hidden).Invoke(null,new object[]{width,height});
    static void Restore(){
        run=null;
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",SessionState.GetBool(Flag+".hadStore",false)?SessionState.GetString(Flag+".store",""):null);
        Environment.SetEnvironmentVariable("CATQUEST_AI_TEST_DIRECTORY",SessionState.GetBool(Flag+".hadAi",false)?SessionState.GetString(Flag+".ai",""):null);
        UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Flag+".scene",""));
        try{Size.SetValue(View,SessionState.GetInt(Flag+".size",0));for(int i=Count("GetCustomCount")-1;i>=SessionState.GetInt(Flag+".custom",Count("GetCustomCount"));i--)Group.GetType().GetMethod("RemoveCustomSize").Invoke(Group,new object[]{Count("GetBuiltinCount")+i});}
        finally{SessionState.SetBool(Flag,false);}
    }
    static readonly List<string> failures=new List<string>();
    static ButtonPagesVerification(){EditorApplication.update+=Poll;EditorApplication.playModeStateChanged+=s=>{
        if(!SessionState.GetBool(Flag,false))return;
        if(s==PlayModeStateChange.EnteredPlayMode){run=Run();next=EditorApplication.timeSinceStartup+2;}
        if(s==PlayModeStateChange.EnteredEditMode)Restore();
    };}
    static void Poll(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        bool dashboardOnly=File.Exists("Temp/verify-button-pages-dashboard.request");
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&(dashboardOnly||File.Exists("Temp/verify-button-pages.request"))){
            SessionState.SetBool(Flag+".dashboardOnly",dashboardOnly);
            if(dashboardOnly)File.Delete("Temp/verify-button-pages-dashboard.request");
            File.Delete("Temp/verify-button-pages.request");Directory.CreateDirectory(Folder);SessionState.SetBool(Flag,true);
            File.WriteAllText(Folder+"/result.txt","RUNNING "+DateTime.UtcNow.ToString("O"));
            SessionState.SetString(Flag+".deadline",DateTime.UtcNow.AddMinutes(6).ToString("O"));
            SessionState.SetInt(Flag+".size",(int)Size.GetValue(View));SessionState.SetInt(Flag+".custom",Count("GetCustomCount"));
            var priorStore=Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");SessionState.SetBool(Flag+".hadStore",priorStore!=null);SessionState.SetString(Flag+".store",priorStore??"");
            var priorAi=Environment.GetEnvironmentVariable("CATQUEST_AI_TEST_DIRECTORY");SessionState.SetBool(Flag+".hadAi",priorAi!=null);SessionState.SetString(Flag+".ai",priorAi??"");
            SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene));
            UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");
            var save=Path.GetFullPath(Folder+"/save-"+Guid.NewGuid().ToString("N")+".json");
            File.WriteAllText(save,"{\"version\":5,\"username\":\"Button verification\",\"experience\":10000,\"tickets\":5,\"tagsInitialized\":true,\"tutorial\":{\"version\":1,\"dismissed\":true,\"taskStep\":4}}");
            Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",save);
            Environment.SetEnvironmentVariable("CATQUEST_AI_TEST_DIRECTORY",Path.GetFullPath(Folder+"/ai-"+Guid.NewGuid().ToString("N")));
            EditorApplication.isPlaying=true;return;
        }
        if(SessionState.GetBool(Flag,false)&&DateTime.UtcNow>DateTime.Parse(SessionState.GetString(Flag+".deadline",DateTime.UtcNow.AddMinutes(1).ToString("O")))){failures.Add("FAIL: timeout");Finish();return;}
        // Keep the Game View rendering while an end-of-frame capture is pending,
        // including when the Editor is behind another application.
        if(run!=null&&EditorApplication.isPlaying){EditorApplication.QueuePlayerLoopUpdate();View.Repaint();}
        if(run==null||!EditorApplication.isPlaying||EditorApplication.timeSinceStartup<next||(capture&&capture.Pending))return;
        EditorApplication.QueuePlayerLoopUpdate();
        try{if(run.MoveNext())next=EditorApplication.timeSinceStartup+Convert.ToDouble(run.Current);else Finish();}
        catch(Exception e){failures.Add(e.ToString());Finish();}
    }
    static void Finish(){File.WriteAllText(Folder+"/result.txt",DateTime.UtcNow.ToString("O")+$"\nButtons: {checkedButtons}; graphic state checks: {checkedGraphics}\n"+(failures.Count==0?"PASS":string.Join("\n",failures)));run=null;EditorApplication.isPlaying=false;}
    class Sample{public Graphic graphic;public Vector3[] vertices;}
    static Vector3[] Vertices(Graphic g){
        if(g is TMP_SubMeshUI sub){var info=sub.GetComponentInParent<TMP_Text>().textInfo;var part=info.meshInfo.FirstOrDefault(m=>m.mesh==sub.mesh);return part.vertices==null?Array.Empty<Vector3>():part.vertices.Take(part.vertexCount).ToArray();}
        if(g is TMP_Text text)return text.textInfo.meshInfo.Length==0?Array.Empty<Vector3>():text.textInfo.meshInfo[0].vertices.Take(text.textInfo.meshInfo[0].vertexCount).ToArray();
        var mesh=g.canvasRenderer.GetMesh();return mesh?mesh.vertices:Array.Empty<Vector3>();
    }
    static void Check(string context,string state,List<Sample> samples){
        foreach(var sample in samples){var g=sample.graphic;if(!g||!g.isActiveAndEnabled)continue;var vertices=Vertices(g);if(vertices.Length!=sample.vertices.Length)continue;
            var owners=g.GetComponentsInParent<JuicyButton>().Where(x=>x.isActiveAndEnabled).ToArray();
            if(owners.Any(x=>x.visual))continue;
            float worst=0;
            for(int i=0;i<vertices.Length;i++){
                Vector3 expected=g.transform.TransformPoint(sample.vertices[i]);
                foreach(var owner in owners){var rect=(RectTransform)owner.transform;Vector3 pivot=rect.rect.center;var p=rect.InverseTransformPoint(expected);p=pivot+Vector3.Scale(p-pivot,new Vector3(owner.DrawScale.x,owner.DrawScale.y,1))+Vector3.up*owner.DrawOffset;expected=rect.TransformPoint(p);}
                worst=Mathf.Max(worst,Vector3.Distance(g.transform.InverseTransformPoint(expected),vertices[i]));
            }
            checkedGraphics++;if(worst>1)failures.Add(context+" "+state+" "+PathOf(g.transform)+" drift="+worst);
        }
    }
    static string PathOf(Transform t)=>t.parent?PathOf(t.parent)+"/"+t.name:t.name;
    static IEnumerator Run(){
        failures.Clear();checkedGraphics=checkedButtons=0;
        UnityEngine.SceneManagement.SceneManager.LoadScene("CharacterHome");yield return 2;
        typeof(DashboardRewardPlayVerification).GetMethod("Resolution",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{1280,720});yield return .5;
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();var service=GameDataManager.EnsureInstance().Tasks;
        if(!GameDataManager.Instance.SavePath.Contains("ButtonPagesVerification"))throw new Exception("Isolated save required");
        // A page can update navigation while its hierarchy is still inactive.
        var inactiveParent=new GameObject("Inactive navigation regression");inactiveParent.SetActive(false);
        try {
            var clone=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Button_Normal.prefab"),inactiveParent.transform);
            var button=clone.GetComponentInChildren<SidebarNavigationButton>(true);
            var original=button.targetGraphic.color;
            button.SetPageSelected(true);button.SetPageSelected(false);
            if(button.targetGraphic.color!=original)throw new Exception("Navigation lost its authored background before Awake");
            inactiveParent.SetActive(true);
            button.SetPageSelected(true);button.SetPageSelected(false);
            if(button.targetGraphic.color!=original)throw new Exception("Navigation lost its authored background after Awake");
        } finally {UnityEngine.Object.DestroyImmediate(inactiveParent);}
        service.Upsert(new QuestTask{title="Button alignment test",daily=true,scheduled=true,date=TaskService.DateKey(service.LocalNow)},null,out var id);
        if(!service.SaveTag(null,"Study","#5879FF",out var tagId))throw new Exception(service.LastError);
        var contexts=new List<(string name,Action open)>();
        for(int i=0;i<nav.pages.Length;i++){int index=i;contexts.Add((((MainMenuPage)i).ToString(),()=>{if(!nav.ShowPage((MainMenuPage)index))throw new Exception("Page inaccessible: "+(MainMenuPage)index);}));}
        var sidebar=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
        var expander=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(sidebar);
        contexts.Insert(1,("Dashboard-expanded",()=>{nav.ShowPage(MainMenuPage.Tasks);nav.ShowPage(MainMenuPage.Dashboard);expander.isOn=true;}));
        contexts.Insert(2,("Dashboard-collapsed",()=>{expander.isOn=false;}));
        foreach(TaskTab tab in Enum.GetValues(typeof(TaskTab))){var selected=tab;contexts.Add(("Tasks-"+tab,()=>{nav.ShowPage(MainMenuPage.Tasks);nav.taskSystem.SelectTab(selected);}));}
        contexts.Add(("Task-details",()=>{nav.taskSystem.SelectTab(TaskTab.Daily);nav.taskSystem.SelectTask(id);}));
        contexts.Add(("Task-edit",()=>typeof(TaskSystemUI).GetMethod("BeginEdit",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(nav.taskSystem,new object[]{service.Query(TaskTab.Daily).First()})));
        contexts.Add(("Task-date",()=>nav.taskSystem.editor.datePicker.Open()));
        contexts.Add(("Task-edit-close",()=>nav.taskSystem.CancelDraft()));
        contexts.Add(("Task-filter",()=>nav.taskSystem.filterButton.onClick.Invoke()));
        contexts.Add(("Boss-stages",()=>{typeof(TaskSystemUI).GetMethod("CloseModal",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(nav.taskSystem,null);nav.ShowPage(MainMenuPage.Boss);nav.pages[3].GetComponent<BossPageUI>().stageButton.onClick.Invoke();}));
        contexts.Add(("Boss-confirmation",()=>{var b=nav.pages[3].GetComponent<BossPageUI>();b.stagePicker.SetActive(false);b.confirmation.SetActive(true);}));
        contexts.Add(("Boss-victory",()=>{var b=nav.pages[3].GetComponent<BossPageUI>();b.confirmation.SetActive(false);b.resultPanels[0].SetActive(true);}));
        contexts.Add(("Boss-defeat",()=>{var b=nav.pages[3].GetComponent<BossPageUI>();b.resultPanels[0].SetActive(false);b.resultPanels[1].SetActive(true);}));
        contexts.Add(("Boss-equipment-confirm",()=>{nav.pages[3].GetComponent<BossPageUI>().resultPanels[1].SetActive(false);nav.pages[3].transform.Find("ConfirmEquipment").gameObject.SetActive(true);}));
        contexts.Add(("Equipment-confirm",()=>{nav.ShowPage(MainMenuPage.CatHome);nav.pages[(int)MainMenuPage.CatHome].transform.Find("ConfirmEquipment").gameObject.SetActive(true);}));
        contexts.Add(("Focus-running",()=>{nav.ShowPage(MainMenuPage.Focus);nav.pages[2].GetComponent<FocusPanelUI>().start.onClick.Invoke();}));
        contexts.Add(("Focus-cancelled",()=>nav.pages[2].GetComponent<FocusPanelUI>().cancel.onClick.Invoke()));
        var settings=UnityEngine.Object.FindFirstObjectByType<SettingsPanelUI>(FindObjectsInactive.Include);
        contexts.Add(("Focus-insights",()=>{nav.pages[(int)MainMenuPage.Focus].GetComponent<FocusPanelUI>().cancelConfirmation.Dismiss();nav.ShowPage(MainMenuPage.Focus);UnityEngine.Object.FindFirstObjectByType<FocusStatisticsUI>(FindObjectsInactive.Include).Show();}));
        contexts.Add(("Settings",()=>{UnityEngine.Object.FindFirstObjectByType<FocusStatisticsUI>(FindObjectsInactive.Include).Hide();settings.Open(nav.gameObject);}));
        contexts.Add(("Settings-tags",()=>settings.OpenTags()));
        contexts.Add(("Settings-delete-confirm",()=>settings.tagsPage.GetComponentsInChildren<SettingsTagRow>().First(r=>r.gameObject.activeInHierarchy).delete.onClick.Invoke()));
        contexts.Add(("Settings-tag-editor",()=>settings.EditTag(null)));
        contexts.Add(("Settings-account",()=>settings.OpenAccount()));
        contexts.Add(("Settings-AI",()=>settings.OpenAI()));
        contexts.Add(("Tutorial-help",()=>{settings.Close();UnityEngine.Object.FindFirstObjectByType<TutorialController>().OpenHelp();}));
        contexts.Add(("Chat-review",()=>{UnityEngine.Object.FindFirstObjectByType<TutorialController>().CloseHelp();nav.ShowPage(MainMenuPage.Chat);nav.pages[(int)MainMenuPage.Chat].GetComponent<AiChatPage>().OpenDraft(new AiTaskDraft{task=new QuestTask{title="Review button appearance"}});}));
        contexts.Add(("Chat-clear",()=>{var chat=nav.pages[(int)MainMenuPage.Chat].GetComponent<AiChatPage>();chat.cancelDraft.onClick.Invoke();chat.clear.onClick.Invoke();}));
        capture=new GameObject("Button audit screenshots").AddComponent<RewardScreenshotCapture>();
        foreach(var context in contexts.Where(c=>!SessionState.GetBool(Flag+".dashboardOnly",false)||c.name.StartsWith("Dashboard"))){
            context.open();yield return .7;
            File.WriteAllText(Folder+"/progress.txt",context.name);
            foreach(var width in new[]{1920,1280}){Resolution(width,width==1920?1080:720);yield return .5;capture.Begin(Path.GetFullPath(Folder+"/"+context.name+"-"+width+".png"));yield return .2;}
            var buttons=Selectable.allSelectablesArray.OfType<Button>().Where(b=>b.gameObject.activeInHierarchy).ToArray();
            foreach(var b in buttons) {
                if(!b.GetComponent<JuicyButton>())failures.Add(context.name+" missing JuicyButton: "+PathOf(b.transform));
                var animator=b.GetComponent<Animator>();
                if(b.transition==Selectable.Transition.Animation || (animator && animator.runtimeAnimatorController && animator.runtimeAnimatorController.name=="Button_Nav"))
                    failures.Add(context.name+" legacy button animation: "+PathOf(b.transform));
            }
            var poses=buttons.Select(b=>(rect:(RectTransform)b.transform,scale:b.transform.localScale,position:((RectTransform)b.transform).anchoredPosition)).ToArray();
            var feedback=buttons.Select(b=>b.GetComponent<JuicyButton>()).Where(x=>x && x.isActiveAndEnabled).ToArray();checkedButtons+=buttons.Length;
            foreach(var f in feedback){f.OnPointerExit(new PointerEventData(EventSystem.current));f.OnDeselect(new BaseEventData(EventSystem.current));}yield return .6;
            var graphics=buttons.SelectMany(b=>b.GetComponentsInChildren<Graphic>()).Distinct().ToArray();
            var samples=graphics.Select(g=>new Sample{graphic=g,vertices=Vertices(g)}).ToList();
            foreach(var f in feedback)f.OnPointerEnter(new PointerEventData(EventSystem.current));yield return .5;
            Check(context.name,"hover",samples);capture.Begin(Path.GetFullPath(Folder+"/"+context.name+"-hover.png"));yield return .2;
            foreach(var f in feedback)f.OnPointerDown(new PointerEventData(EventSystem.current));yield return .5;
            Check(context.name,"pressed",samples);capture.Begin(Path.GetFullPath(Folder+"/"+context.name+"-pressed.png"));yield return .2;
            foreach(var f in feedback){f.OnPointerUp(new PointerEventData(EventSystem.current));f.OnPointerExit(new PointerEventData(EventSystem.current));}yield return .6;
            Check(context.name,"reset",samples);
            foreach(var f in feedback){f.OnSelect(new BaseEventData(EventSystem.current));}yield return .3;
            Check(context.name,"keyboard-selected",samples);
            foreach(var f in feedback){f.OnSubmit(new BaseEventData(EventSystem.current));f.SendMessage("OnApplicationFocus",false,SendMessageOptions.RequireReceiver);}
            yield return .2;Check(context.name,"focus-reset",samples);
            foreach(var f in feedback) {
                for(int repeat=0;repeat<8;repeat++) {
                    f.OnPointerDown(new PointerEventData(EventSystem.current));
                    f.OnPointerUp(new PointerEventData(EventSystem.current));
                }
                f.enabled=false;f.enabled=true;
                if(f.DrawScale!=Vector2.one || f.DrawOffset!=0)failures.Add(context.name+" disable did not reset feedback: "+PathOf(f.transform));
            }
            var interactable=buttons.Select(b=>b.interactable).ToArray();
            foreach(var b in buttons)b.interactable=false;
            foreach(var f in feedback){f.OnPointerEnter(new PointerEventData(EventSystem.current));f.OnPointerDown(new PointerEventData(EventSystem.current));f.OnSubmit(new BaseEventData(EventSystem.current));}
            yield return .5;
            foreach(var f in feedback)if(Vector2.Distance(f.DrawScale,Vector2.one)>.001f || Mathf.Abs(f.DrawOffset)>.001f)failures.Add(context.name+" disabled button animated: "+PathOf(f.transform));
            for(int i=0;i<buttons.Length;i++)buttons[i].interactable=interactable[i];
            foreach(var pose in poses)if(Vector3.Distance(pose.rect.localScale,pose.scale)>.001f || Vector2.Distance(pose.rect.anchoredPosition,pose.position)>.001f)
                failures.Add(context.name+" button hit area moved: "+PathOf(pose.rect));
        }
    }
}

