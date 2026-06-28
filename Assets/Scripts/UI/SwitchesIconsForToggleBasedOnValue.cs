using UnityEngine;
using UnityEngine.UI;

public class SwitchesIconsForToggleBasedOnValue : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private Toggle toggle;
    [SerializeField] private Image toggleIcon;
    [Space]
    [SerializeField] private Sprite isOnIcon;
    [SerializeField] private Sprite isOffIcon;

    private void Reset()
    {
        toggle = GetComponent<Toggle>();
    }

    private void Awake()
    {
        toggle.onValueChanged.AddListener(UpdateIcon);
    }

    private void OnDestroy()
    {
        toggle.onValueChanged.RemoveListener(UpdateIcon);
    }

    private void UpdateIcon(bool isExpanded)
    {
        toggleIcon.sprite = isExpanded ? isOnIcon : isOffIcon;
    }
}
