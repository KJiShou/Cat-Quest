using UnityEngine;
using UnityEngine.UI;

// Keeps an equipment modal owned by its page while covering the root UI canvas.
[RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]
public sealed class EquipmentModalSurface : MonoBehaviour
{
    readonly Vector3[] corners = new Vector3[4];
    RectTransform rect, root;
    void Awake()
    {
        rect=(RectTransform)transform;
        root=(RectTransform)GetComponentInParent<Canvas>().rootCanvas.transform;
    }
    void OnEnable(){if(rect)Fit();}
    void LateUpdate(){Fit();}
    void Fit()
    {
        if(!root||root==rect)return;
        root.GetWorldCorners(corners);
        var parent=rect.parent;
        Vector3 min=parent.InverseTransformPoint(corners[0]),max=parent.InverseTransformPoint(corners[2]);
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
        rect.localScale=Vector3.one;
        rect.sizeDelta=max-min;
        rect.localPosition=(min+max)*.5f;
    }
}
