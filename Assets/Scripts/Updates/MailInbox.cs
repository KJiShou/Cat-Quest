using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CatQuest.Updates;
using UnityEngine;
using UnityEngine.Networking;

internal sealed class MailInbox
{
    readonly SqlitePlayerService service;
    readonly string catalogPath, updatePath;
    readonly Func<Task<string>> fetch;
    readonly Func<DateTimeOffset> now;
    readonly SemaphoreSlim requests = new SemaphoreSlim(1, 1);
    MailCatalog catalog = new MailCatalog { format = 1, letters = Array.Empty<MailLetter>() };
    internal ReleaseManifest Update { get; private set; }

    internal MailInbox(SqlitePlayerService service, string savePath, Func<Task<string>> fetch = null, Func<DateTimeOffset> now = null)
    {
        this.service = service;
        this.fetch = fetch ?? Download;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        catalogPath = savePath + ".mail-cache.json";
        updatePath = savePath + ".update-mail.json";
    }

    internal MailLetter[] Letters()
    {
        var today = now();
        return catalog.letters.Where(letter => letter.PublishedAt <= today)
            .OrderByDescending(letter => letter.PublishedAt).ThenBy(letter => letter.id, StringComparer.Ordinal).ToArray();
    }

    internal async Task LoadCacheAsync()
    {
        string json = await ReadCacheAsync(catalogPath);
        if (json != null)
        {
            try { catalog = MailCatalog.Parse(json); }
            catch (Exception error) when (error is ArgumentException || error is InvalidDataException) { }
        }
        string update = await ReadCacheAsync(updatePath);
        if (update != null)
        {
            try { RememberUpdate(JsonUtility.FromJson<ReleaseManifest>(update)); }
            catch (Exception error) when (error is ArgumentException || error is InvalidDataException) { }
        }
    }

    internal async Task StoreUpdateAsync(ReleaseManifest release)
    {
        if (!RememberUpdate(release)) return;
        await WriteCacheAsync(updatePath, JsonUtility.ToJson(release));
    }

    bool RememberUpdate(ReleaseManifest release)
    {
        UpdateRules.Validate(release);
        if (Update != null && UpdateRules.VersionOf(Update.version) >= UpdateRules.VersionOf(release.version)) return false;
        Update = release;
        return true;
    }

    internal async Task<bool> RefreshAsync()
    {
        await requests.WaitAsync();
        try { return await RefreshCatalogAsync(); }
        finally { requests.Release(); }
    }

    internal async Task<OperationResult<bool>> ClaimAsync(MailLetter shown)
    {
        await requests.WaitAsync();
        try
        {
            if (!await RefreshCatalogAsync())
                return Reject("Connect to the internet and try again to verify this letter.");
            var letter = catalog.letters.FirstOrDefault(candidate => candidate.id == shown.id);
            if (letter == null) return Reject("This reward letter was withdrawn.");
            if (letter.PublishedAt > now()) return Reject("This reward is not available yet.");
            if (!letter.SameReward(shown)) return Reject("The reward changed. Review the letter and try again.");
            return await service.ClaimLetterAsync(letter);
        }
        finally { requests.Release(); }
    }

    static OperationResult<bool> Reject(string message) => new OperationResult<bool> {
        Error = PlayerStorageError.InvalidData, Message = message
    };

    async Task<bool> RefreshCatalogAsync()
    {
        try
        {
            string json = await fetch();
            catalog = MailCatalog.Parse(json);
            await WriteCacheAsync(catalogPath, json);
            return true;
        }
        catch (Exception error) when (error is IOException || error is ArgumentException || error is InvalidDataException)
        {
            return false;
        }
    }

    static async Task<string> Download()
    {
        // Each claim needs an uncached network read, independent of the updater feed.
        using var request = UnityWebRequest.Get(MailCatalog.Feed + "?request=" + Guid.NewGuid().ToString("N"));
        request.timeout = 15;
        request.redirectLimit = 0;
        request.SetRequestHeader("Cache-Control", "no-cache");
        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            if (request.downloadedBytes > MailCatalog.MaxBytes) throw new IOException("Mail list is too large.");
            await Task.Yield();
        }
        if (request.result != UnityWebRequest.Result.Success || request.downloadedBytes > MailCatalog.MaxBytes)
            throw new IOException("Could not download the mail list.");
        return request.downloadHandler.text;
    }

    static Task<string> ReadCacheAsync(string path) => Task.Run(() => {
        try { return File.Exists(path) && new FileInfo(path).Length <= MailCatalog.MaxBytes ? File.ReadAllText(path) : null; }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return null; }
    });

    static Task WriteCacheAsync(string path, string value) => Task.Run(() => {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, value);
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
        }
    });
}
