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
        public Dictionary<string,string> records = new Dictionary<string,string>(); public bool fail; public int chatWrites;
        public string Read(string name, bool encrypted) => records.TryGetValue(name,out var value) ? value : null;
        public void Write(string name,string value,bool encrypted) { if (fail) throw new System.IO.IOException(); records[name] = value; if (name == "chat.dpapi") chatWrites++; }
    }
    sealed class Http : IChatGptHttp {
        public TaskCompletionSource<string> pending;
        public string body;
        public CancellationToken cancellation;
        public ChatGptEventStream stream;
        public void Emit(string delta)
        {
            var payload = new Dictionary<string, object> { ["type"] = "response.output_text.delta", ["delta"] = delta };
            stream.Push("data: " + ChatGptJson.Encode(payload) + "\r\n\r\n");
        }
        public Task<string> Send(string url,string method,string data,string type,string bearer,bool stream,CancellationToken token, Action<string> onDelta = null) {
            body = data;
            cancellation = token;
            this.stream = new ChatGptEventStream(onDelta);
            pending = new TaskCompletionSource<string>();
            return pending.Task;
        }
    }
    static void Check(bool value,string message) { if (!value) throw new Exception(message); }
    static AiTaskDraft New(SqlitePlayerService service,string title = "Study") => AiChatProtocol.Draft(new Dictionary<string,object> { ["title"] = title },service,new Dictionary<string,TaskView>());
    static async Task Pump(AiChatSession chat, Func<bool> waiting)
    {
        for (int i = 0; i < 100 && waiting(); i++) {
            await Task.Delay(5);
            chat.Tick();
        }
        Check(!waiting(), "Timed out waiting for chat request.");
    }

    static async Task VerifyCompaction(ChatGptConnection connection, Memory storage, Http http, SqlitePlayerService service)
    {
        string key = "chat.dpapi";
        string storeSnapshot = storage.records[key];
        try {
            var legacy = Enumerable.Range(0, 19).Select(i => new Dictionary<string, object> {
                ["role"] = i % 2 == 0 ? "assistant" : "user", ["text"] = "prior-" + i + "-text",
                ["drafts"] = Array.Empty<object>()
            }).ToArray();
            storage.records[key] = ChatGptJson.Encode(new Dictionary<string, object> { ["messages"] = legacy });
            var chat = new AiChatSession(connection, storage, service);
            chat.Load();
            chat.Send("Twentieth message");
            Check(http.body.Contains("prior-0-text") && !http.body.Contains("olderMessages"), "The conversation was compressed before exceeding the message limit.");
            http.pending.SetResult("{\"message\":\"Twentieth reply\",\"drafts\":[]}");
            await Pump(chat, () => chat.Busy);

            int writesBeforeTurn = storage.chatWrites;
            chat.Send("Current question");
            var summary = http.pending;
            Check(http.body.Contains("olderMessages") && http.body.Contains("prior-0-text") && !http.body.Contains("Current question"),
                "Compaction did not send the older messages separately from the latest question.");
            summary.SetResult("{\"summary\":\"The user prefers morning study.\"}");
            await Pump(chat, () => http.pending == summary);
            Check(http.body.Contains("The user prefers morning study.") && http.body.Contains("Current question") &&
                http.body.Contains("prior-18-text") && !http.body.Contains("prior-0-text") &&
                chat.History.messages.Count == 22 && chat.History.summarizedMessageCount == 0 && storage.chatWrites == writesBeforeTurn + 1,
                "The reply did not use the summary and recent original messages, or chat history was altered.");
            http.pending.SetResult("{\"message\":\"Try studying tomorrow morning.\",\"drafts\":[]}");
            await Pump(chat, () => chat.Busy);

            var restored = new AiChatSession(connection, storage, service);
            restored.Load();
            Check(storage.chatWrites == writesBeforeTurn + 2 && restored.History.summarizedMessageCount == 12 && restored.History.messages.Count == 23 && restored.History.messages[0].text == "prior-0-text",
                "Original conversation did not survive reload.");
            restored.Send("Follow-up question");
            Check(http.body.Contains("The user prefers morning study.") && !http.body.Contains("olderMessages") &&
                !http.body.Contains("prior-0-text"), "Reloaded summary was not reused for the next turn.");
            http.pending.SetResult("{\"message\":\"Follow-up reply\",\"drafts\":[]}");
            await Pump(restored, () => restored.Busy);

            // A few very long messages should also trigger rolling compaction.
            for (int i = 0; i < 7; i++)
                restored.History.messages.Add(new AiChatMessage { role = "assistant", text = new string('x', 2000) });
            restored.Send(new string('a', 2000));
            summary = http.pending;
            Check(http.body.Contains("olderMessages") && http.body.Contains("The user prefers morning study."),
                "The character limit did not trigger a rolling summary.");
            int previousCount = restored.History.summarizedMessageCount;
            summary.SetResult("{\"summary\":\"This response also attempted an action.\",\"drafts\":[{}]}");
            await Pump(restored, () => http.pending == summary);
            Check(http.body.Contains("The user prefers morning study.") && http.body.Contains(new string('a', 2000)) &&
                restored.History.summarizedMessageCount == previousCount, "Invalid summary replaced valid context or blocked a reply.");

            http.pending.SetResult("{\"message\":\"Continue with recent context.\",\"drafts\":[]}");
            await Pump(restored, () => restored.Busy);
            restored.Send("Try the rolling summary again");
            summary = http.pending;
            Check(http.body.Contains("olderMessages"), "Failed compaction could not be retried on the next turn.");
            storage.fail = true;
            summary.SetResult("{\"summary\":\"Cannot persist this summary.\"}");
            await Pump(restored, () => http.pending == summary);
            Check(restored.History.summarizedMessageCount == previousCount && http.body.Contains("Cannot persist this summary."),
                "The new summary should stay provisional until the final reply is saved.");
            http.pending.SetResult("{\"message\":\"Continue after the storage failure.\",\"drafts\":[]}");
            await Pump(restored, () => restored.Busy);

            storage.fail = false;
            var reloaded = new AiChatSession(connection, storage, service);
            reloaded.Load();
            Check(restored.CanRetry && reloaded.History.summarizedMessageCount == previousCount &&
                restored.History.summary == reloaded.History.summary && reloaded.History.messages.Last().role == "user",
                "A failed final write published a reply or its provisional summary.");
            restored.Send("Update the rolling summary");
            summary = http.pending;
            Check(http.body.Contains("olderMessages") && !http.body.Contains("prior-0-text"),
                "Rolling compaction resent already summarized messages after storage recovered.");
            summary.SetResult("{\"summary\":\"The user prefers morning study and is planning a follow-up.\"}");
            await Pump(restored, () => http.pending == summary);
            Check(restored.History.summarizedMessageCount == previousCount && http.body.Contains("planning a follow-up") &&
                http.body.Contains("Update the rolling summary"), "Rolling compaction did not advance its saved window.");
            http.pending.SetResult("{\"message\":\"Use the revised context.\",\"drafts\":[]}");
            await Pump(restored, () => restored.Busy);

            Check(restored.History.summarizedMessageCount > previousCount, "Committed reply did not advance its summary.");
            for (int i = 0; i < 8; i++)
                restored.History.messages.Add(new AiChatMessage { role = "assistant", text = new string('y', 2000) });
            restored.Send("Stop this turn");
            summary = http.pending;
            Check(http.body.Contains("olderMessages"), "A new overflow did not start compaction.");
            restored.Clear();
            summary.SetResult("{\"summary\":\"Late summary.\"}");
            await Task.Delay(20);
            restored.Tick();
            Check(!restored.Busy && http.pending == summary && restored.History.messages.Count == 0 &&
                restored.History.summarizedMessageCount == 0 && string.IsNullOrEmpty(restored.History.summary),
                "Clear allowed stopped compaction to publish context or start a reply.");
            restored.Load();
            Check(restored.History.messages.Count == 0 && string.IsNullOrEmpty(restored.History.summary),
                "Cleared conversation or its summary persisted.");
        } finally {
            storage.fail = false;
            storage.records[key] = storeSnapshot;
        }
    }

    static async Task VerifyStreaming(ChatGptConnection connection, Memory storage, Http http, SqlitePlayerService service)
    {
        string snapshot = storage.records["chat.dpapi"];
        try {
            storage.records.Remove("chat.dpapi");
            var chat = new AiChatSession(connection, storage, service);
            chat.Load();
            chat.Send("A synthetic streaming question");
            var first = http.pending;
            string firstChunk = "{\"query\":null,\"drafts\":[{\"message\":\"hidden draft data\"}],\"message\":\"你好\\n\\uD83D";
            http.Emit(firstChunk);
            chat.Tick();
            Check(chat.Busy && chat.PreviewText == "你好\n" && chat.History.messages.Count == 1, "Preview leaked JSON, held back text or published a draft.");
            http.Emit("\\uDE3A \\\"quoted\\\" \\\\ literal");
            chat.Tick();
            string partial = "你好\n😺 \"quoted\" \\ literal";
            Check(chat.PreviewText == partial, "Split JSON escapes or surrogate pairs were decoded incorrectly.");

            chat.StopReply();
            Check(http.cancellation.IsCancellationRequested && !chat.Busy && chat.PreviewText == null &&
                chat.History.messages.Last().stopped && chat.History.messages.Last().text == partial &&
                chat.History.messages.Last().drafts.Count == 0, "Stop did not cancel and save exactly the partial text.");
            http.Emit("late");
            first.SetResult("{\"message\":\"Late final\",\"drafts\":[]}");
            chat.Tick();
            Check(chat.History.messages.Count == 2 && chat.History.messages.Last().text == partial, "Cancelled output reached saved history.");

            var restored = new AiChatSession(connection, storage, service);
            restored.Load();
            Check(restored.CanRegenerate && restored.History.messages.Last().stopped, "Stopped reply and its action did not reload.");
            restored.Regenerate();
            var replacement = http.pending;
            restored.Regenerate();
            Check(replacement != first && replacement == http.pending && restored.History.messages.Count == 2 && restored.VisibleMessageCount == 1 &&
                http.body.Contains("A synthetic streaming question") && !http.body.Contains("quoted"), "Regeneration duplicated the prompt or requested twice.");
            restored.StopReply();
            Check(restored.CanRegenerate && restored.History.messages.Last().text == partial && restored.PreviewText == null,
                "Stopping before the first delta did not leave a retryable prompt.");

            restored.Regenerate();
            http.Emit("{\"message\":\"Preview before a dropped connection");
            restored.Tick();
            Check(restored.PreviewText == "Preview before a dropped connection", "Failed stream never reached the preview.");
            http.pending.SetException(new ArgumentException("Truncated"));
            await Pump(restored, () => restored.Busy);
            Check(restored.CanRegenerate && restored.PreviewText == null && restored.History.messages.Last().text == partial,
                "Unexpected disconnection published an unfinished reply.");

            restored.Regenerate();
            http.pending.SetResult("{\"message\":\"Entire reply\",\"drafts\":[{\"title\":\"Streaming fixture\"}]}");
            await Pump(restored, () => restored.Busy);
            var original = restored.History.messages.Last();
            var priorRequest = http.pending;
            int writesBeforeReplacement = storage.chatWrites;
            restored.Regenerate();
            Check(storage.chatWrites == writesBeforeReplacement && !http.body.Contains("Entire reply"),
                "Regeneration wrote or included the old reply before completion.");
            http.Emit("{\"message\":\"Unfinished replacement");
            restored.StopReply();
            Check(restored.History.messages.Last() == original && storage.chatWrites == writesBeforeReplacement,
                "Stopping a streamed replacement discarded the pending draft.");
            restored.Regenerate();
            storage.fail = true;
            http.pending.SetResult("{\"message\":\"Unsaved replacement\",\"drafts\":[]}");
            await Pump(restored, () => restored.Busy);
            storage.fail = false;
            var afterFailure = new AiChatSession(connection, storage, service);
            afterFailure.Load();
            Check(restored.History.messages.Last() == original && afterFailure.History.messages.Last().text == original.text &&
                afterFailure.History.messages.Last().drafts.Single().task.id == original.drafts.Single().task.id,
                "Failed final write lost the previous reply or draft on reload.");

            restored.Regenerate();
            Check(http.pending != priorRequest && restored.VisibleMessageCount == 1, "Completed reply could not be regenerated.");
            http.pending.SetResult("{\"message\":\"Replacement\",\"drafts\":[{\"title\":\"Saved streaming fixture\"}]}");
            await Pump(restored, () => restored.Busy);
            var saved = restored.History.messages.Last().drafts.Single();
            Check(await restored.ConfirmAsync(saved, saved.task) && !restored.CanRegenerate, "A reply with a confirmed task was replaceable.");
            restored.Send("Keep a stopped reply in history");
            http.Emit("{\"message\":\"stopped context marker");
            restored.StopReply();
            restored.Send("Another question without the stopped text");
            Check(!http.body.Contains("stopped context marker"), "Stopped text was sent as model context.");
            var active = http.pending;
            http.Emit("{\"message\":\"Do not keep me");
            restored.Stop();
            active.SetResult("{\"message\":\"Late after administrative cancel\",\"drafts\":[]}");
            restored.Tick();
            Check(restored.History.messages.Last().role == "user", "Administrative cancellation saved an unfinished reply.");
        } finally {
            storage.fail = false;
            storage.records["chat.dpapi"] = snapshot;
        }
    }

