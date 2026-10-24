using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Story-scene artwork, with battle-specific confirmation and lifecycle ownership.
public sealed class BossBattleSkipUI : MonoBehaviour
{
    public BossPageUI owner;
    public Button skip, later, yes, backdrop;
    public GameObject confirmation;
    Animator[] pausedAnimators;
    bool[] animatorEnabled;
    public bool IsConfirming => confirmation && confirmation.activeSelf;

    void Awake()
    {
        confirmation.SetActive(false);
        skip.onClick.AddListener(Open);
        later.onClick.AddListener(Cancel);
        yes.onClick.AddListener(Confirm);
        backdrop.onClick.AddListener(Cancel);
        RefreshState();
    }
    public void RefreshState()
    {
        bool available = owner && owner.IsBattling;
        skip.gameObject.SetActive(available);
        if (!available) Dismiss();
    }
    public void Open()
    {
        if (!owner.IsBattling || IsConfirming || owner.confirmation.activeSelf) return;
        pausedAnimators = new[] { owner.actors.Cat.GetComponentInChildren<Animator>(), owner.actors.Boss.GetComponentInChildren<Animator>() };
        animatorEnabled = new bool[pausedAnimators.Length];
        for (int i = 0; i < pausedAnimators.Length; i++)
        {
            animatorEnabled[i] = pausedAnimators[i].enabled;
            pausedAnimators[i].enabled = false;
        }
        confirmation.SetActive(true);
        confirmation.transform.SetAsLastSibling();
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(later.gameObject);
    }
    public void Dismiss()
    {
        if (confirmation) confirmation.SetActive(false);
        if (pausedAnimators != null)
            for (int i = 0; i < pausedAnimators.Length; i++)
                if (pausedAnimators[i]) pausedAnimators[i].enabled = animatorEnabled[i];
        pausedAnimators = null;
        animatorEnabled = null;
    }
    public void Cancel()
    {
        Dismiss();
        if (skip.gameObject.activeInHierarchy && EventSystem.current) EventSystem.current.SetSelectedGameObject(skip.gameObject);
    }
    public void Confirm()
    {
        if (!IsConfirming) return;
        Dismiss();
        owner.SkipBattle();
        if (EventSystem.current)
        {
            GameObject target = owner.action.gameObject;
            for (int i = 0; i < owner.resultPanels.Length; i++)
                if (owner.resultPanels[i].activeInHierarchy) target = owner.resultContinue[i].gameObject;
            EventSystem.current.SetSelectedGameObject(target);
        }
    }
    void Update()
    {
        if (!IsConfirming) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
        if (!EventSystem.current) return;
        var selected = EventSystem.current.currentSelectedGameObject;
        if (Input.GetKeyDown(KeyCode.Tab))
            EventSystem.current.SetSelectedGameObject(selected == later.gameObject ? yes.gameObject : later.gameObject);
        else if (selected != later.gameObject && selected != yes.gameObject)
            EventSystem.current.SetSelectedGameObject(later.gameObject);
    }
    void OnDisable() => Dismiss();
    void OnDestroy()
    {
        if (skip) skip.onClick.RemoveListener(Open);
        if (later) later.onClick.RemoveListener(Cancel);
        if (yes) yes.onClick.RemoveListener(Confirm);
        if (backdrop) backdrop.onClick.RemoveListener(Cancel);
    }
}
