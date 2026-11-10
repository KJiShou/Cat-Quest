using UnityEngine;

// Presentation only: a fixed body anchor, independent of animated sprite bounds.
[DefaultExecutionOrder(250)]
public sealed class MasterBossAura : MonoBehaviour
{
    public ParticleSystem halo, motes;
    public Color[] bossColors = { new Color(1,.55f,.12f), new Color(.65f,.22f,1), new Color(.85f,.08f,.12f) };
    public float sizeMultiplier=2.2f, breathAmount=.035f, breathPeriod=3.2f;
    Transform owner;
    Vector3 offset, restScale;
    float age;
    bool motionKnown,reducedLast;
    public void Bind(Transform boss,Transform cat,int bossId)
    {
        if(owner==boss)return;
        owner=boss;age=0;
        var sprites=boss.GetComponentsInChildren<SpriteRenderer>();
        if(sprites.Length==0)return;
        var bounds=sprites[0].bounds;int order=sprites[0].sortingOrder;
        foreach(var sprite in sprites){bounds.Encapsulate(sprite.bounds);order=Mathf.Min(order,sprite.sortingOrder);}
        foreach(var sprite in cat.GetComponentsInChildren<SpriteRenderer>())order=Mathf.Min(order,sprite.sortingOrder);
        offset=bounds.center-boss.position;offset.y-=bounds.size.y*.06f;
        restScale=Vector3.one*(bounds.size.y*sizeMultiplier);
        var color=bossColors[Mathf.Clamp(bossId-1,0,bossColors.Length-1)];
        foreach(var ps in new[]{halo,motes}){
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.startColor=new Color(color.r,color.g,color.b,ps==halo?.65f:.7f);
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sortingLayerID=sprites[0].sortingLayerID;renderer.sortingOrder=order-1;
            if(!UiMotion.Reduced)ps.Play(true);
        }
        Follow();
    }
    void Follow(){if(owner){transform.position=owner.position+offset;transform.rotation=Quaternion.identity;transform.localScale=restScale*(UiMotion.Reduced?1:1+breathAmount*Mathf.Sin(age*2*Mathf.PI/Mathf.Max(.1f,breathPeriod)));}}
    void LateUpdate(){
        bool reduced=UiMotion.Reduced;
        if(!motionKnown||reduced!=reducedLast){
            motionKnown=true;reducedLast=reduced;
            if(reduced){halo.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);motes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
            else if(owner){halo.Play(true);motes.Play(true);}
        }
        if(!reduced)age+=Time.unscaledDeltaTime;Follow();
    }
    void OnDisable(){motionKnown=false;age=0;transform.localScale=restScale;}
}
