using System;
using System.Collections;
using TMPro;
using UnityEngine;

// Shared presentation only; callers own input, sequencing and character animation.
public sealed class StoryDialogueView : MonoBehaviour
{
    public TMP_Text speaker, body;
    public GameObject continueIcon;
    public void Show(string name, string text)
    {
        speaker.text = name; body.text = text; body.maxVisibleCharacters = 0;
        if (continueIcon) continueIcon.SetActive(false);
    }
    public IEnumerator Reveal(float delay, Action complete, float voiceDuration = 0) => RevealText(body, delay, complete, voiceDuration);
    public static IEnumerator RevealText(TMP_Text label, float delay, Action complete, float voiceDuration = 0)
    {
        label.maxVisibleCharacters = 0; label.ForceMeshUpdate();
        int count = label.textInfo.characterCount; float elapsed = 0;
        // Keep the approved voice pitch; allow the text/character to finish with the line.
        if (count > 0) delay = Mathf.Max(delay, voiceDuration / count);
        while (label.maxVisibleCharacters < count)
        {
            elapsed += Time.unscaledDeltaTime;
            label.maxVisibleCharacters = delay <= 0 ? count : Mathf.Min(count, Mathf.FloorToInt(elapsed / delay));
            yield return null;
        }
        complete?.Invoke();
    }
    public void Complete() { body.maxVisibleCharacters = int.MaxValue; if (continueIcon) continueIcon.SetActive(true); }
    public void Clear() { body.text = ""; if (continueIcon) continueIcon.SetActive(false); }
}
