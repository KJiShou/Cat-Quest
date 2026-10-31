using System;
using System.Text.RegularExpressions;
using UnityEngine;

// Offline, authored dialogue only. No synthesis, network calls or player data in the catalog.
public static class DialogueAudioLibrary
{
    [Serializable] public sealed class Line
    {
        public string role, text, clip;
        public float duration;
        [NonSerialized] public Regex pattern;
    }
    [Serializable] sealed class Catalog { public Line[] lines; }
    static Catalog catalog;

    public static Line Find(string text, bool player)
    {
        if (catalog == null)
        {
            var asset = Resources.Load<TextAsset>("DialogueAudio/catalog");
            catalog = asset ? JsonUtility.FromJson<Catalog>(asset.text) : new Catalog();
        }
        if (catalog.lines == null || string.IsNullOrEmpty(text)) return null;
        string role = player ? "player" : "mentor";
        foreach (var line in catalog.lines)
            if (line.role == role && line.text == text) return line;
        foreach (var line in catalog.lines)
        {
            if (line.role != role || !line.text.Contains("{")) continue;
            if (line.pattern == null)
            {
                string pattern = "";
                int offset = 0;
                foreach (Match token in Regex.Matches(line.text, @"\{[^}]+\}"))
                {
                    pattern += Regex.Escape(line.text.Substring(offset, token.Index - offset)) + @"[\s\S]+?";
                    offset = token.Index + token.Length;
                }
                line.pattern = new Regex("^" + pattern + Regex.Escape(line.text.Substring(offset)) + "$");
            }
            if (line.pattern.IsMatch(text)) return line;
        }
        return null;
    }
}
