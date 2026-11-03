using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class BossBattleSkipUI : MonoBehaviour
{
    public BossPageUI owner;
    public Button skip;
    public GameObject confirmation;

    void Awake()
    {
        confirmation.SetActive(false);
        skip.onClick.AddListener(Skip);
        RefreshState();
    }
    public void RefreshState() => skip.gameObject.SetActive(owner && owner.IsBattling);

    void Skip()
    {
        if (!owner.IsBattling || owner.confirmation.activeSelf) return;
        owner.SkipBattle();
        if (EventSystem.current)
        {
            GameObject target = owner.action.gameObject;
            for (int i = 0; i < owner.resultPanels.Length; i++)
                if (owner.resultPanels[i].activeInHierarchy) target = owner.resultContinue[i].gameObject;
            EventSystem.current.SetSelectedGameObject(target);
        }
    }
    void OnDestroy()
    {
        if (skip) skip.onClick.RemoveListener(Skip);
    }
}
