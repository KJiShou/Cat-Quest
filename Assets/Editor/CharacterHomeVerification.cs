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
            Require(navigation.buttons.Length == 6 && navigation.pages.Length == 6, "Missing navigation pages.");
            Require(navigation.initialPage == MainMenuPage.Dashboard, "Character dashboard is not the initial page.");
            Require(navigation.taskSystem && navigation.fishCoins && navigation.tickets, "Missing UI bindings.");
            Require(roots.SelectMany(r => r.GetComponentsInChildren<EventSystem>(true)).Count() == 1, "Expected one EventSystem.");
            Require(roots.SelectMany(r => r.GetComponentsInChildren<BaseInputModule>(true)).Count() == 1, "Missing input module.");
            Require(roots.SelectMany(r => r.GetComponentsInChildren<GraphicRaycaster>(true)).Any(), "Missing UI raycaster.");
            var preview = roots.SelectMany(r => r.GetComponentsInChildren<CharacterHomePreview>(true)).Single();
            Require(preview.previewCamera && preview.character, "Missing character preview bindings.");
            Require(!preview.GetComponent<RawImage>().raycastTarget, "Preview blocks UI input.");
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
