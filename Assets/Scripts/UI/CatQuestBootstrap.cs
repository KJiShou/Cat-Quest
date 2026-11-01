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
            AppUpdateController.Ensure();
            if(CompanionProtocol.HasArgument("--focus-smoke")) { var probe=new GameObject("FocusBuildSmoke"); DontDestroyOnLoad(probe); probe.AddComponent<FocusBuildSmoke>(); }
            if(AccountBuildSmoke.Enabled) { var probe=new GameObject("AccountBuildSmoke"); DontDestroyOnLoad(probe); probe.AddComponent<AccountBuildSmoke>(); }
            SceneManager.LoadScene(CompanionProtocol.HasArgument("--focus-smoke") || AccountBuildSmoke.Enabled || AiBuildSmoke.Enabled ? "CharacterHome" : "IntroStory");
        }
    }
}
