using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class ResisesSideMenuInLayoutGroup : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private RectTransform container;
    [SerializeField] private RectTransform sideMenuContainer;
    [SerializeField] private RectTransform contentContainer;
    [SerializeField] private Toggle expander;
    private float TotalWidth => container.rect.width;

    [Header("Size Controls")]
    [SerializeField] private int collapsedWidth = 100;
    [SerializeField] private int expandedWidth= 300;

    [Header("Animation Controls")]
    [SerializeField] private bool instantChange;
    [Space]
    [SerializeField] private AnimationCurve easingCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private float animationDuration = 0.3f;

    [Header("Testing")]
    [SerializeField] private bool testingAvtive;
    [SerializeField] private bool setToggleTo;

    private Coroutine _animationRoutine;
    private float lastContainerWidth = -1;

    private void Reset()
    {
        container = GetComponent<RectTransform>();
        expander = GetComponentInChildren<Toggle>();
    }

    private void Awake()
    {
        testingAvtive = false;
    }

    private void OnEnable()
    {
        if (expander) expander.onValueChanged.AddListener(OnExpanderValueChanged);
        SettleToCurrentState();
    }

    private void OnDisable()
    {
        if (expander) expander.onValueChanged.RemoveListener(OnExpanderValueChanged);
        SettleToCurrentState();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) SettleToCurrentState();
    }

    private void LateUpdate()
    {
        if(UiMotion.Reduced && _animationRoutine!=null)SettleToCurrentState();
        // Window resizing must also resize the page when the toggle has not changed.
        // Use the current sidebar width so an in-flight transition stays continuous.
        float width = TotalWidth;
        if (Mathf.Approximately(width, lastContainerWidth)) return;
        lastContainerWidth = width;
        contentContainer.sizeDelta = SizeOfContent(sideMenuContainer.rect.width);
    }

    private void OnValidate()
    {
        if (!testingAvtive)
            return;

        float destinationWidth = setToggleTo ? expandedWidth : collapsedWidth;
        sideMenuContainer.sizeDelta = SizeOfMenu(destinationWidth);
        contentContainer.sizeDelta = SizeOfContent(destinationWidth);
    }

    private Vector2 SizeOfContent(float value)
    {
        return new Vector2(TotalWidth - value, contentContainer.sizeDelta.y);
    }

    private Vector2 SizeOfMenu(float value)
    {
        return new Vector2(value, sideMenuContainer.sizeDelta.y);
    }

    private void OnExpanderValueChanged(bool value)
    {
        StopAnimation();
        float destinationWidth = value ? expandedWidth : collapsedWidth;
        if (instantChange || UiMotion.Reduced || animationDuration <= 0f || !isActiveAndEnabled)
        {
            ApplyWidth(destinationWidth);
            return;
        }

        _animationRoutine = StartCoroutine(AnimateChange(destinationWidth));
    }

    private void StopAnimation()
    {
        if (_animationRoutine == null) return;
        StopCoroutine(_animationRoutine);
        _animationRoutine = null;
    }

    private void SettleToCurrentState()
    {
        StopAnimation();
        if (expander && container && sideMenuContainer && contentContainer)
            ApplyWidth(expander.isOn ? expandedWidth : collapsedWidth);
    }

    private void ApplyWidth(float width)
    {
        sideMenuContainer.sizeDelta = SizeOfMenu(width);
        contentContainer.sizeDelta = SizeOfContent(width);
    }

    private IEnumerator AnimateChange(float destinationWidth)
    {
        // Reversing a transition continues from its rendered width, not an endpoint.
        float startWidth = sideMenuContainer.rect.width;
        float startTime = Time.unscaledTime;
        float elapsedTime = 0f;

        while (elapsedTime < animationDuration)
        {
            elapsedTime = Time.unscaledTime - startTime;
            float t = Mathf.Clamp01(elapsedTime / animationDuration);
            float easedT = easingCurve.Evaluate(t);
            float currentWidth = Mathf.Lerp(startWidth, destinationWidth, easedT);
            ApplyWidth(currentWidth);
            yield return null;
        }

        ApplyWidth(destinationWidth);
        _animationRoutine = null;
    }
}