#if UNITY_EDITOR_WIN
    static async Task VerifyFileCommit(string directory, ChatGptConnection connection, Http http, SqlitePlayerService service)
    {
        var files = new ChatGptStorage(directory);
        var chat = new AiChatSession(connection, files, service);
        chat.Load();
        chat.Send("Suggest a task.");
        http.pending.SetResult("{\"message\":\"First suggestion\",\"drafts\":[{\"title\":\"File commit fixture\"}]}");
        await Pump(chat, () => chat.Busy);
        var draft = chat.History.messages.Last().drafts.Single();

        chat.Send("A second question");
        http.pending.SetResult("{\"message\":\"Second reply\",\"drafts\":[]}");
        for (int i = 0; i < 100 && !chat.Tick(); i++) await Task.Delay(5);
        chat.Dismiss(draft);
        await Pump(chat, () => chat.Busy);
        var restored = new AiChatSession(connection, files, service);
        restored.Load();
        Check(restored.History.messages.Count == 4 && restored.History.messages[1].drafts.Single().state == "dismissed",
            "Writing the reply overwrote the earlier draft change.");

        chat.Regenerate();
        http.pending.SetResult("{\"message\":\"Superseded reply\",\"drafts\":[]}");
        for (int i = 0; i < 100 && !chat.Tick(); i++) await Task.Delay(5);
        chat.Clear();
        chat.Send("Fresh conversation");
        http.pending.SetResult("{\"message\":\"Fresh reply\",\"drafts\":[]}");
        await Pump(chat, () => chat.Busy);
        restored.Load();
        Check(restored.History.messages.Count == 2 && restored.History.messages.Last().text == "Fresh reply",
            "An older staged reply replaced the cleared conversation.");
    }
