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
    static readonly List<string> failures=new List<string>();
    static ButtonPagesVerification(){EditorApplication.update+=Poll;EditorApplication.playModeStateChanged+=s=>{
        if(!SessionState.GetBool(Flag,false))return;
        if(s==PlayModeStateChange.EnteredPlayMode){run=Run();next=EditorApplication.timeSinceStartup+2;}
        if(s==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Flag,false);Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",null);UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Flag+".scene",""));run=null;}
    };}
    static void Poll(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists("Temp/verify-button-pages.request")){
            File.Delete("Temp/verify-button-pages.request");Directory.CreateDirectory(Folder);SessionState.SetBool(Flag,true);
            SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene));
            UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");
            Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",Path.GetFullPath(Folder+"/save-"+Guid.NewGuid().ToString("N")+".json"));EditorApplication.isPlaying=true;return;
        }
        if(run==null||!EditorApplication.isPlaying||EditorApplication.timeSinceStartup<next)return;
        EditorApplication.QueuePlayerLoopUpdate();
        try{if(run.MoveNext())next=EditorApplication.timeSinceStartup+Convert.ToDouble(run.Current);else Finish();}
        catch(Exception e){failures.Add(e.ToString());Finish();}
    }
    static void Finish(){File.WriteAllText(Folder+"/result.txt",$"Buttons: {checkedButtons}; graphic state checks: {checkedGraphics}\n"+(failures.Count==0?"PASS":string.Join("\n",failures)));run=null;EditorApplication.isPlaying=false;}
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
        service.Upsert(new QuestTask{title="Button alignment test",daily=true,scheduled=true,date=TaskService.DateKey(service.LocalNow)},null,out var id);
        var contexts=new List<(string name,Action open)>();
        for(int i=0;i<nav.pages.Length;i++){int index=i;contexts.Add((((MainMenuPage)i).ToString(),()=>nav.ShowPage((MainMenuPage)index)));}
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
        contexts.Add(("Focus-running",()=>{nav.ShowPage(MainMenuPage.Focus);nav.pages[2].GetComponent<FocusPanelUI>().start.onClick.Invoke();}));
        contexts.Add(("Focus-cancelled",()=>nav.pages[2].GetComponent<FocusPanelUI>().cancel.onClick.Invoke()));
        var capture=new GameObject("Button audit screenshots").AddComponent<RewardScreenshotCapture>();
        foreach(var context in contexts){
            context.open();yield return .7;
            var buttons=Selectable.allSelectablesArray.OfType<Button>().Where(b=>b.gameObject.activeInHierarchy).ToArray();
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
        }
    }
}

