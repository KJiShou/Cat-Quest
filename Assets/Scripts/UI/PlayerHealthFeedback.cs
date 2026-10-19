using UnityEngine;
using UnityEngine.UI;

// Presentation only: the simulation retains the exact HP immediately on impact.
[RequireComponent(typeof(Slider))]
public sealed class PlayerHealthFeedback : MonoBehaviour
{
    public Sprite neutralFill;
    public bool useColorStates=true;
    public float drainTime=.16f, ghostDelay=.35f, ghostDrainPerSecond=.7f;
    public Color healthy=new Color32(31,203,255,255), warning=new Color32(255,185,62,255), panic=new Color32(255,59,86,255);
    Slider slider;
    RectTransform rect,ghost;
    Image fill,ghostImage;
    Vector3 restScale;
    float target=1,ghostValue=1,velocity,hold,impact;
    public float Target => target;
    public float GhostValue => ghostValue;
    public Color FillColor => fill.color;
    void Awake(){Initialize();}
    void Initialize()
    {
        if(slider)return;
        slider=GetComponent<Slider>();rect=(RectTransform)transform;restScale=rect.localScale;
        fill=slider.fillRect.GetComponent<Image>();
        if(neutralFill){fill.sprite=neutralFill;fill.type=Image.Type.Sliced;}
        var go=new GameObject("DamageGhost",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
        ghost=(RectTransform)go.transform;ghost.SetParent(slider.fillRect.parent,false);
        ghost.SetSiblingIndex(slider.fillRect.GetSiblingIndex());
        ghost.anchorMin=Vector2.zero;ghost.anchorMax=Vector2.one;ghost.offsetMin=slider.fillRect.offsetMin;ghost.offsetMax=slider.fillRect.offsetMax;
        ghostImage=go.GetComponent<Image>();ghostImage.sprite=fill.sprite;ghostImage.type=fill.type;ghostImage.pixelsPerUnitMultiplier=fill.pixelsPerUnitMultiplier;
        ghostImage.raycastTarget=false;ghostImage.color=new Color32(255,218,153,255);
        Snap(slider.normalizedValue);
    }
    public void SetHealth(float ratio,bool animate)
    {
        Initialize();ratio=Mathf.Clamp01(ratio);
        if(!animate){Snap(ratio);return;}
        if(ratio<target){hold=ghostDelay;impact=.3f;ghostValue=Mathf.Max(ghostValue,slider.normalizedValue);}
        else if(ratio>target){Snap(ratio);return;}
        target=ratio;
    }
    void Snap(float ratio)
    {
        target=ghostValue=ratio;velocity=hold=impact=0;
        slider.SetValueWithoutNotify(ratio);ghost.anchorMax=new Vector2(ratio,1);rect.localScale=restScale;ApplyColor();
    }
    void ApplyColor()
    {
        fill.color=useColorStates?(target<=.25f?panic:target<=.5f?warning:healthy):healthy;
        if(useColorStates && target>0 && target<=.25f)fill.color=Color.Lerp(panic,new Color(1,.65f,.65f),(.5f+.5f*Mathf.Sin(Time.unscaledTime*9))*.28f);
    }
    // Called after the head-follow position is assigned, so shake never accumulates/drifts.
    public void Tick(float delta)
    {
        Initialize();
        float shown=Mathf.SmoothDamp(slider.normalizedValue,target,ref velocity,drainTime,Mathf.Infinity,delta);
        if(Mathf.Abs(shown-target)<.0005f)shown=target;
        slider.SetValueWithoutNotify(shown);
        hold=Mathf.Max(0,hold-delta);
        if(hold<=0)ghostValue=Mathf.MoveTowards(ghostValue,target,ghostDrainPerSecond*delta);
        ghostValue=Mathf.Max(shown,ghostValue);ghost.anchorMax=new Vector2(ghostValue,1);
        impact=Mathf.Max(0,impact-delta);
        float strength=impact/.3f,age=.3f-impact;
        rect.anchoredPosition+=new Vector2(Mathf.Sin(age*105),Mathf.Sin(age*81))*6*strength*strength;
        float punch=Mathf.Sin(Mathf.Clamp01(age/.3f)*Mathf.PI)*strength;
        float pulse=useColorStates&&target>0&&target<=.25f?Mathf.Sin(Time.unscaledTime*9)*.025f:0;
        rect.localScale=Vector3.Scale(restScale,new Vector3(1+.18f*punch+pulse,1+.3f*punch+pulse,1));ApplyColor();
    }
    public void ResetFeedback(){if(slider)Snap(1);}
    void OnDisable(){if(slider){rect.localScale=restScale;impact=hold=velocity=0;}}
}
