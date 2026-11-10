using System;
using System.IO;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

public sealed class CompanionWindowUI : MonoBehaviour
{
    [Serializable] public sealed class CameraCount { public string name,type,hierarchy,scene,targetAtFirstRender;public int instanceId; public long renders,textureRenders; }
    [Serializable] public sealed class FeedbackState { public string name; public bool active,interactable; public float scaleX,scaleY,offset,x,y,width,height; }
    [Serializable] public sealed class ViewState { public string lastAck; public bool lastAccepted,reduceMotion; public int sentCommands,snapshots,connections,ackCount; public string selected; public bool keyboard; public FeedbackState[] feedback; public int width,height,fish,auraTier,auraPieces;public bool connected,compact,controls,training,noPlayerStore,pinned,cancelPrompt;public string timer,phase,animationState,ninjaSprite,acornSprite,playerName;public float animationTime,controlAlpha;public string utc;public int frameCount;public long cameraRenders,textureCameraRenders;public CameraCount[] cameras; }
    public FocusScenePreview preview;
    public TMP_Text timer,title;
    public RectTransform controls,header,sceneFrame,dragHandle,timerBacking;
    public RectTransform[] edges=new RectTransform[8];
    public CanvasGroup controlFade;
    public Button start,end,rest,retry,close,pin,minimize;
    public FocusCancelConfirmation cancelConfirmation;
    string confirmingSession;
    public Image pinIcon;
    public Sprite pinnedSprite,unpinnedSprite;
    string directory,owner,pendingAction;
    string pendingRequest, pipeName, token, lastAck;
    int sentCommands, snapshots, connections, ackCount;
    bool lastAccepted;
    CompanionPipe pipe;
    double lastContact, reconnectAfter, snapshotReceivedAt;
    static double Now => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
    CompanionState state=new CompanionState{connected=false};
    float startup,reveal;
    int lastWidth,lastHeight;
    bool configured,closing,keyboardNavigation;
    Button[] keyboardButtons;
    PointerEventData pointerExit;
    bool IsCompact=>Screen.width<360 || Screen.height<260;
    Dictionary<int,CameraCount> verificationCameras;
    long verificationCameraRenders,verificationTextureCameraRenders;
    void OnEnable()
    {
        if(!CompanionProtocol.HasArgument("--companion-smoke"))return;
        if(verificationCameras==null)verificationCameras=new Dictionary<int,CameraCount>();
        RenderPipelineManager.beginCameraRendering+=CountVerificationCamera;
    }
    void CountVerificationCamera(ScriptableRenderContext context,Camera camera)
    {
        if(!camera)return;
        int id=camera.GetInstanceID();
        if(!verificationCameras.TryGetValue(id,out var count)) {
            string hierarchy=camera.name;for(var parent=camera.transform.parent;parent;parent=parent.parent)hierarchy=parent.name+"/"+hierarchy;
            count=new CameraCount{name=camera.name,type=camera.cameraType.ToString(),hierarchy=hierarchy,scene=camera.gameObject.scene.name,instanceId=id,targetAtFirstRender=camera.targetTexture?camera.targetTexture.name:""};verificationCameras.Add(id,count);
        }
        count.renders++;verificationCameraRenders++;
        if(camera.targetTexture){count.textureRenders++;verificationTextureCameraRenders++;}
    }
    CameraCount[] VerificationCameraCounts()
    {
        if(verificationCameras==null)return Array.Empty<CameraCount>();
        var result=new CameraCount[verificationCameras.Count];verificationCameras.Values.CopyTo(result,0);return result;
    }
    void Start()
    {
        Application.runInBackground=true;Application.targetFrameRate=30;
        directory=CompanionProtocol.Argument("--companion-dir");owner=CompanionProtocol.Argument("--companion-owner");
        pipeName=CompanionProtocol.Argument("--companion-pipe"); token=CompanionProtocol.Argument("--companion-token");
        if(string.IsNullOrEmpty(pipeName)||string.IsNullOrEmpty(token)||string.IsNullOrEmpty(owner)){enabled=false;return;}
        startup=Time.unscaledTime;
        start.onClick.AddListener(()=>Send("start"));end.onClick.AddListener(EndFocus);rest.onClick.AddListener(()=>Send("rest"));retry.onClick.AddListener(()=>Send("retry"));
        close.onClick.AddListener(CloseDisplay);
        pin.onClick.AddListener(TogglePinned);
        minimize.onClick.AddListener(CompanionNativeWindow.Minimize);
        keyboardButtons=new[]{pin,minimize,close,start,end,rest,retry};
        if(EventSystem.current)pointerExit=new PointerEventData(EventSystem.current);
        RefreshState();
        RefreshPin();
    }
    void Update()
    {
        if(closing)return;
        if(!configured && Time.unscaledTime-startup>.4f) configured=CompanionNativeWindow.Configure();
        if(configured) CompanionNativeWindow.EnforceMinimum();
        UpdatePipe();
        if (closing) return;
        if (state.connected) RefreshTimer();
        var mouse=Mouse.current;var point=mouse!=null?mouse.position.ReadValue():new Vector2(-1,-1);
        bool inside=CompanionHover.IsInside();
#if UNITY_EDITOR || !UNITY_STANDALONE_WIN
        inside=Application.isFocused && point.x>=0 && point.y>=0 && point.x<Screen.width && point.y<Screen.height;
#endif
        // Native windows can stop reporting pointer motion outside their client area.
        // Recheck while outside: reopening a modal can replay cached pointer-enter events.
        // Pointer exits preserve keyboard selection.
        if(!inside && pointerExit!=null)
            foreach(var button in keyboardButtons)
                if(button.gameObject.activeInHierarchy)ExecuteEvents.Execute(button.gameObject,pointerExit,ExecuteEvents.pointerExitHandler);
        bool focused=CompanionNativeWindow.IsForeground;
        if(!focused && (keyboardNavigation || (EventSystem.current && EventSystem.current.currentSelectedGameObject)))OnApplicationFocus(false);
        UpdateKeyboardNavigation();
        bool showControls=inside || (keyboardNavigation && focused);
        reveal=Mathf.MoveTowards(reveal,showControls?1:0,Time.unscaledDeltaTime/(showControls?.32f:.24f));
        controlFade.alpha=Mathf.SmoothStep(0,1,reveal);controlFade.blocksRaycasts=inside && reveal>.7f;
        controlFade.interactable=showControls && (keyboardNavigation || reveal>.7f);
        if(lastWidth!=Screen.width || lastHeight!=Screen.height){ApplyLayout(IsCompact,inside);lastWidth=Screen.width;lastHeight=Screen.height;}
        if(CompanionProtocol.HasArgument("--companion-smoke"))WriteVerification();
    }
    void UpdatePipe()
    {
        double now = Now;
        if (pipe == null) {
            if (now < reconnectAfter) return;
            pipe = new CompanionPipe(pipeName, false);
            pipe.Send(token);
            lastContact = now;
        }
        while (pipe.Receive(out string frame)) {
            if (frame == "close") { CloseDisplay(); return; }
            lastContact = now;
            if (frame == "pulse") continue;
            var incoming = JsonUtility.FromJson<CompanionState>(frame);
            if (incoming == null || incoming.ownerId != owner) { Disconnect(); return; }
            if (!state.connected) connections++;
            snapshots++;
            snapshotReceivedAt = now;
            state = incoming;
            UiMotion.ApplyCompanion(state.reduceMotion);
            if (!string.IsNullOrEmpty(state.requestId)) {
                ackCount++;
                lastAck = state.requestId;
                lastAccepted = state.accepted;
                if (pendingRequest == lastAck) pendingAction = pendingRequest = null;
            }
            preview.Bind(state);
            RefreshState();
        }
        if (pipe.Stopped || now - lastContact >= 3) Disconnect();
    }
    void Disconnect()
    {
        pipe?.Dispose(); pipe = null;
        reconnectAfter = Now + .4;
        state.connected = false;
        pendingAction = pendingRequest = null;
        preview.Bind(state);
        RefreshState();
    }
    void OnDestroy() { pipe?.Dispose();UiMotion.ApplyCompanion(false); }
    void RefreshTimer()
    {
        bool up = state.mode == FocusTimerMode.Stopwatch && state.phase != FocusPhase.Resting;
        double delta = state.phase == FocusPhase.Focusing || state.phase == FocusPhase.Resting
            ? Math.Max(0, Now - snapshotReceivedAt) : 0;
        double elapsed = state.phase == FocusPhase.Ready || state.phase == FocusPhase.Interrupted ? 0 : state.elapsedSeconds + delta;
        int second = FocusController.DisplaySecond(up ? elapsed : state.remainingSeconds - delta, up);
        if (second != displayedSecond) {
            displayedSecond = second;
            timer.text = FocusController.FormatTime(second, up);
        }
    }
    int displayedSecond = -1;
    void UpdateKeyboardNavigation()
    {
        var events=EventSystem.current;var keys=Keyboard.current;
        if(!events || keys==null || !CompanionNativeWindow.IsForeground)return;
        if(cancelConfirmation && cancelConfirmation.IsOpen)return; // The prompt owns its focus and Escape.
        if(Mouse.current!=null && Mouse.current.leftButton.wasPressedThisFrame)keyboardNavigation=false;
        if(keys.escapeKey.wasPressedThisFrame){keyboardNavigation=false;events.SetSelectedGameObject(null);return;}
        bool tab=keys.tabKey.wasPressedThisFrame;
        bool arrow=keys.leftArrowKey.wasPressedThisFrame || keys.rightArrowKey.wasPressedThisFrame
            || keys.upArrowKey.wasPressedThisFrame || keys.downArrowKey.wasPressedThisFrame;
        if(!tab && !arrow)return;
        keyboardNavigation=true;
        controlFade.interactable=true;
        int current=Array.FindIndex(keyboardButtons,b=>b.gameObject==events.currentSelectedGameObject);
        if(!tab && current>=0)return; // InputSystemUIInputModule owns ordinary arrow navigation.
        int direction=tab && (keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed)?-1:1;
        if(current<0)current=direction>0?-1:0;
        for(int step=1;step<=keyboardButtons.Length;step++){
            var candidate=keyboardButtons[(current+direction*step+keyboardButtons.Length*2)%keyboardButtons.Length];
            if(candidate.gameObject.activeInHierarchy && candidate.interactable){events.SetSelectedGameObject(candidate.gameObject);return;}
        }
    }
    void OnApplicationFocus(bool focused)
    {
        if(focused)return;
        keyboardNavigation=false;
        if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);
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
        if(cancelConfirmation&&cancelConfirmation.IsOpen&&(!state.connected||state.phase!=FocusPhase.Focusing||state.sessionId!=confirmingSession))cancelConfirmation.Dismiss();
        title.text=string.IsNullOrWhiteSpace(state.playerName)?"Player":state.playerName.Trim();
        if (state.connected) RefreshTimer();
        timer.color=state.connected?new Color32(29,51,70,255):new Color32(146,91,44,255);
        bool ready=state.phase==FocusPhase.Ready || state.phase==FocusPhase.Finished || state.phase==FocusPhase.Interrupted;
        start.gameObject.SetActive(ready);end.gameObject.SetActive(state.phase==FocusPhase.Focusing);rest.gameObject.SetActive(state.phase==FocusPhase.Resting);retry.gameObject.SetActive(state.phase==FocusPhase.Saving);
        foreach(var b in new[]{start,end,rest,retry})b.interactable=state.connected && pendingAction==null;
        SetCaption(start,pendingAction=="start"?"Starting...":"Start focus");SetCaption(end,pendingAction=="cancel"?"Cancelling...":state.mode==FocusTimerMode.Pomodoro?"Cancel":pendingAction=="end"?"Finishing...":"End focus");SetCaption(rest,pendingAction=="rest"?"Finishing...":"Finish rest");SetCaption(retry,pendingAction=="retry"?"Saving...":"Retry save");
    }
    void EndFocus()
    {
        if(!state.connected||pendingAction!=null||state.phase!=FocusPhase.Focusing)return;
        bool cancelSession = state.mode == FocusTimerMode.Pomodoro;
        if(!cancelConfirmation||cancelConfirmation.IsOpen)return;
        confirmingSession=state.sessionId;string session=confirmingSession;
        cancelConfirmation.Show(cancelSession, ()=>{if(state.connected&&state.phase==FocusPhase.Focusing&&state.sessionId==session)Send(cancelSession ? "cancel" : "end");});
    }
    void OnDisable(){RenderPipelineManager.beginCameraRendering-=CountVerificationCamera;if(cancelConfirmation)cancelConfirmation.Dismiss();}
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
        if (SendCommand(command)) { pendingAction=action; pendingRequest=command.requestId; RefreshState(); }
    }
    bool SendCommand(CompanionCommand command)
    {
        if (pipe == null || !pipe.Send(JsonUtility.ToJson(command))) return false;
        sentCommands++;
        return true;
    }
    static string SpriteName(FocusTrainingBattle battle,string child)
    {
        var sprite=battle?battle.transform.Find(child)?.GetComponent<SpriteRenderer>().sprite:null;
        return sprite?sprite.name:"";
    }
    FeedbackState[] FeedbackSnapshot()
    {
        var result=new FeedbackState[keyboardButtons.Length];
        for(int i=0;i<keyboardButtons.Length;i++){
            var button=keyboardButtons[i];var effect=button.GetComponent<JuicyButton>();var rect=(RectTransform)button.transform;
            result[i]=new FeedbackState{name=button.name,active=button.gameObject.activeInHierarchy,interactable=button.IsInteractable(),
                scaleX=effect?effect.DrawScale.x:1,scaleY=effect?effect.DrawScale.y:1,offset=effect?effect.DrawOffset:0,
                x=rect.anchoredPosition.x,y=rect.anchoredPosition.y,width=rect.rect.width,height=rect.rect.height};
        }
        return result;
    }
    double verificationQuietUntil;
    void WriteVerification()
    {
        // Only called with --companion-smoke. Silence the file probe, not the pipe,
        // during bounded process measurements; automatically resume afterwards.
        if(Now<verificationQuietUntil)return;
        try
        {
            var battle=preview.Stage?preview.Stage.GetComponentInChildren<FocusTrainingBattle>():null;
            var aura=preview.Stage?preview.Stage.GetComponentInChildren<CharacterEquipmentAura>():null;
            CompanionProtocol.Write(Path.Combine(directory,"companion-window.json"),new ViewState{utc=DateTime.UtcNow.ToString("O"),frameCount=Time.frameCount,cameraRenders=verificationCameraRenders,textureCameraRenders=verificationTextureCameraRenders,cameras=VerificationCameraCounts(),reduceMotion=UiMotion.Reduced,ackCount=ackCount,lastAck=lastAck,lastAccepted=lastAccepted,sentCommands=sentCommands,snapshots=snapshots,connections=connections,selected=EventSystem.current&&EventSystem.current.currentSelectedGameObject?EventSystem.current.currentSelectedGameObject.name:"",keyboard=keyboardNavigation,feedback=FeedbackSnapshot(),auraTier=aura?aura.Tier:0,auraPieces=aura?aura.Pieces:0,cancelPrompt=cancelConfirmation&&cancelConfirmation.IsOpen,pinned=CompanionNativeWindow.IsPinned,width=Screen.width,height=Screen.height,connected=state.connected,compact=IsCompact,controls=controlFade.alpha>.02f,controlAlpha=controlFade.alpha,fish=preview.Stage?preview.Stage.GetComponentInChildren<FocusFishSpectators>().VisibleFishCount:0,animationState=battle?(battle.animator.GetCurrentAnimatorStateInfo(0).IsName("Training")?"Training":"Idle"):"",animationTime=battle?battle.animator.GetCurrentAnimatorStateInfo(0).normalizedTime:0,ninjaSprite=SpriteName(battle,"Training Ninja"),acornSprite=SpriteName(battle,"Acorn"),training=battle&&battle.IsTraining,noPlayerStore=GameDataManager.Instance==null,timer=timer.text,phase=state.Phase.ToString(),playerName=title.text});
            string request = Path.Combine(directory,"pipe-command.request");
            var command = CompanionProtocol.Read<CompanionCommand>(request);
            if (command != null) { File.Delete(request); SendCommand(command); }
            string input=Path.Combine(directory,"companion-ui.request");
            if(File.Exists(input)){
                string action=File.ReadAllText(input).Trim();File.Delete(input);
                if(action=="cancel")EndFocus();
                else if(action=="confirm-cancel")cancelConfirmation.Confirm();
                else if(action=="keep-focus")cancelConfirmation.Dismiss();
                else if(action.StartsWith("performance-quiet:") && int.TryParse(action.Substring("performance-quiet:".Length),out int seconds))
                    verificationQuietUntil=Now+Mathf.Clamp(seconds,1,45);
            }
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
