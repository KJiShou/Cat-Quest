using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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
    static void ClickSkipControl(StorySkipUI owner, string field)
    {
        var button = (Button)typeof(StorySkipUI).GetField(field, Private).GetValue(owner);
        Check(button && button.IsActive() && button.IsInteractable(), field + " is available: " + (button ? button.name + " active=" + button.gameObject.activeInHierarchy + " enabled=" + button.enabled + " interactable=" + button.IsInteractable() : "missing binding"));
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)button.transform;
        var canvas = button.GetComponentInParent<Canvas>();
        var pointer = new PointerEventData(EventSystem.current) {
            position = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, rect.TransformPoint(rect.rect.center))
        };
        var hits = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) == button.gameObject,
            field + " raycast; first hit: " + (hits.Count > 0 ? hits[0].gameObject.name : "none"));
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
    }
    static IEnumerator Run()
    {
        Check(GameDataManager.Instance.SavePath.Contains("DialogueAudioVerification"), "Isolated save path");
        var service = GameDataManager.Instance.Tasks;
        var defaults = service.Settings;
        var initialAudio = SettingsAudio.Instance;
        Check(defaults.soundVolume == 1f && defaults.musicVolume == 1f
            && initialAudio.soundSource.volume == 1f && initialAudio.musicSource.volume == 1f,
            "New user music and SFX start at 100 percent");
        var muted = service.Settings; muted.soundVolume = 0; muted.musicVolume = 0;
        Check(service.SaveSettings(muted), "Save explicit mute in isolated store");
        var reloaded = new PlayerDataStore(GameDataManager.Instance.SavePath);
        Check(reloaded.Data.settings.soundVolume == 0 && reloaded.Data.settings.musicVolume == 0,
            "Existing saved music/SFX mute survives reload");
        Check(service.SaveSettings(defaults), "Restore isolated default volumes");
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
        ClickSkipControl(skipUI, "skipButton"); yield return .2;
        Check(!Voice(audio).clip && Time.timeScale == 0, "Skip confirmation stops voice and preserves pause behavior");
        ClickSkipControl(skipUI, "laterButton");
        Check(Time.timeScale == timeScale, "Cancel skip restores time scale");
        ClickSkipControl(skipUI, "skipButton"); yield return .2;
        ClickSkipControl(skipUI, "yesButton");
        Check(!Voice(audio).clip && Time.timeScale == timeScale, "Skip stops Story voice immediately and restores time"); yield return 4;
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
        tutorial.SendMessage("OnApplicationFocus", true);
        tutorial.OpenTopic(5);
        for(int step = 0; step < 9; step++) {
            yield return .2;
            Check(tutorial.CurrentTopic == "help-settings-" + step, "Settings guide step " + step);
            var line = DialogueAudioLibrary.Find(tutorial.dialogue.body.text, false);
            Check(line != null && Voice(audio).clip == Resources.Load<AudioClip>(line.clip)
                && Voice(audio).isPlaying && Voice(audio).pitch == 1f,
                "Settings step " + step + " plays its approved mentor clip");
            tutorial.CompleteTyping();
            Check(!Voice(audio).clip && !tutorial.IsTyping, "Settings typing skip stops voice at step " + step);
            tutorial.Continue();
        }
        Check(!tutorial.panel.activeSelf && tutorial.settings.gameObject.activeSelf && !Voice(audio).clip,
            "Finishing Settings guide returns to Settings without lingering voice");
        tutorial.OpenTopic(5); yield return .2;
        Check(Voice(audio).isPlaying, "Reopening Settings guide restarts voice");
        tutorial.CompleteTyping();tutorial.Continue();yield return .2;tutorial.GoBack();yield return .2;
        Check(tutorial.CurrentTopic=="help-settings-0" && !tutorial.back.gameObject.activeSelf && Voice(audio).isPlaying,"Settings Back restores the preceding spoken step and hides at first step");
        tutorial.Skip(); Check(!Voice(audio).clip, "Skipping Settings guide stops voice");
        GameDataManager.Instance.PlayerData.equipmentUnlocked=true;GameDataManager.Instance.SaveData();
        tutorial.OpenHelp();tutorial.OpenTopic(2);yield return .2;tutorial.CompleteTyping();tutorial.Continue();yield return .2;
        if(tutorial.CurrentTopic=="equipment-open") {
            Check(Voice(audio).isPlaying,"Equipment navigation explanation has a voice");
            tutorial.CompleteTyping();tutorial.Continue();yield return .2;
        }
        for(int step=0;step<7;step++) {
            Check(tutorial.CurrentTopic=="equipment-step-"+step,"Equipment audio step "+step);
            var line=DialogueAudioLibrary.Find(tutorial.dialogue.body.text,false);
            Check(line!=null && Voice(audio).clip==Resources.Load<AudioClip>(line.clip) && Voice(audio).isPlaying && Voice(audio).pitch==1f,"Equipment step "+step+" plays approved mentor voice");
            if(step==1) {
                tutorial.GoBack();yield return .2;
                var previous=DialogueAudioLibrary.Find(tutorial.dialogue.body.text,false);
                Check(tutorial.CurrentTopic=="equipment-step-0" && previous!=null && Voice(audio).clip==Resources.Load<AudioClip>(previous.clip),"Back replaces the current voice with the previous line");
                tutorial.CompleteTyping();tutorial.Continue();yield return .2;
            }
            tutorial.CompleteTyping();Check(!Voice(audio).clip,"Equipment typing skip stops voice");
            tutorial.Continue();yield return .2;
        }
        Check(!tutorial.panel.activeSelf && !Voice(audio).clip,"Equipment completion releases dialogue voice");
        Check(audio.PlayDialogue(tutorial, "Uncatalogued future text", false) == 0 && !Voice(audio).clip, "Unknown text remains silent and clears previous voice");
    }
}
