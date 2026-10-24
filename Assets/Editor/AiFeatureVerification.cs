using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class AiFeatureVerification
{
    static AiFeatureVerification() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-ai-data.request")) return;
        File.Delete("Temp/verify-ai-data.request");
        string folder = "Library/AiVerification/Data-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"); Directory.CreateDirectory(folder);
        string result;
        try {
            AiChatVerification.Run(); AiProtocolVerification.Run(); VerifyBindings();
            RewardSystemVerification.Run(); SettingsAccountVerification.Run(); SettingsVerification.Run(folder);
            result = "PASS: isolated chat, task/reward/CharacterHome, account, settings, protocol and prefab bindings.";
        } catch(Exception error) { result = "FAIL: " + error; }
        File.WriteAllText(folder + "/result.txt", DateTime.UtcNow.ToString("O") + "\n" + result);
        File.WriteAllText("Temp/verify-ai-data.result",folder + "/result.txt\n" + result);
    }
    public static void VerifyBindings()
    {
        var canvas = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Canvas.prefab");
        var chat = canvas.GetComponentInChildren<AiChatPage>(true);
        if (!chat || !chat.editor || !chat.messageTemplate || !chat.messageTemplate.avatar || !chat.messageTemplate.userAvatar || !chat.thinking || !chat.draftTemplate || !chat.input) throw new Exception("Missing chat bindings.");
        var nav = canvas.GetComponentInChildren<MainMenuNavigation>(true);
        if(nav.pages.Length != 7 || nav.buttons.Length != 7 || nav.pages[6] != chat.gameObject) throw new Exception("Chat navigation is not bound.");
        var settings = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Settings/Settings.prefab");
        if (!settings.GetComponentInChildren<AiSettingsUI>(true)) throw new Exception("AI settings removed.");
        var language = settings.GetComponentInChildren<AiSettingsUI>(true).language;
        if(!language || !language.itemText || !language.template.GetComponentInChildren<UnityEngine.UI.Toggle>(true))throw new Exception("Language selector bindings missing.");
        var model = settings.GetComponentInChildren<AiSettingsUI>(true).model;
        var item = model.template.GetComponentInChildren<UnityEngine.UI.Toggle>(true);
        if (!item || !model.itemText || !model.itemText.transform.IsChildOf(item.transform))
            throw new Exception("Model dropdown template/label invalid.");
        foreach(var root in new[]{canvas,settings}) foreach(var t in root.GetComponentsInChildren<Transform>(true))
            if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0) throw new Exception("Missing script: "+t.name);
    }
}
