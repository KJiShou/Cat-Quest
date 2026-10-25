using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

// Shared, presentation-only prompt. The caller validates the captured session.
public sealed class FocusCancelConfirmation : MonoBehaviour
{
    public Button confirm, keepFocusing, backdrop;
    public RectTransform card;
    public TMP_Text heading, info;
    Action accepted;
    GameObject previousSelection;
    Vector2 lastSize;
    public bool IsOpen => gameObject.activeSelf;
    void Awake(){confirm.onClick.AddListener(Confirm);keepFocusing.onClick.AddListener(Dismiss);backdrop.onClick.AddListener(Dismiss);}
    public void Show(Action action)
    {
        if(IsOpen)return;
        accepted=action;previousSelection=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
        gameObject.SetActive(true);transform.SetAsLastSibling();
        if(EventSystem.current)EventSystem.current.SetSelectedGameObject(keepFocusing.gameObject);
    }
    public void Confirm(){if(!IsOpen)return;var action=accepted;Dismiss();action?.Invoke();}
    public void Dismiss(){accepted=null;gameObject.SetActive(false);if(EventSystem.current&&previousSelection&&previousSelection.activeInHierarchy)EventSystem.current.SetSelectedGameObject(previousSelection);previousSelection=null;}
    void OnDisable(){accepted=null;}
    void LateUpdate()
    {
        var available=((RectTransform)transform).rect.size;
        if(available==lastSize)return;
        lastSize=available;
        float scale=Mathf.Min(1,available.x*.9f/600,available.y*.85f/300);
        card.localScale=Vector3.one*scale;
        heading.fontSize=Mathf.Max(36,14/Mathf.Max(.1f,scale));info.fontSize=Mathf.Max(28,11/Mathf.Max(.1f,scale));
        foreach(var button in new[]{confirm,keepFocusing})button.GetComponentInChildren<TMP_Text>().fontSize=Mathf.Max(24,11/Mathf.Max(.1f,scale));
    }
    void Update()
    {
        if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame){Dismiss();return;}
        if(!EventSystem.current)return;
        var selected=EventSystem.current.currentSelectedGameObject;
        if(Keyboard.current!=null&&Keyboard.current.tabKey.wasPressedThisFrame)EventSystem.current.SetSelectedGameObject(selected==keepFocusing.gameObject?confirm.gameObject:keepFocusing.gameObject);
        else if(selected!=confirm.gameObject&&selected!=keepFocusing.gameObject)EventSystem.current.SetSelectedGameObject(keepFocusing.gameObject);
    }
}
