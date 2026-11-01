using System;
using System.IO;
using System.Linq;
using CatQuest.Updates;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Explicit production build request; does not author UI or publish a release.
[InitializeOnLoad] public static class AppUpdateReleaseBuild
{
    [Serializable] public sealed class Request { public string version, outputDirectory; public bool restoreVersion = true; }
    const string RequestPath = "Temp/build-app-release.request";
    static AppUpdateReleaseBuild() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
        string json = File.ReadAllText(RequestPath); File.Delete(RequestPath);
        string prior = PlayerSettings.bundleVersion;
        byte[] settings = File.ReadAllBytes("ProjectSettings/ProjectSettings.asset");
        bool restore = true;
        try
        {
            var request = JsonUtility.FromJson<Request>(json);
            UpdateRules.VersionOf(request.version);
            if (!Path.IsPathRooted(request.outputDirectory)) throw new ArgumentException("An absolute output directory is required.");
            string output = Path.GetFullPath(request.outputDirectory);
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Use an empty production build directory.");
            Directory.CreateDirectory(output);
            PlayerSettings.bundleVersion = request.version;
            var report = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(), Path.Combine(output,"CatQuest.exe"), BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report == null || report.summary.result != BuildResult.Succeeded) throw new Exception("Release build failed. See the Editor log.");
            restore = request.restoreVersion;
            File.WriteAllText("Temp/build-app-release.result", "PASS " + DateTime.UtcNow.ToString("O") + "\nversion=" + request.version + "\ndevelopment=false\nerrors=" + report.summary.totalErrors + "\n" + output);
        }
        catch (Exception e) { File.WriteAllText("Temp/build-app-release.result", "FAIL " + e); Debug.LogException(e); }
        finally { if (restore) { PlayerSettings.bundleVersion = prior; File.WriteAllBytes("ProjectSettings/ProjectSettings.asset",settings); } }
    }
}
