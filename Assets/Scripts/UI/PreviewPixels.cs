using UnityEngine;
using UnityEngine.UI;

internal static class PreviewPixels
{
    // Keep rendering into the previous target during a resize/side-menu tween.
    // Projection still follows the live rect; allocate only once the pixels settle.
    internal struct ResizeGate
    {
        Vector2Int pending;
        float changedAt;
        public bool Ready(RenderTexture current,Vector2Int desired)
        {
            if(!current)return true;
            if(current.width==desired.x && current.height==desired.y){pending=desired;changedAt=Time.unscaledTime;return false;}
            if(!Application.isPlaying)return true;
            if(pending!=desired){pending=desired;changedAt=Time.unscaledTime;return false;}
            return Time.unscaledTime-changedAt>=.15f;
        }
    }
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
