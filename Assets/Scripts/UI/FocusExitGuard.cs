using TMPro;
using UnityEngine;

public sealed class FocusExitGuard : MonoBehaviour
{
    bool confirmed;
    GameObject dialog;
    FocusController dialogFocus;
    TMP_Text dialogLabel, stayLabel, quitLabel;
    void OnEnable() { Application.wantsToQuit+=WantsToQuit; }
    void OnDisable() { Application.wantsToQuit-=WantsToQuit; Dismiss(); }
    bool WantsToQuit()
    {
        var focus=GetComponent<FocusRunner>().Controller;
        if(confirmed || (focus.Phase!=FocusPhase.Focusing && focus.Phase!=FocusPhase.Saving)) return true;
        if(dialog) return false;
        var panel=FindFirstObjectByType<FocusPanelUI>(FindObjectsInactive.Include);
        if(!panel) return false;
        var canvas=panel.GetComponentInParent<UnityEngine.Canvas>();
        var ui=new TaskUIBuilder(panel.timer.font,panel.settingsSprite);
        var overlay=TaskUIBuilder.Rect(canvas.transform,"ExitFocusConfirmation"); TaskUIBuilder.Stretch(overlay);
        ui.Image(overlay,new Color(0,0,0,.7f)); dialog=overlay.gameObject;
        var card=TaskUIBuilder.Rect(overlay,"Dialog"); card.anchorMin=new Vector2(.3f,.35f); card.anchorMax=new Vector2(.7f,.65f); card.offsetMin=card.offsetMax=Vector2.zero;
        ui.Image(card,new Color32(240,250,255,255),ui.Rounded);
        var label=ui.Text(card,"",23);
        label.alignment=TextAlignmentOptions.Center; label.rectTransform.anchorMin=new Vector2(.05f,.4f);label.rectTransform.anchorMax=new Vector2(.95f,.95f);label.rectTransform.offsetMin=label.rectTransform.offsetMax=Vector2.zero;
        var stay=ui.Button(card,"Stay","Keep focusing"); Place(stay.transform,.05f,.08f,.48f,.3f); stay.onClick.AddListener(Dismiss);
        var quit=ui.Button(card,"Quit","Cancel and exit"); Place(quit.transform,.52f,.08f,.95f,.3f);
        dialogFocus=focus;dialogLabel=label;stayLabel=stay.GetComponentInChildren<TMP_Text>();quitLabel=quit.GetComponentInChildren<TMP_Text>();
        dialogFocus.Changed+=RefreshDialog;RefreshDialog();
        quit.onClick.AddListener(()=> {
            bool saved=focus.Phase==FocusPhase.Saving?focus.RetrySettlement():focus.Phase!=FocusPhase.Focusing||focus.Cancel(true);
            if(!saved) { RefreshDialog(); return; }
            confirmed=true; Application.Quit();
        });
        return false;
    }
    void RefreshDialog()
    {
        if(!dialog||dialogFocus==null)return;
        bool saving=dialogFocus.Phase==FocusPhase.Saving,focusing=dialogFocus.Phase==FocusPhase.Focusing;
        dialogLabel.text=saving?"Exit Cat Quest?\nYour Focus result is not saved. Retry saving before exiting."
            :focusing?"Exit Cat Quest?\nYour active Focus will be cancelled without rewards.":"Your Focus result is saved.\nExit Cat Quest?";
        stayLabel.text=focusing?"Keep focusing":"Stay";
        quitLabel.text=saving?"Retry Save and exit":focusing?"Cancel and exit":"Exit";
    }
    void Dismiss()
    {
        if(dialogFocus!=null)dialogFocus.Changed-=RefreshDialog;
        dialogFocus=null;
        if(dialog)Destroy(dialog);
        dialog=null;dialogLabel=stayLabel=quitLabel=null;
    }
    static void Place(Transform t,float x0,float y0,float x1,float y1) { var r=(RectTransform)t;r.anchorMin=new Vector2(x0,y0);r.anchorMax=new Vector2(x1,y1);r.offsetMin=r.offsetMax=Vector2.zero; }
}
