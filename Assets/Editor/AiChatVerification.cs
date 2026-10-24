using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public static class AiChatVerification
{
    sealed class Clock : ITaskClock { public DateTime day = new DateTime(2026,10,24,12,0,0); public DateTime LocalNow => day; public DateTime UtcNow => day.AddHours(-8); }
    sealed class Store : IPlayerDataStore {
        public PlayerData Data { get; private set; } = new PlayerData(); public bool fail;
        public bool TrySave(PlayerData data, out string error) { error = fail ? "Injected save failure" : null; if (!fail) Data = data; return !fail; }
    }
    sealed class Memory : IChatGptStorage {
        public Dictionary<string,string> records = new Dictionary<string,string>(); public bool fail;
        public string Read(string name, bool encrypted) => records.TryGetValue(name,out var value) ? value : null;
        public void Write(string name,string value,bool encrypted) { if (fail) throw new System.IO.IOException(); records[name] = value; }
    }
    sealed class Http : IChatGptHttp {
        public TaskCompletionSource<string> pending;
        public string body;
        public Task<string> Send(string url,string method,string data,string type,string bearer,bool stream,CancellationToken token) { body = data; pending = new TaskCompletionSource<string>(); return pending.Task; }
    }
    static void Check(bool value,string message) { if (!value) throw new Exception(message); }
    static AiTaskDraft New(TaskService service,string title = "Study") => AiChatProtocol.Draft(new Dictionary<string,object> { ["title"] = title },service,new Dictionary<string,TaskView>());
    public static void Run()
    {
        var clock = new Clock(); var store = new Store(); var service = new TaskService(store,clock);
        service.SaveTag(null, "Chat verification", out var tagId);
        var tagged = AiChatProtocol.Draft(new Dictionary<string,object> { ["title"]="Tagged", ["tagIds"]=new List<object>{tagId} },service,new Dictionary<string,TaskView>());
        Check(tagged.task.tagIds.SequenceEqual(new[]{tagId}),"Existing tag proposal lost.");
        bool unknownTagRejected=false;
        try { AiChatProtocol.Draft(new Dictionary<string,object>{["tagIds"]=new List<object>{"invented"}},service,new Dictionary<string,TaskView>()); } catch(ArgumentException){unknownTagRejected=true;}
        Check(unknownTagRejected,"Invented tag accepted.");
        var proposal = New(service);
        store.fail = true; Check(!service.ConfirmChatDraft(proposal,out _) && store.Data.tasks.Count == 0,"Failed save mutated task data."); store.fail = false;
        Check(service.ConfirmChatDraft(proposal,out var id),service.LastError);
        Check(!service.ConfirmChatDraft(proposal,out _) && store.Data.tasks.Count == 1,"Duplicate proposal added a task.");
        var original = service.QueryForChat("id",id).Single();
        var read = new Dictionary<string,TaskView> { [id] = original };
        var edit = AiChatProtocol.Draft(new Dictionary<string,object> { ["targetId"] = id,["title"] = "Edited" },service,read);
        Check(service.ConfirmChatDraft(edit,out _),service.LastError);
        Check(!service.ConfirmChatDraft(edit,out _),"Stale edit accepted.");
        Check(store.Data.tasks.Single().difficulty == TaskDifficulty.Normal,"AI changed difficulty.");
        var deleted = AiChatProtocol.Draft(new Dictionary<string,object> { ["targetId"] = id,["description"] = "Change" },service,
            service.QueryForChat("id",id).ToDictionary(v=>v.task.id));
        Check(service.Delete(id) && !service.ConfirmChatDraft(deleted,out _) && store.Data.tasks.Count == 0,"Edit resurrected deleted task.");
        var detailed=New(service,"Existing task"); detailed.task.difficulty=TaskDifficulty.Hard;
        detailed.task.description="Keep this description";
        detailed.task.subtasks.Add(new QuestSubtask {id="step-a",title="Done step"}); detailed.task.subtasks.Add(new QuestSubtask {id="step-b",title="Next step"});
        detailed.completed.Add("step-a"); Check(service.ConfirmChatDraft(detailed,out var detailedId),service.LastError);
        var detailView=service.QueryForChat("id",detailedId).Single();
        var rename=AiChatProtocol.Draft(new Dictionary<string,object>{["targetId"]=detailedId,["title"]="Renamed"},service,new Dictionary<string,TaskView>{[detailedId]=detailView});
        Check(service.ConfirmChatDraft(rename,out _),service.LastError);
        var afterRename=service.QueryForChat("id",detailedId).Single();
        Check(afterRename.task.description=="Keep this description" && afterRename.task.difficulty==TaskDifficulty.Hard && afterRename.occurrence.completedSubtaskIds.SequenceEqual(new[]{"step-a"}),"Edit overwrote unchanged task data or completion.");
        var beforeComplete=AiChatProtocol.Draft(new Dictionary<string,object>{["targetId"]=detailedId,["title"]="Should fail"},service,new Dictionary<string,TaskView>{[detailedId]=afterRename});
        Check(service.Complete(detailedId,true) && !service.ConfirmChatDraft(beforeComplete,out _),"Settled task accepted chat edit.");
        var dated = New(service,"Yesterday"); dated.task.scheduled = true; dated.task.date = "2026-10-23"; Check(service.ConfirmChatDraft(dated,out _),service.LastError);
        var daily = New(service,"Daily"); daily.task.daily = true; daily.task.date = "2026-10-24"; Check(service.ConfirmChatDraft(daily,out _),service.LastError);
        Check(service.QueryForChat("today",null).Select(v=>v.task.id).SequenceEqual(service.Query(TaskTab.Today).Select(v=>v.task.id)),"Chat today differs from task today.");
        Check(service.QueryForChat("search","Yesterday").Count == 1,"Search escaped its filter.");
        Check(service.QueryForChat("date","2026-10-24").Count == 1,"Date query wrong.");
        var stale = New(service); clock.day = clock.day.AddDays(1); Check(!service.ConfirmChatDraft(stale,out _),"Cross-day proposal accepted.");
        bool rejected = false; try { AiChatProtocol.Draft(new Dictionary<string,object> { ["title"] = "Bad",["difficulty"] = "Hard" },service,read); } catch (ArgumentException) { rejected = true; }
        Check(rejected,"Model difficulty accepted.");
        rejected = false; try { AiChatProtocol.Parse("{\"message\":\"bad\",\"drafts\":{}}"); } catch (ArgumentException) { rejected = true; } Check(rejected,"Malformed output accepted.");
        var storage = new Memory();
        storage.records["connection.dpapi"] = "{\"client_id\":\"test\",\"refresh_token\":\"test\",\"access_token\":\"test\",\"scope\":\"chatgpt.tokens.use.direct\",\"expires_at\":9999999999}";
        storage.records["preferences.json"] = "{\"model\":\"mock\",\"automatic\":true}";
        var http = new Http(); using var connection = new ChatGptConnection(storage,http); connection.Initialize().GetAwaiter().GetResult();
        Check(connection.Language=="auto","Legacy language default changed.");
        connection.SavePreferences("mock","en").GetAwaiter().GetResult();
        using(var reload=new ChatGptConnection(storage,new Http())){reload.Initialize().GetAwaiter().GetResult();Check(reload.Language=="en"&&reload.Model=="mock","Language preference did not reload.");}
        storage.fail=true;try{connection.SavePreferences("mock","zh").GetAwaiter().GetResult();}catch(System.IO.IOException){}storage.fail=false;
        Check(connection.Language=="en","Failed language save changed in-memory preference.");
        var chat = new AiChatSession(connection,storage,service); chat.Load(); chat.Send("Hello");
        Check(http.body.Contains("in English"),"Explicit language missing from request.");
        Check(!http.body.Contains("Yesterday") && !http.body.Contains("Daily"),"Ordinary chat sent tasks.");
        var cancelled = http.pending; chat.Stop(); cancelled.SetResult("{\"message\":\"late\",\"drafts\":[]}"); chat.Tick();
        Check(chat.History.messages.Count == 1,"Cancelled reply published."); chat.Retry();
        http.pending.SetResult("{\"message\":\"Review this task\",\"drafts\":[{\"title\":\"New chat task\"}]}");
        // Connection completion can resume on a worker; wait without accessing Unity from that worker.
        for(int i=0;i<100 && chat.Busy;i++) { Thread.Sleep(5); chat.Tick(); }
        Check(!chat.Busy && chat.History.messages.Count == 2,"Chat reply missing.");
        var restored = new AiChatSession(connection,storage,service); restored.Load();
        var draft = restored.History.messages.Last().drafts.Single();
        storage.fail = true; Check(!restored.Confirm(draft,draft.task),"Task committed before local draft persistence."); storage.fail = false;
        Check(restored.Confirm(draft,draft.task),"Confirmed restored draft failed.");
        Check(!restored.Confirm(draft,draft.task),"Repeated confirmation accepted.");
        var count = service.Snapshot.tasks.Count; restored.Clear(); Check(service.Snapshot.tasks.Count == count && restored.History.messages.Count == 0,"Clear chat removed tasks.");
        var tagChat = new AiChatSession(connection,storage,service); tagChat.Load(); tagChat.Send("Choose an existing tag for a study task");
        var tagLookup=http.pending;tagLookup.SetResult("{\"message\":\"\",\"query\":{\"scope\":\"tags\"},\"drafts\":[]}");
        for(int i=0;i<100 && http.pending==tagLookup;i++){Thread.Sleep(5);tagChat.Tick();}
        Check(http.pending!=tagLookup && http.body.Contains(tagId) && !http.body.Contains("New chat task"),"Tag lookup exposed tasks or omitted catalog.");
        http.pending.SetResult("{\"message\":\"Here is a tagged task\",\"drafts\":[{\"title\":\"Tagged proposal\",\"tagIds\":[\""+tagId+"\"]}]}");
        for(int i=0;i<100 && tagChat.Busy;i++){Thread.Sleep(5);tagChat.Tick();}
        Check(tagChat.History.messages.Last().drafts.Single().task.tagIds.Contains(tagId) && service.Snapshot.tasks.Count==count,"Tag proposal changed player data without confirmation.");
        Check(AiChatProtocol.ResolveLanguage("auto", "What tasks do I have today?")=="en", "English turn was not resolved.");
        Check(AiChatProtocol.ResolveLanguage("auto", "今天有什么任务？")=="zh", "Chinese turn was not resolved.");
        Check(AiChatProtocol.ResolveLanguage("en", "今天有什么任务？")=="en", "Explicit language lost priority.");
        Check(!AiChatProtocol.HasLanguageDrift("Today: Изучить", "en", new[]{"Изучить"}), "Original task name rejected.");
        connection.SavePreferences("mock","auto").GetAwaiter().GetResult();
        var languageChat = new AiChatSession(connection,storage,service); languageChat.Load(); languageChat.Send("What tasks do I have today?");
        var lookup=http.pending; lookup.SetResult("{\"message\":\"\",\"query\":{\"scope\":\"today\"},\"drafts\":[]}");
        for(int i=0;i<100 && http.pending==lookup;i++){Thread.Sleep(5);languageChat.Tick();}
        Check(http.pending!=lookup && http.body.Contains("in English"), "Lookup follow-up lost English language.");
        int messagesBefore=languageChat.History.messages.Count;
        var wrong=http.pending; wrong.SetResult("{\"message\":\"Сегодня у тебя 2 задачи\",\"drafts\":[{\"title\":\"Must not publish\"}]}");
        for(int i=0;i<100 && http.pending==wrong;i++){Thread.Sleep(5);languageChat.Tick();}
        Check(http.pending!=wrong && languageChat.History.messages.Count==messagesBefore && service.Snapshot.tasks.Count==count,"Wrong-language reply or drafts published.");
        http.pending.SetResult("{\"message\":\"Here are your tasks for today.\",\"drafts\":[]}");
        for(int i=0;i<100 && languageChat.Busy;i++){Thread.Sleep(5);languageChat.Tick();}
        Check(languageChat.History.messages.Last().text=="Here are your tasks for today.","Language repair did not publish corrected reply.");
        languageChat.Send("Hello again");
        wrong=http.pending;wrong.SetResult("{\"message\":\"Привет\",\"drafts\":[]}");
        for(int i=0;i<100 && http.pending==wrong;i++){Thread.Sleep(5);languageChat.Tick();}
        http.pending.SetResult("{\"message\":\"Привет\",\"drafts\":[]}");
        for(int i=0;i<100 && languageChat.Busy;i++){Thread.Sleep(5);languageChat.Tick();}
        Check(!languageChat.Busy && languageChat.CanRetry && languageChat.History.messages.Last().role=="user", "Language repair was unbounded or published invalid reply.");
        languageChat.Retry();wrong=http.pending;wrong.SetResult("{\"message\":\"Привет\",\"drafts\":[]}");
        for(int i=0;i<100 && http.pending==wrong;i++){Thread.Sleep(5);languageChat.Tick();}
        var lateRepair=http.pending;languageChat.Stop();lateRepair.SetResult("{\"message\":\"Hello\",\"drafts\":[]}");
        Thread.Sleep(20);languageChat.Tick();
        Check(languageChat.History.messages.Last().role=="user", "Stopped language repair published a late result.");
        storage.records["chat.dpapi"]="{\"messages\":[{}]}"; restored.Load(); Check(!restored.Loaded && restored.History.messages.Count==0,"Malformed history reached the UI.");
    }
}
