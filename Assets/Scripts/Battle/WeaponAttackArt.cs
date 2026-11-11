using UnityEngine;

// Shared authored presentation for combat and training. Never applies damage.
public sealed class WeaponAttackArt : ScriptableObject
{
    public RuntimeAnimatorController unarmedCombat, unarmedTraining;
    public GameObject gauntletExplosion, staffBeam;
    public static int Style(string id) => EquipmentRules.Find(id)?.IconName switch {
        "Armor_GauntletsGold" or "Armor_GauntletsSteel" => 1,
        "Weapon_MagicStaff_01" => 2,
        _ => 0
    };
    public GameObject Effect(int style) => style==1?gauntletExplosion:style==2?staffBeam:null;
}

// One reusable instance per presenter; attacks restart it rather than accumulating objects.
public sealed class WeaponAttackEffect
{
    GameObject instance;
    ParticleSystem[] particles;
    int style;
    public void Configure(GameObject prefab, Transform parent, int attackStyle, SpriteRenderer source)
    {
        Clear();style=attackStyle;if(!prefab)return;
        instance=Object.Instantiate(prefab,parent);instance.name="Weapon Attack Effect";
        particles=instance.GetComponentsInChildren<ParticleSystem>(true);
        foreach(var ps in particles){ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=false;main.playOnAwake=false;main.useUnscaledTime=true;
            main.scalingMode=ParticleSystemScalingMode.Hierarchy;
            if(style==2){main.startDelay=0;main.duration=.35f;main.startLifetime=Mathf.Min(main.startLifetime.constantMax,.35f);}
        }
        foreach(var renderer in instance.GetComponentsInChildren<ParticleSystemRenderer>(true)){
            renderer.sortingLayerID=source.sortingLayerID;renderer.sortingOrder=source.sortingOrder+10;
        }
    }
    public void Play(Bounds source, Bounds target, float speed)
    {
        if(!instance||UiMotion.Reduced)return;
        var from=source.center;var to=target.center;from.z=to.z;
        from.x+=source.size.y*.08f;from.y-=source.size.y*.12f;
        if(style==2){
            instance.transform.position=from;
            // The vendor's local-aligned beam sheet extends along +X.
            var direction=to-from;
            instance.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg);
            float scale=Mathf.Max(.04f,Vector3.Distance(from,to)/16f);
            instance.transform.localScale=new Vector3(scale,scale*2,scale);
        }else{
            instance.transform.position=to;instance.transform.rotation=Quaternion.identity;
            instance.transform.localScale=Vector3.one*Mathf.Max(.1f,source.size.y*.2f);
        }
        foreach(var ps in particles){ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.simulationSpeed=Mathf.Clamp(speed,1,8);ps.Play(false);}
    }
    public void Stop(){if(particles!=null)foreach(var ps in particles)if(ps)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);}
    public void Clear(){Stop();if(instance)Object.Destroy(instance);instance=null;particles=null;}
}
