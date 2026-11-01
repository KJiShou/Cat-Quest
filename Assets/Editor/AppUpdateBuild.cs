using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Every Windows build gets the same helper and build identity. Publishing remains explicit.
public sealed class AppUpdateBuild : IPostprocessBuildWithReport
{
    public int callbackOrder => 1000;
    public void OnPostprocessBuild(BuildReport report)
    {
        if(report.summary.platform!=BuildTarget.StandaloneWindows64) return;
        string output=Path.GetDirectoryName(Path.GetFullPath(report.summary.outputPath));
        string script=Path.GetFullPath("Tools/Updater/Build-Updater.ps1");
        var start=new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \""+script+"\" -OutputDirectory \""+output+"\"") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        using(var process=Process.Start(start))
        {
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(60000)) {process.Kill();throw new BuildFailedException("Updater compilation timed out.");}
            if(process.ExitCode!=0) throw new BuildFailedException(stdout.Result+stderr.Result);
        }
        File.WriteAllText(Path.Combine(output,"catquest-build.json"),JsonUtility.ToJson(new BuildIdentity {version=PlayerSettings.bundleVersion,development=(report.summary.options&BuildOptions.Development)!=0}));
    }
    [Serializable] public sealed class BuildIdentity { public string version; public bool development; }
}
