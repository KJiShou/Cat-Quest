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

    private void Start()
    {
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

    public void ConfirmUsername()
    {
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

        GameDataManager.Instance.SetUsername(username);

        usernamePanel.SetActive(false);
        skipButton.SetActive(true);

        storyController.StartStory();
    }
}