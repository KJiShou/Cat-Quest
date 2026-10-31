using System;
using System.IO;
using System.Linq;
using UnityEditor;

// Retains Windows packaging only; authored UI and bootstrap assets are edited directly.
[InitializeOnLoad]
public static class CompanionPageSetup
{
    static CompanionPageSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
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
}
