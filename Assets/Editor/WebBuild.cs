using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Explicit build only. Does not regenerate scenes, prefabs, or player data.
[InitializeOnLoad]
public static class WebBuild
{
    const string RequestPath = "Temp/build-web.request", ResultPath = "Temp/build-web.result";
    const string Pending = "CatQuest.WebBuild.Pending";
    [Serializable] sealed class Request { public string outputDirectory; public bool development; }
    [Serializable] sealed class State
    {
        public Request request;
        public BuildTarget priorTarget;
        public WebGLCompressionFormat compression;
        public bool fallback, threads;
        public string template;
    }
    static WebBuild() { EditorApplication.update += Poll; }
    [MenuItem("Tools/Cat Quest/Build itch.io Web Version")]
    public static void RequestBuild()
    {
        if (File.Exists(RequestPath) || !string.IsNullOrEmpty(SessionState.GetString(Pending, "")))
            throw new InvalidOperationException("A Web build is already pending.");
        Directory.CreateDirectory("Temp");
        File.WriteAllText(RequestPath, JsonUtility.ToJson(new Request {
            outputDirectory = Path.GetFullPath("Builds/Web-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"))
        }));
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string pending = SessionState.GetString(Pending, "");
        if (string.IsNullOrEmpty(pending))
        {
            if (!File.Exists(RequestPath)) return;
            string json = File.ReadAllText(RequestPath); File.Delete(RequestPath);
            try
            {
                var request = JsonUtility.FromJson<Request>(json);
                if (request == null || string.IsNullOrEmpty(request.outputDirectory) || !Path.IsPathRooted(request.outputDirectory))
                    throw new ArgumentException("An absolute output directory is required.");
                request.outputDirectory = Path.GetFullPath(request.outputDirectory);
                if (Directory.Exists(request.outputDirectory) && Directory.EnumerateFileSystemEntries(request.outputDirectory).Any())
                    throw new IOException("Use an empty Web build directory.");
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                    throw new InvalidOperationException("Web Build Support is not loaded. Install the module for this Editor, then restart Unity and retry.");
                if (Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Any(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty))
                    throw new InvalidOperationException("Save or discard your scene edits before building. No scenes were saved by this tool.");
                var state = new State { request = request, priorTarget = EditorUserBuildSettings.activeBuildTarget,
                    compression = PlayerSettings.WebGL.compressionFormat, fallback = PlayerSettings.WebGL.decompressionFallback,
                    threads = PlayerSettings.WebGL.threadsSupport, template = PlayerSettings.WebGL.template };
                pending = JsonUtility.ToJson(state); SessionState.SetString(Pending, pending);
                File.WriteAllText(ResultPath, "RUNNING " + DateTime.UtcNow.ToString("O"));
                if (state.priorTarget != BuildTarget.WebGL) {
                    if (!EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                        throw new InvalidOperationException("Unity could not switch to the Web build target.");
                    return;
                }
            }
            catch (Exception error) { SessionState.EraseString(Pending); File.WriteAllText(ResultPath, "FAIL " + error); Debug.LogException(error); return; }
        }
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL) return;
        var saved = JsonUtility.FromJson<State>(pending);
        SessionState.EraseString(Pending);
        try
        {
            AssetDatabase.Refresh();
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.threadsSupport = false;
            PlayerSettings.WebGL.template = "PROJECT:CatQuest";
            Directory.CreateDirectory(saved.request.outputDirectory);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = saved.request.outputDirectory, target = BuildTarget.WebGL,
                options = saved.request.development ? BuildOptions.Development : BuildOptions.None
            });
            if (report == null || report.summary.result != BuildResult.Succeeded) throw new Exception("Web build failed. See the current Editor log.");
            if (!File.Exists(Path.Combine(saved.request.outputDirectory, "index.html")))
                throw new Exception("Unity did not generate index.html. Restart the Editor after installing Web Build Support, then retry in a new output directory.");
            // Symbols aid local diagnostics but do not belong in an itch.io upload.
            string zip = saved.request.outputDirectory + ".zip";
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var files = Directory.GetFiles(saved.request.outputDirectory, "*", SearchOption.AllDirectories)
                    .Where(path => !Path.GetRelativePath(saved.request.outputDirectory, path).StartsWith("Symbols", StringComparison.Ordinal)).ToArray();
                if (files.Length > 1000 || files.Sum(f => new FileInfo(f).Length) > 500L * 1024 * 1024)
                    throw new IOException("The build exceeds itch.io's default archive limits.");
                foreach (string file in files) {
                    string relative = Path.GetRelativePath(saved.request.outputDirectory, file).Replace('\\', '/');
                    if (relative.Length > 240 || new FileInfo(file).Length > 200L * 1024 * 1024)
                        throw new IOException("The build exceeds itch.io's default single-file limits: " + relative);
                    archive.CreateEntryFromFile(file, relative, System.IO.Compression.CompressionLevel.Optimal);
                }
            }
            File.WriteAllText(ResultPath, "PASS " + DateTime.UtcNow.ToString("O") + "\nerrors=" + report.summary.totalErrors + "\n" + zip);
        }
        catch (Exception error) { File.WriteAllText(ResultPath, "FAIL " + DateTime.UtcNow.ToString("O") + "\n" + error); Debug.LogException(error); }
        finally
        {
            PlayerSettings.WebGL.compressionFormat = saved.compression;
            PlayerSettings.WebGL.decompressionFallback = saved.fallback;
            PlayerSettings.WebGL.threadsSupport = saved.threads;
            PlayerSettings.WebGL.template = saved.template;
            if (saved.priorTarget != BuildTarget.WebGL)
                EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildPipeline.GetBuildTargetGroup(saved.priorTarget), saved.priorTarget);
        }
    }
}
