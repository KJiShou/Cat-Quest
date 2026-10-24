using UnityEngine;
public sealed class AiChatReviewLayout : MonoBehaviour
{
    public RectTransform panel;
    void OnEnable(){Resize();}
    void LateUpdate(){Resize();}
    void Resize(){var size=((RectTransform)transform).rect.size;panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,.5f);panel.anchoredPosition=Vector2.zero;panel.sizeDelta=new Vector2(Mathf.Min(1320,size.x-64),Mathf.Min(880,size.y-64));}
}
