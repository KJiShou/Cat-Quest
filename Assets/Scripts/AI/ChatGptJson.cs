using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Small, bounded JSON codec shared by OAuth/JWKS and the text-only inference protocol.
// Reject duplicate keys: security claims must have one unambiguous value.
public static class ChatGptJson
{
    public const int MaximumDocumentCharacters = 4194304;
    public static Dictionary<string, object> Object(string json)
    {
        if (json == null || json.Length > MaximumDocumentCharacters) throw new ArgumentException("Invalid JSON size.");
        var parser = new Parser(json);
        var result = parser.Value(0) as Dictionary<string, object>;
        parser.Space();
        if (result == null || !parser.End) throw new ArgumentException("Invalid JSON object.");
        return result;
    }
    public static string Text(IDictionary<string, object> value, string key) =>
        value != null && value.TryGetValue(key, out var item) ? item as string : null;
    public static long Number(IDictionary<string, object> value, string key) =>
        value != null && value.TryGetValue(key, out var item) && item is long number ? number : 0;
    public static string Encode(object value)
    {
        var text = new StringBuilder(); Write(text, value); return text.ToString();
    }
    static void Write(StringBuilder text, object value)
    {
        if (value == null) { text.Append("null"); return; }
        if (value is string str)
        {
            text.Append('"');
            foreach (char c in str)
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default: if (c < 32) text.Append("\\u").Append(((int)c).ToString("x4")); else text.Append(c); break;
                }
            text.Append('"'); return;
        }
        if (value is bool flag) { text.Append(flag ? "true" : "false"); return; }
        if (value is IDictionary<string, object> map)
        {
            text.Append('{'); bool first = true;
            foreach (var pair in map) { if (!first) text.Append(','); first = false; Write(text, pair.Key); text.Append(':'); Write(text, pair.Value); }
            text.Append('}'); return;
        }
        if (value is IEnumerable list)
        {
            text.Append('['); bool first = true;
            foreach (var item in list) { if (!first) text.Append(','); first = false; Write(text, item); }
            text.Append(']'); return;
        }
        if (value is int || value is long || value is double) { text.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
        throw new ArgumentException("Unsupported JSON value.");
    }
    sealed class Parser
    {
        readonly string text; int at;
        public Parser(string text) { this.text = text; }
        public bool End => at == text.Length;
        public void Space() { while (!End && (text[at] == ' ' || text[at] == '\t' || text[at] == '\r' || text[at] == '\n')) at++; }
        bool Eat(char c) { Space(); if (End || text[at] != c) return false; at++; return true; }
        public object Value(int depth)
        {
            Space(); if (End || depth > 24) throw new ArgumentException("Invalid JSON.");
            if (text[at] == '"') return String();
            if (Eat('{'))
            {
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                if (Eat('}')) return map;
                do {
                    Space(); string key = String();
                    if (!Eat(':') || map.ContainsKey(key)) throw new ArgumentException("Invalid or duplicate JSON property.");
                    map.Add(key, Value(depth + 1));
                    if (Eat('}')) return map;
                } while (Eat(','));
                throw new ArgumentException("Invalid JSON object.");
            }
            if (Eat('['))
            {
                var list = new List<object>(); if (Eat(']')) return list;
                do { list.Add(Value(depth + 1)); if (Eat(']')) return list; } while (Eat(','));
                throw new ArgumentException("Invalid JSON array.");
            }
            foreach (string word in new[] { "true", "false", "null" })
                if (at + word.Length <= text.Length && string.CompareOrdinal(text, at, word, 0, word.Length) == 0)
                { at += word.Length; return word == "null" ? null : (object)(word == "true"); }
            int start = at;
            if (!End && text[at] == '-') at++;
            if (End || text[at] < '0' || text[at] > '9') throw new ArgumentException("Invalid JSON number.");
            if (text[at] == '0') at++; else while (!End && char.IsDigit(text[at])) at++;
            if (!End && (text[at] == '.' || text[at] == 'e' || text[at] == 'E'))
            {
                if (text[at] == '.') { at++; Digits(); }
                if (!End && (text[at] == 'e' || text[at] == 'E')) { at++; if (!End && (text[at] == '+' || text[at] == '-')) at++; Digits(); }
                if (!double.TryParse(text.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || double.IsInfinity(number)) throw new ArgumentException("Invalid JSON number.");
                return number;
            }
            if (!long.TryParse(text.Substring(start, at - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer)) throw new ArgumentException("Invalid JSON integer.");
            return integer;
        }
        void Digits() { int start = at; while (!End && text[at] >= '0' && text[at] <= '9') at++; if (start == at) throw new ArgumentException("Invalid JSON number."); }
        string String()
        {
            if (End || text[at++] != '"') throw new ArgumentException("Invalid JSON string.");
            var result = new StringBuilder();
            while (!End)
            {
                char c = text[at++]; if (c == '"') return result.ToString();
                if (c < 32) throw new ArgumentException("Invalid JSON string.");
                if (c != '\\') { result.Append(c); continue; }
                if (End) break;
                switch (text[at++])
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (at + 4 > text.Length || !ushort.TryParse(text.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code)) throw new ArgumentException("Invalid Unicode escape.");
                        at += 4; result.Append((char)code); break;
                    default: throw new ArgumentException("Invalid JSON escape.");
                }
            }
            throw new ArgumentException("Unterminated JSON string.");
        }
    }
}
