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
    bool reducedLast;
    RectTransform reward;
    RectTransform legendaryCard;
    public bool IsLegendaryActive => legendaryCard && gameObject.activeInHierarchy;
    public float LegendaryAge => elapsed;
    JuicyButtonMesh rewardPulse;
    public bool IsCelebrating => reward;
    const float RevealDuration=.95f;
    public override Texture mainTexture => Texture2D.whiteTexture;

    public void PlayLegendary(RectTransform card)
    {
        if (legendaryCard == card && IsLegendaryActive) return;
        gameObject.SetActive(true);legendaryCard=card;elapsed=0;focused=true;
        raycastTarget=false;SetVerticesDirty();
    }
    public void StopLegendary()
    {
        legendaryCard=null;elapsed=0;gameObject.SetActive(false);
    }

    // Presentation follows committed ownership; reuse the shared mesh-only pulse.
    public void PlayCollected(Image icon)
    {
        if(UiMotion.Reduced){StopCollected();ResetChest();return;}
        gameObject.SetActive(true);
        ResetChest();reward=icon.rectTransform;elapsed=0;
        rewardPulse=icon.GetComponent<JuicyButtonMesh>();
        if(rewardPulse)rewardPulse.PlayRewardReveal();
        SetVerticesDirty();
    }
    public void StopCollected()
    {
        if(!reward)return;
        reward=null;
        if(rewardPulse)rewardPulse.ResetRewardPulse();
        rewardPulse=null;gameObject.SetActive(false);
    }

    public void Bind(RectTransform target)
    {
        ResetChest();chest=target;
        if(chest)restRotation=chest.localRotation;
    }
    protected override void OnEnable(){base.OnEnable();focused=Application.isFocused;elapsed=0;raycastTarget=false;SetVerticesDirty();}
    void Update()
    {
        bool reduced=UiMotion.Reduced;
        if(reduced!=reducedLast){reducedLast=reduced;ResetChest();SetVerticesDirty();}
        if(reduced){StopCollected();return;}
        if(!focused)return;
        elapsed+=Time.unscaledDeltaTime;
        if(legendaryCard){SetVerticesDirty();return;}
        if(reward){
            if(elapsed>=RevealDuration)StopCollected();
            else SetVerticesDirty();
            return;
        }
        if(chest){
            float phase=(Mathf.Repeat(elapsed,5.2f)-1.2f)/.7f;
            float angle=phase>0&&phase<1?10f*Mathf.Sin(phase*Mathf.PI*6)*Mathf.Sin(phase*Mathf.PI):0;
            chest.localRotation=restRotation*Quaternion.Euler(0,0,angle);
        }
        SetVerticesDirty();
    }
    void ResetChest(){if(chest)chest.localRotation=restRotation;}
    void OnApplicationFocus(bool value){focused=value;if(!value)StopCollected();elapsed=0;ResetChest();SetVerticesDirty();}
    protected override void OnDisable(){StopCollected();legendaryCard=null;elapsed=0;ResetChest();base.OnDisable();}
    protected override void OnDestroy(){ResetChest();base.OnDestroy();}

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();if(UiMotion.Reduced)return;if(legendaryCard){DrawLegendary(mesh);return;}if(reward){DrawCollected(mesh);return;}if(!chest)return;
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
    // SimpleCasual reward sparkles stay around the card silhouette. Nothing moves
    // the control, covers the equipment centre, or changes its hit area.
    void DrawLegendary(VertexHelper mesh)
    {
        var source=legendaryCard.rect;
        Vector2 center=rectTransform.InverseTransformPoint(legendaryCard.TransformPoint(source.center));
        Vector2 half=source.size*.5f;
        float breathe=.85f+.15f*Mathf.Sin(elapsed*1.8f);
        Outline(mesh,center,half+Vector2.one*5,7,new Color(1,.64f,.05f,.18f*breathe));
        Outline(mesh,center,half,3,new Color(1,.66f,.07f,.92f));
        Outline(mesh,center,half-Vector2.one*3,1,new Color(1,1,.72f,.85f));
        // A single expanding outline marks arrival, then the quiet loop takes over.
        if(elapsed<.85f){float t=elapsed/.85f;Outline(mesh,center,half+Vector2.one*(2+12*t),2*(1-t),new Color(1,.76f,.12f,(1-t)*.7f));}
        for(int i=0;i<10;i++){
            float phase=Mathf.Repeat(elapsed*(.22f+i*.008f)+i*.618f,1);
            Vector2 edge=Edge(half,(i*.137f+.04f)%1);
            Vector2 direction=edge.normalized;
            Vector2 point=center+edge+direction*(3+phase*11)+Vector2.up*(phase*10);
            float alpha=Mathf.Pow(Mathf.Sin(phase*Mathf.PI),.65f);
            float size=i%3==0?6.2f:4.2f;
            SoftDisc(mesh,point,Vector2.one*size*2.2f,new Color(1,.67f,.04f,alpha*.5f));
            SoftDisc(mesh,point,Vector2.one*size,new Color(1,.82f,.22f,alpha));
            SoftDisc(mesh,point,Vector2.one*size*.65f,new Color(1,1,.94f,alpha));
        }
        for(int i=0;i<4;i++){
            float t=Mathf.Repeat(elapsed*.075f+i*.25f,1);
            Vector2 point=center+Edge(half,t);
            float size=13f+6f*Mathf.Pow(.5f+.5f*Mathf.Sin(elapsed*3.2f+i*1.9f),3);
            SoftDisc(mesh,point,Vector2.one*size*1.6f,new Color(1,.65f,.04f,.48f));
            Star(mesh,point,size,new Color(1,.76f,.12f,1));
            Star(mesh,point,size*.72f,new Color(1,1,.97f,1));
        }
    }
    static Vector2 Corner(Vector2 half,int index)
    {
        const int perCorner=9;
        int quadrant=index/perCorner;float a=(quadrant+(index%perCorner)/(float)(perCorner-1))*Mathf.PI*.5f;
        float radius=Mathf.Min(24,Mathf.Min(half.x,half.y)*.28f);
        var c=new Vector2((quadrant==0||quadrant==3?1:-1)*(half.x-radius),(quadrant<2?1:-1)*(half.y-radius));
        return c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;
    }
    static Vector2 Edge(Vector2 half,float phase)
    {
        // Interpolate around rounded corners AND straight sides at uniform speed.
        float radius=Mathf.Min(24,Mathf.Min(half.x,half.y)*.28f);
        float x=half.x-radius,y=half.y-radius,arc=Mathf.PI*radius*.5f;
        float distance=phase*(4*x+4*y+4*arc);
        for(int q=0;q<4;q++){
            float straight=q%2==0?2*y:2*x;
            if(distance<straight){
                float u=distance/straight;
                if(q==0)return new Vector2(half.x,Mathf.Lerp(-y,y,u));
                if(q==1)return new Vector2(Mathf.Lerp(x,-x,u),half.y);
                if(q==2)return new Vector2(-half.x,Mathf.Lerp(y,-y,u));
                return new Vector2(Mathf.Lerp(-x,x,u),-half.y);
            }
            distance-=straight;
            if(distance<arc){float a=(q+distance/arc)*Mathf.PI*.5f;return new Vector2(q==0||q==3?x:-x,q<2?y:-y)+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;}
            distance-=arc;
        }
        return new Vector2(half.x,-y);
    }
    static void Outline(VertexHelper mesh,Vector2 center,Vector2 half,float width,Color tint)
    {
        for(int i=0;i<36;i++){
            int n=mesh.currentVertCount,j=(i+1)%36;
            mesh.AddVert(center+Corner(half,i),tint,Vector2.zero);
            mesh.AddVert(center+Corner(half-Vector2.one*width,i),tint,Vector2.zero);
            mesh.AddVert(center+Corner(half-Vector2.one*width,j),tint,Vector2.zero);
            mesh.AddVert(center+Corner(half,j),tint,Vector2.zero);
            mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
        }
    }
    static void Star(VertexHelper mesh,Vector2 center,float radius,Color tint)
    {
        int start=mesh.currentVertCount;mesh.AddVert(center,tint,Vector2.zero);
        for(int i=0;i<=8;i++){
            float a=i*Mathf.PI/4,r=i%2==0?radius:radius*.22f;
            mesh.AddVert(center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,tint,Vector2.zero);
            if(i>0)mesh.AddTriangle(start,start+i,start+i+1);
        }
    }
    void DrawCollected(VertexHelper mesh)
    {
        Vector2 center=rectTransform.InverseTransformPoint(reward.TransformPoint(reward.rect.center));
        float size=Mathf.Min(reward.rect.width,reward.rect.height);
        float t=Mathf.Clamp01(elapsed/RevealDuration),ease=1-Mathf.Pow(1-t,3);
        float fade=1-Mathf.SmoothStep(.35f,1,t);
        SoftDisc(mesh,center,Vector2.one*size*(.45f+.25f*ease),new Color(1,.52f,.04f,.95f*fade));
        SoftDisc(mesh,center,Vector2.one*size*.42f,new Color(1,1,.75f,.9f*fade));
        // Expanding ring makes the reveal readable even on the yellow tier card.
        float radiusRing=size*(.30f+.37f*ease),thickness=size*.035f*(1-t);
        for(int i=0;i<48;i++){
            float a=i*Mathf.PI*2/48,b=(i+1)*Mathf.PI*2/48;
            Vector2 da=new Vector2(Mathf.Cos(a),Mathf.Sin(a)),db=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
            int n=mesh.currentVertCount;
            Color rim=new Color(1,.52f,.02f,fade*.9f),light=new Color(1,1,.8f,fade);
            mesh.AddVert(center+da*(radiusRing-thickness),light,Vector2.zero);
            mesh.AddVert(center+da*(radiusRing+thickness),rim,Vector2.zero);
            mesh.AddVert(center+db*(radiusRing+thickness),rim,Vector2.zero);
            mesh.AddVert(center+db*(radiusRing-thickness),light,Vector2.zero);
            mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
        }
        // Four-point stars echo SimpleCasual Popup_Reward_Get's Image_Sparkle.
        for(int i=0;i<8;i++){
            float angle=(i+.5f)*Mathf.PI/4;
            Vector2 direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
            Vector2 point=center+direction*size*(.35f+.30f*ease);
            float radius=size*(i%2==0?.14f:.10f)*Mathf.Sin(Mathf.PI*Mathf.Sqrt(t));
            SoftDisc(mesh,point,Vector2.one*radius*2,new Color(1,.65f,.12f,fade*.4f));
            int start=mesh.currentVertCount;
            mesh.AddVert(point,new Color(1,1,.82f,fade),Vector2.zero);
            for(int n=0;n<=8;n++){
                float a=n*Mathf.PI/4;
                float r=n%2==0?radius:radius*.25f;
                mesh.AddVert(point+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,new Color(1,.48f,.04f,fade),Vector2.zero);
                if(n>0)mesh.AddTriangle(start,start+n,start+n+1);
            }
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
