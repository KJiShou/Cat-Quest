using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class DashboardTaskRow : MonoBehaviour
{
    public TMP_Text taskTitle;
    public TMP_Text difficultyLabel;
    public Image difficultyBackground;
    public TMP_Text fishAmount;
    public TMP_Text EXP_Amount;
    public GameObject tick;
    public Button completeButton;

    public void Bind(
            TaskView view,
            Sprite[] difficultySprites,
            Action<TaskView> clicked,
            Action<TaskView> completeRequested
        )
    {
        taskTitle.text = view.task.title;
        taskTitle.textWrappingMode = TextWrappingModes.NoWrap;
        taskTitle.overflowMode = TextOverflowModes.Ellipsis;

        difficultyLabel.text = view.task.difficulty.ToString();
        difficultyBackground.sprite =
            difficultySprites[(int)view.task.difficulty];

        difficultyLabel.color = view.task.difficulty == TaskDifficulty.Hard
            ? new Color32(255, 235, 96, 255)
            : view.task.difficulty == TaskDifficulty.Easy
                ? new Color32(0, 166, 85, 255)
                : new Color32(0, 137, 245, 255);

        fishAmount.text = TaskService.Reward(view.task.difficulty).ToString();
        EXP_Amount.text = TaskService.Reward(view.task.difficulty).ToString();
        tick.SetActive(view.Completed);

        var button = GetComponent<Button>();
        button.targetGraphic = GetComponent<Graphic>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => clicked(view));

        completeButton.interactable = !view.Completed;

        completeButton.onClick.RemoveAllListeners();
        completeButton.onClick.AddListener(
            () => completeRequested(view));
    }
}