using UnityEngine;
using UnityEngine.UI;

internal static class PreviewPixels
{
    // The RawImage rect is in canvas units; a scaled canvas can display many fewer pixels.
    public static Vector2Int For(RawImage view, int maximumHeight)
    {
        Vector2 rect = view.rectTransform.rect.size;
        float scale = view.canvas ? view.canvas.scaleFactor : 1f;
        scale = Mathf.Min(scale, maximumHeight / Mathf.Max(1f, rect.y));
        scale = Mathf.Min(scale, 2048f / Mathf.Max(1f, rect.x));
        return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(rect.x * scale)), Mathf.Max(1, Mathf.RoundToInt(rect.y * scale)));
    }
}
