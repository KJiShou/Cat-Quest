using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class TaskTabsVerification
{
    const string Flag="CatQuest.TaskTabsVerification";
    const string Folder="Library/TaskTabsVerification";
    static int step;
    static double next;
    static RewardScreenshotCapture capture;
    static readonly Vector2Int[] Sizes={new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)};
    static TaskTabsVerification() {
        EditorApplication.update+=Poll;
        EditorApplication.playModeStateChanged+=state=>{
            if(!SessionState.GetBool(Flag,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){step=0;next=EditorApplication.timeSinceStartup+3;}
            if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Flag,false);Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",null);}
        };
    }
    static void Bind(GameObject root) {
        var owner=root.GetComponentInChildren<TaskSystemUI>(true);
        var bar=owner.tabButtons[0].transform.parent;
        var layout=bar.GetComponent<TaskTabsLayout>()??bar.gameObject.AddComponent<TaskTabsLayout>();
        layout.owner=owner;layout.Apply();
    }
    static void Poll() {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists("Temp/setup-task-tabs.request")) {
            File.Delete("Temp/setup-task-tabs.request");
            var root=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Canvas.prefab");
            try{Bind(root);PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/Canvas.prefab");AssetDatabase.SaveAssets();File.WriteAllText("Temp/setup-task-tabs.result","PASS");}
            catch(Exception e){File.WriteAllText("Temp/setup-task-tabs.result",e.ToString());}
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        if(EditorApplication.isPlaying&&SessionState.GetBool(Flag,false)) {
            EditorApplication.QueuePlayerLoopUpdate();
            if(EditorApplication.timeSinceStartup<next||(capture&&capture.Pending))return;
            try{Tick();}catch(Exception e){File.WriteAllText(Folder+"/result.txt",e.ToString());EditorApplication.isPlaying=false;}
            return;
        }
        if(EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-task-tabs.request"))return;
        File.Delete("Temp/verify-task-tabs.request");Directory.CreateDirectory(Folder);
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",Path.GetFullPath(Folder+"/save-"+Guid.NewGuid().ToString("N")+".json"));
        SessionState.SetBool(Flag,true);EditorApplication.isPlaying=true;
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Tick() {
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        var owner=nav.taskSystem;
        var sidebar=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
        var toggle=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sidebar);
        if(step==0){Check(nav.ShowPage(MainMenuPage.Tasks),"Cannot open Tasks");}
        else if(step<=16) {
            int index=(step-1)/2;
            if(step%2==1) {
                var size=Sizes[index/2];
                typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{size.x,size.y});
                toggle.isOn=index%2==1;owner.SelectTab(TaskTab.History);
            } else {
                Canvas.ForceUpdateCanvases();
                float width=((RectTransform)owner.tabButtons[0].transform).rect.width;
                float font=owner.tabLabels[0].fontSize;
                var bar=(RectTransform)owner.tabButtons[0].transform.parent;
                Check(Mathf.Abs(bar.rect.height-(width<205?84:64))<.2f,"Tab header stretched vertically");
                foreach(var button in owner.tabButtons)Check(Mathf.Abs(((RectTransform)button.transform).rect.width-width)<.2f,"Unequal tab widths");
                foreach(var label in owner.tabLabels) {
                    Check(Mathf.Abs(label.fontSize-font)<.01f,"Tab fonts differ");
                    Check(!label.isTextOverflowing,"Tab text overflows: "+label.text);
                    var corners=new Vector3[4];label.rectTransform.GetWorldCorners(corners);
                    var parent=(RectTransform)label.transform.parent;
                    foreach(var point in corners){var local=parent.InverseTransformPoint(point);Check(local.x>=parent.rect.xMin-.5f&&local.x<=parent.rect.xMax+.5f&&local.y>=parent.rect.yMin-.5f&&local.y<=parent.rect.yMax+.5f,"Caption leaves tab bounds");}
                }
                if(!capture)capture=new GameObject("Task Tab Capture").AddComponent<RewardScreenshotCapture>();
                var size=Sizes[index/2];capture.Begin(Path.GetFullPath(Folder+"/tabs-"+size.x+"x"+size.y+(index%2==1?"-expanded":"-collapsed")+".png"));
            }
        } else {
            owner.SelectTab(TaskTab.Today);
            File.WriteAllText(Folder+"/result.txt","PASS: equal tab widths and font sizes, full captions within bounds, History selection at 1920x1080, 1280x720, 1024x768 and 2560x1080 with both expanded and collapsed sidebar.");
            EditorApplication.isPlaying=false;return;
        }
        step++;next=EditorApplication.timeSinceStartup+1;
    }
}
