using UnityEngine;

// Session state only: the shared root Animator owns every character frame.
[RequireComponent(typeof(Animator))]
public sealed class FocusTrainingBattle : MonoBehaviour
{
    public Animator animator;
    public WeaponAttackArt weaponAttackArt;
    RuntimeAnimatorController defaultController;
    SpriteRenderer ninja,acorn;
    readonly WeaponAttackEffect weaponEffect=new WeaponAttackEffect();
    string weaponId;
    int effectCycle=-1;
    public bool previewWithoutFocusSession = true;
    public bool IsTraining { get; private set; }
    public IFocusPresentation Presentation { get; set; }
    FocusRunner runner;
    static readonly int Training = Animator.StringToHash("IsTraining");

    void Awake()
    {
        if (!animator) animator = GetComponent<Animator>();
        defaultController=animator.runtimeAnimatorController;
        ninja=transform.Find("Training Ninja").GetComponent<SpriteRenderer>();
        acorn=transform.Find("Acorn").GetComponent<SpriteRenderer>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        SetTraining(previewWithoutFocusSession);
    }

    void Update()
    {
        RefreshWeapon();
        if (Presentation != null) SetTraining(Presentation.Phase == FocusPhase.Focusing);
        else
        {
            if (!runner) runner = FindFirstObjectByType<FocusRunner>();
            if (runner) SetTraining(runner.Controller.Phase == FocusPhase.Focusing);
        }
    }

    void RefreshWeapon()
    {
        string current=Presentation is CompanionState remote
            ?(remote.connected?remote.equippedWeaponId:null)
            :GameDataManager.Instance?GameDataManager.Instance.Tasks.EquippedItem(EquipmentSlot.Weapon):null;
        if(weaponId==current)return;weaponId=current;effectCycle=-1;
        int style=WeaponAttackArt.Style(current);
        animator.runtimeAnimatorController=style!=0&&weaponAttackArt?weaponAttackArt.unarmedTraining:defaultController;
        animator.SetBool(Training,IsTraining);
        weaponEffect.Configure(weaponAttackArt?weaponAttackArt.Effect(style):null,transform,style,ninja);
    }
    void LateUpdate()
    {
        if(!IsTraining||UiMotion.Reduced){weaponEffect.Stop();return;}
        var state=animator.GetCurrentAnimatorStateInfo(0);
        if(!state.IsName("Training"))return;
        int cycle=Mathf.FloorToInt(state.normalizedTime);
        float seconds=(state.normalizedTime-cycle)*state.length;
        if(seconds>=.45f&&effectCycle!=cycle){effectCycle=cycle;weaponEffect.Play(ninja.bounds,acorn.bounds,1);}
    }
    public void SetTraining(bool training)
    {
        if(IsTraining!=training){effectCycle=-1;weaponEffect.Stop();}
        IsTraining = training;
        if (animator) animator.SetBool(Training, training);
    }

    void OnDisable() { SetTraining(false);weaponEffect.Stop(); }
    void OnApplicationFocus(bool focus){if(!focus)weaponEffect.Stop();}
    void OnDestroy(){weaponEffect.Clear();}
}
