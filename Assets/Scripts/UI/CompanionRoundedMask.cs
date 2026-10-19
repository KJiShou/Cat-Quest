using UnityEngine;
using UnityEngine.UI;

public sealed class CompanionRoundedMask : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();var r=rectTransform.rect;float radius=Mathf.Min(16,r.width/2,r.height/2);
        mesh.AddVert(r.center,Color.white,Vector2.zero);
        var centers=new[]{new Vector2(r.xMax-radius,r.yMax-radius),new Vector2(r.xMin+radius,r.yMax-radius),new Vector2(r.xMin+radius,r.yMin+radius),new Vector2(r.xMax-radius,r.yMin+radius)};
        for(int corner=0;corner<4;corner++)for(int i=0;i<=12;i++){
            float a=(corner*90+i*7.5f)*Mathf.Deg2Rad;
            mesh.AddVert(centers[corner]+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,Color.white,Vector2.zero);
        }
        int count=mesh.currentVertCount-1;for(int i=1;i<=count;i++)mesh.AddTriangle(0,i,i==count?1:i+1);
    }
}
