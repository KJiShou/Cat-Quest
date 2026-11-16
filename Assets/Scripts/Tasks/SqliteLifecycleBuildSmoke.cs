#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Explicit, isolated Development Player probe of the real application's ownership
// and quit handler. No code is registered in an ordinary launch.
public sealed class SqliteLifecycleBuildSmoke : MonoBehaviour
{
    static SqliteLifecycleBuildSmoke active;
    public static void Trace(string stage){if(active)active.Record(stage);}
    string directory,mode;int frames;bool finished;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap(){
        if(!CompanionProtocol.HasArgument("--sqlite-lifecycle"))return;
        if(Application.isEditor||!Debug.isDebugBuild||!CompanionProtocol.HasArgument("--focus-smoke"))throw new InvalidOperationException("Development smoke run required.");
        string folder=CompanionProtocol.Argument("--smoke-dir");
        if(string.IsNullOrWhiteSpace(folder)||!Path.IsPathRooted(folder)||!File.Exists(Path.Combine(folder,"lifecycle-authorized.txt")))throw new InvalidOperationException("Lifecycle authorization missing.");
        string live=Path.GetFullPath(Application.persistentDataPath),isolated=Path.GetFullPath(folder);
        if(isolated.StartsWith(live,StringComparison.OrdinalIgnoreCase)||live.StartsWith(isolated,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Isolated directory required.");
        var root=new GameObject("Isolated SQLite lifecycle verification");DontDestroyOnLoad(root);
        var probe=root.AddComponent<SqliteLifecycleBuildSmoke>();probe.directory=isolated;probe.mode=CompanionProtocol.Argument("--lifecycle-mode");
        active=probe;
        Application.wantsToQuit+=probe.ObserveQuit;Application.quitting+=probe.ObserveCommittedQuit;
    }
    bool ObserveQuit(){Record("EVENT wantsToQuit thread="+Thread.CurrentThread.ManagedThreadId);return true;}
    void ObserveCommittedQuit(){Record("EVENT quitting");}
    void OnDestroy(){Record("EVENT probe destroyed");Application.wantsToQuit-=ObserveQuit;Application.quitting-=ObserveCommittedQuit;}
    void Update(){frames++;}
    void Record(string text)=>File.AppendAllText(Path.Combine(directory,mode+"-lifecycle.txt"),DateTime.UtcNow.ToString("O")+" "+text+"\n");
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    async void Start(){
        GameDataManager manager=null;SqlitePlayerRepository repo=null;ManualResetEventSlim gate=null;
        try {
            var deadline=DateTime.UtcNow.AddSeconds(30);
            while(!GameDataManager.Instance||GameDataManager.Instance.Ready==null){Require(DateTime.UtcNow<deadline,"Startup timeout");await Task.Delay(20);}
            manager=GameDataManager.Instance;var ready=await manager.Ready;
            var smoke=FindFirstObjectByType<FocusBuildSmoke>();if(smoke)smoke.enabled=false;
            Require(Path.GetFullPath(manager.SavePath)==Path.Combine(directory,"test-save.sqlite3"),"Unexpected storage path");
            if(mode=="contender"){
                Require(!ready.Succeeded&&ready.Error==PlayerStorageError.Busy,"Second main instance must be rejected by ownership lock");
                Require(!(await manager.Service.RenamePlayerAsync("must not write")).Succeeded,"Rejected instance accepted a write");
                Record("PASS second main instance rejected; no accepted writes");
            } else {
                Require(mode=="owner"&&ready.Succeeded,"Owner failed initialization");
                Record("READY owner initialized");deadline=DateTime.UtcNow.AddMinutes(3);
                while(!File.Exists(Path.Combine(directory,"begin-close.request"))){Require(DateTime.UtcNow<deadline,"Coordinator timeout");await Task.Delay(50);}
                repo=(SqlitePlayerRepository)typeof(GameDataManager).GetField("repository",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager);
                gate=new ManualResetEventSlim(false);var signal=gate;int entered=0;
                repo.FaultPoint=stage=>{if(stage=="before-commit"&&Interlocked.Exchange(ref entered,1)==0){Record("COMMIT held for shutdown timeout");signal.Wait(TimeSpan.FromSeconds(25));}};
                var command=manager.Service.RenamePlayerAsync("Lifecycle committed");
                while(Volatile.Read(ref entered)==0){Require(DateTime.UtcNow<deadline,"Command did not reach transaction");await Task.Delay(20);}
                int firstFrame=frames;var watch=System.Diagnostics.Stopwatch.StartNew();
                _=Task.Run(async()=>{await Task.Delay(14000);signal.Set();});
                Record("REQUEST quit; manager="+manager.GetInstanceID()+" current="+GameDataManager.Instance.GetInstanceID()+" ready="+manager.IsReady+" thread="+Thread.CurrentThread.ManagedThreadId);
                Application.Quit();Record("RETURN quit request");
                // The real quit handler must keep this process alive, display its
                // native retry notice after ten seconds and retain the command.
                while(!(manager.LoadNotice??"").Contains("Saving is still in progress")){Require(watch.Elapsed.TotalSeconds<30,"Quit timeout notice missing");await Task.Delay(50);}
                Require(watch.Elapsed.TotalSeconds>=9.5&&frames-firstFrame>=30,"Pending shutdown blocked rendering before timeout");
                Record("TIMEOUT preserved process; renderedFrames="+(frames-firstFrame)+" elapsed="+watch.Elapsed.TotalSeconds);
                Require((await command).Succeeded,"Accepted command was discarded");repo.FaultPoint=null;
                await Task.Delay(250);
                Require((await manager.Service.RenamePlayerAsync("Lifecycle retry")).Succeeded,"Command acceptance did not resume after timeout");
                Record("PASS timeout notice, queue retention, resumed writes and retry shutdown");
            }
            await manager.PrepareShutdownAsync();await manager.CloseAndApproveQuitAsync();finished=true;Application.Quit();
        } catch(Exception error){Record("FAIL "+error);if(gate!=null)gate.Set();if(repo!=null)repo.FaultPoint=null;finished=true;}
        finally{if(finished&&gate!=null)gate.Dispose();}
    }
}
#endif
