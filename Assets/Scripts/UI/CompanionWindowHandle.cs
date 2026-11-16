using UnityEngine;
using UnityEngine.EventSystems;
public sealed class CompanionWindowHandle : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
{
    public bool resize;
    public int resizeEdge=8;
    bool hover;
    public void OnPointerDown(PointerEventData e)
    {
        if(e.button!=PointerEventData.InputButton.Left)return;
        if(resize){if(!CompanionNativeWindow.NativeResizeInstalled)CompanionNativeWindow.BeginResize(resizeEdge);}
        else CompanionNativeWindow.BeginDrag(false);
    }
    public void OnPointerEnter(PointerEventData e){hover=true;}
    public void OnPointerExit(PointerEventData e){hover=false;if(resize&&!CompanionNativeWindow.NativeResizeInstalled)CompanionNativeWindow.ResizeCursor(0);}
    void Update()
    {
        if(resize&&hover&&!CompanionNativeWindow.NativeResizeInstalled)CompanionNativeWindow.ResizeCursor(resizeEdge);
    }
}
