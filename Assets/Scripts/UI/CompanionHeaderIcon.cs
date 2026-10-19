using UnityEngine;
using UnityEngine.UI;

// Small vector controls remain crisp at every window size; no hover tooltips.
public sealed class CompanionHeaderIcon : MaskableGraphic
{
    public enum Icon { Target, Grip, More, Close }
    public Icon icon;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); var center = rectTransform.rect.center;
        if (icon == Icon.Target) { Ring(mesh,center,9,1.4f); Ring(mesh,center,5.5f,1.4f); Ring(mesh,center,2,2); }
        else if (icon == Icon.Grip) for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++) Ring(mesh,center+new Vector2(x*5,y*5),1.4f,1.4f);
        else if (icon == Icon.More) for(int x=-1;x<=1;x++) Ring(mesh,center+new Vector2(x*5,0),1.7f,1.7f);
        else { Line(mesh,center+new Vector2(-5,-5),center+new Vector2(5,5));Line(mesh,center+new Vector2(-5,5),center+new Vector2(5,-5)); }
    }
    void Ring(VertexHelper mesh,Vector2 center,float radius,float thickness)
    {
        for(int i=0;i<32;i++) {
            float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16;
            var u=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var v=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
            Quad(mesh,center+u*radius,center+v*radius,center+v*(radius-thickness),center+u*(radius-thickness));
        }
    }
    void Line(VertexHelper mesh,Vector2 a,Vector2 b) { var n=new Vector2(-(b-a).y,(b-a).x).normalized*.9f;Quad(mesh,a+n,b+n,b-n,a-n); }
    void Quad(VertexHelper mesh,Vector2 a,Vector2 b,Vector2 c,Vector2 d)
    {
        int i=mesh.currentVertCount;mesh.AddVert(a,color,Vector2.zero);mesh.AddVert(b,color,Vector2.zero);mesh.AddVert(c,color,Vector2.zero);mesh.AddVert(d,color,Vector2.zero);
        mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);
    }
}