#endif

    public static async Task Run()
    {
        var clock = new Clock();
        string directory = System.IO.Path.GetFullPath(System.IO.Path.Combine("Temp/SQLiteMigrationVerification", "ai-" + Guid.NewGuid().ToString("N")));
        System.IO.Directory.CreateDirectory(directory);
        var repository = new SqlitePlayerRepository(System.IO.Path.Combine(directory,"data.sqlite3"), null);
        var service = new SqlitePlayerService(repository, clock);
        bool fail = false;
        repository.FaultPoint = stage => { if (fail && stage == "before-commit") throw new System.IO.IOException("Injected save failure"); };
        try {
        Check((await service.InitializeAsync()).Succeeded,"SQLite AI fixture did not initialize.");
        var tagResult = await service.SaveTagAsync(null, "Chat verification"); Check(tagResult.Succeeded, tagResult.Message); var tagId = tagResult.Value;
        var tagged = AiChatProtocol.Draft(new Dictionary<string,object> { ["title"]="Tagged", ["tagIds"]=new List<object>{tagId} },service,new Dictionary<string,TaskView>());
        Check(tagged.task.tagIds.SequenceEqual(new[]{tagId}),"Existing tag proposal lost.");
        bool unknownTagRejected=false;
        try { AiChatProtocol.Draft(new Dictionary<string,object>{["tagIds"]=new List<object>{"invented"}},service,new Dictionary<string,TaskView>()); } catch(ArgumentException){unknownTagRejected=true;}
        Check(unknownTagRejected,"Invented tag accepted.");
        var proposal = New(service);
        fail = true; Check(!(await service.ConfirmChatDraftAsync(proposal)).Succeeded && service.TaskSnapshot.tasks.Count == 0,"Failed save mutated task data."); fail = false;
        var proposalResult = await service.ConfirmChatDraftAsync(proposal); Check(proposalResult.Succeeded,service.LastError); var id = proposalResult.Value;
        Check(!(await service.ConfirmChatDraftAsync(proposal)).Succeeded && service.TaskSnapshot.tasks.Count == 1,"Duplicate proposal added a task.");
        var original = (await service.QueryForChatAsync("id",id)).Value.Single();
        var read = new Dictionary<string,TaskView> { [id] = original };
        var edit = AiChatProtocol.Draft(new Dictionary<string,object> { ["targetId"] = id,["title"] = "Edited" },service,read);
        Check((await service.ConfirmChatDraftAsync(edit)).Succeeded,service.LastError);
        Check(!(await service.ConfirmChatDraftAsync(edit)).Succeeded,"Stale edit accepted.");
        Check(service.TaskSnapshot.tasks.Single().difficulty == TaskDifficulty.Normal,"AI changed difficulty.");
        var deleted = AiChatProtocol.Draft(new Dictionary<string,object> { ["targetId"] = id,["description"] = "Change" },service,
            (await service.QueryForChatAsync("id",id)).Value.ToDictionary(v=>v.task.id));
        Check((await service.DeleteAsync(id)).Succeeded && !(await service.ConfirmChatDraftAsync(deleted)).Succeeded && service.TaskSnapshot.tasks.Count == 0,"Edit resurrected deleted task.");
        var detailed=New(service,"Existing task"); detailed.task.difficulty=TaskDifficulty.Hard;
        detailed.task.description="Keep this description";
        detailed.task.subtasks.Add(new QuestSubtask {id="step-a",title="Done step"}); detailed.task.subtasks.Add(new QuestSubtask {id="step-b",title="Next step"});
        detailed.completed.Add("step-a"); var detailResult = await service.ConfirmChatDraftAsync(detailed); Check(detailResult.Succeeded,service.LastError); var detailedId = detailResult.Value;
        var detailView=(await service.QueryForChatAsync("id",detailedId)).Value.Single();
        var rename=AiChatProtocol.Draft(new Dictionary<string,object>{["targetId"]=detailedId,["title"]="Renamed"},service,new Dictionary<string,TaskView>{[detailedId]=detailView});
        Check((await service.ConfirmChatDraftAsync(rename)).Succeeded,service.LastError);
        var afterRename=(await service.QueryForChatAsync("id",detailedId)).Value.Single();
        Check(afterRename.task.description=="Keep this description" && afterRename.task.difficulty==TaskDifficulty.Hard && afterRename.occurrence.completedSubtaskIds.SequenceEqual(new[]{"step-a"}),"Edit overwrote unchanged task data or completion.");
        var beforeComplete=AiChatProtocol.Draft(new Dictionary<string,object>{["targetId"]=detailedId,["title"]="Should fail"},service,new Dictionary<string,TaskView>{[detailedId]=afterRename});
        Check((await service.CompleteAsync(detailedId,true)).Succeeded && !(await service.ConfirmChatDraftAsync(beforeComplete)).Succeeded,"Settled task accepted chat edit.");
        var dated = New(service,"Yesterday"); dated.task.scheduled = true; dated.task.date = "2026-10-23"; Check((await service.ConfirmChatDraftAsync(dated)).Succeeded,service.LastError);
        var daily = New(service,"Daily"); daily.task.daily = true; daily.task.date = "2026-10-24"; Check((await service.ConfirmChatDraftAsync(daily)).Succeeded,service.LastError);
        Check((await service.QueryForChatAsync("today",null)).Value.Select(v=>v.task.id).SequenceEqual(service.Query(TaskTab.Today).Select(v=>v.task.id)),"Chat today differs from task today.");
        Check((await service.QueryForChatAsync("search","Yesterday")).Value.Count == 1,"Search escaped its filter.");
        Check((await service.QueryForChatAsync("date","2026-10-24")).Value.Count == 1,"Date query wrong.");
        var stale = New(service); clock.day = clock.day.AddDays(1); await service.RefreshDateAsync(); Check(!(await service.ConfirmChatDraftAsync(stale)).Succeeded,"Cross-day proposal accepted.");
        bool rejected = false; try { AiChatProtocol.Draft(new Dictionary<string,object> { ["title"] = "Bad",["difficulty"] = "Hard" },service,read); } catch (ArgumentException) { rejected = true; }
        Check(rejected,"Model difficulty accepted.");
        rejected = false; try { AiChatProtocol.Parse("{\"message\":\"bad\",\"drafts\":{}}"); } catch (ArgumentException) { rejected = true; } Check(rejected,"Malformed output accepted.");
        var storage = new Memory();
        storage.records["connection.dpapi"] = "{\"client_id\":\"test\",\"refresh_token\":\"test\",\"access_token\":\"test\",\"scope\":\"chatgpt.tokens.use.direct\",\"expires_at\":9999999999}";
        storage.records["preferences.json"] = "{\"model\":\"mock\",\"automatic\":true}";
        var http = new Http(); using var connection = new ChatGptConnection(storage,http); await connection.Initialize();
        Check(connection.Language=="auto","Legacy language default changed.");
        await connection.SavePreferences("mock","en");
        using(var reload=new ChatGptConnection(storage,new Http())){await reload.Initialize();Check(reload.Language=="en"&&reload.Model=="mock","Language preference did not reload.");}
        storage.fail=true;try{await connection.SavePreferences("mock","zh");}catch(System.IO.IOException){}storage.fail=false;
        Check(connection.Language=="en","Failed language save changed in-memory preference.");
        var chat = new AiChatSession(connection,storage,service); chat.Load(); chat.Send("Hello");
        Check(http.body.Contains("in English"),"Explicit language missing from request.");
        Check(!http.body.Contains("Yesterday") && !http.body.Contains("Daily"),"Ordinary chat sent tasks.");
        var cancelled = http.pending; chat.Stop(); cancelled.SetResult("{\"message\":\"late\",\"drafts\":[]}"); chat.Tick();
        Check(chat.History.messages.Count == 1,"Cancelled reply published."); chat.Retry();
        http.pending.SetResult("{\"message\":\"Review this task\",\"drafts\":[{\"title\":\"New chat task\"}]}");
        // Connection completion can resume on a worker; wait without accessing Unity from that worker.
        for(int i=0;i<100 && chat.Busy;i++) { await Task.Delay(5); chat.Tick(); }
        Check(!chat.Busy && chat.History.messages.Count == 2,"Chat reply missing.");
        var restored = new AiChatSession(connection,storage,service); restored.Load();
        var draft = restored.History.messages.Last().drafts.Single();
        storage.fail = true; Check(!(await restored.ConfirmAsync(draft,draft.task)),"Task committed before local draft persistence."); storage.fail = false;
        Check((await restored.ConfirmAsync(draft,draft.task)),"Confirmed restored draft failed.");
        Check(!(await restored.ConfirmAsync(draft,draft.task)),"Repeated confirmation accepted.");
        var count = service.TaskSnapshot.tasks.Count; restored.Clear(); Check(service.TaskSnapshot.tasks.Count == count && restored.History.messages.Count == 0,"Clear chat removed tasks.");
        var tagChat = new AiChatSession(connection,storage,service); tagChat.Load(); tagChat.Send("Choose an existing tag for a study task");
        var tagLookup=http.pending;
        http.Emit("{\"message\":\"\",\"query\":{\"scope\":\"tags\"},\"drafts\":[]}");
        tagChat.Tick();
        Check(tagChat.PreviewText == null, "A tool query was shown as a chat reply.");
        tagLookup.SetResult("{\"message\":\"\",\"query\":{\"scope\":\"tags\"},\"drafts\":[]}");
        for(int i=0;i<100 && http.pending==tagLookup;i++){await Task.Delay(5);tagChat.Tick();}
        Check(http.pending!=tagLookup && http.body.Contains(tagId) && !http.body.Contains("New chat task"),"Tag lookup exposed tasks or omitted catalog.");
        http.pending.SetResult("{\"message\":\"Here is a tagged task\",\"drafts\":[{\"title\":\"Tagged proposal\",\"tagIds\":[\""+tagId+"\"]}]}");
        for(int i=0;i<100 && tagChat.Busy;i++){await Task.Delay(5);tagChat.Tick();}
        Check(tagChat.History.messages.Last().drafts.Single().task.tagIds.Contains(tagId) && service.TaskSnapshot.tasks.Count==count,"Tag proposal changed player data without confirmation.");
        Check(AiChatProtocol.ResolveLanguage("auto", "What tasks do I have today?")=="en", "English turn was not resolved.");
        Check(AiChatProtocol.ResolveLanguage("auto", "今天有什么任务？")=="zh", "Chinese turn was not resolved.");
        Check(AiChatProtocol.ResolveLanguage("en", "今天有什么任务？")=="en", "Explicit language lost priority.");
        Check(!AiChatProtocol.HasLanguageDrift("Today: Изучить", "en", new[]{"Изучить"}), "Original task name rejected.");
        await connection.SavePreferences("mock","auto");
        var languageChat = new AiChatSession(connection,storage,service); languageChat.Load(); languageChat.Send("What tasks do I have today?");
        var lookup=http.pending; lookup.SetResult("{\"message\":\"\",\"query\":{\"scope\":\"today\"},\"drafts\":[]}");
        for(int i=0;i<100 && http.pending==lookup;i++){await Task.Delay(5);languageChat.Tick();}
        Check(http.pending!=lookup && http.body.Contains("in English"), "Lookup follow-up lost English language.");
        int messagesBefore=languageChat.History.messages.Count;
        var wrong=http.pending;
        http.Emit("{\"message\":\"Сегодня у тебя 2 задачи\",\"drafts\":[]}");
        languageChat.Tick();
        Check(!string.IsNullOrEmpty(languageChat.PreviewText), "The provisional language reply did not stream.");
        wrong.SetResult("{\"message\":\"Сегодня у тебя 2 задачи\",\"drafts\":[{\"title\":\"Must not publish\"}]}");
        for(int i=0;i<100 && http.pending==wrong;i++){await Task.Delay(5);languageChat.Tick();}
        Check(http.pending!=wrong && languageChat.PreviewText == null && languageChat.History.messages.Count==messagesBefore && service.TaskSnapshot.tasks.Count==count,"Wrong-language reply or drafts published.");
        http.pending.SetResult("{\"message\":\"Here are your tasks for today.\",\"drafts\":[]}");
        for(int i=0;i<100 && languageChat.Busy;i++){await Task.Delay(5);languageChat.Tick();}
        Check(languageChat.History.messages.Last().text=="Here are your tasks for today.","Language repair did not publish corrected reply.");
        languageChat.Send("Hello again");
        wrong=http.pending;wrong.SetResult("{\"message\":\"Привет\",\"drafts\":[]}");
        for(int i=0;i<100 && http.pending==wrong;i++){await Task.Delay(5);languageChat.Tick();}
        http.pending.SetResult("{\"message\":\"Привет\",\"drafts\":[]}");
        for(int i=0;i<100 && languageChat.Busy;i++){await Task.Delay(5);languageChat.Tick();}
        Check(!languageChat.Busy && languageChat.CanRetry && languageChat.History.messages.Last().role=="user", "Language repair was unbounded or published invalid reply.");
        languageChat.Retry();wrong=http.pending;wrong.SetResult("{\"message\":\"Привет\",\"drafts\":[]}");
        for(int i=0;i<100 && http.pending==wrong;i++){await Task.Delay(5);languageChat.Tick();}
        var lateRepair=http.pending;languageChat.Stop();lateRepair.SetResult("{\"message\":\"Hello\",\"drafts\":[]}");
        await Task.Delay(20);languageChat.Tick();
        Check(languageChat.History.messages.Last().role=="user", "Stopped language repair published a late result.");
        await VerifyCompaction(connection, storage, http, service);
        await VerifyStreaming(connection, storage, http, service);
#if UNITY_EDITOR_WIN
        await VerifyFileCommit(System.IO.Path.Combine(directory, "chat-files"), connection, http, service);
#endif
        storage.records["chat.dpapi"]="{\"messages\":[{}]}"; restored.Load(); Check(!restored.Loaded && restored.History.messages.Count==0,"Malformed history reached the UI.");
        } finally { await service.CloseAsync(); }
    }
}
