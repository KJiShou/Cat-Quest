using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CatQuestBootstrap : MonoBehaviour
{
    public GameObject companionPrefab;
    void Start()
    {
        Application.runInBackground=true;
        if (AppUpdateController.InstallationLocked()) { Application.Quit(); return; }
        if(CompanionProtocol.HasArgument("--companion")) Instantiate(companionPrefab);
        else
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // Override a fullscreen mode remembered by an earlier Windows build.
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Application.targetFrameRate = Application.isFocused ? 60 : 30;
#endif
            AppUpdateController.Ensure();
            if(CompanionProtocol.HasArgument("--focus-smoke")) { var probe=new GameObject("FocusBuildSmoke"); DontDestroyOnLoad(probe); probe.AddComponent<FocusBuildSmoke>(); }
            if(AccountBuildSmoke.Enabled) { var probe=new GameObject("AccountBuildSmoke"); DontDestroyOnLoad(probe); probe.AddComponent<AccountBuildSmoke>(); }
            SceneManager.LoadScene(CompanionProtocol.HasArgument("--focus-smoke") || AccountBuildSmoke.Enabled || AiBuildSmoke.Enabled ? "CharacterHome" : "IntroStory");
        }
    }
}
