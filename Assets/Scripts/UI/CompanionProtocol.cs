using System;
using System.IO;
using UnityEngine;

[Serializable] public sealed class CompanionState : IFocusPresentation
{
    public string ownerId, sessionId, questTitle, message, playerName;
    public string requestId;
    public bool accepted;
    public bool reduceMotion;
    public FocusPhase phase;
    public FocusTimerMode mode;
    public double elapsedSeconds;
    public double remainingSeconds;
    public int fishCount, focusMinutes, restMinutes;
    public int equipmentAuraTier, equipmentAuraPieces;
    public bool connected = true;
    public FocusPhase Phase => connected ? phase : FocusPhase.Interrupted;
    public int SpectatorFishCount => fishCount;
}
[Serializable] public sealed class CompanionCommand
{
    public string ownerId, sessionId, requestId, action;
}
public static class CompanionProtocol
{
#if UNITY_WEBGL && !UNITY_EDITOR
    static readonly string[] Args = Array.Empty<string>();
#else
    static readonly string[] Args = Environment.GetCommandLineArgs();
#endif
    public static string Argument(string key)
    {
        var args = Args;
        for (int i=0;i<args.Length-1;i++) if(args[i]==key) return args[i+1];
        return null;
    }
    public static bool HasArgument(string key) => Array.IndexOf(Args,key)>=0;
    public static void Write<T>(string path,T value)
    {
        string temp=path+".tmp";
        File.WriteAllText(temp,JsonUtility.ToJson(value));
        for(int attempt=0;;attempt++)
        {
            try { if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path); return; }
            catch(IOException) { if(attempt>=3) throw; System.Threading.Thread.Sleep(2); }
        }
    }
    public static T Read<T>(string path) where T:class
    {
        try {
            if(!File.Exists(path)) return null;
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var reader=new StreamReader(stream)) return JsonUtility.FromJson<T>(reader.ReadToEnd());
        }
        catch(IOException) { return null; } catch(ArgumentException) { return null; }
    }
}
