using System.IO;
using UnityEditor;

// A manual refresh command for editors that have deferred external script changes.
[InitializeOnLoad]
public static class VerificationCommands
{
    static VerificationCommands()
    {
        EditorApplication.update += Poll;
    }
    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists("Temp/read-verification-editor-state.request")) {
            File.Delete("Temp/read-verification-editor-state.request");
            var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            var size=view.GetType().GetProperty("selectedSizeIndex",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
            File.WriteAllLines("Temp/verification-editor-state.txt",new[]{
                System.DateTime.UtcNow.ToString("O"),
                "playing="+EditorApplication.isPlaying+"; transitioning="+EditorApplication.isPlayingOrWillChangePlaymode,
                "testStore="+(System.Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE")??"<unset>"),
                "startScene="+AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene),
                "gameViewSize="+size.GetValue(view),
                "equipmentActive="+SessionState.GetBool("CatQuest.EquipmentPlay",false),
                "equipmentPriorStore="+SessionState.GetString("CatQuest.EquipmentPlay.env","<missing>"),
                "settingsPriorStore="+SessionState.GetString("CatQuest.SettingsPlayVerification.store","<missing>"),
                "calendarPriorStore="+SessionState.GetString("CatQuest.TaskCalendarPlayVerification.store","<missing>")
            });
            return;
        }
        if (File.Exists("Temp/stop-verification-play.request")) {
            File.Delete("Temp/stop-verification-play.request");
            EditorApplication.isPlaying = false;
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists("Temp/reimport-ai-bindings.request")) {
            File.Delete("Temp/reimport-ai-bindings.request");
            ReimportAiBindings();
            return;
        }
        if (!File.Exists("Temp/refresh-verification-scripts.request")) return;
        File.Delete("Temp/refresh-verification-scripts.request");
        Refresh();
    }
    // Explicit repair/import action, deliberately separate from the read-only binding verifier.
    [MenuItem("Tools/Cat Quest/Reimport AI Binding Assets")]
    public static void ReimportAiBindings()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new System.InvalidOperationException("Reimport requires Edit Mode.");
        var paths=new[]{"Assets/Prefabs/AI/ChatTaskDraft.prefab","Assets/Prefabs/AI/Chat.prefab","Assets/Prefabs/Canvas.prefab"};
        var original=new System.Collections.Generic.Dictionary<string,byte[]>();
        try {
            foreach(string path in paths) {original[path]=File.ReadAllBytes(path);original[path+".meta"]=File.ReadAllBytes(path+".meta");}
            foreach(string path in paths)AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
            foreach(var item in original) {
                byte[] current=File.ReadAllBytes(item.Key);
                if(current.Length!=item.Value.Length)throw new System.InvalidOperationException("Import changed authored bytes: "+item.Key);
                for(int i=0;i<current.Length;i++)if(current[i]!=item.Value[i])throw new System.InvalidOperationException("Import changed authored bytes: "+item.Key);
            }
            File.WriteAllText("Temp/reimport-ai-bindings.result",System.DateTime.UtcNow.ToString("O")+" PASS explicit targeted imports; prefab/meta bytes unchanged. Binding verification must run separately.");
        } catch(System.Exception error) {
            File.WriteAllText("Temp/reimport-ai-bindings.result",System.DateTime.UtcNow.ToString("O")+" FAIL "+error);
        }
    }
    [MenuItem("Tools/Cat Quest/Refresh Verification Scripts")]
    public static void Refresh()
    {
        AssetDatabase.ImportAsset("Assets/Scripts", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
        AssetDatabase.ImportAsset("Assets/Editor", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
        UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
    }
}
