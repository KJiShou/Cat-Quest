using System.Linq;
using UnityEngine;

public sealed class FocusExitGuard : MonoBehaviour
{
    bool exitPending;
    FocusCancelConfirmation dialog;
    FocusController dialogFocus;

    void OnEnable() => Application.wantsToQuit += WantsToQuit;

    void OnDisable()
    {
        Application.wantsToQuit -= WantsToQuit;
        if (dialog) dialog.Dismiss();
    }

    bool WantsToQuit()
    {
        var focus = GetComponent<FocusRunner>().Controller;
        if (focus.Phase != FocusPhase.Focusing && focus.Phase != FocusPhase.Saving) return true;
        if (dialog) return false;

        var panel = FindObjectsByType<FocusPanelUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.cancelConfirmation);
        var canvas = panel ? panel.GetComponentInParent<Canvas>(true) : null;
        if (!canvas) return false;

        // The Focus page can be hidden while its session runs. Mount the prompt on the main canvas.
        var template = panel.cancelConfirmation;
        if (template.IsOpen) template.Dismiss();
        var prompt = Instantiate(template, canvas.rootCanvas.transform, false);
        prompt.name = "ExitFocusConfirmation";
        dialog = prompt;
        dialogFocus = focus;
        focus.Changed += RefreshDialog;
        prompt.ShowExit(() => ConfirmExit(prompt, focus), () => CloseDialog(prompt));
        RefreshDialog();
        return false;
    }

    async void ConfirmExit(FocusCancelConfirmation prompt, FocusController focus)
    {
        if (exitPending || focus.OperationPending) return;
        exitPending = true;
        RefreshDialog();
        bool saved;
        try
        {
            saved = focus.Phase == FocusPhase.Saving
                ? await focus.RetrySettlementAsync()
                : focus.Phase != FocusPhase.Focusing || await focus.CancelAsync(true);
        }
        finally
        {
            exitPending = false;
            RefreshDialog();
        }
        if (!saved || !this || !isActiveAndEnabled || dialog != prompt || !prompt) return;
        prompt.Dismiss();
        // GameDataManager still drains and closes storage before the application exits.
        Application.Quit();
    }

    void RefreshDialog()
    {
        if (!dialog) return;
        bool focusing = dialogFocus.Phase == FocusPhase.Focusing;
        bool saving = dialogFocus.Phase == FocusPhase.Saving;
        bool busy = exitPending || dialogFocus.OperationPending;
        string message = busy ? "Saving the session result before closing Cat Quest."
            : saving ? "Your Focus result could not be saved. Retry saving before exiting."
            : focusing ? "This Focus session will be cancelled.\nNo rewards will be earned."
            : "Your Focus session has ended. You can now close Cat Quest.";
        string action = busy ? "Saving..." : saving ? "Retry and exit" : focusing ? "Cancel and exit" : "Exit";
        dialog.SetExitMessage(message, focusing ? "Keep focusing" : "Stay", action, busy);
    }

    void CloseDialog(FocusCancelConfirmation prompt)
    {
        dialogFocus.Changed -= RefreshDialog;
        dialogFocus = null;
        dialog = null;
        Destroy(prompt.gameObject);
    }
}
