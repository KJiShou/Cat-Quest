using System;
using System.Linq;
using UnityEngine;

// Animation is presentation only. The deterministic simulation owns every hit.
public sealed class BossBattleActors : MonoBehaviour
{
    public RuntimeAnimatorController catController, bossController;
    public GameObject orcPrefab, demonPrefab;
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
    float catFlash,bossFlash;
    SpriteRenderer[] catRenderers,bossRenderers;
    public float RunProgress {get;private set;}
    public bool Ready => catAnimator && bossAnimator;
    public float CatAnimationSpeed => catAnimator ? catAnimator.speed : 0;
    public Transform Cat => cat;
    public Transform Boss => boss;
    public void Bind(Transform root)
    {
        if(stage==root) return;
        stage=root;cat=stage.Find("Ninja Cat");originalBoss=stage.Find("Cyclope");boss=originalBoss;
        home=cat.position;bossScale=boss.localScale;
        catPose=new PoseSnapshot(cat);originalBossPose=bossPose=new PoseSnapshot(boss);
        catAnimator=cat.GetComponentInChildren<Animator>();catAnimator.runtimeAnimatorController=catController;
        catRenderers=cat.GetComponentsInChildren<SpriteRenderer>();
        ConfigureBoss();ResetPose();
    }
    public void Select(int id)
    {
        if(!stage) return;
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
    public void StartCombat(CombatStats catStats,CombatStats bossStats)
    {
        // One whole attack cycle per 1 / ASPD seconds. Impact sits halfway through a cycle.
        Play(catAnimator,"Attack",AttackLength(catAnimator)*(float)catStats.aspd,.5f);
        Play(bossAnimator,"Attack",AttackLength(bossAnimator)*(float)bossStats.aspd,.5f);
    }
    public void Hit(bool catAttacks) {if(catAttacks) bossFlash=.13f;else catFlash=.13f;}
    public void Finish(BattleOutcome outcome)
    {
        Play(catAnimator,outcome==BattleOutcome.Defeat?"Die":"Idle");
        Play(bossAnimator,outcome==BattleOutcome.Victory?"Die":"Idle");
    }
    void Update()
    {
        catFlash=Mathf.Max(0,catFlash-Time.unscaledDeltaTime);bossFlash=Mathf.Max(0,bossFlash-Time.unscaledDeltaTime);
        Tint(catRenderers,catFlash>0?new Color(1,.4f,.4f):Color.white);
        Tint(bossRenderers,bossFlash>0?new Color(1,.4f,.4f):Color.white);
    }
    static void Tint(SpriteRenderer[] renderers,Color color){if(renderers!=null)foreach(var r in renderers)if(r)r.color=color;}
}
