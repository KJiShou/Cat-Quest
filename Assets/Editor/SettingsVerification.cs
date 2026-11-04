using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UI;

// Verification never authors or repairs UI. All persistence tests use isolated saves.
[InitializeOnLoad]
public static class SettingsVerification
{
    sealed class Store : IPlayerDataStore
    {
        public PlayerData Data { get; private set; } = new PlayerData();
        public bool fail;
        public bool TrySave(PlayerData candidate, out string error)
        { error = fail ? "Simulated settings write failure" : null; if (!fail) Data = candidate; return !fail; }
    }
    sealed class Clock : ITaskClock
    { public DateTime LocalNow => new DateTime(2026,10,21,12,0,0); public DateTime UtcNow => LocalNow.AddHours(-8); }
    static SettingsVerification() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists("Temp/verify-settings-data.request"))
        {
            File.Delete("Temp/verify-settings-data.request");
            string folder = "Library/SettingsVerification/Data-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0,8);
            Directory.CreateDirectory(folder);
            string report = DateTime.UtcNow.ToString("o") + " Unity " + Application.unityVersion + "\n";
            try
            {
                FocusImplementationVerification.Run();
                Run(folder);
                SettingsAccountVerification.Run();
                report += "PASS: FocusImplementationVerification (including Rewards/Tasks), Settings persistence and prefab bindings, SettingsAccountVerification.\n";
            }
            catch (Exception e) { report += "FAIL: " + e; Debug.LogException(e); }
            File.WriteAllText(folder+"/result.txt",report);
            File.WriteAllText("Temp/verify-settings-data.result",folder+"/result.txt\n"+report);
        }
        if (File.Exists("Temp/build-settings-windows.request"))
        {
            File.Delete("Temp/build-settings-windows.request");
            string folder = "Library/SettingsVerification/Build-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            Directory.CreateDirectory(folder);
            // Unity forbids player outputs inside its Library/Temp work directories.
            string executable = "Builds/SettingsVerification/" + Path.GetFileName(folder) + "/CatQuest.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(executable));
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),
                    locationPathName = executable, target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development });
                if (report == null) throw new InvalidOperationException("Unity did not return a build report. Inspect the current Editor log for the build error.");
                string result = DateTime.UtcNow.ToString("o") + " " + report.summary.result + "\n" + Path.GetFullPath(executable) + "\nErrors=" + report.summary.totalErrors;
                File.WriteAllText(folder+"/build-result.txt",result); File.WriteAllText("Temp/build-settings-windows.result",result);
                if(report.summary.result != BuildResult.Succeeded) Debug.LogError(result);
            }
            catch(Exception e) { File.WriteAllText("Temp/build-settings-windows.result",e.ToString()); Debug.LogException(e); }
        }
    }
    static void Check(bool condition,string message) { if(!condition)throw new Exception(message); }
    static void VerifyBackupRepeat(string folder)
    {
        string path = Path.GetFullPath(folder + "/repeat-save.json");
        string backup = Path.GetFullPath(folder + "/export.json");
        var service = new TaskService(new PlayerDataStore(path), new Clock());
        var task = new QuestTask { title = "Review", daily = true, date = "2026-10-14", repeatUnit = RepeatUnit.Weeks, repeatInterval = 2 };
        Check(service.Upsert(task, Array.Empty<string>(), out string id), "Could not save custom repeat");
        Check(!service.Complete(id) && service.Query(TaskTab.Today).Count == 0, "Off-week allowed completion");
        var week = service.QueryCalendar(service.LocalNow.Date, service.LocalNow.Date.AddDays(7));
        Check(week.days.Values.Sum(d => d.items.Count) == 1 && week.days[service.LocalNow.Date.AddDays(7)].items.Count == 1, "Biweekly projection");
        service.TransferBackup(backup, true);
        Check(service.Delete(id), "Could not prepare import");
        service.TransferBackup(backup, false);
        service.TransferBackup(backup, false);
        var restored = new PlayerDataStore(path).Data;
        Check(restored.tasks.Single().id == id && restored.tasks[0].repeatInterval == 2 && restored.fishCoins == 0, "Backup reload duplicated or lost task");
        Check(Directory.GetFiles(folder, "repeat-save.json.before-import-*.json").Length == 2, "Missing recovery copy");
        string original = File.ReadAllText(path);
        File.WriteAllText(backup, "{\"version\":999}");
        try { service.TransferBackup(backup, false); throw new Exception("Unsupported backup accepted"); }
        catch (IOException) { Check(File.ReadAllText(path) == original, "Rejected import altered save"); }
        task.repeatWeekdays = (1 << 0) | (1 << 4);
        task.repeatUntil = "2026-10-30";
        Check(!TaskRepeat.Matches(task, new DateTime(2026,10,12)) && TaskRepeat.Matches(task, new DateTime(2026,10,16))
            && !TaskRepeat.Matches(task, new DateTime(2026,10,23)) && TaskRepeat.Matches(task, new DateTime(2026,10,26))
            && TaskRepeat.Matches(task, new DateTime(2026,10,30)) && !TaskRepeat.Matches(task, new DateTime(2026,11,9)), "Weekly mask, interval or inclusive end");
        task.repeatUntil = null; task.repeatWeekdays = 0;
        task.date = "2024-02-29"; task.repeatUnit = RepeatUnit.Years; task.repeatInterval = 1;
        Check(!TaskRepeat.Matches(task, new DateTime(2025,2,28)) && TaskRepeat.Matches(task, new DateTime(2028,2,29)), "Leap day recurrence");
        task.date = "2026-11-04"; task.repeatUnit = RepeatUnit.MonthlyWeekday;
        Check(TaskRepeat.Matches(task, new DateTime(2026,12,2)) && !TaskRepeat.Matches(task, new DateTime(2026,12,9)), "Ordinal weekday recurrence");
    }

    public static void Run(string folder)
    {
        VerifyDateGroups();
        VerifyBackupRepeat(folder);
        var old=JsonUtility.FromJson<PlayerData>("{\"version\":5,\"username\":\"Settings test\",\"fishCoins\":31}"); PlayerDataStore.Normalize(old);
        Check(old.settings.notificationsEnabled && old.settings.soundVolume==1f && old.settings.musicVolume==1f && old.fishCoins==31,"Legacy setting defaults or unrelated data changed");
        Check(new AppSettings().musicVolume == 1f && new AppSettings().soundVolume == 1f, "New players must start with full music and SFX volume");
        var muted = JsonUtility.FromJson<AppSettings>("{\"musicVolume\":0}");
        Check(AppSettings.Normalize(muted).musicVolume == 0, "Existing explicit mute must be preserved");
        var store=new Store(); var service=new TaskService(store,new Clock()); int changed=0; service.Changed+=()=>changed++;
        var draft=service.Settings; draft.notificationsEnabled=false; draft.soundVolume=.25f; draft.musicVolume=.8f;
        Check(store.Data.settings.notificationsEnabled,"Reading settings exposed mutable live data");
        store.fail=true; Check(!service.SaveSettings(draft) && changed==0 && store.Data.settings.notificationsEnabled,"Failed settings save mutated data or notified success");
        store.fail=false; Check(service.SaveSettings(draft) && changed==1 && !store.Data.settings.notificationsEnabled,"Settings retry failed");
        draft.soundVolume=0; Check(store.Data.settings.soundVolume==.25f,"Saved settings retained external mutable reference");
        Check(!service.SaveSettings(new AppSettings{musicVolume=float.NaN}) && changed==1,"Invalid volume accepted");
        Check(service.SaveSettings(new AppSettings{soundVolume=-1,musicVolume=2}) && service.Settings.soundVolume==0 && service.Settings.musicVolume==1,"Volume boundaries not clamped");
        string path=Path.GetFullPath(folder+"/settings-save.json"); File.WriteAllText(path,JsonUtility.ToJson(old));
        var fileStore=new PlayerDataStore(path); var persisted=new TaskService(fileStore,new Clock());
        Check(persisted.SaveSettings(new AppSettings{notificationsEnabled=false,soundVolume=0,musicVolume=.7f}),"File settings save failed");
        var reload=new PlayerDataStore(path); Check(!reload.Data.settings.notificationsEnabled && reload.Data.settings.musicVolume==.7f && reload.Data.fishCoins==31 && File.Exists(path+".bak"),"Settings did not survive reload/backup");
        var legacy=JsonUtility.FromJson<PlayerData>("{\"settings\":{\"notificationsEnabled\":false,\"musicVolume\":0.3}}");
        PlayerDataStore.Normalize(legacy);
        Check(legacy.settings.DashboardRanking==LeaderboardKind.FocusMinutes && !legacy.settings.notificationsEnabled && Math.Abs(legacy.settings.musicVolume-.3f)<.0001f,"Existing settings without a ranking did not preserve Focus/defaults");
        Check(AppSettings.Normalize(new AppSettings{dashboardRanking="unknown"}).DashboardRanking==LeaderboardKind.FocusMinutes,"Unknown ranking did not fall back safely");
        Check(AppSettings.Normalize(new AppSettings{dashboardRanking="99"}).DashboardRanking==LeaderboardKind.FocusMinutes,"Out-of-range ranking accepted");
        foreach(LeaderboardKind kind in Enum.GetValues(typeof(LeaderboardKind)))
        {
            var preference=persisted.Settings; preference.dashboardRanking=kind.ToString();
            Check(persisted.SaveSettings(preference),"Ranking preference save failed: "+kind);
            var reopened=new TaskService(new PlayerDataStore(path),new Clock());
            Check(reopened.Settings.DashboardRanking==kind && reopened.Settings.musicVolume==.7f && !reopened.Settings.notificationsEnabled && reopened.Snapshot.fishCoins==31,"Ranking reload changed selection or unrelated data: "+kind);
        }
        Check(legacy.settings.DefaultTaskTab == TaskTab.Today, "Legacy settings must open Today");
        Check(AppSettings.Normalize(new AppSettings { defaultTaskTab = TaskTab.History }).DefaultTaskTab == TaskTab.Today, "History accepted as default tab");
        Check(AppSettings.Normalize(new AppSettings { defaultTaskTab = (TaskTab)99 }).DefaultTaskTab == TaskTab.Today, "Invalid default tab accepted");
        foreach(var tab in new[] { TaskTab.Today, TaskTab.Daily, TaskTab.Todo })
        {
            var preference = persisted.Settings; preference.defaultTaskTab = tab;
            Check(persisted.SaveSettings(preference), "Default tab save failed");
            Check(new PlayerDataStore(path).Data.settings.DefaultTaskTab == tab, "Default tab not persisted");
        }
        var tabDraft = service.Settings; tabDraft.defaultTaskTab = TaskTab.Todo;
        store.fail = true;
        Check(!service.SaveSettings(tabDraft) && service.Settings.DefaultTaskTab == TaskTab.Today, "Failed tab save changed committed preference");
        store.fail = false;
        Check(service.SaveSettings(tabDraft) && service.SaveSettings(tabDraft) && service.Settings.DefaultTaskTab == TaskTab.Todo, "Tab save retry/repeat failed");
        var previous=service.Settings;store.fail=true;
        var rejected=service.Settings;rejected.dashboardRanking=LeaderboardKind.Power.ToString();
        Check(!service.SaveSettings(rejected) && service.Settings.DashboardRanking==previous.DashboardRanking,"Failed ranking save changed committed preference");
        store.fail=false;Check(service.SaveSettings(rejected) && service.Settings.DashboardRanking==LeaderboardKind.Power,"Ranking save retry failed");
        Check(!legacy.settings.hideDashboardTasks && !legacy.settings.hideDashboardDailyRewards && !legacy.settings.hideDashboardFocus && !legacy.settings.hideDashboardRanking && !legacy.settings.hideDashboardCharacter,"Legacy saves hid Dashboard components");
        for(int mask=0;mask<32;mask++)
        {
            var visibility=persisted.Settings;
            visibility.hideDashboardTasks=(mask&1)!=0;visibility.hideDashboardDailyRewards=(mask&2)!=0;
            visibility.hideDashboardFocus=(mask&4)!=0;visibility.hideDashboardRanking=(mask&8)!=0;
            visibility.hideDashboardCharacter=(mask&16)!=0;
            Check(persisted.SaveSettings(visibility),"Dashboard visibility save failed: "+persisted.LastError);
            var saved=new PlayerDataStore(path).Data.settings;
            Check(saved.hideDashboardTasks==visibility.hideDashboardTasks && saved.hideDashboardDailyRewards==visibility.hideDashboardDailyRewards && saved.hideDashboardFocus==visibility.hideDashboardFocus && saved.hideDashboardRanking==visibility.hideDashboardRanking && saved.hideDashboardCharacter==visibility.hideDashboardCharacter && saved.DashboardRanking==LeaderboardKind.Power && saved.musicVolume==.7f,"Dashboard visibility combinations did not survive reload independently: "+mask);
        }
        var failedVisibility=service.Settings;failedVisibility.hideDashboardTasks=true;failedVisibility.hideDashboardRanking=true;failedVisibility.hideDashboardCharacter=true;
        store.fail=true;Check(!service.SaveSettings(failedVisibility) && !service.Settings.hideDashboardTasks && !service.Settings.hideDashboardRanking && !service.Settings.hideDashboardCharacter,"Failed visibility save changed committed settings");
        store.fail=false;Check(service.SaveSettings(failedVisibility) && service.Settings.hideDashboardTasks && service.Settings.hideDashboardRanking && service.Settings.hideDashboardCharacter,"Visibility save retry failed");
        var prefab=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Settings/Settings.prefab");
        try
        {
            var ui=prefab.GetComponent<SettingsPanelUI>();
            Check(ui && ui.settingsPage && ui.tagsPage && ui.accountPage && ui.tagEditorPage && ui.confirmPage,"Settings pages not bound");
            Check(ui.notifications && ui.sound && ui.music && ui.tagRowTemplate && ui.tagTintSprite && ui.googleSignIn,"Settings controls not bound");
            Check(ui.colorButtons.Length==TagColors.Palette.Length && ui.tagName.characterLimit==30,"Tag palette or name limit differs from domain");
            Check(ui.sound.minValue==0 && ui.sound.maxValue==1 && ui.music.minValue==0 && ui.music.maxValue==1,"Volume ranges wrong");
            Check(ui.dashboardRankings.Length==4 && ui.dashboardRankings.All(button=>button && button.GetComponent<JuicyButton>()) && ui.rankingNormal && ui.rankingSelected,"Dashboard ranking selector bindings missing");
            foreach(var toggle in new[]{ui.showDashboardTasks,ui.showDashboardDailyRewards,ui.showDashboardFocus,ui.showDashboardRanking,ui.showDashboardCharacter})
                Check(toggle && toggle.GetComponent<SettingsToggleVisual>() && !toggle.group,"Dashboard visibility switch missing or not independent");
            foreach(var visual in ui.GetComponentsInChildren<SettingsToggleVisual>(true))Check(visual.onColor.b>visual.onColor.r && visual.handleOnColor.b>visual.handleOnColor.r && visual.handleOffColor.b>visual.handleOffColor.r,"Switch theme is not blue");
            foreach(var visual in ui.GetComponentsInChildren<SettingsSliderVisual>(true))Check(visual.enabledHandleColor.b>visual.enabledHandleColor.r && visual.mutedHandleColor.b>visual.mutedHandleColor.r && visual.fill.color.b>visual.fill.color.r,"Slider theme is not blue");
            foreach(var visual in ui.GetComponentsInChildren<SettingsSliderVisual>(true))Check(visual.fill.transform.parent.GetComponent<Image>().color.b>visual.fill.transform.parent.GetComponent<Image>().color.r,"Slider track is not blue");
            foreach(var image in ui.GetComponentsInChildren<Image>(true).Where(image=>image.name=="Icon_PopupTitle"))Check(image.color==Color.white,"Settings gear artwork was tinted");
            foreach(var transform in prefab.GetComponentsInChildren<Transform>(true)) Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject)==0,"Missing Settings script: "+transform.name);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var canvas=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Canvas.prefab");
        try
        {
            var launcher=canvas.GetComponentInChildren<SettingsLauncher>(true); Check(launcher && launcher.settingsPrefab,"Header Settings prefab binding missing");
            var display=canvas.GetComponentInChildren<DashboardVisibilityUI>(true);
            Check(display && display.tasks && display.dailyRewards && display.focus && display.ranking && display.character && display.topRow && display.bottomRow && display.emptyState,"Dashboard visibility bindings missing");
            var button=launcher.GetComponent<Button>(); Check(button.onClick.GetPersistentEventCount()==1 && button.onClick.GetPersistentTarget(0)==launcher,"Settings gear listener missing/duplicated");
            foreach(var task in canvas.GetComponentsInChildren<TaskSystemUI>(true)) Check(task.tagTintSprite,"Task tag tint binding missing");
            foreach(var editor in canvas.GetComponentsInChildren<EditTaskDetailsUI>(true)) Check(editor.tagTintSprite,"Task editor tag tint binding missing");
        }
        finally { PrefabUtility.UnloadPrefabContents(canvas); }
    }
    static void VerifyDateGroups()
    {
        var now = new DateTime(2026,10,14); // Wednesday
        var dates = new[] { "2026-10-13", "2026-10-14", "2026-10-15", "2026-10-18", "2026-10-19", "2026-10-25", "2026-10-26", "2026-10-31", "2026-11-01", "2026-11-30", "2026-12-01" };
        var expected = new[] { TaskDateGroup.Due, TaskDateGroup.Today, TaskDateGroup.Tomorrow, TaskDateGroup.ThisWeek, TaskDateGroup.NextWeek, TaskDateGroup.NextWeek, TaskDateGroup.ThisMonth, TaskDateGroup.ThisMonth, TaskDateGroup.NextMonth, TaskDateGroup.NextMonth, TaskDateGroup.Later };
        for(int i=0;i<dates.Length;i++)
            Check(TaskDateGroups.Classify(new QuestTask { scheduled=true,date=dates[i] },now)==expected[i], "Date bucket boundary failed: " + dates[i]);
        Check(TaskDateGroups.Classify(new QuestTask(),now)==TaskDateGroup.NoDate,"Unscheduled task lost");
        Check(TaskDateGroups.Classify(new QuestTask { daily=true, scheduled=true,date="2027-01-01" },now)==TaskDateGroup.Daily,"Future daily template lost");
        Check(TaskDateGroups.Classify(new QuestTask { scheduled=true,date="2026-11-02" },new DateTime(2026,11,1))==TaskDateGroup.Tomorrow,"Tomorrow must win over next week");
        Check(TaskDateGroups.Classify(new QuestTask { scheduled=true,date="2027-01-01" },new DateTime(2026,12,31))==TaskDateGroup.Tomorrow,"Year rollover failed");
        Check(TaskDateGroups.Classify(new QuestTask { scheduled=true,date="2028-02-29" },new DateTime(2028,2,28))==TaskDateGroup.Tomorrow,"Leap day failed");
        Check(TaskDateGroups.Classify(new QuestTask { scheduled=true,date="2026-10-14" },now.AddDays(1))==TaskDateGroup.Due,"Midnight bucket did not advance");
    }

}
