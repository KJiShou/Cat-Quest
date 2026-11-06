using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Explicit Mono/Apple Silicon build. Preserves authored assets and the previous build settings.
[InitializeOnLoad]
public static class MacBuild
{
    const string RequestPath = "Temp/build-mac.request", ResultPath = "Temp/build-mac.result";
    const string Pending = "CatQuest.MacBuild.Pending";
    static string PlatformName => BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX);
    [Serializable] sealed class Request { public string outputDirectory; public bool development; }
    [Serializable] sealed class State
    {
        public Request request;
        public BuildTarget priorTarget;
        public ScriptingImplementation backend;
        public string architecture;
    }
    static MacBuild() { EditorApplication.update += Poll; }
    [MenuItem("Tools/Cat Quest/Build Mac Version (Apple Silicon, No Widget)")]
    public static void RequestBuild()
    {
        if (File.Exists(RequestPath) || !string.IsNullOrEmpty(SessionState.GetString(Pending, "")))
            throw new InvalidOperationException("A Mac build is already pending.");
        Directory.CreateDirectory("Temp");
        File.WriteAllText(RequestPath, JsonUtility.ToJson(new Request {
            outputDirectory = Path.GetFullPath("Builds/CatQuest-Mac-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"))
        }));
    }
    static void Result(string message)
    {
        Directory.CreateDirectory("Library/MacBuildVerification");
        File.WriteAllText("Library/MacBuildVerification/build-result.txt", message);
        File.WriteAllText(ResultPath, message);
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
                    throw new IOException("Use an empty Mac build directory.");
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
                    throw new InvalidOperationException("Install Mac Build Support (Mono) for this Editor and restart Unity first.");
                if (Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Any(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty))
                    throw new InvalidOperationException("Save or discard your scene edits first. This tool does not save scenes.");
                var state = new State { request = request, priorTarget = EditorUserBuildSettings.activeBuildTarget,
                    backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone),
                    architecture = EditorUserBuildSettings.GetPlatformSettings(PlatformName, "Architecture") };
                pending = JsonUtility.ToJson(state); SessionState.SetString(Pending, pending);
                Result("RUNNING " + DateTime.UtcNow.ToString("O"));
                if (state.priorTarget != BuildTarget.StandaloneOSX)
                {
                    if (!EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
                        throw new InvalidOperationException("Unity could not switch to macOS.");
                    return;
                }
            }
            catch (Exception error) { SessionState.EraseString(Pending); Result("FAIL " + error); Debug.LogException(error); return; }
        }
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneOSX) return;
        var saved = JsonUtility.FromJson<State>(pending);
        SessionState.EraseString(Pending);
        try
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            // Match Unity 6000.3 DesktopStandaloneBuildWindowExtension's platform setting.
            EditorUserBuildSettings.SetPlatformSettings(PlatformName, "Architecture", "arm64");
            Directory.CreateDirectory(saved.request.outputDirectory);
            string app = Path.Combine(saved.request.outputDirectory, "Cat Quest.app");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = app, target = BuildTarget.StandaloneOSX,
                options = saved.request.development ? BuildOptions.Development : BuildOptions.None
            });
            if (report == null || report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
                throw new Exception("Mac build failed or reported errors. See the current Editor log and Build Report.");
            string binaries = Path.Combine(app, "Contents/MacOS");
            if (!File.Exists(Path.Combine(app, "Contents/Info.plist")) || !Directory.Exists(binaries) || !Directory.EnumerateFiles(binaries).Any())
                throw new IOException("Unity did not generate a complete .app. Restart the Editor after installing the build module and retry.");
            using (var binary = new BinaryReader(File.OpenRead(Path.Combine(binaries, PlayerSettings.productName))))
                if (binary.ReadUInt32() != 0xFEEDFACF || binary.ReadUInt32() != 0x0100000C)
                    throw new IOException("The exported executable is not ARM64. Check the macOS build profile architecture override.");
            Result("PASS " + DateTime.UtcNow.ToString("O") + "\nerrors=" + report.summary.totalErrors +
                "\nwarnings=" + report.summary.totalWarnings + "\nbackend=Mono\narchitecture=ARM64\n" + app);
        }
        catch (Exception error) { Result("FAIL " + DateTime.UtcNow.ToString("O") + "\n" + error); Debug.LogException(error); }
        finally
        {
            EditorUserBuildSettings.SetPlatformSettings(PlatformName, "Architecture", saved.architecture);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, saved.backend);
            if (saved.priorTarget != BuildTarget.StandaloneOSX)
                EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildPipeline.GetBuildTargetGroup(saved.priorTarget), saved.priorTarget);
        }
    }
}
