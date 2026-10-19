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
    FocusController focus;
    Process process;
    float nextPublish;
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
        Directory.CreateDirectory(DirectoryPath);
        Enabled=PlayerPrefs.GetInt("PetWidgetEnabled",0)==1;
        focus.Changed+=OnChanged;
        Publish();
    }
    void OnChanged() { Publish(); if(Enabled && focus.Phase==FocusPhase.Focusing && !IsOpen) Open(); }
    public void SetEnabled(bool enabled)
    {
        Enabled=enabled; PlayerPrefs.SetInt("PetWidgetEnabled",enabled?1:0);
        if(enabled) Open(); else CloseWindow();
    }
    public bool Open()
    {
        if(IsOpen) return true;
#if UNITY_EDITOR
        string executable=Path.GetFullPath("Builds/Windows/CatQuest.exe");
#else
        string executable=Process.GetCurrentProcess().MainModule.FileName;
#endif
        if(!File.Exists(executable)) { Error="Build the Windows player first, then reopen Pet Widget."; return false; }
        try
        {
            string closeRequest=Path.Combine(DirectoryPath,"close.request");
            if(File.Exists(closeRequest)) File.Delete(closeRequest);
            if(CompanionProtocol.HasArgument("--focus-smoke"))
            {
                string previousView=Path.Combine(DirectoryPath,"companion-window.json");
                if(File.Exists(previousView)) File.Delete(previousView);
            }
            Publish();
            string verification=CompanionProtocol.HasArgument("--focus-smoke")?" --companion-smoke":"";
            // The Unity 6000.3.10 D3D12 backend crashes during this native tool
            // window's shutdown (D3D12Core.dll, 0xc0000005). Keep the main
            // application's renderer unchanged and use D3D11 for the companion.
            string graphicsBackend = "";
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            graphicsBackend = " -force-d3d11";
#endif
            process=Process.Start(new ProcessStartInfo(executable,$"--companion{graphicsBackend} --companion-dir \"{DirectoryPath}\" --companion-owner {OwnerId}{verification} -screen-fullscreen 0 -screen-width 420 -screen-height 300") { UseShellExecute=false, CreateNoWindow=true, WindowStyle=ProcessWindowStyle.Hidden });
            Error=null; return true;
        }
        catch(Exception e) { Error="Could not open Pet Widget: "+e.Message; return false; }
    }
    public void CloseWindow()
    {
        try { if(IsOpen) File.WriteAllText(Path.Combine(DirectoryPath,"close.request"),OwnerId); }
        catch(IOException e) { Error=e.Message; }
    }
    void Update()
    {
        if(Time.unscaledTime<nextPublish) return;
        nextPublish=Time.unscaledTime+.25f;
        foreach(string path in Directory.GetFiles(DirectoryPath,"command-*.json"))
        {
            var command=CompanionProtocol.Read<CompanionCommand>(path);
            if(command==null) continue;
            try { File.Delete(path); } catch(IOException) { continue; }
            Execute(command);
        }
        if(process!=null && process.HasExited) { process.Dispose(); process=null; Enabled=false; PlayerPrefs.SetInt("PetWidgetEnabled",0); }
        Publish();
    }
    public bool Execute(CompanionCommand command)
    {
        if(command==null || command.ownerId!=OwnerId || command.sessionId!=(focus.SessionId ?? "") || string.IsNullOrEmpty(command.requestId) || !handled.Add(command.requestId)) return false;
        switch(command.action)
        {
            case "start": return focus.Start();
            case "end": return focus.EndEarly();
            case "cancel": return focus.Cancel();
            case "rest": if(focus.Phase!=FocusPhase.Resting) return false; focus.EndRest(); return true;
            case "retry": return focus.RetrySettlement();
        }
        return false;
    }
    void Publish()
    {
        if(focus==null) return;
        try { CompanionProtocol.Write(Path.Combine(DirectoryPath,"companion-session.json"),new CompanionState {
            ownerId=OwnerId,sessionId=focus.SessionId ?? "",heartbeatUtcTicks=DateTime.UtcNow.Ticks,
            phase=focus.Phase,remainingSeconds=focus.RemainingSeconds,fishCount=focus.SpectatorFishCount,
            playerName=GameDataManager.Instance.PlayerData.username,questTitle=focus.Options.questTitle,focusMinutes=focus.Options.focusMinutes,restMinutes=focus.Options.restMinutes,message=focus.Message }); }
        catch(IOException e) { Error="Pet Widget connection unavailable: "+e.Message; }
    }
    void OnDestroy()
    {
        if(focus!=null) focus.Changed-=OnChanged;
        CloseWindow();
        if(Instance==this) Instance=null;
        // Keep the last display state so a surviving companion detects a stale heartbeat.
    }
}
