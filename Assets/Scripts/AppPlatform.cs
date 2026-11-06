using UnityEngine;

// Native integrations are Windows-only; Web and Mac keep the local core features.
public static class AppPlatform
{
    public static bool WindowsDesktop => Application.platform == RuntimePlatform.WindowsPlayer
        || Application.platform == RuntimePlatform.WindowsEditor;
    public static bool Browser => Application.platform == RuntimePlatform.WebGLPlayer;
    public const string DesktopMessage = "Available in the Windows desktop version. Local tasks and progress remain available.";
}
