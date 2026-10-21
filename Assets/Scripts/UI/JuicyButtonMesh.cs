using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Move rendered vertices only: layout, hierarchy and pointer hit areas stay intact.
public sealed class JuicyButtonMesh : BaseMeshEffect
{
    readonly System.Collections.Generic.List<JuicyButton> owners=new System.Collections.Generic.List<JuicyButton>();
    public void Register(JuicyButton owner)
    {
        if(owners.Contains(owner))return;
        owners.Add(owner);
        // Child feedback first, then the containing row's shared transformation.
        owners.Sort((a,b)=>a==b?0:a.transform.IsChildOf(b.transform)?-1:b.transform.IsChildOf(a.transform)?1:0);
        Refresh();
    }
    float rewardAge = .32f, rewardStart = 1;
    public float RewardScale { get; private set; } = 1;
    public void PlayRewardPulse() { rewardStart = RewardScale; rewardAge = 0; }
    public void ResetRewardPulse() { rewardAge = .32f; RewardScale = 1; Refresh(); }
    void Update()
    {
        if (rewardAge >= .32f) return;
        rewardAge = Mathf.Min(.32f, rewardAge + Time.unscaledDeltaTime);
        if (rewardAge < .08f)
            RewardScale = Mathf.Lerp(rewardStart, 1.24f, 1 - Mathf.Pow(1 - rewardAge / .08f, 3));
        else
            RewardScale = Mathf.Lerp(1.24f, 1, Mathf.SmoothStep(0, 1, (rewardAge - .08f) / .24f));
        Refresh();
    }
    void OnApplicationFocus(bool focused) { if (!focused) ResetRewardPulse(); }
    TMP_Text label;
    protected override void OnEnable(){base.OnEnable();label=GetComponent<TMP_Text>();if(label)label.OnPreRenderText+=ModifyText;}
    protected override void OnDisable(){ResetRewardPulse();if(label)label.OnPreRenderText-=ModifyText;base.OnDisable();}
    void ModifyText(TMP_TextInfo info)
    {
        for(int m=0;m<info.meshInfo.Length;m++){
            var mesh=info.meshInfo[m];
            for(int i=0;i<mesh.vertexCount;i++)mesh.vertices[i]=TransformPoint(mesh.vertices[i]);
        }
    }
    Vector3 TransformPoint(Vector3 position)
    {
        Vector3 center = graphic.rectTransform.rect.center;
        position = center + (position - center) * RewardScale;
        Vector3 world=graphic.rectTransform.TransformPoint(position);
        foreach(var owner in owners){
            if(!owner || !owner.isActiveAndEnabled || owner.visual)continue;
            var root=(RectTransform)owner.transform;Vector3 pivot=root.rect.center;
            Vector3 point=root.InverseTransformPoint(world);
            point=pivot+Vector3.Scale(point-pivot,new Vector3(owner.DrawScale.x,owner.DrawScale.y,1));point.y+=owner.DrawOffset;
            world=root.TransformPoint(point);
        }
        return graphic.rectTransform.InverseTransformPoint(world);
    }
    public override void ModifyMesh(VertexHelper mesh)
    {
        if(!IsActive())return;
        float shine=0;
        foreach(var owner in owners)if(owner && owner.isActiveAndEnabled)shine=Mathf.Max(shine,owner.DrawShine);
        var vertex=new UIVertex();
        for(int i=0;i<mesh.currentVertCount;i++){
            mesh.PopulateUIVertex(ref vertex,i);
            vertex.position=TransformPoint(vertex.position);
            Color tint=vertex.color;
            vertex.color=new Color(Mathf.Lerp(tint.r,1,shine),Mathf.Lerp(tint.g,1,shine),Mathf.Lerp(tint.b,1,shine),tint.a);
            mesh.SetUIVertex(vertex,i);
        }
    }
    public void Refresh()
    {
        // TMP skips regenerating its mesh unless its own dirty flag is set.
        // Regenerate from the original glyph positions before applying each frame's
        // feedback, so labels follow the background and return to their exact origin.
        if(label)label.havePropertiesChanged=true;
        if(graphic)graphic.SetVerticesDirty();
    }
}
