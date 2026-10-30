using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One batched mesh for the grid and bars. Hover changes only the rendered mesh.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class FocusStatisticsGraph : MaskableGraphic, IPointerMoveHandler, IPointerExitHandler, IPointerClickHandler
{
    public event Action<int, int> Hovered;
    double[] values = Array.Empty<double>();
    double[][] series = Array.Empty<double[]>();
    Color[] colors = Array.Empty<Color>();
    int hoveredSeries = -1;
    double maximum = 3600;
    int hovered = -1;
    public double Maximum => maximum;
    public int SeriesCount => series.Length;
    public void SetSeries(double[][] data, Color[] tints, int bucketCount)
    {
        series = data ?? Array.Empty<double[]>(); colors = tints ?? Array.Empty<Color>();
        values = new double[bucketCount]; hovered = hoveredSeries = -1;
        for (int j=0;j<series.Length;j++)
            for(int i=0;i<bucketCount;i++) values[i] += series[j][i];
        double peak=0;foreach(var value in values)peak=Math.Max(peak,value);
        maximum=Math.Max(3600,Math.Ceiling(peak/3600)*3600);
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); var rect = rectTransform.rect;
        for (int i = 0; i <= 4; i++) Quad(vh, new Rect(rect.xMin, rect.yMin + rect.height * i / 4, rect.width, 1), new Color32(214, 229, 240, 255));
        if (values.Length == 0) return;
        float slot = rect.width / values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            double bottom=0;
            for(int j=0;j<series.Length;j++)
            {
                double value=series[j][i];
                if(value<=0)continue;
                float y=(float)(bottom/maximum)*(rect.height-2);
                float height=(float)(value/maximum)*(rect.height-2);
                var tint=colors[j];
                if(i==hovered && (hoveredSeries<0 || j==hoveredSeries))tint=Color.Lerp(tint,Color.white,.28f);
                // Exact segment heights keep tiny durations from inflating a stack.
                Quad(vh,new Rect(rect.xMin+slot*i+slot*.18f,rect.yMin+1+y,slot*.64f,height),tint);
                bottom+=value;
            }
        }
    }
    static void Quad(VertexHelper vh, Rect r, Color tint)
    {
        int n = vh.currentVertCount;
        vh.AddVert(new Vector3(r.xMin,r.yMin),tint,Vector2.zero); vh.AddVert(new Vector3(r.xMin,r.yMax),tint,Vector2.zero);
        vh.AddVert(new Vector3(r.xMax,r.yMax),tint,Vector2.zero); vh.AddVert(new Vector3(r.xMax,r.yMin),tint,Vector2.zero);
        vh.AddTriangle(n,n+1,n+2); vh.AddTriangle(n,n+2,n+3);
    }
    public void OnPointerMove(PointerEventData e)
    {
        if (values.Length == 0 || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,e.position,e.pressEventCamera,out var point)) return;
        int index = Mathf.Clamp((int)((point.x-rectTransform.rect.xMin)/rectTransform.rect.width*values.Length),0,values.Length-1);
        int segment=-1;
        float slot=rectTransform.rect.width/values.Length;
        float within=point.x-rectTransform.rect.xMin-index*slot;
        double height=(point.y-rectTransform.rect.yMin-1)/(rectTransform.rect.height-2)*maximum;
        if(within>=slot*.18f && within<=slot*.82f && height>=0)
        {
            double bottom=0;
            for(int j=0;j<series.Length;j++)
            {
                double top=bottom+series[j][index];
                if(height>=bottom && height<top){segment=j;break;}
                bottom=top;
            }
        }
        if(hovered==index && hoveredSeries==segment)return;
        hovered=index;hoveredSeries=segment;SetVerticesDirty();Hovered?.Invoke(index,segment);
    }
    public void OnPointerClick(PointerEventData e) => OnPointerMove(e);
    public void OnPointerExit(PointerEventData e) { hovered=hoveredSeries=-1;SetVerticesDirty();Hovered?.Invoke(-1,-1); }
}
