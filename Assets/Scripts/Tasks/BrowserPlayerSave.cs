using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

// One synchronous browser transaction contains both the save and its recovery copy.
// Never acknowledge a reward on a queued, unfinished IndexedDB flush.
public sealed class BrowserPlayerSave
{
    [Serializable] sealed class Record
    {
        public int format = 1;
        public string primary, backup, beforeTutorial, beforeShop, corrupt;
    }
    [Serializable] sealed class ReadResult { public string value, error; }
    readonly Func<string, string, string> write;
    Record record;
    string original;
    public string Primary => record.primary;
    public string Backup => record.backup;

    public BrowserPlayerSave(Func<string> read, Func<string, string, string> write)
    {
        this.write = write;
        original = read() ?? "";
        record = string.IsNullOrEmpty(original) ? new Record() : JsonUtility.FromJson<Record>(original);
        if (record == null || record.format != 1 || !string.IsNullOrEmpty(original) && string.IsNullOrEmpty(record.primary))
            throw new IOException("This browser save format is unsupported. Your stored data has been kept.");
    }

    public void Commit(string json, bool primaryValid, bool preserveBeforeTutorial, bool preserveBeforeShop = false)
    {
        var next = new Record {
            primary = json,
            backup = primaryValid ? record.primary : record.backup,
            beforeTutorial = preserveBeforeTutorial && string.IsNullOrEmpty(record.beforeTutorial) ? record.primary : record.beforeTutorial,
            beforeShop = preserveBeforeShop && string.IsNullOrEmpty(record.beforeShop) ? record.primary : record.beforeShop,
            corrupt = !primaryValid && !string.IsNullOrEmpty(record.primary) ? record.primary : record.corrupt
        };
        string encoded = JsonUtility.ToJson(next);
        string error = write(original, encoded);
        if (!string.IsNullOrEmpty(error)) throw new IOException(error);
        record = next;
        original = encoded;
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern string CatQuest_ReadSave();
    [DllImport("__Internal")] static extern string CatQuest_WriteSave(string expected, string value);
    [DllImport("__Internal")] static extern string CatQuest_DownloadBackup(string json);
    public static BrowserPlayerSave Open() => new BrowserPlayerSave(() => {
        var response = JsonUtility.FromJson<ReadResult>(CatQuest_ReadSave());
        if (response == null || !string.IsNullOrEmpty(response.error))
            throw new IOException(response?.error ?? "Browser storage is unavailable.");
        return response.value;
    }, CatQuest_WriteSave);
    public static void Download(string json)
    {
        string error = CatQuest_DownloadBackup(json);
        if (!string.IsNullOrEmpty(error)) throw new IOException(error);
    }
#endif
}
