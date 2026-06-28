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

    private void Reset()
    {
        container = GetComponent<RectTransform>();
        expander = GetComponentInChildren<Toggle>();
    }

    private void Awake()
    {
        testingAvtive = false;
        expander.onValueChanged.AddListener(OnExpanderValueChanged);
    }

    private void OnDestroy()
    {
        expander.onValueChanged.RemoveListener(OnExpanderValueChanged);
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
        if (_animationRoutine != null)
            StopCoroutine(_animationRoutine);

        StartCoroutine(AnimateChange(value));
    }

    private IEnumerator AnimateChange(bool value)
    {
        float destinationWidth = value ? expandedWidth : collapsedWidth;

        if (instantChange)
        {
            sideMenuContainer.sizeDelta = SizeOfMenu(destinationWidth);
            contentContainer.sizeDelta = SizeOfContent(destinationWidth);
            yield break;
        }

        float startWidth = sideMenuContainer.sizeDelta.x;
        float startTime = Time.time;
        float elapsedTime = 0f;

        while (elapsedTime < animationDuration)
        {
            elapsedTime = Time.time - startTime;
            float t = Mathf.Clamp01(elapsedTime / animationDuration);
            float easedT = easingCurve.Evaluate(t);
            float currentWidth = Mathf.Lerp(startWidth, destinationWidth, easedT);
            sideMenuContainer.sizeDelta = SizeOfMenu(currentWidth);
            contentContainer.sizeDelta = SizeOfContent(currentWidth);
            yield return null;
        }

        sideMenuContainer.sizeDelta = SizeOfMenu(destinationWidth);
        contentContainer.sizeDelta = SizeOfContent(destinationWidth);
    }
}
