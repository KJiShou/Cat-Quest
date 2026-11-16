using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class UsernameInputController : MonoBehaviour
{
    [Header("Username UI")]
    [SerializeField] private GameObject usernamePanel;
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_Text errorText;

    [Header("Story")]
    [SerializeField] private StoryController storyController;
    [SerializeField] private GameObject skipButton;

    [Header("Existing Player")]
    [SerializeField] private string existingPlayerScene = "CharacterHome";

    private async void Start()
    {
        var ready = await GameDataManager.EnsureInstance().Ready;
        if (!this || ShowSaveVersionError()) return;
        // Already has username -> skip intro completely
        if (GameDataManager.Instance.HasUsername())
        {
            SceneManager.LoadScene(existingPlayerScene);
            return;
        }

        // New player -> show username popup
        usernamePanel.SetActive(true);
        skipButton.SetActive(false);

        if (errorText != null)
        {
            errorText.text = "";
        }

        usernameInput.ActivateInputField();
    }

    public async void ConfirmUsername()
    {
        if (!usernameInput.interactable || ShowSaveVersionError()) return;
        string username = usernameInput.text.Trim();

        if (string.IsNullOrWhiteSpace(username))
        {
            if (errorText != null)
            {
                errorText.text = "Please enter your name.";
            }

            return;
        }

        if (username.Length > 20)
        {
            if (errorText != null)
            {
                errorText.text =
                    "Name must be 20 characters or fewer.";
            }

            return;
        }

        usernameInput.interactable = false;
        try { var saved = await GameDataManager.Instance.SetUsernameAsync(username); if (!saved.Succeeded) throw new System.IO.IOException(saved.Message); }
        catch (System.IO.IOException error) {
            if(!this)return;
            if (errorText) errorText.text = AppPlatform.Browser
                ? "Could not save. Check available browser storage."
                : error.Message;
            usernameInput.interactable = true; return;
        }
        if (!this) return;
        usernameInput.interactable = true;
        usernamePanel.SetActive(false);
        skipButton.SetActive(true);

        storyController.StartStory();
    }

    private bool ShowSaveVersionError()
    {
        if (!GameDataManager.Instance.IsSaveReadOnly) return false;
        usernamePanel.SetActive(true);
        skipButton.SetActive(false);
        usernameInput.interactable = false;
        if (errorText) errorText.text = AppPlatform.Browser
            ? "Cannot open save. Check storage or app version."
            : GameDataManager.Instance.LoadNotice ?? "Wait for Cat Quest to finish opening your save.";
        return true;
    }
}
