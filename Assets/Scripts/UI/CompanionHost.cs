using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Only the main process can access FocusController and the player save.
public sealed class CompanionHost : MonoBehaviour
{
    public static CompanionHost Instance { get; private set; }
    public bool IsOpen => process != null && !process.HasExited;
    public string Error { get; private set; }
    public string DirectoryPath { get; private set; }
    public string OwnerId { get; private set; }
    public bool Enabled { get; private set; }
    public bool IsUnlocked => AppPlatform.WindowsDesktop && GameDataManager.Instance.Tasks.CanAccess(ProgressionFeature.Focus);
    FocusController focus;
    Process process;
    CompanionPipe pipe;
    string pipeName, token;
    bool authenticated, closing, dirty;
    float nextHeartbeat;
    CompanionState lastSnapshot;
    bool updateShutdown;
    int auraVersion = -1, auraTier, auraPieces;
    void PublishIfOpen() { dirty = true; }
    readonly HashSet<string> handled = new HashSet<string>();
    public static CompanionHost Ensure()
    {
        if(Instance) return Instance;
        return FocusRunner.EnsureInstance().gameObject.AddComponent<CompanionHost>();
    }
    void Awake()
    {
        Instance=this; focus=GetComponent<FocusRunner>().Controller;
        OwnerId=Guid.NewGuid().ToString("N");
        DirectoryPath=Path.Combine(Application.temporaryCachePath,"Companion",OwnerId);
        if (CompanionProtocol.HasArgument("--focus-smoke")) Directory.CreateDirectory(DirectoryPath);
        Enabled=IsUnlocked && PlayerPrefs.GetInt("PetWidgetEnabled",0)==1;
        focus.Changed+=PublishIfOpen;
    }
    public void SetEnabled(bool enabled)
    {
        if (!AppPlatform.WindowsDesktop) { Error = AppPlatform.DesktopMessage; return; }
        if(enabled && !IsUnlocked) { Error=GameDataManager.Instance.Tasks.AccessMessage(ProgressionFeature.Focus); return; }
        Enabled=enabled; PlayerPrefs.SetInt("PetWidgetEnabled",enabled?1:0);
        if(enabled) Open(); else CloseWindow();
    }
    public bool Open()
    {
        if (!AppPlatform.WindowsDesktop) { Error = AppPlatform.DesktopMessage; return false; }
        if(updateShutdown) return false;
        if(!IsUnlocked) { Error=GameDataManager.Instance.Tasks.AccessMessage(ProgressionFeature.Focus); return false; }
        if(IsOpen) return true;
#if UNITY_EDITOR
        string executable=Path.GetFullPath("Builds/Windows/CatQuest.exe");
#else
        string executable=Process.GetCurrentProcess().MainModule.FileName;
#endif
        if(!File.Exists(executable)) { Error="Build the Windows player first, then reopen Pet Widget."; return false; }
        try
        {
            process?.Dispose(); process = null;
            pipe?.Dispose();
            pipeName = "CatQuest-" + Guid.NewGuid().ToString("N");
            token = Guid.NewGuid().ToString("N");
            closing = authenticated = false;
            pipe = new CompanionPipe(pipeName, true);
            if(CompanionProtocol.HasArgument("--focus-smoke"))
            {
                string previousView=Path.Combine(DirectoryPath,"companion-window.json");
                if(File.Exists(previousView)) File.Delete(previousView);
            }
            string verification=CompanionProtocol.HasArgument("--focus-smoke")?" --companion-smoke":"";
            // The Unity 6000.3.10 D3D12 backend crashes during this native tool
            // window's shutdown (D3D12Core.dll, 0xc0000005). Keep the main
            // application's renderer unchanged and use D3D11 for the companion.
            string graphicsBackend = "";
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            graphicsBackend = " -force-d3d11";
#endif
            process=Process.Start(new ProcessStartInfo(executable,$"--companion{graphicsBackend} --companion-dir \"{DirectoryPath}\" --companion-owner {OwnerId} --companion-pipe {pipeName} --companion-token {token}{verification} -screen-fullscreen 0 -screen-width 420 -screen-height 300") { UseShellExecute=false, CreateNoWindow=true, WindowStyle=ProcessWindowStyle.Hidden });
            Error=null; return true;
        }
        catch(Exception e) { pipe?.Dispose(); Error="Could not open Pet Widget: "+e.Message; return false; }
    }
    public void CloseWindow()
    {
        closing = true;
        if (authenticated) pipe?.Send("close");
        if (IsOpen && !authenticated) process.CloseMainWindow();
    }
    public void BeginUpdateShutdown() { updateShutdown=true; CloseWindow(); }
    public void CancelUpdateShutdown() { updateShutdown=false; }
    void Update()
    {
        if (process == null) return;
        if (process.HasExited) {
            pipe?.Dispose(); pipe = null; process.Dispose(); process = null;
            if (!updateShutdown) { Enabled=false; PlayerPrefs.SetInt("PetWidgetEnabled",0); }
            return;
        }
        if (pipe.Stopped) {
            authenticated = false;
            pipe = new CompanionPipe(pipeName, true);
        }
        while (pipe.Receive(out string frame)) {
            if (!authenticated) {
                if (frame != token) { pipe.Dispose(); return; }
                authenticated = true;
                dirty = true;
                if (closing) pipe.Send("close");
            } else if (!closing) {
                CompanionCommand command;
                try { command = JsonUtility.FromJson<CompanionCommand>(frame); }
                catch (ArgumentException) { pipe.Dispose(); return; }
                bool accepted = Execute(command);
                Publish(command?.requestId, accepted);
            }
        }
        if (!authenticated || closing) return;
        if (dirty) Publish();
        if (Time.unscaledTime >= nextHeartbeat) {
            nextHeartbeat = Time.unscaledTime + 1;
            RefreshAura();
            if (lastSnapshot == null || lastSnapshot.playerName != GameDataManager.Instance.PlayerData.username
                || lastSnapshot.equipmentAuraPieces != auraPieces || lastSnapshot.equipmentAuraTier != auraTier
                || lastSnapshot.fishCount != focus.SpectatorFishCount) Publish();
            else pipe.Send("pulse");
        }
    }
    public bool Execute(CompanionCommand command)
    {
        if(updateShutdown) return false;
        if(command==null || command.ownerId!=OwnerId || command.sessionId!=(focus.SessionId ?? "") || string.IsNullOrEmpty(command.requestId) || !handled.Add(command.requestId)) return false;
        switch(command.action)
        {
            case "start": return focus.Start();
            case "end": return focus.Options.mode!=FocusTimerMode.Pomodoro && focus.EndEarly();
            case "cancel": return focus.Cancel();
            case "rest": if(focus.Phase!=FocusPhase.Resting) return false; focus.EndRest(); return true;
            case "retry": return focus.RetrySettlement();
        }
        return false;
    }
    void RefreshAura()
    {
        var service = GameDataManager.Instance.Tasks;
        if (auraVersion != service.ReadVersion) {
            CharacterEquipmentAura.Select(service.EquipmentStats.SetPieces, out auraTier, out auraPieces);
            auraVersion = service.ReadVersion;
        }
    }
    void Publish(string requestId = null, bool accepted = false)
    {
        RefreshAura();
        dirty = false;
        lastSnapshot = new CompanionState {
            requestId=requestId, accepted=accepted,
            equipmentAuraTier=auraTier,equipmentAuraPieces=auraPieces,
            ownerId=OwnerId,sessionId=focus.SessionId ?? "",
            phase=focus.Phase,mode=focus.Options.mode,elapsedSeconds=focus.ElapsedSeconds,remainingSeconds=focus.RemainingSeconds,fishCount=focus.SpectatorFishCount,
            playerName=GameDataManager.Instance.PlayerData.username,questTitle=focus.Options.questTitle,focusMinutes=focus.Options.focusMinutes,restMinutes=focus.Options.restMinutes,message=focus.Message };
        pipe.Send(JsonUtility.ToJson(lastSnapshot));
    }
    void OnDestroy()
    {
        if(focus!=null) focus.Changed-=PublishIfOpen;
        if (IsOpen) process.CloseMainWindow();
        pipe?.Dispose();
        process?.Dispose();
        if(Instance==this) Instance=null;
    }
}
