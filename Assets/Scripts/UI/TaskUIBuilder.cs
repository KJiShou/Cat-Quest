using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shared small controls use the scene's existing font and rounded sprites.
public sealed class TaskUIBuilder
{
    public readonly TMP_FontAsset Font;
    public readonly Sprite Rounded;
    public static readonly Color Ink = new Color32(20,45,77,255);
    public static readonly Color Blue = new Color32(0,137,245,255);
    public static readonly Color Overdue = new Color32(220,55,65,255);
    public static readonly Color ScheduleInk = new Color32(137,145,156,255);
    public static Color DifficultyColor(TaskDifficulty difficulty) => difficulty == TaskDifficulty.Hard
        ? new Color32(255,235,96,255) : difficulty == TaskDifficulty.Easy
        ? new Color32(0,166,85,255) : new Color32(0,118,220,255);
    public static void BlueCaret(TMP_InputField input)
    {
        input.customCaretColor = true;
        input.caretColor = Blue;
    }
    public static void DifficultyBadge(Transform badge, TaskDifficulty difficulty, Sprite[] sprites)
    {
        var text = badge.GetComponentInChildren<TMP_Text>(true);
        text.text = difficulty.ToString(); text.color = DifficultyColor(difficulty);
        text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.NoWrap;
        text.enableAutoSizing = false;
        var image = badge.GetComponentInChildren<Image>(true);
        image.sprite = sprites[(int)difficulty]; image.type = UnityEngine.UI.Image.Type.Simple;
        image.preserveAspect = true;
        Stretch(text.rectTransform,6,6);
    }
    public TaskUIBuilder(TMP_FontAsset font, Sprite rounded) { Font = font; Rounded = rounded; }
    public static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true)
        .FirstOrDefault(t => t.name == name && (t.gameObject.activeInHierarchy || !root.gameObject.activeInHierarchy));
    public static void Clear(Transform parent)
    {
        foreach (Transform child in parent) { child.gameObject.SetActive(false); Object.Destroy(child.gameObject); }
    }
    public static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
        go.transform.SetParent(parent, false); return (RectTransform)go.transform;
    }
    public static LayoutElement Height(RectTransform rect, float height)
    {
        var le = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = height; le.flexibleHeight = 0; return le;
    }
    public static void Flex(Transform t, float weight = 1)
    {
        var le = t.GetComponent<LayoutElement>() ?? t.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = 0; le.flexibleWidth = weight;
    }
    public static void Stretch(RectTransform r, float left=0,float right=0,float top=0,float bottom=0)
    {
        r.anchorMin=Vector2.zero; r.anchorMax=Vector2.one; r.offsetMin=new Vector2(left,bottom); r.offsetMax=new Vector2(-right,-top);
    }
    public RectTransform Row(Transform parent,string name,float height=40,float spacing=8)
    {
        var r=Rect(parent,name); Height(r,height); var l=r.gameObject.AddComponent<HorizontalLayoutGroup>();
        l.spacing=spacing; l.childControlWidth=l.childControlHeight=true; l.childForceExpandWidth=false; l.childForceExpandHeight=false; l.childAlignment=TextAnchor.MiddleLeft; return r;
    }
    public RectTransform Column(Transform parent,string name,float height=-1,float spacing=8)
    {
        var r=Rect(parent,name); if(height>=0) Height(r,height); var l=r.gameObject.AddComponent<VerticalLayoutGroup>();
        l.spacing=spacing; l.childControlWidth=l.childControlHeight=true; l.childForceExpandWidth=true; l.childForceExpandHeight=false; return r;
    }
    public Image Image(Transform parent,Color color,Sprite sprite=null)
    {
        var image=parent.GetComponent<Image>() ?? parent.gameObject.AddComponent<Image>(); image.sprite=sprite; image.color=color; image.type=sprite ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple; image.pixelsPerUnitMultiplier=3; return image;
    }
    public TMP_Text Text(Transform parent,string text,float size=20,Color? color=null)
    {
        var r=Rect(parent,"Text"); var t=r.gameObject.AddComponent<TextMeshProUGUI>(); t.font=Font; t.fontSize=size; t.text=text; t.color=color ?? Ink; t.raycastTarget=false; t.alignment=TextAlignmentOptions.MidlineLeft; t.textWrappingMode=TextWrappingModes.Normal; Height(r,size+8); return t;
    }
    public Button Button(Transform parent,string name,string caption,float width=0,Color? color=null)
    {
        var r=Rect(parent,name); var le=Height(r,40); le.preferredWidth=le.minWidth=width; if(width==0) Flex(r);
        var image=Image(r,color ?? new Color32(215,240,255,255),Rounded); var button=r.gameObject.AddComponent<Button>(); button.targetGraphic=image;
        var text=Text(r,caption,18,Blue); text.alignment=TextAlignmentOptions.Center; Stretch(text.rectTransform,8,8); return button;
    }
    public static void LayoutTag(GameObject chip, string caption, float fontSize=18, float height=34)
    {
        var text=chip.GetComponentInChildren<TMP_Text>(true);
        text.text=caption; text.fontSize=fontSize; text.enableAutoSizing=false;
        text.textWrappingMode=TextWrappingModes.NoWrap; text.alignment=TextAlignmentOptions.Center;
        var rect=chip.GetComponent<RectTransform>(); rect.localScale=Vector3.one;
        var size=Height(rect,height); size.minWidth=size.preferredWidth=Mathf.Ceil(text.GetPreferredValues().x)+28; size.flexibleWidth=0;
        Stretch(text.rectTransform,12,12,2,2);
    }
    public TMP_InputField Input(Transform parent,string placeholder,float height=40)
    {
        var r=Rect(parent,"Input"); Height(r,height); Flex(r); var image=Image(r,new Color32(241,247,255,255),Rounded);
        var field=r.gameObject.AddComponent<TMP_InputField>(); field.targetGraphic=image;
        var area=Rect(r,"Text Area"); Stretch(area,12,12,4,4); area.gameObject.AddComponent<RectMask2D>();
        var text=(TextMeshProUGUI)Text(area,"",18); Stretch(text.rectTransform); var hint=Text(area,placeholder,18,new Color32(137,156,180,255)); Stretch(hint.rectTransform);
        field.textViewport=area; field.textComponent=text; field.placeholder=hint; return field;
    }
    public Toggle Toggle(Transform parent,string label,bool selected=false)
    {
        var r=Row(parent,label,32,8); Flex(r); var toggle=r.gameObject.AddComponent<Toggle>();
        var box=Rect(r,"Box"); var le=Height(box,24); le.preferredWidth=le.minWidth=24;
        var bg=Image(box,new Color32(194,211,238,255),Rounded); toggle.targetGraphic=bg;
        var mark=Rect(box,"Check"); Stretch(mark,4,4,4,4); var graphic=Image(mark,Blue,Rounded); toggle.graphic=graphic;
        var text=Text(r,label,16); Flex(text.transform); toggle.SetIsOnWithoutNotify(selected); return toggle;
    }
    public RectTransform ScrollContent(Transform parent,string name,float height=-1)
    {
        var view=Rect(parent,name); if(height>=0) Height(view,height); Image(view,Color.clear); view.gameObject.AddComponent<RectMask2D>();
        var content=Column(view,"Content"); content.anchorMin=new Vector2(0,1); content.anchorMax=Vector2.one; content.pivot=new Vector2(.5f,1); content.anchoredPosition=Vector2.zero; content.sizeDelta=Vector2.zero;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        var scroll=view.gameObject.AddComponent<ScrollRect>(); scroll.content=content; scroll.viewport=view; scroll.horizontal=false; scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=28;
        return content;
    }
}
