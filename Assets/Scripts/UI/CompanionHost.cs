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
    // Process lifetime is separate from the user-visible toggle (updates must await exit).
    public bool IsRunning => process != null && !process.HasExited;
    public bool IsOpen => visible && IsRunning;
    bool visible;
    static bool IsolatedSmoke => CompanionProtocol.HasArgument("--focus-smoke");
    static bool UsesWidgetPreference => !IsolatedSmoke && !DemoProfile.IsActive;
    static void SaveWidgetPreference(bool enabled) {if(UsesWidgetPreference)PlayerPrefs.SetInt("PetWidgetEnabled",enabled?1:0); }
    public string Error { get; private set; }
    public string DirectoryPath { get; private set; }
    public string OwnerId { get; private set; }
    public bool Enabled { get; private set; }
    public bool IsUnlocked => AppPlatform.WindowsDesktop && GameDataManager.Instance.Service.CanAccess(ProgressionFeature.Focus);
    FocusController focus;
    SqlitePlayerService service;
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
    readonly Dictionary<string,(string identity,System.Threading.Tasks.Task<bool> result)> commandResults = new Dictionary<string,(string,System.Threading.Tasks.Task<bool>)>();
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
        Enabled=IsUnlocked && UsesWidgetPreference && PlayerPrefs.GetInt("PetWidgetEnabled",0)==1;
        focus.Changed+=PublishIfOpen;
        service=GameDataManager.Instance.Service;service.Changed+=PublishIfOpen;
    }
    public void SetEnabled(bool enabled)
    {
        if (!AppPlatform.WindowsDesktop) { Error = AppPlatform.DesktopMessage; return; }
        if(enabled && !IsUnlocked) { Error=GameDataManager.Instance.Service.AccessMessage(ProgressionFeature.Focus); return; }
        Enabled=enabled; SaveWidgetPreference(enabled);
        if(enabled) Open(); else CloseWindow();
    }
    public bool Open() => Launch(true);
    public void WarmUp() { if (!IsRunning) Launch(false); }
    bool Launch(bool show)
    {
        if (!AppPlatform.WindowsDesktop) { Error = AppPlatform.DesktopMessage; return false; }
        if(updateShutdown) return false;
        if(show && !IsUnlocked) { Error=GameDataManager.Instance.Service.AccessMessage(ProgressionFeature.Focus); return false; }
        if(closing && IsRunning) { Error="Pet Widget is closing. Try again shortly."; return false; }
        visible = show;
        if(IsRunning) { if(authenticated) pipe.Send(show ? "show" : "hide"); return true; }
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
            process=Process.Start(new ProcessStartInfo(executable,$"--companion --companion-hidden --companion-parent {Process.GetCurrentProcess().Id}{graphicsBackend} --companion-dir \"{DirectoryPath}\" --companion-owner {OwnerId} --companion-pipe {pipeName} --companion-token {token}{verification} -screen-fullscreen 0 -screen-width 420 -screen-height 300") { UseShellExecute=false, CreateNoWindow=true, WindowStyle=ProcessWindowStyle.Hidden });
            Error=null; return true;
        }
        catch(Exception e) { visible=false; pipe?.Dispose(); Error="Could not open Pet Widget: "+e.Message; return false; }
    }
    public void CloseWindow()
    {
        visible = false;
        if (authenticated) pipe?.Send("hide");
    }
    public void BeginUpdateShutdown() { updateShutdown=true; closing=true; visible=false; if(authenticated) pipe?.Send("close"); }
    public void CancelUpdateShutdown() { updateShutdown=false; }
    void Update()
    {
        if (process == null) return;
        if (process.HasExited) {
            pipe?.Dispose(); pipe = null; process.Dispose(); process = null;
            if (!updateShutdown) { Enabled=false; SaveWidgetPreference(false); }
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
                else pipe.Send(visible ? "show" : "hide");
            } else if (!closing) {
                CompanionCommand command;
                try { command = JsonUtility.FromJson<CompanionCommand>(frame); }
                catch (ArgumentException) { pipe.Dispose(); return; }
                _ = HandleCommandAsync(command);
            }
        }
        if (!authenticated || closing) return;
        if (dirty) Publish();
        if (Time.unscaledTime >= nextHeartbeat) {
            nextHeartbeat = Time.unscaledTime + 1;
            RefreshAura();
            if (lastSnapshot == null || lastSnapshot.playerName != GameDataManager.Instance.Service.PlayerName
                || lastSnapshot.equipmentAuraPieces != auraPieces || lastSnapshot.equipmentAuraTier != auraTier
                || lastSnapshot.equippedWeaponId != service.EquippedItem(EquipmentSlot.Weapon)
                || lastSnapshot.fishCount != focus.SpectatorFishCount) Publish();
            else pipe.Send("pulse");
        }
    }
    async System.Threading.Tasks.Task HandleCommandAsync(CompanionCommand command)
    {
        var activePipe = pipe;
        string owner = OwnerId;
        bool accepted = await ExecuteAsync(command);
        if (this && activePipe == pipe && owner == OwnerId && authenticated && !closing) Publish(command?.requestId, accepted);
    }
    public async System.Threading.Tasks.Task<bool> ExecuteAsync(CompanionCommand command)
    {
        if (updateShutdown || command == null || command.ownerId != OwnerId || string.IsNullOrEmpty(command.requestId)) return false;
        if (command.action != "hide" && command.sessionId != (focus.SessionId ?? "")) return false;
        string identity=command.action+"\n"+(command.sessionId ?? "");
        if(commandResults.TryGetValue(command.requestId,out var prior))return prior.identity==identity && await prior.result;
        if (!handled.Add(command.requestId)) return false;
        var pending=ExecuteNewCommandAsync(command);
        commandResults.Add(command.requestId,(identity,pending));
        bool result=await pending;
        if(commandResults.Count>256) {
            var expired=new List<string>();
            foreach(var item in commandResults) {
                if(item.Key!=command.requestId && item.Value.result.IsCompleted)expired.Add(item.Key);
                if(commandResults.Count-expired.Count<=256)break;
            }
            foreach(string key in expired)commandResults.Remove(key);
        }
        return result;
    }
    async System.Threading.Tasks.Task<bool> ExecuteNewCommandAsync(CompanionCommand command)
    {
        switch (command.action) {
            case "start": return await focus.StartAsync();
            case "end": return await focus.EndEarlyAsync();
            case "cancel": return await focus.CancelAsync();
            case "retry": return await focus.RetrySettlementAsync();
            case "hide": visible = false; Enabled = false; SaveWidgetPreference(false); return true;
            case "rest": if (focus.Phase != FocusPhase.Resting) return false; focus.EndRest(); return true;
            default: return false;
        }
    }
    void RefreshAura()
    {
        var service = GameDataManager.Instance.Service;
        if (auraVersion != service.ReadVersion) {
            CharacterEquipmentAura.SelectEquipment(service.EquippedItem(EquipmentSlot.Weapon), service.EquippedItem(EquipmentSlot.Body),
                service.EquippedItem(EquipmentSlot.Head), out auraTier, out auraPieces);
            auraVersion = service.ReadVersion;
        }
    }
    void Publish(string requestId = null, bool accepted = false)
    {
        RefreshAura();
        dirty = false;
        lastSnapshot = new CompanionState {
            requestId=requestId, accepted=accepted,
            reduceMotion=service.ReduceMotion,
            equipmentAuraTier=auraTier,equipmentAuraPieces=auraPieces,
            equippedWeaponId=service.EquippedItem(EquipmentSlot.Weapon),
            ownerId=OwnerId,sessionId=focus.SessionId ?? "",
            phase=focus.Phase,mode=focus.Options.mode,elapsedSeconds=focus.ElapsedSeconds,remainingSeconds=focus.RemainingSeconds,fishCount=focus.SpectatorFishCount,
            playerName=GameDataManager.Instance.Service.PlayerName,questTitle=focus.Options.questTitle,focusMinutes=focus.Options.focusMinutes,restMinutes=focus.Options.restMinutes,message=focus.Message };
        pipe.Send(JsonUtility.ToJson(lastSnapshot));
    }
    void OnDestroy()
    {
        if(focus!=null) focus.Changed-=PublishIfOpen;
        if(service!=null)service.Changed-=PublishIfOpen;
        if (IsRunning) { if(authenticated) pipe?.Send("close"); process.CloseMainWindow(); }
        pipe?.Dispose();
        process?.Dispose();
        if(Instance==this) Instance=null;
    }
}
