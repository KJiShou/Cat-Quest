using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public sealed class CompanionWindowUI : MonoBehaviour
{
    [Serializable] public sealed class ViewState { public int width,height,fish;public bool connected,compact,controls,training,noPlayerStore,pinned;public string timer,phase,animationState,ninjaSprite,acornSprite,playerName;public float animationTime,controlAlpha; }
    public FocusScenePreview preview;
    public TMP_Text timer,title;
    public RectTransform controls,header,sceneFrame,dragHandle,timerBacking;
    public RectTransform[] edges=new RectTransform[8];
    public CanvasGroup controlFade;
    public Button start,end,rest,retry,close,pin,minimize;
    public Image pinIcon;
    public Sprite pinnedSprite,unpinnedSprite;
    string directory,owner,pendingAction;
    FocusPhase pendingPhase;
    CompanionState state=new CompanionState{connected=false};
    float nextPoll,startup,pendingSince,reveal;
    int lastWidth,lastHeight;
    bool configured,closing;
    bool IsCompact=>Screen.width<360 || Screen.height<260;
    void Start()
    {
        Application.runInBackground=true;Application.targetFrameRate=30;
        directory=CompanionProtocol.Argument("--companion-dir");owner=CompanionProtocol.Argument("--companion-owner");
        if(string.IsNullOrEmpty(directory)||string.IsNullOrEmpty(owner)){enabled=false;return;}
        startup=Time.unscaledTime;
        start.onClick.AddListener(()=>Send("start"));end.onClick.AddListener(()=>Send("end"));rest.onClick.AddListener(()=>Send("rest"));retry.onClick.AddListener(()=>Send("retry"));
        close.onClick.AddListener(CloseDisplay);
        pin.onClick.AddListener(TogglePinned);
        minimize.onClick.AddListener(CompanionNativeWindow.Minimize);
        RefreshPin();
    }
    void Update()
    {
        if(closing)return;
        if(!configured && Time.unscaledTime-startup>.4f) configured=CompanionNativeWindow.Configure();
        if(configured) CompanionNativeWindow.EnforceMinimum();
        if(Time.unscaledTime>=nextPoll){
            nextPoll=Time.unscaledTime+.2f;
            if(File.Exists(Path.Combine(directory,"close.request"))){CloseDisplay();return;}
            var incoming=CompanionProtocol.Read<CompanionState>(Path.Combine(directory,"companion-session.json"));
            if(incoming!=null && incoming.ownerId==owner) state=incoming;
            state.connected=(DateTime.UtcNow.Ticks-state.heartbeatUtcTicks)<TimeSpan.FromSeconds(3).Ticks;
            preview.Bind(state);
            if(pendingAction!=null && (state.phase!=pendingPhase || Time.unscaledTime-pendingSince>3 || !state.connected))pendingAction=null;
            RefreshState();
        }
        var mouse=Mouse.current;var point=mouse!=null?mouse.position.ReadValue():new Vector2(-1,-1);
        bool inside=CompanionHover.IsInside();
#if UNITY_EDITOR || !UNITY_STANDALONE_WIN
        inside=Application.isFocused && point.x>=0 && point.y>=0 && point.x<Screen.width && point.y<Screen.height;
#endif
        reveal=Mathf.MoveTowards(reveal,inside?1:0,Time.unscaledDeltaTime/(inside?.32f:.24f));
        controlFade.alpha=Mathf.SmoothStep(0,1,reveal);controlFade.blocksRaycasts=inside && reveal>.7f;controlFade.interactable=controlFade.blocksRaycasts;
        if(lastWidth!=Screen.width || lastHeight!=Screen.height){ApplyLayout(IsCompact,inside);lastWidth=Screen.width;lastHeight=Screen.height;}
        if(CompanionProtocol.HasArgument("--companion-smoke"))WriteVerification();
    }
    void TogglePinned()
    {
        if (CompanionNativeWindow.SetPinned(!CompanionNativeWindow.IsPinned)) RefreshPin();
    }
    void RefreshPin()
    {
        pinIcon.sprite = CompanionNativeWindow.IsPinned ? pinnedSprite : unpinnedSprite;
        pinIcon.color = CompanionNativeWindow.IsPinned ? new Color32(29,104,119,255) : new Color32(132,148,159,255);
    }
    void CloseDisplay()
    {
        if(closing)return;closing=true;
        // Hide immediately; Unity may take longer to release rendering resources and exit.
        CompanionNativeWindow.Hide();Application.Quit();
    }
    void RefreshState()
    {
        title.text=string.IsNullOrWhiteSpace(state.playerName)?"Player":state.playerName.Trim();
        bool countsUp=state.mode==FocusTimerMode.Stopwatch && state.phase!=FocusPhase.Resting;
        double elapsed=state.phase==FocusPhase.Ready || state.phase==FocusPhase.Interrupted ? 0 : state.elapsedSeconds;
        timer.text=FocusController.FormatTime(countsUp?elapsed:state.remainingSeconds,countsUp);
        timer.color=state.connected?new Color32(29,51,70,255):new Color32(146,91,44,255);
        bool ready=state.phase==FocusPhase.Ready || state.phase==FocusPhase.Finished || state.phase==FocusPhase.Interrupted;
        start.gameObject.SetActive(ready);end.gameObject.SetActive(state.phase==FocusPhase.Focusing);rest.gameObject.SetActive(state.phase==FocusPhase.Resting);retry.gameObject.SetActive(state.phase==FocusPhase.Saving);
        foreach(var b in new[]{start,end,rest,retry})b.interactable=state.connected && pendingAction==null;
        SetCaption(start,pendingAction=="start"?"Starting...":"Start focus");SetCaption(end,pendingAction=="end"?"Finishing...":countsUp?"End focus":"End focus early");SetCaption(rest,pendingAction=="rest"?"Finishing...":"Finish rest");SetCaption(retry,pendingAction=="retry"?"Saving...":"Retry save");
    }
    static void SetCaption(Button b,string text){b.GetComponentInChildren<TMP_Text>().text=text;}
    public void ApplyLayout(bool compact,bool showControls)
    {
        float w=Application.isPlaying?Screen.width:420,h=Application.isPlaying?Screen.height:300;
        const float bar=36;
        Rect(sceneFrame,0,bar,w,h-bar);Rect(header,0,0,w,bar);
        Rect(title.rectTransform,12,4,w-126,28);title.alignment=TextAlignmentOptions.Center;
        Rect(dragHandle,12,6,w-126,27);
        Rect(pin.transform as RectTransform,w-108,6,30,27);
        Rect(minimize.transform as RectTransform,w-74,6,30,27);
        Rect(close.transform as RectTransform,w-40,3,30,30);
        float clockWidth=Mathf.Min(w-32,210),clockHeight=compact?34:46;
        preview.minimumOrthographicSize=compact?7.4f:6.8f;preview.framingOffset=new Vector2(0,compact?-.5f:-1.3f);
        Rect(timerBacking,(w-clockWidth)/2,bar+(compact?2:4),clockWidth,clockHeight);timer.fontSize=compact?28:42;
        timer.overflowMode=TextOverflowModes.Overflow;
        float buttonWidth=Mathf.Min(w-32,280),buttonHeight=compact?34:40;
        Rect(controls,(w-buttonWidth)/2,h-buttonHeight-18,buttonWidth,buttonHeight);
        foreach(var b in new[]{start,end,rest,retry}){Rect(b.transform as RectTransform,0,0,buttonWidth,buttonHeight);b.GetComponentInChildren<TMP_Text>().fontSize=compact?14:16;}
        const float edge=6,corner=12;
        Rect(edges[0],0,corner,edge,h-2*corner);Rect(edges[1],w-edge,corner,edge,h-2*corner);
        Rect(edges[2],corner,0,w-2*corner,edge);Rect(edges[3],0,0,corner,corner);Rect(edges[4],w-corner,0,corner,corner);
        Rect(edges[5],corner,h-edge,w-2*corner,edge);Rect(edges[6],0,h-corner,corner,corner);Rect(edges[7],w-corner,h-corner,corner,corner);
    }
    static void Rect(RectTransform r,float x,float y,float width,float height){r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(width,Mathf.Max(1,height));}
    void Send(string action)
    {
        if(!state.connected || pendingAction!=null)return;
        var command=new CompanionCommand{ownerId=owner,sessionId=state.sessionId,requestId=Guid.NewGuid().ToString("N"),action=action};
        try{CompanionProtocol.Write(Path.Combine(directory,"command-"+command.requestId+".json"),command);pendingAction=action;pendingPhase=state.phase;pendingSince=Time.unscaledTime;RefreshState();}
        catch(IOException){SetCaption(state.phase==FocusPhase.Focusing?end:state.phase==FocusPhase.Resting?rest:state.phase==FocusPhase.Saving?retry:start,"Try again");}
    }
    static string SpriteName(FocusTrainingBattle battle,string child)
    {
        var sprite=battle?battle.transform.Find(child)?.GetComponent<SpriteRenderer>().sprite:null;
        return sprite?sprite.name:"";
    }
    void WriteVerification()
    {
        try
        {
            var battle=preview.Stage?preview.Stage.GetComponentInChildren<FocusTrainingBattle>():null;
            CompanionProtocol.Write(Path.Combine(directory,"companion-window.json"),new ViewState{pinned=CompanionNativeWindow.IsPinned,width=Screen.width,height=Screen.height,connected=state.connected,compact=IsCompact,controls=controlFade.alpha>.02f,controlAlpha=controlFade.alpha,fish=preview.Stage?preview.Stage.GetComponentInChildren<FocusFishSpectators>().VisibleFishCount:0,animationState=battle?(battle.animator.GetCurrentAnimatorStateInfo(0).IsName("Training")?"Training":"Idle"):"",animationTime=battle?battle.animator.GetCurrentAnimatorStateInfo(0).normalizedTime:0,ninjaSprite=SpriteName(battle,"Training Ninja"),acornSprite=SpriteName(battle,"Acorn"),training=battle&&battle.IsTraining,noPlayerStore=GameDataManager.Instance==null,timer=timer.text,phase=state.Phase.ToString(),playerName=title.text});
            string capture=Path.Combine(directory,"capture.request");if(File.Exists(capture)){File.Delete(capture);ScreenCapture.CaptureScreenshot(Path.Combine(directory,"companion-capture.png"));}
        }
        catch(IOException) { }
    }
}
public static class CompanionHover
{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct Point { public int x,y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
#endif
    public static bool IsInside()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        GetCursorPos(out var point); GetWindowThreadProcessId(WindowFromPoint(point),out uint pid);
        return pid==(uint)System.Diagnostics.Process.GetCurrentProcess().Id;
#else
        return false;
#endif
    }
}
