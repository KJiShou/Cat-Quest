using UnityEngine;

// Session state only: the shared root Animator owns every character frame.
[RequireComponent(typeof(Animator))]
public sealed class FocusTrainingBattle : MonoBehaviour
{
    public Animator animator;
    public bool previewWithoutFocusSession = true;
    public bool IsTraining { get; private set; }
    public IFocusPresentation Presentation { get; set; }
    FocusRunner runner;
    static readonly int Training = Animator.StringToHash("IsTraining");

    void Awake()
    {
        if (!animator) animator = GetComponent<Animator>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        SetTraining(previewWithoutFocusSession);
    }

    void Update()
    {
        if (Presentation != null) SetTraining(Presentation.Phase == FocusPhase.Focusing);
        else
        {
            if (!runner) runner = FindFirstObjectByType<FocusRunner>();
            if (runner) SetTraining(runner.Controller.Phase == FocusPhase.Focusing);
        }
    }

    public void SetTraining(bool training)
    {
        IsTraining = training;
        if (animator) animator.SetBool(Training, training);
    }

    void OnDisable() { SetTraining(false); }
}
