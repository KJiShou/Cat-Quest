using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Explicit development-player smoke mode. A marker is mandatory before any save is opened.
public sealed class AiBuildSmoke : MonoBehaviour
{
    public static bool Enabled => Debug.isDebugBuild && CompanionProtocol.HasArgument("--ai-smoke") && !CompanionProtocol.HasArgument("--companion");
    public static string DirectoryPath
    {
        get
        {
            string path = CompanionProtocol.Argument("--smoke-dir");
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path) || !File.Exists(Path.Combine(path, "ai-smoke.marker")))
                throw new InvalidOperationException("AI smoke mode requires an absolute isolated directory and marker.");
            path = Path.GetFullPath(path);
            string live = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), live, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(live + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("AI smoke mode cannot use the live application data directory.");
            return path;
        }
    }
    public static string IsolatedSavePath => Path.Combine(DirectoryPath, "test-save.json");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartSmoke()
    {
        if (Enabled)
        {
            var host = new GameObject("AI build smoke"); DontDestroyOnLoad(host); host.AddComponent<AiBuildSmoke>();
        }
    }
    Task operation;
    IEnumerator Start()
    {
        string directory = DirectoryPath;
        if (CompanionProtocol.HasArgument("--ai-chat-offline")) { yield return Offline(); yield break; }
        File.WriteAllText(Path.Combine(directory, "progress.txt"), "Opening isolated AI settings. Complete browser authorization after clicking Continue with ChatGPT.");
        yield return null;
        var launcher = FindFirstObjectByType<SettingsLauncher>(); if (!launcher) { Finish(false, "Settings launcher missing."); yield break; }
        launcher.Open(); yield return null;
        var settings = FindFirstObjectByType<SettingsPanelUI>(); settings.OpenAI();
        var client = ChatGptClient.EnsureInstance();
        double deadline = Time.realtimeSinceStartupAsDouble + 180;
        bool restoredCatalogRequested = false;
        while (Time.realtimeSinceStartupAsDouble < deadline && (!client.Ready || client.Busy || !client.Connection.Connected || client.Connection.Models.Count == 0))
        {
            if (client.Ready && !client.Busy && client.Connection.Connected && client.Connection.Models.Count == 0 && !restoredCatalogRequested)
            { restoredCatalogRequested = true; client.LoadModels(); }
            yield return null;
        }
        if (!client.Ready || client.Busy || !client.Connection.Connected || client.Connection.Models.Count == 0)
        { Finish(false, "No connected account/model before smoke deadline. Native OAuth remains unverified."); yield break; }
        operation = Verify(client.Connection);
        while (!operation.IsCompleted) yield return null;
        bool inference = !operation.IsFaulted && !operation.IsCanceled;
        // Model reload/selection may still be finishing in the settings UI.
        // Disconnect intentionally rejects calls while the client is busy.
        while (client.Busy) yield return null;
        client.Disconnect(); while (client.Busy) yield return null;
        if (!string.IsNullOrEmpty(client.VerificationFailure))
            File.WriteAllText(Path.Combine(directory, "operation-failure.txt"), client.VerificationFailure);
        Finish(inference && !client.Connection.Connected && client.Status.StartsWith("ChatGPT disconnected.", StringComparison.Ordinal),
            inference ? "Live inference and refresh passed. " + client.Status : "Synthetic inference failed in the Windows player; see AI settings.");
    }
    sealed class OfflineHttp : IChatGptHttp
    {
        public Task<string> Send(string url,string method,string body,string type,string bearer,bool stream,CancellationToken token)
        {
            if(url.EndsWith("/models")) return Task.FromResult("{\"models\":[{\"visibility\":\"list\",\"slug\":\"isolated-mock\"}]}");
            return Task.FromResult("{\"message\":\"Review this synthetic task before saving.\",\"drafts\":[{\"title\":\"Windows chat smoke task\",\"description\":\"测试本地聊天记录恢复。\"}]}");
        }
    }
    IEnumerator Offline()
    {
        yield return null;
        var client=ChatGptClient.EnsureInstance();
        while(client.Busy) yield return null;
        var storage=new ChatGptStorage(Path.Combine(DirectoryPath,"ai"));
        storage.Write("connection.dpapi","{\"client_id\":\"isolated-test\",\"refresh_token\":\"synthetic\",\"access_token\":\"synthetic\",\"scope\":\"chatgpt.tokens.use.direct\",\"expires_at\":9999999999}",true);
        storage.Write("preferences.json","{\"model\":\"isolated-mock\"}",false);
        client.ConfigureForVerification(storage,new OfflineHttp()); while(client.Busy) yield return null;
        var nav=FindFirstObjectByType<MainMenuNavigation>();
        if(!nav || !nav.ShowPage(MainMenuPage.Chat)) { Finish(false,"Chat navigation missing in player."); yield break; }
        bool restored=client.Chat.History.messages.Count>0;
        if(!restored) {
            client.Chat.Send("Create a synthetic test task.");
            double end=Time.realtimeSinceStartupAsDouble+15;
            while(client.Chat.Busy&&Time.realtimeSinceStartupAsDouble<end) yield return null;
            var messages=client.Chat.History.messages;
            if(messages.Count<2 || messages[messages.Count-1].drafts.Count!=1) { Finish(false,"Mock chat response failed."); yield break; }
            var draft=messages[messages.Count-1].drafts[0];
            if(!client.Chat.Confirm(draft,draft.task)) { Finish(false,client.Chat.Status); yield break; }
        }
        yield return new WaitForSecondsRealtime(.8f);
        var last=client.Chat.History.messages[client.Chat.History.messages.Count-1];
        bool pass=last.drafts.Count==1&&last.drafts[0].state=="saved"&&GameDataManager.EnsureInstance().Tasks.Snapshot.tasks.Count==1;
        Finish(pass,restored?"Offline Windows restart: chat and one confirmed task restored. Live provider not tested.":"Offline Windows chat and confirmation passed. Live provider not tested.");
    }
    static async Task Verify(ChatGptConnection connection)
    {
        await connection.RefreshForVerification(CancellationToken.None).ConfigureAwait(false);
        string result = await connection.Chat(new object[] { new System.Collections.Generic.Dictionary<string, object> {
            ["role"] = "user", ["content"] = "Say hello without querying tasks or making proposals." } }, CancellationToken.None).ConfigureAwait(false);
        AiChatProtocol.Parse(result);
    }
    void Finish(bool pass, string message)
    {
        ScreenCapture.CaptureScreenshot(Path.Combine(DirectoryPath, "ai-player.png"));
        File.WriteAllText(Path.Combine(DirectoryPath, "result.json"), ChatGptJson.Encode(new System.Collections.Generic.Dictionary<string, object> {
            ["utc"] = DateTime.UtcNow.ToString("O"), ["unity"] = Application.unityVersion, ["result"] = pass ? "PASS" : "FAIL",
            ["message"] = message, ["isolatedSave"] = GameDataManager.Instance.SavePath == IsolatedSavePath
        }));
    }
#endif
}
