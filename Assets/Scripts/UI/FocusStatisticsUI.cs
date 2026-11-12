using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public sealed class FocusStatisticsUI : MonoBehaviour
{
    public GameObject panel;
    public Button open, close, previous, next, today, allTags;
    public Button[] periods;
    public TMP_Text range, total, sessionCount, chartTitle, detail, empty;
    public TMP_Text[] xLabels, yLabels;
    public FocusStatisticsGraph graph;
    public RectTransform tagContent;
    public Button tagTemplate;
    public Image todayIcon, allTagsIcon;
    public FocusStatisticsPeriod Period { get; private set; } = FocusStatisticsPeriod.Week;
    public DateTime Date { get; private set; }
    public FocusStatistics.Result Data { get; private set; }
    public string SelectedTag { get; private set; }
    TaskService service;
    readonly Dictionary<string,Color> chartColors = new Dictionary<string,Color>();
    FocusStatistics.Series[] displayedSeries = Array.Empty<FocusStatistics.Series>();
    readonly List<Button> rows = new List<Button>();
    readonly Dictionary<string,Button> rowsByTag = new Dictionary<string,Button>();
    TaskService colorService;
    int colorVersion = -1;
    UnityEngine.Events.UnityAction[] periodActions;
    DateTime observedDay;
    void Awake()
    {
        open.onClick.AddListener(Show); close.onClick.AddListener(Hide);
        previous.onClick.AddListener(Previous); next.onClick.AddListener(Next); today.onClick.AddListener(Today);
        allTags.onClick.AddListener(AllTags);
        periodActions = new UnityEngine.Events.UnityAction[periods.Length];
        for (int i=0;i<periods.Length;i++) { int index=i;periodActions[i]=()=>ChoosePeriod((FocusStatisticsPeriod)index);periods[i].onClick.AddListener(periodActions[i]); }
        graph.Hovered += Hover;
    }
    void OnEnable() { service=GameDataManager.EnsureInstance().Tasks;service.Changed+=RefreshIfOpen; }
    void OnDisable() { if(service!=null)service.Changed-=RefreshIfOpen;panel.SetActive(false); }
    void OnDestroy()
    {
        open.onClick.RemoveListener(Show);close.onClick.RemoveListener(Hide);
        previous.onClick.RemoveListener(Previous);next.onClick.RemoveListener(Next);today.onClick.RemoveListener(Today);allTags.onClick.RemoveListener(AllTags);
        if(periodActions!=null)for(int i=0;i<periods.Length;i++)periods[i].onClick.RemoveListener(periodActions[i]);
        graph.Hovered-=Hover;
    }
    void Update()
    {
        if(!panel.activeSelf)return;
        if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)Hide();
        if(service.LocalNow.Date!=observedDay)Refresh();
        var selected=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
        if(selected&&!selected.transform.IsChildOf(panel.transform))EventSystem.current.SetSelectedGameObject(close.gameObject);
    }
    public void Show() { Date=service.LocalNow.Date;SelectedTag=null;panel.SetActive(true);panel.transform.SetAsLastSibling();Refresh();if(EventSystem.current)EventSystem.current.SetSelectedGameObject(close.gameObject); }
    public void Hide() { panel.SetActive(false);if(EventSystem.current&&open.gameObject.activeInHierarchy)EventSystem.current.SetSelectedGameObject(open.gameObject); }
    public void ChoosePeriod(FocusStatisticsPeriod period) { Period=period;Refresh(); }
    void Previous() { Date=FocusStatistics.Move(Date,Period,-1);Refresh(); }
    void Next() { if(!next.interactable)return;Date=FocusStatistics.Move(Date,Period,1);Refresh(); }
    void Today() { Date=service.LocalNow.Date;Refresh(); }
    void AllTags() { SelectedTag=null;RefreshGraph(); }
    void RefreshIfOpen() { if(panel.activeSelf)Refresh(); }
    public void Refresh()
    {
        observedDay=service.LocalNow.Date;
        BuildColors();
        Data=service.QueryFocusStatistics(Date,Period,TimeZoneInfo.Local);
        range.text=Period==FocusStatisticsPeriod.Year?Data.start.ToString("yyyy"):Period==FocusStatisticsPeriod.Month?Data.start.ToString("MMMM yyyy",CultureInfo.InvariantCulture)
            :Period==FocusStatisticsPeriod.Day?Data.start.ToString("ddd, dd MMM yyyy",CultureInfo.InvariantCulture):Data.start.ToString("dd MMM",CultureInfo.InvariantCulture)+" - "+Data.end.AddDays(-1).ToString("dd MMM yyyy",CultureInfo.InvariantCulture);
        total.text=FocusStatistics.Duration(Data.seconds);
        sessionCount.text=Data.sessions.ToString(CultureInfo.InvariantCulture);
        next.interactable=Data.end<=FocusStatistics.Start(service.LocalNow,Period);
        previous.interactable=Data.start.Year>1;
        Tint(today,Data.start==FocusStatistics.Start(observedDay,Period));
        if(todayIcon)todayIcon.color=Data.start==FocusStatistics.Start(observedDay,Period)?Color.white:TaskUIBuilder.Ink;
        for(int i=0;i<periods.Length;i++)Tint(periods[i],i==(int)Period);
        foreach(var row in rows)
            if(row && !Data.tags.Any(tag=>tag.id==row.name))row.gameObject.SetActive(false);
        rows.Clear();
        foreach(var tag in Data.tags)
        {
            if(!rowsByTag.TryGetValue(tag.id,out var row))
            {
                row=Instantiate(tagTemplate,tagContent);row.name=tag.id;
                string id=tag.id;row.onClick.AddListener(()=>{SelectedTag=id;RefreshGraph();});
                rowsByTag.Add(id,row);
            }
            row.transform.SetAsLastSibling();row.gameObject.SetActive(true);
            var label=row.transform.Find("Text").GetComponent<TMP_Text>();label.richText=false;label.textWrappingMode=TextWrappingModes.NoWrap;label.overflowMode=TextOverflowModes.Ellipsis;label.text=tag.name;
            row.transform.Find("Duration").GetComponent<TMP_Text>().text=FocusStatistics.Duration(tag.seconds);
            row.transform.Find("Swatch").GetComponent<Image>().color=chartColors[tag.id];
            var bar=row.transform.Find("TimeTrack/Fill").GetComponent<Image>();
            bar.color=chartColors[tag.id];
            // Compare tag durations against the longest tag, including legacy overlapping tags.
            // This is not a percentage of unique session time.
            float fraction=Data.tags.Count>0 && Data.tags[0].seconds>0?(float)(tag.seconds/Data.tags[0].seconds):0;
            bar.rectTransform.anchorMax=new Vector2(fraction,1);
            rows.Add(row);
        }
        if(!Data.tags.Any(t=>t.id==SelectedTag))SelectedTag=null;
        RefreshGraph();
    }
    void RefreshGraph()
    {
        var selected=Data.tags.FirstOrDefault(t=>t.id==SelectedTag);
        var values=selected?.buckets??Data.buckets;
        displayedSeries=selected==null?Data.tags.ToArray():new[]{selected};
        graph.SetSeries(displayedSeries.Select(t=>t.buckets).ToArray(),displayedSeries.Select(t=>chartColors[t.id]).ToArray(),values.Length);
        chartTitle.richText=false;chartTitle.text=selected==null?"Trend":selected.name;
        empty.gameObject.SetActive(values.All(v=>v<=0));empty.text="No sessions yet";
        for(int i=0;i<xLabels.Length;i++)
        {
            bool visible=i<values.Length&&(values.Length<=12||i% (Period==FocusStatisticsPeriod.Day?4:5)==0||i==values.Length-1);
            xLabels[i].gameObject.SetActive(visible);
            if(!visible)continue;
            xLabels[i].text=Data.labels[i];var rect=xLabels[i].rectTransform;rect.anchorMin=rect.anchorMax=new Vector2((i+.5f)/values.Length,0);rect.anchoredPosition=new Vector2(0,-22);
        }
        for(int i=0;i<yLabels.Length;i++)yLabels[i].text=(graph.Maximum/3600*i/4).ToString("0.##",CultureInfo.InvariantCulture)+"h";
        foreach(var row in rows)Tint(row,row.name==SelectedTag);Tint(allTags,SelectedTag==null);Hover(-1,-1);
        if(allTagsIcon)allTagsIcon.color=SelectedTag==null?Color.white:TaskUIBuilder.Ink;
    }
    void Hover(int index,int segment)
    {
        if(index<0 || Data==null || index>=Data.buckets.Length){detail.text="";return;}
        var tag=segment>=0 && segment<displayedSeries.Length?displayedSeries[segment]:null;
        var selected=Data.tags.FirstOrDefault(t=>t.id==SelectedTag);
        double seconds=tag!=null?tag.buckets[index]:selected!=null?selected.buckets[index]:Data.buckets[index];
        detail.richText=false;
        detail.text=Data.labels[index]+(Period==FocusStatisticsPeriod.Day?":00":"")+" / "+(tag?.name??selected?.name??"Total")+" / "+FocusStatistics.Duration(seconds);
    }
    void BuildColors()
    {
        int version=service.ReadVersion;
        if(ReferenceEquals(colorService,service) && colorVersion==version)return;
        chartColors.Clear();
        chartColors[FocusStatistics.Untagged]=new Color32(115,139,157,255);
        // Include the whole catalog/history so changing the date cannot recolor a tag.
        foreach(var tag in service.FocusStatisticsTagCatalog())
        {
            string id=tag.id;
            if(chartColors.ContainsKey(id))continue;
            Color candidate=Color.Lerp(TagColors.Background(tag.colorHex??"#5CA6CE"),TaskUIBuilder.Ink,.15f);
            uint hash=2166136261;foreach(char c in id)hash=unchecked((hash^c)*16777619);
            for(int attempt=0;attempt<32 && chartColors.Values.Any(c=>Vector3.Distance(new Vector3(c.r,c.g,c.b),new Vector3(candidate.r,candidate.g,candidate.b))<.24f);attempt++)
                candidate=Color.HSVToRGB((hash%360/360f+attempt*.618034f)%1f,.65f,.83f);
            chartColors[id]=candidate;
        }
        // A reset/import may remove historical tags; retire only those pooled rows.
        foreach(string id in rowsByTag.Keys.Where(id=>!chartColors.ContainsKey(id)).ToArray())
        {
            var row=rowsByTag[id];row.gameObject.SetActive(false);Destroy(row.gameObject);rowsByTag.Remove(id);
        }
        colorService=service;colorVersion=version;
    }
    static void Tint(Button button,bool selected) {
        if(button.targetGraphic)button.targetGraphic.color=selected?new Color32(96,115,168,255):new Color32(231,239,250,255);
        var label=button.transform.Find("Text")?.GetComponent<TMP_Text>();
        if(label)label.color=selected?Color.white:TaskUIBuilder.Ink;
        var duration=button.transform.Find("Duration");
        if(duration)duration.GetComponent<TMP_Text>().color=selected?Color.white:TaskUIBuilder.ScheduleInk;
    }
}
