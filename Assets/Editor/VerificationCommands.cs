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
        if (File.Exists("Temp/stop-verification-play.request")) {
            File.Delete("Temp/stop-verification-play.request");
            EditorApplication.isPlaying = false;
            return;
        }
        if (!File.Exists("Temp/refresh-verification-scripts.request")) return;
        File.Delete("Temp/refresh-verification-scripts.request");
        Refresh();
    }
    [MenuItem("Tools/Cat Quest/Refresh Verification Scripts")]
    public static void Refresh()
    {
        AssetDatabase.ImportAsset("Assets/Scripts", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
        AssetDatabase.ImportAsset("Assets/Editor", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
        UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
    }
}
