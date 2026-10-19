using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Animate a child visual, leaving the button's hit area stationary.
[RequireComponent(typeof(Button))]
public sealed class JuicyButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
{
    public RectTransform visual;
    public Image shine;
    Button button;
    Vector2 scale=Vector2.one,velocity;
    bool hovered,pressed,selected;
    float submitUntil;
    bool taskRow;
    JuicyButtonMesh[] meshes;
    public Vector2 DrawScale => scale;
    public float DrawOffset {get;private set;}
    public float DrawShine {get;private set;}
    void Awake(){
        button=GetComponent<Button>();
        taskRow=GetComponent<DashboardTaskRow>();
        if(!visual){
            var effects=new System.Collections.Generic.List<JuicyButtonMesh>();
            foreach(var graphic in GetComponentsInChildren<Graphic>(true)){
                // TMP's text callback already transforms every fallback-font submesh.
                if(graphic is TMPro.TMP_SubMeshUI)continue;
                var effect=graphic.GetComponent<JuicyButtonMesh>()??graphic.gameObject.AddComponent<JuicyButtonMesh>();
                effect.Register(this);effects.Add(effect);
            }
            meshes=effects.ToArray();
            // Keep native disabled tint/sprite while replacing hover and press styling.
            var colors=button.colors;colors.highlightedColor=colors.pressedColor=colors.selectedColor=colors.normalColor;button.colors=colors;
            if(button.transition==Selectable.Transition.SpriteSwap && button.targetGraphic is Image background){
                var sprites=button.spriteState;sprites.highlightedSprite=sprites.pressedSprite=sprites.selectedSprite=background.sprite;button.spriteState=sprites;
            }
        }
    }
    void Update()
    {
        var oldScale=scale;float oldOffset=DrawOffset,oldShine=DrawShine;
        bool active=button.IsInteractable();
        bool down=active&&(pressed||Time.unscaledTime<submitUntil);
        bool over=active&&(hovered||selected);
        var target=down?new Vector2(.96f,.89f):over?new Vector2(1.045f,1.065f):Vector2.one;
        // Dense rows have very little horizontal padding; keep their growth within it.
        if(taskRow){float width=Mathf.Max(1,((RectTransform)transform).rect.width);target.x=down?1-Mathf.Min(.04f,6/width):over?1+Mathf.Min(.045f,6/width):1;}
        // Full-width list controls must remain inside their scroll viewport.
        var bounds=((RectTransform)transform).rect;
        if(bounds.width>bounds.height*6 && GetComponentInParent<RectMask2D>())target.x=1;
        // Substeps keep the spring stable even during slow editor frames.
        float remaining=Mathf.Min(Time.unscaledDeltaTime,.1f);
        while(remaining>0){float dt=Mathf.Min(remaining,1f/120);remaining-=dt;
            velocity+=((target-scale)*650-velocity*(down?40:27))*dt;scale+=velocity*dt;}
        DrawOffset=Mathf.Lerp(DrawOffset,down?-3:over?3:0,1-Mathf.Exp(-22*Time.unscaledDeltaTime));
        DrawShine=Mathf.Lerp(DrawShine,down?0:over?.15f:0,1-Mathf.Exp(-18*Time.unscaledDeltaTime));
        if(visual){visual.localScale=new Vector3(scale.x,scale.y,1);visual.anchoredPosition=new Vector2(0,DrawOffset);}
        else if(oldScale!=scale || oldOffset!=DrawOffset || oldShine!=DrawShine)RefreshMeshes();
        if(shine){var c=shine.color;c.a=Mathf.Lerp(c.a,down?0:over?.15f:0,1-Mathf.Exp(-18*Time.unscaledDeltaTime));shine.color=c;}
        if(!active){pressed=hovered=selected=false;submitUntil=0;}
    }
    public void OnPointerEnter(PointerEventData e){hovered=true;}
    public void OnPointerExit(PointerEventData e){hovered=false;pressed=false;}
    public void OnPointerDown(PointerEventData e){if(e.button==PointerEventData.InputButton.Left&&button.IsInteractable())pressed=true;}
    public void OnPointerUp(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)pressed=false;}
    public void OnSelect(BaseEventData e){selected=!(e is PointerEventData);}
    public void OnDeselect(BaseEventData e){selected=false;pressed=false;}
    public void OnSubmit(BaseEventData e){if(button.IsInteractable())submitUntil=Time.unscaledTime+.12f;}
    void OnApplicationFocus(bool focus){if(!focus)ResetVisual();}
    void OnDisable(){ResetVisual();}
    void ResetVisual(){hovered=pressed=selected=false;submitUntil=0;scale=Vector2.one;velocity=Vector2.zero;
        DrawOffset=DrawShine=0;RefreshMeshes();
        if(visual){visual.localScale=Vector3.one;visual.anchoredPosition=Vector2.zero;}
        if(shine){var c=shine.color;c.a=0;shine.color=c;}}
    void RefreshMeshes(){if(meshes!=null)foreach(var mesh in meshes)if(mesh)mesh.Refresh();}
}
