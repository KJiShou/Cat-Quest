using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shared saved palette and chip styling. IDs, never names or colors, own associations.
public static class TagColors
{
    public const string DefaultHex = "#CDFFCC";
    public static readonly string[] Palette =
    {
        DefaultHex, "#3D5FC3", "#27DC95", "#2A3957", "#FF9E3A", "#FF6B6D"
    };

    public static bool TryNormalize(string value, out string normalized)
    {
        normalized = null;
        value = value?.Trim();
        if (value == null || value.Length != 7 || value[0] != '#') return false;
        for (int i = 1; i < value.Length; i++)
            if (!Uri.IsHexDigit(value[i])) return false;
        normalized = value.ToUpperInvariant();
        return true;
    }

    public static string Normalize(string value) => TryNormalize(value, out var color) ? color : DefaultHex;

    public static Color Background(string value)
    {
        ColorUtility.TryParseHtmlString(Normalize(value), out var color);
        return color;
    }

    public static Color Foreground(string value)
    {
        var color = Background(value).linear;
        float luminance = .2126f * color.r + .7152f * color.g + .0722f * color.b;
        return luminance < .179f ? Color.white : new Color32(20, 45, 77, 255);
    }

    public static void Apply(GameObject chip, string colorHex, Sprite tintSprite = null)
    {
        var image = chip.GetComponent<Image>();
        if (image)
        {
            if (tintSprite) image.sprite = tintSprite;
            image.color = !tintSprite && Normalize(colorHex) == DefaultHex ? Color.white : Background(colorHex);
        }
        var text = chip.GetComponentInChildren<TMP_Text>(true);
        if (text) text.color = Foreground(colorHex);
    }
}
