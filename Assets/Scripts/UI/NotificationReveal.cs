using UnityEngine;
using UnityEngine.UI;

// Shared presentation only: owners retain message lifetime, dismissal and persistence.
[RequireComponent(typeof(RectTransform),typeof(RectMask2D))]
public sealed class NotificationReveal : MonoBehaviour
{
    public bool playOnEnable=true;
    public float duration=.24f;
    public CanvasGroup[] textFades;
    RectTransform rect;
    RectMask2D clip;
    RectTransform[] children;
    Vector2[] childSizes,childPositions;
    Vector2 restingSize;
    float elapsed;
    bool initialized,running;
    public float Progress {get;private set;}=1;

    // Explicitly recapture after an existing presenter configures its popup layout.
    public void CaptureLayout()
    {
        rect=(RectTransform)transform;clip=GetComponent<RectMask2D>();restingSize=rect.sizeDelta;
        children=new RectTransform[rect.childCount];childSizes=new Vector2[children.Length];childPositions=new Vector2[children.Length];
        for(int i=0;i<children.Length;i++){
            children[i]=rect.GetChild(i) as RectTransform;
            if(children[i]){childSizes[i]=children[i].sizeDelta;childPositions[i]=children[i].anchoredPosition;}
        }
        initialized=true;
    }
    void OnEnable(){if(!initialized)CaptureLayout();if(playOnEnable)Play();else Settle();}
    public void Play()
    {
        if(!initialized)CaptureLayout();elapsed=0;running=!UiMotion.Reduced&&duration>0;
        Apply(running?0:1);
    }
    public void Settle(){if(!initialized)return;running=false;Apply(1);}
    void LateUpdate()
    {
        if(!running)return;
        elapsed+=Time.unscaledDeltaTime;
        float t=UiMotion.Reduced?1:Mathf.Clamp01(elapsed/duration);
        Apply(Mathf.SmoothStep(0,1,t));
        if(t>=1)running=false;
    }
    void Apply(float progress)
    {
        Progress=progress;
        var parent=rect.parent as RectTransform;
        float width=Mathf.Max(1,restingSize.x+(parent?parent.rect.width:0)*(rect.anchorMax.x-rect.anchorMin.x));
        float missing=width*(1-Mathf.Lerp(.02f,1,progress));
        rect.sizeDelta=new Vector2(restingSize.x-missing,restingSize.y);
        for(int i=0;i<children.Length;i++){
            var child=children[i];if(!child)continue;
            float span=child.anchorMax.x-child.anchorMin.x;
            child.sizeDelta=new Vector2(childSizes[i].x+missing*span,childSizes[i].y);
            // Hold content at its final position/size while the centered frame reveals it.
            float anchor=child.anchorMin.x+span*child.pivot.x;
            child.anchoredPosition=new Vector2(childPositions[i].x+(anchor-rect.pivot.x)*missing,childPositions[i].y);
        }
        if(textFades!=null)foreach(var group in textFades)if(group)group.alpha=progress;
        clip.enabled=progress<1; // Preserve authored overflow/shadows once settled.
    }
    void OnDisable(){Settle();}
    void OnApplicationFocus(bool focused){if(!focused)Settle();}
}
