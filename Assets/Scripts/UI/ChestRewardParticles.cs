using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Sprites;

// Lightweight UI particles: no world camera, input surface or reward ownership.
public sealed class ChestRewardParticles : MaskableGraphic
{
    public Sprite particleSprite;
    float elapsed;
    public override Texture mainTexture => particleSprite ? particleSprite.texture : Texture2D.whiteTexture;
    protected override void OnEnable(){base.OnEnable();elapsed=0;raycastTarget=false;SetVerticesDirty();}
    void Update(){elapsed+=Time.unscaledDeltaTime;SetVerticesDirty();}
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();if(!particleSprite)return;
        var rect=rectTransform.rect;var uv=DataUtility.GetOuterUV(particleSprite);
        for(int i=0;i<9;i++){
            float phase=Mathf.Repeat(elapsed*.65f+i*.381966f,1);
            float angle=i*2.39996f;
            var center=rect.center+new Vector2(Mathf.Cos(angle)*rect.width*.44f,Mathf.Sin(angle)*rect.height*.40f+phase*rect.height*.12f);
            float size=Mathf.Min(rect.width,rect.height)*(.065f+.025f*(i%3))*Mathf.Sin(phase*Mathf.PI);
            Color tint=color;tint.a*=Mathf.Sin(phase*Mathf.PI)*.85f;
            int start=mesh.currentVertCount;
            mesh.AddVert(new Vector3(center.x-size,center.y-size),tint,new Vector2(uv.x,uv.y));
            mesh.AddVert(new Vector3(center.x-size,center.y+size),tint,new Vector2(uv.x,uv.w));
            mesh.AddVert(new Vector3(center.x+size,center.y+size),tint,new Vector2(uv.z,uv.w));
            mesh.AddVert(new Vector3(center.x+size,center.y-size),tint,new Vector2(uv.z,uv.y));
            mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);
        }
    }
}
