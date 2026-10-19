using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CatQuestBootstrap : MonoBehaviour
{
    public GameObject companionPrefab;
    void Start()
    {
        Application.runInBackground=true;
        if(CompanionProtocol.HasArgument("--companion")) Instantiate(companionPrefab);
        else
        {
            if(CompanionProtocol.HasArgument("--focus-smoke")) { var probe=new GameObject("FocusBuildSmoke"); DontDestroyOnLoad(probe); probe.AddComponent<FocusBuildSmoke>(); }
            SceneManager.LoadScene(CompanionProtocol.HasArgument("--focus-smoke") ? "CharacterHome" : "IntroStory");
        }
    }
}
