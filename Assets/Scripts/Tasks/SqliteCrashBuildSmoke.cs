#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

// Process-kill verification only. A pause after writes and before COMMIT does not
// exercise a particular instruction inside native COMMIT or simulate power loss.
public sealed class SqliteCrashBuildSmoke : MonoBehaviour
{
    const string TaskId = "process-crash-task";
    sealed class Clock : ITaskClock {
        public DateTime LocalNow => new DateTime(2026,11,16,12,0,0);
        public DateTime UtcNow => new DateTime(2026,11,16,4,0,0,DateTimeKind.Utc);
    }
    sealed class Memory : IPlayerDataStore {
        public PlayerData Data { get; private set; }
        public Memory(PlayerData data) { Data=data; }
        public bool TrySave(PlayerData data,out string error) { Data=data;error=null;return true; }
    }
    [Serializable] sealed class Marker { public int pid,publishedNotifications; public string runId,stage,utc; }
    [Serializable] sealed class Result {
        public string runId,stage,phase,utc,error,receiptId;
        public int pid,fishCoins,experience,tickets,receipts;
        public bool passed;
    }
    public static string ValidateArguments()
    {
        if(Application.isEditor||!Debug.isDebugBuild||!CompanionProtocol.HasArgument("--focus-smoke")||!CompanionProtocol.HasArgument("--sqlite-crash")
            ||CompanionProtocol.HasArgument("--companion")||CompanionProtocol.HasArgument("--sqlite-performance")||CompanionProtocol.HasArgument("--save-smoke")
            ||CompanionProtocol.HasArgument("--account-smoke")||CompanionProtocol.HasArgument("--ai-smoke"))
            throw new InvalidOperationException("Crash verification requires its exclusive Development Player smoke mode.");
        string path=CompanionProtocol.Argument("--smoke-dir");
        if(string.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path))throw new InvalidOperationException("Crash verification needs an absolute isolated directory.");
        path=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        string live=Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(string.Equals(path,live,StringComparison.OrdinalIgnoreCase)||path.StartsWith(live+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||live.StartsWith(path+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
            ||!File.Exists(Path.Combine(path,"sqlite-crash-authorized.txt")))throw new InvalidOperationException("Crash verification authorization is missing or overlaps live storage.");
        if(!Guid.TryParseExact(CompanionProtocol.Argument("--crash-run-id"),"N",out _))throw new InvalidOperationException("Crash verification run identity is invalid.");
        string stage=CompanionProtocol.Argument("--crash-stage");
        if(stage!="before-transaction"&&stage!="before-commit"&&stage!="after-commit")throw new InvalidOperationException("Crash verification stage is invalid.");
        return path;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        if(!CompanionProtocol.HasArgument("--sqlite-crash"))return;
        try {
            string path=ValidateArguments();var root=new GameObject("SQLite process crash verification");DontDestroyOnLoad(root);
            root.AddComponent<SqliteCrashBuildSmoke>().directory=path;
        }catch(Exception error){Debug.LogError("Crash verification rejected: "+error.GetType().Name);Application.Quit(1);}
    }
    string directory;
    void Awake(){SceneManager.sceneLoaded+=Loaded;}
    void Loaded(Scene scene,LoadSceneMode mode){foreach(var probe in FindObjectsByType<FocusBuildSmoke>(FindObjectsInactive.Include,FindObjectsSortMode.None))probe.enabled=false;}
    void OnDestroy(){SceneManager.sceneLoaded-=Loaded;}
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static string Canonical(PlayerData data)
    {
        var copy=JsonUtility.FromJson<PlayerData>(JsonUtility.ToJson(data));
        foreach(var receipt in copy.history)receipt.id=receipt.taskId+":"+receipt.day;
        return JsonUtility.ToJson(copy);
    }
    async void Start()
    {
        string stage=CompanionProtocol.Argument("--crash-stage"),runId=CompanionProtocol.Argument("--crash-run-id");
        bool verify=CompanionProtocol.HasArgument("--crash-verify"),finalRead=CompanionProtocol.HasArgument("--crash-final-read");
        var result=new Result{stage=stage,runId=runId,pid=System.Diagnostics.Process.GetCurrentProcess().Id,phase=finalRead?"final-read":verify?"verify-retry":"arm"};
        string output=Path.Combine(directory,result.phase+"-result.json");
        SqlitePlayerRepository repository=null;GameDataManager manager=null;
        try {
            var until=DateTime.UtcNow.AddSeconds(30);
            while(!GameDataManager.Instance||GameDataManager.Instance.Ready==null){Require(DateTime.UtcNow<until,"Application readiness timed out.");await Task.Delay(25);}
            manager=GameDataManager.Instance;
            Require((await manager.Ready).Succeeded,"Application isolated storage failed to initialize.");Loaded(default,LoadSceneMode.Single);
            Require(string.Equals(Path.GetFullPath(manager.SavePath),Path.Combine(directory,"test-save.sqlite3"),StringComparison.OrdinalIgnoreCase),"Application did not use the authorized isolated save.");
            string database=Path.Combine(directory,"crash.sqlite3"),source=Path.Combine(directory,"crash-source.json"),expectedPath=Path.Combine(directory,"expected.json");
            var clock=new Clock();
            if(!verify&&!finalRead){
                Require(!File.Exists(database)&&!File.Exists(source),"Crash fixture must be new.");
                var baseline=new PlayerData{username="Process crash verification",fishCoins=37,experience=100,tickets=2,tagsInitialized=true,tutorial=new TutorialProgress{version=1,dismissed=true}};
                baseline.tasks.Add(new QuestTask{id=TaskId,title="Exactly once crash completion",difficulty=TaskDifficulty.Normal});
                PlayerDataStore.Normalize(baseline);baseline.settings.notificationsEnabled=false;File.WriteAllText(source,JsonUtility.ToJson(baseline));
                var memory=new Memory(PlayerDataStore.Copy(baseline));var oracle=new TaskService(memory,clock);
                Require(oracle.Complete(TaskId,true),"Legacy reward oracle failed.");File.WriteAllText(expectedPath,Canonical(memory.Data));
                File.WriteAllText(Path.Combine(directory,"baseline.json"),Canonical(baseline));
            }else Require(File.Exists(database)&&File.Exists(expectedPath),"Restart requires the prior crash database and oracle.");
            repository=new SqlitePlayerRepository(database,source);var service=new SqlitePlayerService(repository,clock);
            Require((await service.InitializeAsync()).Succeeded,"Crash fixture database failed to initialize.");
            if(!verify&&!finalRead){
                int pid=result.pid;
                int notifications=0;service.Changed+=()=>Interlocked.Increment(ref notifications);
                repository.FaultPoint=point=>{
                    if(point!=stage)return;
                    var marker=new Marker{pid=pid,publishedNotifications=Volatile.Read(ref notifications),runId=runId,stage=stage,utc=DateTime.UtcNow.ToString("O")};
                    byte[] bytes=System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(marker));
                    using(var stream=new FileStream(Path.Combine(directory,"worker-paused.json"),FileMode.CreateNew,FileAccess.Write,FileShare.Read)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                    using(var gate=new ManualResetEventSlim(false))gate.Wait(TimeSpan.FromSeconds(45));
                    throw new TimeoutException("Driver did not terminate the paused owned process within45 seconds.");
                };
                await service.CompleteAsync(TaskId,true);
                throw new Exception("Expected process termination did not occur.");
            }
            var before=await repository.ReadAllForVerificationAsync();Require(before.Succeeded,"Restart diagnostic read failed.");
            File.WriteAllText(Path.Combine(directory,result.phase+"-before.json"),JsonUtility.ToJson(before.Value,true));
            string expected=File.ReadAllText(expectedPath);
            bool committed=finalRead||stage=="after-commit";
            Require(Canonical(before.Value)==(committed?expected:File.ReadAllText(Path.Combine(directory,"baseline.json"))),"Restart fields, exact wallet or receipt differ from the commit boundary.");
            if(!finalRead){
                string priorId=committed?before.Value.history.Single().id:null;
                var retry=await service.CompleteAsync(TaskId,true);Require(retry.Succeeded&&retry.Value!=null,"Retry of the same task/once business identity failed.");
                if(priorId!=null)Require(retry.Value.id==priorId,"Committed retry replaced the receipt identity.");
                var duplicate=await service.CompleteAsync(TaskId,true);Require(duplicate.Succeeded&&duplicate.Value.id==retry.Value.id,"Repeated retry produced another receipt.");
            }
            var after=await repository.ReadAllForVerificationAsync();Require(after.Succeeded&&Canonical(after.Value)==expected,"Retry or final restart duplicated rewards or changed fields.");
            result.receipts=after.Value.history.Count;result.receiptId=after.Value.history.Single().id;
            result.fishCoins=after.Value.fishCoins;result.experience=after.Value.experience;result.tickets=after.Value.tickets;result.passed=true;
        }catch(Exception error){result.error=error.GetType().Name+": "+error.Message;}
        try {
            if(repository!=null){repository.FaultPoint=null;await repository.CloseAsync();}
            manager=manager?manager:GameDataManager.Instance;
            if(manager){var drain=manager.PrepareShutdownAsync();Require(await Task.WhenAny(drain,Task.Delay(10000))==drain,"Application drain timeout.");await drain;await manager.CloseAndApproveQuitAsync();}
        }catch(Exception error){result.passed=false;result.error="Shutdown: "+error.GetType().Name;result.utc=DateTime.UtcNow.ToString("O");File.WriteAllText(output,JsonUtility.ToJson(result,true));return;}
        result.utc=DateTime.UtcNow.ToString("O");File.WriteAllText(output,JsonUtility.ToJson(result,true));Application.Quit(result.passed?0:1);
    }
}
#endif
