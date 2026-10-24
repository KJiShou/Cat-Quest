using UnityEngine;
using UnityEngine.UI;

// Presentation only: light from the open chest, fireflies and an occasional gentle wobble.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ChestRewardParticles : MaskableGraphic
{
    // Retain the previous serialized reference; the new soft light uses an untextured mesh.
    [HideInInspector] public Sprite particleSprite;
    RectTransform chest;
    Quaternion restRotation;
    float elapsed;
    bool focused=true;
    public override Texture mainTexture => Texture2D.whiteTexture;

    public void Bind(RectTransform target)
    {
        ResetChest();chest=target;
        if(chest)restRotation=chest.localRotation;
    }
    protected override void OnEnable(){base.OnEnable();elapsed=0;raycastTarget=false;SetVerticesDirty();}
    void Update()
    {
        if(!focused)return;
        elapsed+=Time.unscaledDeltaTime;
        if(chest){
            float phase=(Mathf.Repeat(elapsed,5.2f)-1.2f)/.7f;
            float angle=phase>0&&phase<1?10f*Mathf.Sin(phase*Mathf.PI*6)*Mathf.Sin(phase*Mathf.PI):0;
            chest.localRotation=restRotation*Quaternion.Euler(0,0,angle);
        }
        SetVerticesDirty();
    }
    void ResetChest(){if(chest)chest.localRotation=restRotation;}
    void OnApplicationFocus(bool value){focused=value;elapsed=0;ResetChest();SetVerticesDirty();}
    protected override void OnDisable(){elapsed=0;ResetChest();base.OnDisable();}
    protected override void OnDestroy(){ResetChest();base.OnDestroy();}

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();if(!chest)return;
        var bounds=chest.rect;
        float size=Mathf.Min(bounds.width,bounds.height);
        // The opening of SimpleCasual's open chest is just above the front gold rim.
        Vector2 opening=rectTransform.InverseTransformPoint(chest.TransformPoint(bounds.center+new Vector2(0,size*.10f)));
        float breathe=.88f+.12f*Mathf.Sin(elapsed*1.7f);
        const int columns=12,rows=10;
        for(int row=0;row<=rows;row++){
            float v=(float)row/rows;
            for(int col=0;col<=columns;col++){
                float u=(float)col/columns*2-1;
                float edge=Mathf.Cos(u*Mathf.PI*.5f);
                Color light=Color.Lerp(new Color(1,.48f,.02f),new Color(1,.67f,.06f),v);
                light.a=breathe*edge*edge*Mathf.Pow(1-v,.75f)*color.a;
                mesh.AddVert(opening+new Vector2(u*size*Mathf.Lerp(.27f,.52f,v),size*.88f*v),light,Vector2.zero);
                if(row>0&&col>0){int n=row*(columns+1)+col;mesh.AddTriangle(n,n-1,n-columns-2);mesh.AddTriangle(n,n-columns-2,n-columns-1);}
            }
        }
        SoftDisc(mesh,opening,new Vector2(size*.34f,size*.15f),new Color(1,.55f,.03f,breathe));
        SoftDisc(mesh,opening,new Vector2(size*.22f,size*.07f),new Color(1,1,.82f,breathe));
        for(int i=0;i<5;i++){
            float phase=Mathf.Repeat(elapsed*.23f+i*.217f,1);
            float alpha=Mathf.Pow(Mathf.Sin(phase*Mathf.PI),.55f)*(.85f+.15f*Mathf.Sin(elapsed*2+i));
            var point=opening+new Vector2((Mathf.Sin(i*2.4f)*.29f+Mathf.Sin(elapsed*.8f+i)*.055f)*size,(.16f+phase*.70f)*size);
            float radius=size*(.05f+.007f*(i%2));
            SoftDisc(mesh,point,Vector2.one*radius*2.8f,new Color(1,.48f,.02f,alpha*.72f));
            SoftDisc(mesh,point,Vector2.one*radius,new Color(1,.59f,.03f,alpha));
            SoftDisc(mesh,point,Vector2.one*radius*.42f,new Color(1,1,.80f,alpha));
        }
    }
    void SoftDisc(VertexHelper mesh,Vector2 center,Vector2 radius,Color tint)
    {
        int start=mesh.currentVertCount;tint.a*=color.a;
        mesh.AddVert(center,tint,Vector2.zero);tint.a=0;
        const int segments=24;
        for(int i=0;i<=segments;i++){
            float angle=i*Mathf.PI*2/segments;
            mesh.AddVert(center+new Vector2(Mathf.Cos(angle)*radius.x,Mathf.Sin(angle)*radius.y),tint,Vector2.zero);
            if(i>0)mesh.AddTriangle(start,start+i,start+i+1);
        }
    }
}
