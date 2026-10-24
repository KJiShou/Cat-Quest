using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class CharacterHomeVerification
{
    static CharacterHomeVerification() { EditorApplication.delayCall += AutoRun; }
    private static void AutoRun()
    {
        if (!File.Exists("Temp/verify-character-home.request")) return;
        File.Delete("Temp/verify-character-home.request");
        Run();
    }

    [MenuItem("Tools/Cat Quest/Verify Character Home")]
    public static void Run()
    {
        Scene scene;
        try { scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/CharacterHome.unity"); }
        catch (Exception ex)
        {
            File.WriteAllText("Temp/character-home-verification.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            throw;
        }
        try { RunForScene(scene); }
        finally { if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene); }
    }

    internal static void RunForScene(Scene scene)
    {
        try
        {
            var roots = scene.GetRootGameObjects();
            var navigation = roots.SelectMany(r => r.GetComponentsInChildren<MainMenuNavigation>(true)).Single();
            Require(navigation.buttons.Length == 7 && navigation.pages.Length == 7 && navigation.pages[(int)MainMenuPage.Chat].GetComponent<AiChatPage>(), "Missing navigation pages.");
            Require(navigation.initialPage == MainMenuPage.Dashboard, "Character dashboard is not the initial page.");
            Require(navigation.taskSystem && navigation.fishCoins && navigation.tickets, "Missing UI bindings.");
            Require(roots.SelectMany(r => r.GetComponentsInChildren<EventSystem>(true)).Count() == 1, "Expected one EventSystem.");
            Require(roots.SelectMany(r => r.GetComponentsInChildren<BaseInputModule>(true)).Count() == 1, "Missing input module.");
            Require(roots.SelectMany(r => r.GetComponentsInChildren<GraphicRaycaster>(true)).Any(), "Missing UI raycaster.");
            var preview = navigation.pages[(int)MainMenuPage.Dashboard].GetComponentInChildren<CharacterHomePreview>(true);
            Require(preview.previewCamera && preview.character, "Missing character preview bindings.");
            Require(!preview.GetComponent<RawImage>().raycastTarget, "Preview blocks UI input.");
            var home = navigation.pages[(int)MainMenuPage.CatHome];
            var settings = roots.SelectMany(r => r.GetComponentsInChildren<SettingsPanelUI>(true)).Single();
            var launcher = roots.SelectMany(r => r.GetComponentsInChildren<SettingsLauncher>(true)).Single();
            Require(new SerializedObject(launcher).FindProperty("instance").objectReferenceValue == settings, "Settings launcher must reference its authored scene instance.");
            Require(home.GetComponentsInChildren<EquipmentInventoryUI>(true).Length == 1, "Cat Home inventory must exist before Play Mode.");
            Require(home.GetComponentsInChildren<EquipmentStatsUI>(true).Length == 1, "Cat Home stats must exist before Play Mode.");
            foreach (var page in roots.SelectMany(r => r.GetComponentsInChildren<EquipmentPageUI>(true)))
            {
                Require(page.GetComponentsInChildren<EquipmentItemDetailUI>(true).Length == 1, "Equipment details must exist before Play Mode.");
                Require(page.transform.Find("ItemModal") && page.transform.Find("ConfirmEquipment"), "Equipment dialogs must be authored objects.");
                foreach (var child in page.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0, "Missing UI script: " + child.name);
            }
            var catPreview = home.GetComponentInChildren<CharacterHomePreview>(true);
            Require(catPreview && catPreview.previewCamera == preview.previewCamera && catPreview.character == preview.character, "Cat Home must use the authored scene preview.");
            Require(preview.GetComponent<RectTransform>().anchoredPosition == Vector2.zero, "Preview is not centered.");
            for (int i = 0; i < 6; i++)
            {
                Require(navigation.buttons[i] && navigation.buttons[i].interactable, "Disabled navigation button.");
                Require(navigation.ShowPage((MainMenuPage)i), "Page navigation failed.");
                for (int j = 0; j < 6; j++) Require(navigation.pages[j].activeSelf == (i == j), "Page visibility mismatch.");
            }
            File.WriteAllText("Temp/character-home-verification.txt", "PASS: Unity loaded CharacterHome; all six page switches, UI bindings, EventSystem, input module, raycaster and centered character preview passed.");
            Debug.Log("Character Home verification passed.");
        }
        catch (Exception ex)
        {
            File.WriteAllText("Temp/character-home-verification.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            throw;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
