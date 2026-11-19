using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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

    private Button confirmButton;
    private bool canConfirm;

    private void Awake()
    {
        confirmButton = usernamePanel.GetComponentInChildren<Button>(true);
        SetConfirmationAvailable(false);
    }

    private async void Start()
    {
        skipButton.SetActive(false);
        SetMessage("Opening your save...");
        try
        {
            var manager = GameDataManager.EnsureInstance();
            var ready = await manager.Ready;
            if (!this) return;
            if (!ready.Succeeded)
            {
                ShowOpenError(ready.Message);
                return;
            }

            if (manager.HasUsername())
            {
                SceneManager.LoadScene(existingPlayerScene);
                return;
            }

            SetMessage("");
            SetConfirmationAvailable(true);
            usernameInput.ActivateInputField();
        }
        catch (Exception error)
        {
            if (!this) return;
            Debug.LogException(error, this);
            ShowOpenError(null);
        }
    }

    public async void ConfirmUsername()
    {
        if (!canConfirm) return;
        string username = usernameInput.text.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            SetMessage("Please enter your name.");
            return;
        }

        if (username.Length > 20)
        {
            SetMessage("Name must be 20 characters or fewer.");
            return;
        }

        SetConfirmationAvailable(false);
        SetMessage("Saving your name...");
        var manager = GameDataManager.Instance;
        OperationResult<bool> saved;
        try
        {
            saved = await manager.SetUsernameAsync(username);
        }
        catch (Exception error)
        {
            if (!this) return;
            Debug.LogException(error, this);
            ShowWriteError(null);
            SetConfirmationAvailable(manager && manager.IsReady);
            return;
        }

        if (!this) return;
        if (!saved.Succeeded || !manager.IsReady)
        {
            ShowWriteError(saved.Message);
            SetConfirmationAvailable(manager.IsReady);
            return;
        }

        usernamePanel.SetActive(false);
        skipButton.SetActive(true);
        storyController.StartStory();
    }

    private void SetConfirmationAvailable(bool available)
    {
        canConfirm = available;
        usernameInput.interactable = available;
        if (confirmButton) confirmButton.interactable = available;
    }

    private void SetMessage(string message)
    {
        if (errorText) errorText.text = message;
    }

    private void ShowOpenError(string message)
    {
        SetMessage(AppPlatform.Browser
            ? "Cannot open save. Check storage or app version."
            : message ?? "Could not open your save. Restart Cat Quest to retry.");
    }

    private void ShowWriteError(string message)
    {
        SetMessage(message ?? (AppPlatform.Browser
            ? "Could not save. Check available browser storage."
            : "Could not save your name. Please try again."));
    }
}
