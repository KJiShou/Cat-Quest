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
    public IEnumerator Reveal(float delay, Action complete) => RevealText(body, delay, complete);
    public static IEnumerator RevealText(TMP_Text label, float delay, Action complete)
    {
        label.maxVisibleCharacters = 0; label.ForceMeshUpdate();
        int count = label.textInfo.characterCount; float elapsed = 0;
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
