using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Adapts GUI Pro's native popup and buttons, keeping its frame, header and shadows.
public sealed class TaskConfirmDialog
{
    public readonly GameObject Root;
    private readonly TMP_Text heading, message;
    private readonly Button confirm, cancel;
    private Action accepted;
    public TaskConfirmDialog(GameObject template, Transform canvas, TMP_FontAsset font)
    {
        Root=UnityEngine.Object.Instantiate(template,canvas); Root.name="TaskConfirmDialog";
        TaskUIBuilder.Stretch(Root.GetComponent<RectTransform>());
        var popup=Root.transform.Find("Popup").GetComponent<RectTransform>();
        popup.anchorMin=popup.anchorMax=popup.pivot=new Vector2(.5f,.5f);
        popup.anchoredPosition=Vector2.zero; popup.sizeDelta=new Vector2(640,350); popup.localScale=Vector3.one;
        popup.Find("InputField_UserName").gameObject.SetActive(false);
        popup.Find("Icon_PopupTitle").gameObject.SetActive(false);
        var frame=popup.Find("TopFrame02").GetComponent<RectTransform>();
        Place(frame,new Vector2(.5f,1),new Vector2(0,-30),new Vector2(480,80));
        heading=popup.Find("Text_Title").GetComponent<TMP_Text>();
        Place(heading.rectTransform,new Vector2(.5f,1),new Vector2(0,-33),new Vector2(480,62));
        heading.font=font; heading.fontSize=24; heading.enableAutoSizing=false; heading.alignment=TextAlignmentOptions.Center;
        message=popup.Find("Text_Info").GetComponent<TMP_Text>();
        Place(message.rectTransform,new Vector2(.5f,.5f),new Vector2(0,5),new Vector2(550,110));
        message.font=font; message.fontSize=18; message.enableAutoSizing=false;
        message.textWrappingMode=TextWrappingModes.Normal; message.alignment=TextAlignmentOptions.Center;
        confirm=popup.Find("Button_Ok").GetComponent<Button>(); confirm.name="Confirm";
        cancel=UnityEngine.Object.Instantiate(confirm,popup); cancel.name="Cancel";
        Place(confirm.GetComponent<RectTransform>(),new Vector2(.5f,0),new Vector2(130,60),new Vector2(220,62));
        Place(cancel.GetComponent<RectTransform>(),new Vector2(.5f,0),new Vector2(-130,60),new Vector2(220,62));
        cancel.GetComponent<Image>().color=new Color32(200,220,241,255);
        SetButton(confirm,"Confirm",font); SetButton(cancel,"Cancel",font);
        confirm.onClick.RemoveAllListeners(); cancel.onClick.RemoveAllListeners();
        confirm.onClick.AddListener(()=> { var action=accepted; Close(); action?.Invoke(); });
        cancel.onClick.AddListener(Close);
        var close=popup.Find("Button_Close").GetComponent<Button>();
        Place(close.GetComponent<RectTransform>(),new Vector2(1,1),new Vector2(-28,-28),new Vector2(38,38));
        close.onClick.RemoveAllListeners(); close.onClick.AddListener(Close);
        var icon=close.transform.Find("Icon_Close").GetComponent<RectTransform>(); icon.sizeDelta=new Vector2(20,20);
        var dim=Root.transform.Find("ScreenDim").GetComponent<Image>(); dim.color=new Color(0,0,0,.18f);
        dim.raycastTarget=true; var outside=dim.gameObject.GetComponent<Button>() ?? dim.gameObject.AddComponent<Button>();
        (outside.GetComponent<JuicyButton>()??outside.gameObject.AddComponent<JuicyButton>()).enabled=false;
        outside.onClick.RemoveAllListeners(); outside.onClick.AddListener(Close);
        Root.SetActive(false);
    }
    private static void Place(RectTransform rect,Vector2 anchor,Vector2 position,Vector2 size)
    {
        rect.anchorMin=rect.anchorMax=anchor; rect.pivot=new Vector2(.5f,.5f);
        rect.anchoredPosition=position; rect.sizeDelta=size; rect.localScale=Vector3.one;
    }
    private static void SetButton(Button button,string caption,TMP_FontAsset font)
    {
        var text=button.GetComponentInChildren<TMP_Text>(true); text.text=caption; text.font=font; text.fontSize=22;
        text.enableAutoSizing=false; text.alignment=TextAlignmentOptions.Center; TaskUIBuilder.Stretch(text.rectTransform,14,14,4,4);
    }
    public void Show(string title,string body,Action onConfirm)
    {
        accepted=onConfirm; heading.text=title; message.text=body; Root.SetActive(true); Root.transform.SetAsLastSibling();
    }
    public void Dismiss() => Close();
    private void Close() { Root.SetActive(false); accepted=null; }
}
