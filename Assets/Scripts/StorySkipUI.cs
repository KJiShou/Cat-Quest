using UnityEngine;
using UnityEngine.UI;

public class StorySkipUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button skipButton;
    [SerializeField] private GameObject confirmationPanel;
    [SerializeField] private Button laterButton;
    [SerializeField] private Button yesButton;

    [Header("Story")]
    [SerializeField] private StoryController storyController;

    private float previousTimeScale = 1f;

    private void Start()
    {
        confirmationPanel.SetActive(false);

        skipButton.onClick.AddListener(OpenConfirmation);
        laterButton.onClick.AddListener(CancelSkip);
        yesButton.onClick.AddListener(ConfirmSkip);
    }

    private void OpenConfirmation()
    {
        PlayClick(skipButton);
        SettingsAudio.Instance?.StopDialogue(storyController);
        confirmationPanel.SetActive(true);

        // Pause the story while confirmation is open
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
    }

    private void CancelSkip()
    {
        PlayClick(laterButton);
        confirmationPanel.SetActive(false);

        // Continue story
        Time.timeScale = previousTimeScale;
    }

    private void ConfirmSkip()
    {
        PlayClick(yesButton);
        confirmationPanel.SetActive(false);

        // Restore time before ending story
        Time.timeScale = previousTimeScale;

        if (storyController != null)
        {
            storyController.SkipStory();
        }
    }

    private void OnDestroy()
    {
        skipButton.onClick.RemoveListener(OpenConfirmation);
        laterButton.onClick.RemoveListener(CancelSkip);
        yesButton.onClick.RemoveListener(ConfirmSkip);

        // Safety
        if (Time.timeScale == 0f)
        {
            Time.timeScale = previousTimeScale;
        }
    }

    private static void PlayClick(Button button)
    {
        // Project feedback buttons already own their click sound.
        if (button && !button.GetComponent<JuicyButton>())
            SettingsAudio.EnsureInstance()?.PlayButtonClick();
    }
}
