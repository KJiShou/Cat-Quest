using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Size tabs from the available content width, including during sidebar animation.
[ExecuteAlways]
public sealed class TaskTabsLayout : MonoBehaviour
{
    public TaskSystemUI owner;
    float lastWidth = -1;
    string lastCounts;
    void OnEnable() { lastWidth = -1; }
    void LateUpdate() => Apply();

    public void Apply()
    {
        if (!owner || owner.tabButtons == null || owner.tabButtons.Length != 5) return;
        var bar = (RectTransform)transform;
        float width = bar.rect.width;
        if (width <= 0) return;
        string counts = string.Join("/", System.Array.ConvertAll(owner.tabCounts, t => t ? t.text : ""));
        if (Mathf.Abs(width-lastWidth) < .1f && counts == lastCounts) return;
        lastWidth = width; lastCounts = counts;
        var barLayout = GetComponent<HorizontalLayoutGroup>(); if (barLayout) barLayout.enabled = false;
        float cell = width / 5;
        bool stacked = cell < 205;
        float height = stacked ? 84 : 64;
        // The page previously distributed spare height to the tab header as well
        // as the task list, leaving a tall empty area at different aspect ratios.
        var header=(RectTransform)bar.parent;
        TaskUIBuilder.Height(header,height);
        var headerLayout=header.GetComponent<HorizontalLayoutGroup>();
        if(headerLayout)headerLayout.childForceExpandHeight=false;
        var pageLayout=header.parent.GetComponent<VerticalLayoutGroup>();
        if(pageLayout)pageLayout.childForceExpandHeight=false;
        var content=header.parent.Find("Top");
        if(content) {
            var fill=content.GetComponent<LayoutElement>()??content.gameObject.AddComponent<LayoutElement>();
            fill.minHeight=0;fill.preferredHeight=0;fill.flexibleHeight=1;
        }
        TaskUIBuilder.Height(bar,height);
        bar.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,height);
        float iconSize = stacked ? 32 : 40;
        float fontSize = stacked ? 22 : 25;
        // All captions share one font size; History never scales independently.
        for (int i=0; i<5; i++)
        {
            var label = owner.tabLabels[i];
            label.enableAutoSizing = false; label.fontSize = fontSize;
            float available = stacked ? cell-16 : cell-iconSize-28-36;
            float preferred = label.GetPreferredValues(label.text).x;
            if (preferred > available) fontSize = Mathf.Min(fontSize,label.fontSize*Mathf.Max(1,available)/preferred);
        }
        fontSize = Mathf.Max(14,fontSize);
        for (int i=0; i<5; i++)
        {
            var tab = (RectTransform)owner.tabButtons[i].transform;
            var layout = tab.GetComponent<HorizontalLayoutGroup>(); if (layout) layout.enabled = false;
            tab.localScale = Vector3.one;
            tab.anchorMin = new Vector2(i/5f,0); tab.anchorMax = new Vector2((i+1)/5f,1);
            tab.offsetMin = tab.offsetMax = Vector2.zero;
            var icon = tab.Find("Icon").GetComponent<Image>();
            icon.preserveAspect = true;
            var label = owner.tabLabels[i];
            label.fontSize = fontSize; label.margin = Vector4.zero;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            var badge = (RectTransform)owner.tabCounts[i].transform.parent;
            float badgeSize = Mathf.Max(26,owner.tabCounts[i].GetPreferredValues(owner.tabCounts[i].text).x+12);
            owner.tabCounts[i].fontSize=18;owner.tabCounts[i].enableAutoSizing=false;
            owner.tabCounts[i].alignment=TextAlignmentOptions.Center;
            TaskUIBuilder.Stretch(owner.tabCounts[i].rectTransform);
            if (stacked)
            {
                Box(icon.rectTransform,cell/2,height-24,iconSize,iconSize);
                Box(label.rectTransform,cell/2,22,cell-16,30);
                Box(badge,cell/2+iconSize/2+18,height-24,badgeSize,26);
            }
            else
            {
                float caption = label.GetPreferredValues(label.text).x+2;
                float group = iconSize+10+caption+(i<3 ? 10+badgeSize : 0);
                float left = (cell-group)/2;
                Box(icon.rectTransform,left+iconSize/2,height/2,iconSize,iconSize);
                Box(label.rectTransform,left+iconSize+10+caption/2,height/2,caption,40);
                Box(badge,left+iconSize+10+caption+10+badgeSize/2,height/2,badgeSize,26);
            }
            var focus = (RectTransform)owner.tabFocus[i].transform;
            focus.anchorMin = new Vector2(.12f,0);focus.anchorMax = new Vector2(.88f,0);
            focus.pivot = new Vector2(.5f,0);focus.offsetMin = Vector2.zero;focus.offsetMax = new Vector2(0,4);
        }
        int divider=0;
        foreach(Transform child in transform)
            if(child.name.StartsWith("Line")) {
                var line=(RectTransform)child;divider++;
                line.anchorMin=line.anchorMax=new Vector2(divider/5f,.5f);
                line.pivot=new Vector2(.5f,.5f);line.anchoredPosition=Vector2.zero;line.sizeDelta=new Vector2(2,height-20);
                var image=line.GetComponent<Image>();if(image)image.raycastTarget=false;
            }
    }
    static void Box(RectTransform rect,float x,float y,float width,float height)
    {
        rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=new Vector2(.5f,.5f);
        rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(width,height);
    }
}
