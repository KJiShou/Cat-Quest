using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;

// Bounded verification only: no asset generation or authored-scene saves.
[InitializeOnLoad]
public static class DialogueAudioVerification
{
    const string Flag = "CatQuest.DialogueAudioVerification";
    const string Request = "Temp/verify-dialogue-audio.request";
    static IEnumerator flow;
    static double next;
    static string Folder => SessionState.GetString(Flag + ".folder", "");
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static DialogueAudioVerification()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state => {
            if (!SessionState.GetBool(Flag, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { flow = Run(); next = EditorApplication.timeSinceStartup + 3; }
            if (state == PlayModeStateChange.EnteredEditMode) Restore();
        };
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (SessionState.GetBool(Flag, false))
        {
            if (DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Flag + ".deadline", "0"))) { Finish("FAIL: timeout"); return; }
            if (!EditorApplication.isPlaying || flow == null || EditorApplication.timeSinceStartup < next) return;
            try { if (!flow.MoveNext()) { Finish("PASS"); return; } next = EditorApplication.timeSinceStartup + Convert.ToDouble(flow.Current); }
            catch (Exception e) { Finish("FAIL: " + e); }
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        File.Delete(Request);
        string folder = "Library/DialogueAudioVerification/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(folder); SessionState.SetString(Flag + ".folder", folder);
        var store = Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        SessionState.SetBool(Flag + ".hadStore", store != null); SessionState.SetString(Flag + ".store", store ?? "");
        SessionState.SetString(Flag + ".scene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Flag + ".paused", EditorApplication.isPaused);
        SessionState.SetString(Flag + ".deadline", DateTime.UtcNow.AddSeconds(120).Ticks.ToString());
        SessionState.SetBool(Flag, true);
        File.WriteAllText(folder + "/result.txt", DateTime.UtcNow.ToString("O") + " RUNNING");
        try {
            Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", Path.GetFullPath(folder + "/save.json"));
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/IntroStory.unity");
            EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
        } catch (Exception e) { Finish("FAIL: " + e); }
    }
    static void Restore()
    {
        flow = null;
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", SessionState.GetBool(Flag + ".hadStore", false) ? SessionState.GetString(Flag + ".store", "") : null);
        string scene = SessionState.GetString(Flag + ".scene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(scene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
        EditorApplication.isPaused = SessionState.GetBool(Flag + ".paused", false);
        SessionState.SetBool(Flag, false);
        File.WriteAllText(Folder + "/restored.txt", DateTime.UtcNow.ToString("O") + " Restored isolated save environment, Play Mode start scene and pause state. Game View unchanged.");
    }
    static void Finish(string result)
    {
        File.WriteAllText(Folder + "/result.txt", DateTime.UtcNow.ToString("O") + " " + result);
        flow = null;
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false; else Restore();
    }
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        File.AppendAllText(Folder + "/checks.txt", "PASS: " + message + "\n");
    }
    static AudioSource Voice(SettingsAudio audio) => (AudioSource)typeof(SettingsAudio).GetField("dialogueSource", Private).GetValue(audio);
    static void Invoke(object instance, string method) => instance.GetType().GetMethod(method, Private).Invoke(instance, null);
    static IEnumerator Run()
    {
        Check(GameDataManager.Instance.SavePath.Contains("DialogueAudioVerification"), "Isolated save path");
        var story = UnityEngine.Object.FindFirstObjectByType<StoryController>();
        Check(story, "Story scene loaded");
        var serialized = new SerializedObject(story);
        var steps = serialized.FindProperty("storySteps");
        int dialogueCount = 0;
        for (int i = 0; i < steps.arraySize; i++)
        {
            var step = steps.GetArrayElementAtIndex(i);
            if (step.FindPropertyRelative("type").enumValueIndex != 1) continue;
            var character = (StoryCharacter)step.FindPropertyRelative("character").objectReferenceValue;
            var line = DialogueAudioLibrary.Find(step.FindPropertyRelative("dialogueText").stringValue, character.DisplayName == "Player");
            Check(line != null && Resources.Load<AudioClip>(line.clip), "Story dialogue asset " + i);
            dialogueCount++;
        }
        Check(dialogueCount > 0, "All actual serialized Story dialogue steps covered");
        Check(DialogueAudioLibrary.Find("Well done. This task earned 7 EXP and 3 Fish Coins. EXP grows your cat. Fish Coins can upgrade equipment.", false) != null, "Dynamic reward amounts match template");
        Check(DialogueAudioLibrary.Find("Disk full Your tasks are safe. Try again.", false) != null, "Dynamic save error matches template");
        var input = UnityEngine.Object.FindFirstObjectByType<UsernameInputController>();
        ((TMP_InputField)typeof(UsernameInputController).GetField("usernameInput", Private).GetValue(input)).text = "Audio Apprentice";
        input.ConfirmUsername(); yield return .15;
        var audio = SettingsAudio.Instance;
        Check(Voice(audio) && Voice(audio).isPlaying && Voice(audio).clip && Voice(audio).clip.name.StartsWith("player-"), "Story starts approved B voice");
        Check(audio.musicSource.clip == audio.musicClip && audio.musicSource.loop, "Story reuses menu BGM loop");
        yield return Voice(audio).clip.length + .3;
        Check(!Voice(audio).clip, "Natural Story completion stops and releases voice");
        Invoke(story, "HandleAdvanceInput"); yield return .1;
        Check(Voice(audio).isPlaying, "Next line starts after natural completion");
        Invoke(story, "HandleAdvanceInput");
        Check(!Voice(audio).clip, "Complete typing stops voice");
        int guard = 0;
        while (guard++ < 160)
        {
            var current = (StoryStep)typeof(StoryController).GetField("currentStep", Private).GetValue(story);
            if (current?.character?.DisplayName == "Master Loke" && Voice(audio).clip) break;
            Invoke(story, "HandleAdvanceInput"); yield return .03;
        }
        Check(guard < 160 && Voice(audio).clip.name.StartsWith("mentor-"), "Story reaches approved D mentor voice");
        var skipUI = UnityEngine.Object.FindFirstObjectByType<StorySkipUI>(FindObjectsInactive.Include);
        float timeScale = Time.timeScale;
        Invoke(skipUI, "OpenConfirmation");
        Check(!Voice(audio).clip && Time.timeScale == 0, "Skip confirmation stops voice and preserves pause behavior");
        Invoke(skipUI, "CancelSkip");
        Check(Time.timeScale == timeScale, "Cancel skip restores time scale");
        story.SkipStory(); Check(!Voice(audio).clip, "Skip stops Story voice immediately"); yield return 4;
        var tutorial = UnityEngine.Object.FindFirstObjectByType<TutorialController>();
        Check(tutorial && tutorial.IsTyping && Voice(audio).clip && Voice(audio).clip.name.StartsWith("mentor-"), "Tutorial welcome plays approved D voice");
        var key = tutorial.CurrentTopic; tutorial.CompleteTyping();
        Check(!Voice(audio).clip && !tutorial.IsTyping && tutorial.CurrentTopic == key, "Tutorial typing skip stops without advancing");
        tutorial.OpenTopic(0); yield return .5;
        Check(Voice(audio).clip, "Reopened tutorial plays voice again");
        audio.StopDialogue(story); Check(Voice(audio).clip, "Stale Story owner cannot stop Tutorial voice");
        audio.Preview(new AppSettings { soundVolume = 0, musicVolume = .2f });
        Check(Voice(audio).volume == 0 && Mathf.Approximately(audio.musicSource.volume, .2f), "Sound mute and Music volumes stay independent");
        audio.Preview(new AppSettings { soundVolume = .4f, musicVolume = .2f });
        Check(Mathf.Approximately(Voice(audio).volume, .4f), "Live Sound volume applies to voice");
        typeof(SettingsAudio).GetMethod("OnApplicationFocus", Private).Invoke(audio, new object[] { false });
        Check(!Voice(audio).clip, "Focus loss stops voice");
        tutorial.CompleteTyping(); tutorial.OpenTopic(0); yield return .5;
        tutorial.enabled = false; Check(!Voice(audio).clip, "Disabling tutorial stops voice");
        tutorial.enabled = true; tutorial.OpenTopic(0); yield return .5;
        Check(Voice(audio).clip, "Disable/re-enable recovers voice");
        UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>().ShowPage(MainMenuPage.Tasks);
        Check(!Voice(audio).clip, "Page change immediately stops current voice");
        yield return .5;
        tutorial.Skip(); Check(!Voice(audio).clip, "Skip tutorial stops voice");
        Check(audio.PlayDialogue(tutorial, "Uncatalogued future text", false) == 0 && !Voice(audio).clip, "Unknown text remains silent and clears previous voice");
    }
}
