using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class AiFeatureVerification
{
    static AiFeatureVerification() { EditorApplication.update += Poll; }
    static async void Poll()
    {
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode && File.Exists("Temp/verify-ai-bindings.request")) {
            File.Delete("Temp/verify-ai-bindings.request");
            try { VerifyBindings(); File.WriteAllText("Temp/verify-ai-bindings.result", DateTime.UtcNow.ToString("O") + " PASS"); }
            catch (Exception error) { File.WriteAllText("Temp/verify-ai-bindings.result", DateTime.UtcNow.ToString("O") + " FAIL " + error); }
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-ai-data.request")) return;
        File.Delete("Temp/verify-ai-data.request");
        string folder = "Library/AiVerification/Data-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"); Directory.CreateDirectory(folder);
        string result;
        try {
            await AiChatVerification.Run(); AiProtocolVerification.Run(); VerifyBindings();
            RewardSystemVerification.Run(); await SettingsAccountVerification.Run(); SettingsVerification.Run(folder);
            result = "PASS: isolated chat, task/reward/CharacterHome, account, settings, protocol and prefab bindings.";
        } catch(Exception error) { result = "FAIL: " + error; }
        File.WriteAllText(folder + "/result.txt", DateTime.UtcNow.ToString("O") + "\n" + result);
        File.WriteAllText("Temp/verify-ai-data.result",folder + "/result.txt\n" + result);
    }
    public static void VerifyBindings()
    {
        var canvas = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Canvas.prefab");
        var chat = canvas.GetComponentInChildren<AiChatPage>(true);
        if(!chat)throw new Exception("Missing chat binding: Canvas/AiChatPage component.");
        var missing=new System.Collections.Generic.List<string>();
        if(!chat.editor)missing.Add("editor");
        if(!chat.messageTemplate)missing.Add("messageTemplate");
        else {
            if(!chat.messageTemplate.avatar)missing.Add("messageTemplate.avatar");
            if(!chat.messageTemplate.userAvatar)missing.Add("messageTemplate.userAvatar");
        }
        if(!chat.thinking)missing.Add("thinking");
        if(!chat.draftTemplate) {
            var draftPath=AssetDatabase.GUIDToAssetPath("fb1014f266dc058428ebb9d6a7f9910c");
            var draftRoot=AssetDatabase.LoadAssetAtPath<GameObject>(draftPath);
            var draft=draftRoot?draftRoot.GetComponent<AiChatDraftCard>():null;
            var script=AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Scripts/AI/AiChatDraftCard.cs");
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AI/Chat.prefab");
            var sourceChat=source?source.GetComponent<AiChatPage>():null;
            string identity="unavailable";
            if(draft && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(draft,out string guid,out long local))identity=guid+"/"+local;
            missing.Add("draftTemplate [path="+draftPath+", root="+(bool)draftRoot+", component="+(bool)draft+", scriptClass="+(script?script.GetClass()?.FullName:"missing")+", sourceBound="+(sourceChat&&(bool)sourceChat.draftTemplate)+", identity="+identity+", scriptGuidPath="+AssetDatabase.GUIDToAssetPath("1b96d560e28b3cb4e9b90b1eca517c10")+", dependency="+AssetDatabase.GetDependencies("Assets/Prefabs/AI/Chat.prefab",false).Contains(draftPath)+"]");
        }
        if(!chat.input)missing.Add("input");
        if(missing.Count>0)throw new Exception("Missing chat bindings: "+string.Join(", ",missing)+". Prefab: "+AssetDatabase.GetAssetPath(chat));
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
