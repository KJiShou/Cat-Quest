using System;
using TMPro;
using UnityEngine;

// Bounded reusable pool: simultaneous/fast hits keep separate numbers without changing combat RNG.
public sealed class BattleDamageNumbers : MonoBehaviour
{
    sealed class Number
    {
        public TMP_Text text;
        public Vector2 origin,velocity;
        public float age;
        public bool active;
    }
    readonly Number[] pool=new Number[32];
    readonly System.Random random=new System.Random();
    int cursor,catSequence,bossSequence;
    const float Lifetime=.95f,Gravity=430;
    public int ActiveCount {get {int n=0;foreach(var p in pool)if(p!=null&&p.active)n++;return n;}}
    public void Initialize(TMP_Text template)
    {
        if(pool[0]!=null)return;
        for(int i=0;i<pool.Length;i++) {
            var text=Instantiate(template,template.transform.parent);text.name="DamageNumber"+i;
            text.fontSize=36;text.fontSizeMax=36;text.fontSizeMin=24;text.raycastTarget=false;
            text.outlineColor=new Color32(20,35,55,255);text.outlineWidth=.18f;
            text.gameObject.SetActive(false);pool[i]=new Number{text=text};
        }
    }
    public void Spawn(int damage,bool onCat,Transform target,Camera camera)
    {
        if(pool[0]==null||!target||!camera)return;
        var point=camera.WorldToViewportPoint(target.position+Vector3.up*3);
        if(point.z<=0)return;
        var n=pool[cursor];cursor=(cursor+1)%pool.Length;
        int sequence=onCat?catSequence++:bossSequence++;
        // Alternate left/centre/right lanes so rapid impacts remain readable.
        int lane=sequence%3-1;
        n.origin=new Vector2(lane*62+((float)random.NextDouble()-.5f)*10,(sequence%2)*12);
        n.velocity=new Vector2(lane*65+((float)random.NextDouble()-.5f)*25,145+(float)random.NextDouble()*40);
        n.age=0;n.active=true;n.text.text="−"+damage;
        n.text.color=onCat?new Color32(255,111,126,255):new Color32(255,241,182,255);n.text.alpha=1;
        var rect=n.text.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(point.x,point.y);
        rect.anchoredPosition=n.origin;rect.localScale=Vector3.one*.55f;n.text.gameObject.SetActive(true);
    }
    void Update()
    {
        foreach(var n in pool){
            if(n==null||!n.active)continue;
            n.age+=Time.unscaledDeltaTime;
            if(n.age>=Lifetime){n.active=false;n.text.gameObject.SetActive(false);continue;}
            float t=n.age;
            n.text.rectTransform.anchoredPosition=n.origin+n.velocity*t+Vector2.down*(.5f*Gravity*t*t);
            float scale=t<.09f?Mathf.Lerp(.55f,1.4f,t/.09f):t<.23f?Mathf.Lerp(1.4f,1,(t-.09f)/.14f):1;
            n.text.rectTransform.localScale=Vector3.one*scale;
            n.text.alpha=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.45f,Lifetime,t));
        }
    }
    public void Clear(){foreach(var n in pool)if(n!=null){n.active=false;n.text.gameObject.SetActive(false);}}
    void OnDisable(){Clear();}
}
