using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad] public static class AppUpdateBuildVerification
{
    static AppUpdateBuildVerification(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Temp/build-app-updates.request"))return;
        File.Delete("Temp/build-app-updates.request");
        string prior=PlayerSettings.bundleVersion;
        byte[] priorSettings=File.ReadAllBytes("ProjectSettings/ProjectSettings.asset");
        string folder="Builds/UpdateVerification/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        try
        {
            foreach(string version in new[]{"1.2","1.2.1"})
            {
                PlayerSettings.bundleVersion=version;
                string output=folder+"/"+version+"/CatQuest.exe";Directory.CreateDirectory(Path.GetDirectoryName(output));
                var report=BuildPipeline.BuildPlayer(EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),output,BuildTarget.StandaloneWindows64,BuildOptions.Development);
                if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Windows build failed: "+version+", errors="+report.summary.totalErrors);
            }
            File.WriteAllText("Temp/build-app-updates.result","PASS "+DateTime.UtcNow.ToString("O")+"\n"+Path.GetFullPath(folder));
        }
        catch(Exception e){File.WriteAllText("Temp/build-app-updates.result","FAIL "+e);Debug.LogException(e);}
        finally{PlayerSettings.bundleVersion=prior;File.WriteAllBytes("ProjectSettings/ProjectSettings.asset",priorSettings);}
    }
}
