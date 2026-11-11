using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Bounded reusable pool: simultaneous/fast hits keep separate numbers without changing combat RNG.
public sealed class BattleDamageNumbers : MonoBehaviour
{
    sealed class Number
    {
        public TMP_Text text;
        public Image criticalIcon;
        public Vector2 origin,velocity;
        public float age;
        public bool active;
    }
    readonly Number[] pool=new Number[32];
    readonly System.Random random=new System.Random();
    int cursor,catSequence,bossSequence;
    const float Lifetime=.95f,Gravity=430;
    public int ActiveCount {get {int n=0;foreach(var p in pool)if(p!=null&&p.active)n++;return n;}}
    public void Initialize(TMP_Text template,Sprite criticalSprite=null)
    {
        if(pool[0]!=null)return;
        for(int i=0;i<pool.Length;i++) {
            var text=Instantiate(template,template.transform.parent);text.name="DamageNumber"+i;
            text.fontSize=36;text.fontSizeMax=36;text.fontSizeMin=24;text.raycastTarget=false;
            text.outlineColor=new Color32(20,35,55,255);text.outlineWidth=.18f;
            var go=new GameObject("CriticalIcon",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));go.layer=text.gameObject.layer;go.transform.SetParent(text.transform,false);
            var icon=go.GetComponent<Image>();icon.sprite=criticalSprite;icon.preserveAspect=true;icon.raycastTarget=false;icon.color=new Color32(198,138,255,255);icon.rectTransform.sizeDelta=new Vector2(40,40);go.SetActive(false);
            text.gameObject.SetActive(false);pool[i]=new Number{text=text,criticalIcon=icon};
        }
    }
    public void Spawn(int damage,bool onCat,Transform target,Camera camera,bool critical=false)
    {
        if(pool[0]==null||!target||!camera)return;
        var point=camera.WorldToViewportPoint(target.position+Vector3.up*3);
        if(point.z<=0)return;
        var n=pool[cursor];cursor=(cursor+1)%pool.Length;
        int sequence=onCat?catSequence++:bossSequence++;
        // Alternate left/centre/right lanes so rapid impacts remain readable.
        int lane=sequence%3-1;
        // Longer ability labels sit above the actor's health bar, including the left lane.
        n.origin=new Vector2(lane*62+((float)random.NextDouble()-.5f)*10,(sequence%2)*12+(critical||damage==0?64:0));
        n.velocity=new Vector2(lane*65+((float)random.NextDouble()-.5f)*25,145+(float)random.NextDouble()*40);
        if(critical||damage==0){
            // Send long labels outwards, away from the cat bar and the other fighter's numbers.
            int side=onCat?-1:1;
            n.origin.x=side*(100+(sequence%2)*40);
            n.velocity.x=side*(25+(float)random.NextDouble()*20);
        }
        n.age=0;n.active=true;n.text.text=damage==0?"BLOCK":(critical?"":"−")+damage;
        n.text.color=damage==0?new Color32(137,255,220,255):critical?new Color32(198,138,255,255):onCat?new Color32(255,111,126,255):new Color32(255,241,182,255);n.text.alpha=1;
        var rect=n.text.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(point.x,point.y);
        rect.anchoredPosition=n.origin;rect.localScale=Vector3.one*(UiMotion.Reduced?1:.55f);n.text.gameObject.SetActive(true);
        n.criticalIcon.gameObject.SetActive(critical);
        if(critical){n.text.ForceMeshUpdate();var bounds=n.text.textBounds;n.criticalIcon.rectTransform.localPosition=new Vector3(bounds.min.x-26,bounds.center.y,0);var c=n.criticalIcon.color;c.a=1;n.criticalIcon.color=c;}
    }
    void Update()
    {
        foreach(var n in pool){
            if(n==null||!n.active)continue;
            n.age+=Time.unscaledDeltaTime;
            if(n.age>=Lifetime){n.active=false;n.text.gameObject.SetActive(false);continue;}
            float t=n.age;
            n.text.rectTransform.anchoredPosition=UiMotion.Reduced?n.origin:n.origin+n.velocity*t+Vector2.down*(.5f*Gravity*t*t);
            float scale=t<.09f?Mathf.Lerp(.55f,1.4f,t/.09f):t<.23f?Mathf.Lerp(1.4f,1,(t-.09f)/.14f):1;
            n.text.rectTransform.localScale=Vector3.one*(UiMotion.Reduced?1:scale);
            n.text.alpha=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.45f,Lifetime,t));
            if(n.criticalIcon.gameObject.activeSelf){var color=n.criticalIcon.color;color.a=n.text.alpha;n.criticalIcon.color=color;}
        }
    }
    public void Clear(){foreach(var n in pool)if(n!=null){n.active=false;n.text.gameObject.SetActive(false);}}
    void OnDisable(){Clear();}
}
