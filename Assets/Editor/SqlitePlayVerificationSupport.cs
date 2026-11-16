using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// Only used by explicit Play Mode verifiers with an injected isolated save.
public static class SqlitePlayVerificationSupport
{
    static object GameViewGroup()
    {
        var type=typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameViewSizes");
        var instance=typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(type).GetProperty("instance",BindingFlags.Public|BindingFlags.Static).GetValue(null);
        var method=type.GetMethod("GetGroup");return method.Invoke(instance,new[]{Enum.ToObject(method.GetParameters()[0].ParameterType,0)});
    }
    public static void RememberCustomSizes(string key)
    {
        UnityEditor.SessionState.SetBool(key+"priorPaused",UnityEditor.EditorApplication.isPaused);
        UnityEditor.SessionState.SetFloat(key+"priorTimeScale",UnityEngine.Time.timeScale);UnityEditor.EditorApplication.isPaused=false;
        var group=GameViewGroup();UnityEditor.SessionState.SetInt(key+"customSizes",(int)group.GetType().GetMethod("GetCustomCount").Invoke(group,null));
    }
    public static void RestoreCustomSizes(string key)
    {
        UnityEditor.EditorApplication.isPaused=UnityEditor.SessionState.GetBool(key+"priorPaused",false);
        UnityEngine.Time.timeScale=UnityEditor.SessionState.GetFloat(key+"priorTimeScale",1);
        var group=GameViewGroup();int builtin=(int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group,null);
        int count=(int)group.GetType().GetMethod("GetCustomCount").Invoke(group,null);
        for(int i=count-1;i>=UnityEditor.SessionState.GetInt(key+"customSizes",count);i--)group.GetType().GetMethod("RemoveCustomSize").Invoke(group,new object[]{builtin+i});
    }
    public static IEnumerator Flatten(IEnumerator source)
    {
        using var cleanup = source as IDisposable;
        while(source.MoveNext()) {
            if(source.Current is IEnumerator child) { var nested=Flatten(child); using var dispose=nested as IDisposable; while(nested.MoveNext())yield return nested.Current; }
            else yield return source.Current;
        }
    }
    public static IEnumerator Command<T>(Task<OperationResult<T>> task)
    { foreach(var delay in Wait(task))yield return delay; var value=Value(task);if(value is bool success&&!success)throw new Exception("SQLite verification command returned false."); }
    public static async Task<T> Bounded<T>(Task<T> task)
    {
        if(await Task.WhenAny(task,Task.Delay(10000))!=task)throw new TimeoutException("SQLite verification operation timed out.");
        return await task;
    }
    public static IEnumerator Ui(SqlitePlayerService service)
    { yield return Command(service.FlushAsync()); yield return .05; }
    public static Task<OperationResult<bool>> FixtureAsync(Action<PlayerData> change) => ChangeFixtureAsync(change);
    public static Task<OperationResult<bool>> SetFixtureAsync(PlayerData fixture)
        =>FixtureAsync(target=>{foreach(var field in typeof(PlayerData).GetFields(BindingFlags.Instance|BindingFlags.Public))field.SetValue(target,field.GetValue(fixture));});
    public static async Task<PlayerData> DrainAsync(SqlitePlayerService service)
    {
        var ready=GameDataManager.Instance.Ready;
        if(ready!=null&&!ready.IsCompleted){var opened=await Bounded(ready);if(!opened.Succeeded)throw new Exception(opened.Message);}
        var flushed=await Bounded(service.FlushAsync());if(!flushed.Succeeded)throw new Exception(flushed.Message);
        await Task.Delay(20);
        var read=await Bounded(service.ReadVerificationSnapshotAsync());if(!read.Succeeded)throw new Exception(read.Message);
        return read.Value;
    }
    static async Task<OperationResult<bool>> ChangeFixtureAsync(Action<PlayerData> change)
    {
        var service=GameDataManager.EnsureInstance().Service;
        _=Repository();
        var beforeResult=await Bounded(service.ReadVerificationSnapshotAsync());
        if(!beforeResult.Succeeded)throw new Exception(beforeResult.Message);
        var before=beforeResult.Value;var after=before.DetachedCopy();change(after);
        string Id(PlayerRecordKind kind,object record) {
            if(kind==PlayerRecordKind.Occurrences){var o=(TaskOccurrence)record;return o.taskId.Length+":"+o.taskId+o.day;}
            string field=kind==PlayerRecordKind.RewardDays?"day":kind==PlayerRecordKind.BossProgress?"bossId":"id";
            return Convert.ToString(record.GetType().GetField(field).GetValue(record),System.Globalization.CultureInfo.InvariantCulture);
        }
        var levelEvent=typeof(SqlitePlayerService).GetField("LevelUp",BindingFlags.Instance|BindingFlags.NonPublic);
        var listeners=levelEvent.GetValue(service);levelEvent.SetValue(service,null);
        try { return await Bounded(service.ApplyVerificationFixtureAsync(tx=>{
            tx.Put(PlayerRecordKind.Profile,after);
            foreach(PlayerRecordKind kind in Enum.GetValues(typeof(PlayerRecordKind))) {
                if(kind==PlayerRecordKind.Profile)continue;
                string name=kind.ToString();var field=typeof(PlayerData).GetField(char.ToLowerInvariant(name[0])+name.Substring(1));
                object previous=field.GetValue(before),current=field.GetValue(after);
                if(current is IList list) {
                    var old=(previous as IList)?.Cast<object>().ToDictionary(v=>Id(kind,v))??new Dictionary<string,object>();
                    var next=list.Cast<object>().ToDictionary(v=>Id(kind,v));
                    foreach(string id in old.Keys.Except(next.Keys))tx.Delete(kind,id);
                    foreach(var pair in next)if(!old.TryGetValue(pair.Key,out var prior)||UnityEngine.JsonUtility.ToJson(prior)!=UnityEngine.JsonUtility.ToJson(pair.Value))tx.Put(kind,pair.Value);
                } else if(current!=null&&UnityEngine.JsonUtility.ToJson(previous)!=UnityEngine.JsonUtility.ToJson(current))tx.Put(kind,current);
            }
        })); } finally { levelEvent.SetValue(service,listeners); }
    }
    sealed class Payload { public string payload { get; set; } }
    public static Task<PlayerData> ReloadAsync()
    {
        _=Repository();string path=GameDataManager.Instance.SavePath;
        string isolated=Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        if(Path.GetFullPath(path)!=Path.GetFullPath(Path.ChangeExtension(isolated,".sqlite3")))throw new InvalidOperationException("Reload requires the injected database.");
        return Task.Run(()=>{
            using var db=new SQLite.SQLiteConnection(path,SQLite.SQLiteOpenFlags.ReadOnly|SQLite.SQLiteOpenFlags.FullMutex);
            db.Execute("BEGIN");
            var data=UnityEngine.JsonUtility.FromJson<PlayerData>(db.ExecuteScalar<string>("SELECT payload FROM cq_Profile WHERE id='profile'"));
            foreach(PlayerRecordKind kind in Enum.GetValues(typeof(PlayerRecordKind))) {
                if(kind==PlayerRecordKind.Profile)continue;
                string name=kind.ToString();var field=typeof(PlayerData).GetField(char.ToLowerInvariant(name[0])+name.Substring(1));
                var rows=db.Query<Payload>("SELECT payload FROM cq_"+kind+" ORDER BY sequence");
                if(typeof(IList).IsAssignableFrom(field.FieldType)) {
                    var list=(IList)Activator.CreateInstance(field.FieldType);Type type=field.FieldType.GetGenericArguments()[0];
                    foreach(var row in rows)list.Add(UnityEngine.JsonUtility.FromJson(row.payload,type));field.SetValue(data,list);
                }else field.SetValue(data,rows.Count==0?null:UnityEngine.JsonUtility.FromJson(rows.Single().payload,field.FieldType));
            }
            return data;
        });
    }
    public static IEnumerable<double> Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted) {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("SQLite verification operation timed out.");
            yield return .02;
        }
        task.GetAwaiter().GetResult(); // Already completed; never blocks the Editor thread.
    }
    public static T Value<T>(Task<OperationResult<T>> task)
    {
        if (!task.IsCompleted) throw new InvalidOperationException("Await verification operation first.");
        var result = task.GetAwaiter().GetResult();
        if (!result.Succeeded) throw new Exception(result.Message);
        return result.Value;
    }
    public static IPlayerRepository Repository()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE")))
            throw new InvalidOperationException("Verification requires an isolated save.");
        if(Path.GetFullPath(GameDataManager.Instance.SavePath)!=Path.GetFullPath(Path.ChangeExtension(Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE"),".sqlite3")))
            throw new InvalidOperationException("Verification database does not match its isolated injection.");
        return (IPlayerRepository)typeof(GameDataManager).GetField("repository", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(GameDataManager.Instance);
    }
}
