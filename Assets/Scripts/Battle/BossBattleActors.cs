using System;
using System.Linq;
using UnityEngine;

// Animation is presentation only. The deterministic simulation owns every hit.
[DefaultExecutionOrder(-100)]
public sealed class BossBattleActors : MonoBehaviour
{
    public RuntimeAnimatorController catController, bossController;
    public GameObject orcPrefab, demonPrefab;
    public GameObject[] weaponSlashPrefabs;
    GameObject slash;
    ParticleSystem[] slashParticles;
    int slashTier;
    float slashSpeed=1;
    public int WeaponEffectTier => slashTier;
    Transform stage,cat,boss,originalBoss;
    Animator catAnimator,bossAnimator;
    Vector3 home, bossScale;
    PoseSnapshot catPose, bossPose, originalBossPose;
    // Death clips animate transforms that idle does not key. Restore the authored rig,
    // then rebind so those unkeyed values cannot leak into the next battle.
    sealed class PoseSnapshot
    {
        readonly Transform[] nodes;
        readonly Vector3[] positions, scales;
        readonly Quaternion[] rotations;
        public PoseSnapshot(Transform root) {
            nodes=root.GetComponentsInChildren<Transform>(true);
            positions=nodes.Select(t=>t.localPosition).ToArray();
            rotations=nodes.Select(t=>t.localRotation).ToArray();
            scales=nodes.Select(t=>t.localScale).ToArray();
        }
        public void Restore() {
            for(int i=0;i<nodes.Length;i++) if(nodes[i]) {
                nodes[i].localPosition=positions[i];nodes[i].localRotation=rotations[i];nodes[i].localScale=scales[i];
            }
        }
    }
    // Remove last frame's additive pose before Animator evaluation, then layer the
    // impact over this frame's animated pose in LateUpdate. No attack state restart.
    sealed class HitRecoil
    {
        const float Duration=.32f;
        Transform target;
        Vector3 position,scale;
        Quaternion rotation;
        float age=Duration,direction,distance;
        bool applied;
        public bool Active=>age<Duration;
        public void Trigger(Transform victim,Transform attacker,SpriteRenderer[] renderers)
        {
            Remove();target=victim;age=.045f;direction=victim.position.x>=attacker.position.x?1:-1;
            var bounds=renderers[0].bounds;
            foreach(var renderer in renderers)if(renderer)bounds.Encapsulate(renderer.bounds);
            distance=Mathf.Clamp(bounds.size.y*.16f,.06f,.5f);
        }
        public void Remove()
        {
            if(applied&&target){target.localPosition=position;target.localRotation=rotation;target.localScale=scale;}
            applied=false;
        }
        public void Apply(float delta)
        {
            if(!Active||!target)return;
            float t=age/Duration;
            // Fast impact, small overshoot, then an exact return to the animated pose.
            float kick=t<.2f?Mathf.Sin(t/.2f*Mathf.PI*.5f):Mathf.Cos((t-.2f)/.8f*Mathf.PI*.5f);
            float rebound=Mathf.Sin(t*Mathf.PI*3)*(1-t)*.12f;
            float strength=kick*(1-t)+rebound;
            position=target.localPosition;rotation=target.localRotation;scale=target.localScale;
            var worldOffset=Vector3.right*(direction*distance*strength);
            target.localPosition=position+(target.parent?target.parent.InverseTransformVector(worldOffset):worldOffset);
            target.localRotation=rotation*Quaternion.Euler(0,0,-direction*13*strength);
            target.localScale=Vector3.Scale(scale,new Vector3(1-.18f*strength,1+.10f*strength,1));
            applied=true;age=Mathf.Min(Duration,age+delta);
        }
        public void Clear(){Remove();age=Duration;}
    }
    readonly HitRecoil catRecoil=new HitRecoil(),bossRecoil=new HitRecoil();
    const float HitStopDuration=.055f,ShakeDuration=.16f;
    float stoppedTime,catResumeSpeed,bossResumeSpeed,shakeRemaining;
    Transform stageCamera;
    Vector3 cameraPosition;
    bool cameraOffsetApplied,hitStopped;
    bool combatActive;
    public bool IsHitStopped=>hitStopped;
    public bool IsShaking=>shakeRemaining>0;
    public bool CatReacting=>catRecoil.Active;
    public bool BossReacting=>bossRecoil.Active;
    float catFlash,bossFlash;
    SpriteRenderer[] catRenderers,bossRenderers;
    public float RunProgress {get;private set;}
    public bool Ready => catAnimator && bossAnimator;
    public float CatAnimationSpeed => catAnimator ? (IsHitStopped?catResumeSpeed:catAnimator.speed) : 0;
    public Transform Cat => cat;
    public Transform Boss => boss;
    public void Bind(Transform root)
    {
        if(stage==root) return;
        ClearReactions();ClearSlash();
        stage=root;stageCamera=stage.GetComponentInChildren<Camera>(true)?.transform;cat=stage.Find("Ninja Cat");originalBoss=stage.Find("Cyclope");boss=originalBoss;
        home=cat.position;bossScale=boss.localScale;
        catPose=new PoseSnapshot(cat);originalBossPose=bossPose=new PoseSnapshot(boss);
        catAnimator=cat.GetComponentInChildren<Animator>();catAnimator.runtimeAnimatorController=catController;
        catRenderers=cat.GetComponentsInChildren<SpriteRenderer>();
        ConfigureBoss();ResetPose();
    }
    public void Select(int id)
    {
        if(!stage) return;
        ClearReactions();
        if(boss && boss!=originalBoss) Destroy(boss.gameObject);
        originalBoss.gameObject.SetActive(id==1);
        originalBossPose.Restore();
        if(id==1) {boss=originalBoss;bossPose=originalBossPose;}
        else {
            boss=Instantiate(id==2?orcPrefab:demonPrefab,stage).transform;
            boss.position=originalBoss.position;boss.rotation=originalBoss.rotation;boss.localScale=bossScale;
            bossPose=new PoseSnapshot(boss);
        }
        ConfigureBoss();ResetPose();
    }
    void ConfigureBoss()
    {
        foreach(var demo in boss.GetComponentsInChildren<MonsterCharacterAnimation>(true)) demo.enabled=false;
        bossAnimator=boss.GetComponentInChildren<Animator>();bossAnimator.runtimeAnimatorController=bossController;
        bossRenderers=boss.GetComponentsInChildren<SpriteRenderer>();
        foreach(var a in new[]{catAnimator,bossAnimator}) {a.cullingMode=AnimatorCullingMode.AlwaysAnimate;a.updateMode=AnimatorUpdateMode.UnscaledTime;}
    }
    void Play(Animator animator,string state,float speed=1,float normalized=0)
    {
        if(!animator || !animator.isActiveAndEnabled)return;
        animator.speed=speed;animator.Play(state,0,normalized);animator.Update(0);
    }
    public void ResetPose()
    {
        combatActive=false;ClearReactions();StopSlash();
        if(!Ready)return;
        catPose.Restore();bossPose.Restore();
        if(catAnimator.isActiveAndEnabled)catAnimator.Rebind();
        if(bossAnimator.isActiveAndEnabled)bossAnimator.Rebind();
        cat.position=home;RunProgress=0;catFlash=bossFlash=0;
        Play(catAnimator,"Idle");Play(bossAnimator,"Idle");Tint(catRenderers,Color.white);Tint(bossRenderers,Color.white);
    }
    public void StartRun(){RunProgress=0;Play(catAnimator,"Run");}
    public bool Run(float delta)
    {
        RunProgress=Mathf.Min(1,RunProgress+delta/1.1f);
        // Keep the authored ground height. Stop within sword reach of the boss.
        var dest=home;dest.x=boss.position.x-Mathf.Abs(bossScale.x)*1.25f;
        cat.position=Vector3.Lerp(home,dest,RunProgress);
        return RunProgress>=1;
    }
    float AttackLength(Animator animator)=>animator.runtimeAnimatorController.animationClips.First(c=>c.name.Contains("slash")||c.name.Contains("attack-smash")).length;
    public void StartCombat(CombatStats catStats,CombatStats bossStats,string weaponId=null)
    {
        ClearReactions();combatActive=true;
        ConfigureSlash(weaponId,(float)catStats.aspd);
        // One whole attack cycle per 1 / ASPD seconds. Impact sits halfway through a cycle.
        Play(catAnimator,"Attack",AttackLength(catAnimator)*(float)catStats.aspd,.5f);
        Play(bossAnimator,"Attack",AttackLength(bossAnimator)*(float)bossStats.aspd,.5f);
    }
    public void Hit(bool catAttacks)
    {
        if(!combatActive||!Ready||!isActiveAndEnabled)return;
        if(catAttacks){bossFlash=.13f;bossRecoil.Trigger(boss,cat,bossRenderers);PlaySlash();}
        else {catFlash=.13f;catRecoil.Trigger(cat,boss,catRenderers);}
        // Simultaneous hits share one bounded freeze instead of extending it.
        if(!IsHitStopped){
            catResumeSpeed=catAnimator.speed;bossResumeSpeed=bossAnimator.speed;
            catAnimator.speed=bossAnimator.speed=0;stoppedTime=0;hitStopped=true;
        }
        shakeRemaining=ShakeDuration;
    }
    void ReleaseHitStop(bool catchUp)
    {
        if(!IsHitStopped)return;
        hitStopped=false;
        if(catAnimator){catAnimator.speed=catResumeSpeed;if(catchUp&&catAnimator.isActiveAndEnabled)catAnimator.Update(stoppedTime);}
        if(bossAnimator){bossAnimator.speed=bossResumeSpeed;if(catchUp&&bossAnimator.isActiveAndEnabled)bossAnimator.Update(stoppedTime);}
        stoppedTime=0;
    }
    void RemoveCameraOffset()
    {
        if(cameraOffsetApplied&&stageCamera)stageCamera.localPosition=cameraPosition;
        cameraOffsetApplied=false;
    }
    void ClearReactions()
    {
        ReleaseHitStop(false);RemoveCameraOffset();shakeRemaining=0;
        catRecoil.Clear();bossRecoil.Clear();catFlash=bossFlash=0;
        Tint(catRenderers,Color.white);Tint(bossRenderers,Color.white);
    }
    void ConfigureSlash(string weaponId,float attackSpeed){
        int tier=EquipmentRules.Find(weaponId)?.Tier??1;
        slashSpeed=Mathf.Max(1,attackSpeed);
        if(slash&&slashTier==tier)return;
        ClearSlash();slashTier=tier;
        if(!stage||weaponSlashPrefabs==null||weaponSlashPrefabs.Length<tier||!weaponSlashPrefabs[tier-1])return;
        slash=Instantiate(weaponSlashPrefabs[tier-1],stage);slash.name="Weapon Slash Grade "+tier;
        slashParticles=slash.GetComponentsInChildren<ParticleSystem>(true);
        foreach(var ps in slashParticles){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.loop=false;main.playOnAwake=false;main.useUnscaledTime=true;main.scalingMode=ParticleSystemScalingMode.Hierarchy;}
        var reference=catRenderers.FirstOrDefault();
        foreach(var renderer in slash.GetComponentsInChildren<ParticleSystemRenderer>(true)){if(reference)renderer.sortingLayerID=reference.sortingLayerID;renderer.sortingOrder=Mathf.Max(catRenderers.Max(r=>r.sortingOrder),bossRenderers.Max(r=>r.sortingOrder))+2;}
    }
    void PlaySlash(){
        if(!slash||slashParticles==null||!Ready)return;
        var bounds=catRenderers[0].bounds;
        foreach(var renderer in catRenderers)bounds.Encapsulate(renderer.bounds);
        var point=bounds.center;point.x=Mathf.Lerp(bounds.center.x,bossRenderers[0].bounds.center.x,.6f);
        slash.transform.position=point;slash.transform.rotation=Quaternion.identity;
        slash.transform.localScale=Vector3.one*Mathf.Max(.5f,bounds.size.y*1.4f);
        foreach(var ps in slashParticles){ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.simulationSpeed=slashSpeed;ps.Play(false);}
    }
    void StopSlash(){if(slashParticles!=null)foreach(var ps in slashParticles)if(ps)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);}
    void ClearSlash(){StopSlash();if(slash)Destroy(slash);slash=null;slashParticles=null;}
    void OnDisable(){combatActive=false;ClearReactions();StopSlash();}
    void OnApplicationFocus(bool focus){if(!focus)ClearReactions();}
    void OnDestroy(){ClearSlash();}
    public void Finish(BattleOutcome outcome)
    {
        combatActive=false;ClearReactions();StopSlash();
        Play(catAnimator,outcome==BattleOutcome.Defeat?"Die":"Idle");
        Play(bossAnimator,outcome==BattleOutcome.Victory?"Die":"Idle");
    }
    void Update()
    {
        catRecoil.Remove();bossRecoil.Remove();RemoveCameraOffset();
        if(IsHitStopped){
            stoppedTime+=Time.unscaledDeltaTime;
            if(stoppedTime>=HitStopDuration)ReleaseHitStop(true);
        }
        shakeRemaining=Mathf.Max(0,shakeRemaining-Time.unscaledDeltaTime);
        if(IsHitStopped)return;
        catFlash=Mathf.Max(0,catFlash-Time.unscaledDeltaTime);bossFlash=Mathf.Max(0,bossFlash-Time.unscaledDeltaTime);
        Tint(catRenderers,catFlash>0?new Color(1,.4f,.4f):Color.white);
        Tint(bossRenderers,bossFlash>0?new Color(1,.4f,.4f):Color.white);
    }
    void LateUpdate()
    {
        float delta=IsHitStopped?0:Time.unscaledDeltaTime;
        catRecoil.Apply(delta);bossRecoil.Apply(delta);
        // Shake only the arena camera, never the Canvas, HUD layout or input targets.
        if(stageCamera&&IsShaking){
            cameraPosition=stageCamera.localPosition;
            float strength=shakeRemaining/ShakeDuration,age=ShakeDuration-shakeRemaining;
            stageCamera.localPosition=cameraPosition+new Vector3(Mathf.Cos(age*105),Mathf.Sin(age*83)*.6f,0)*(.045f*strength*strength);
            cameraOffsetApplied=true;
        }
        Tint(catRenderers,catFlash>0?new Color(1,.4f,.4f):Color.white);
        Tint(bossRenderers,bossFlash>0?new Color(1,.4f,.4f):Color.white);
    }
    static void Tint(SpriteRenderer[] renderers,Color color){if(renderers!=null)foreach(var r in renderers)if(r)r.color=color;}
}
