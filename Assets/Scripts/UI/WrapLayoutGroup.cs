using UnityEngine;
using UnityEngine.UI;

// A Unity auto-layout group for tags whose preferred widths vary with their text.
[AddComponentMenu("Layout/Wrap Layout Group")]
public class WrapLayoutGroup : LayoutGroup
{
    public Vector2 spacing = new Vector2(8, 6);

    public override void CalculateLayoutInputHorizontal()
    {
        base.CalculateLayoutInputHorizontal();
        float minimum = 0, preferred = padding.horizontal;
        foreach (var child in rectChildren)
        {
            float width = LayoutUtility.GetPreferredWidth(child);
            minimum = Mathf.Max(minimum, width);
            preferred += width + spacing.x;
        }
        SetLayoutInputForAxis(minimum + padding.horizontal, preferred, -1, 0);
    }

    public override void CalculateLayoutInputVertical()
    {
        float height = Arrange(false);
        SetLayoutInputForAxis(height, height, -1, 1);
    }

    public override void SetLayoutHorizontal() => Arrange(true);
    public override void SetLayoutVertical() => Arrange(true);

    private float Arrange(bool position)
    {
        float available = Mathf.Max(1, rectTransform.rect.width - padding.horizontal);
        float x = 0, y = padding.top, rowHeight = 0;
        foreach (var child in rectChildren)
        {
            float width = Mathf.Min(available, LayoutUtility.GetPreferredWidth(child));
            float height = LayoutUtility.GetPreferredHeight(child);
            if (x > 0 && x + width > available)
            {
                x = 0;
                y += rowHeight + spacing.y;
                rowHeight = 0;
            }
            if (position)
            {
                SetChildAlongAxis(child, 0, padding.left + x, width);
                SetChildAlongAxis(child, 1, y, height);
            }
            x += width + spacing.x;
            rowHeight = Mathf.Max(rowHeight, height);
        }
        return y + rowHeight + padding.bottom;
    }
}
