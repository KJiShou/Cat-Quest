using UnityEngine;
using UnityEngine.UI;

public sealed class JuicyButtonsInstaller : MonoBehaviour
{
    Selectable[] controls=new Selectable[128];
    int frame=-1;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        var host=new GameObject("Button Feedback");
        DontDestroyOnLoad(host);host.AddComponent<JuicyButtonsInstaller>();
    }
    void OnEnable(){Canvas.willRenderCanvases+=Apply;}
    void OnDisable(){Canvas.willRenderCanvases-=Apply;}
    void Apply()
    {
        if(frame==Time.frameCount)return;frame=Time.frameCount;
        if(controls.Length<Selectable.allSelectableCount)controls=new Selectable[Selectable.allSelectableCount*2];
        int count=Selectable.AllSelectablesNoAlloc(controls);
        for(int i=0;i<count;i++){
            if(controls[i] is Button button && !button.GetComponent<JuicyButton>())button.gameObject.AddComponent<JuicyButton>();
            controls[i]=null;
        }
    }
}
