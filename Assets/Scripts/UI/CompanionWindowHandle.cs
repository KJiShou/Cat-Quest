using UnityEngine;
using UnityEngine.EventSystems;
public sealed class CompanionWindowHandle : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
{
    public bool resize;
    public int resizeEdge=8;
    bool resizing,hover;
    public void OnPointerDown(PointerEventData e)
    {
        if(e.button!=PointerEventData.InputButton.Left)return;
        if(resize){CompanionNativeWindow.BeginResize(resizeEdge);resizing=true;}
        else CompanionNativeWindow.BeginDrag(false);
    }
    public void OnPointerEnter(PointerEventData e){hover=true;}
    public void OnPointerExit(PointerEventData e){hover=false;if(resize&&!resizing)CompanionNativeWindow.ResizeCursor(0);}
    void Update()
    {
        if(resizing)resizing=CompanionNativeWindow.UpdateResize();
        if(resize&&hover)CompanionNativeWindow.ResizeCursor(resizeEdge);
    }
}
