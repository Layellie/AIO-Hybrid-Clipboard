using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace AIO_Hybrid_Clipboard.Services
{
    public enum TextTransformKind
    {
        FormatJson,
        MinifyJson,
        SingleLine,
        UpperCase,
        LowerCase,
        TrimLines,
        UrlDecode,
        UrlEncode
    }

    /// <summary>
    /// Fast, zero-dependency clipboard text transformations for developer workflows.
    /// </summary>
    internal static class TextTransformer
    {
        private static readonly JsonSerializerOptions PrettyOptions = new() { WriteIndented = true };
        private static readonly JsonSerializerOptions CompactOptions = new() { WriteIndented = false };

        /// <summary>
        /// Applies the requested transform and returns the transformed string,
        /// or null when the input is not valid for the transform (e.g. non-JSON for JSON Format).
        /// </summary>
        public static string? Apply(string text, TextTransformKind kind)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            return kind switch
            {
                TextTransformKind.FormatJson => TryFormatJson(text, indented: true),
                TextTransformKind.MinifyJson => TryFormatJson(text, indented: false),
                TextTransformKind.SingleLine => ToSingleLine(text),
                TextTransformKind.UpperCase  => text.ToUpperInvariant(),
                TextTransformKind.LowerCase  => text.ToLowerInvariant(),
                TextTransformKind.TrimLines  => TrimLines(text),
                TextTransformKind.UrlDecode  => Uri.UnescapeDataString(text),
                TextTransformKind.UrlEncode  => Uri.EscapeDataString(text),
                _                            => null
            };
        }

        public static string? TryFormatJson(string text, bool indented)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                return JsonSerializer.Serialize(doc.RootElement, indented ? PrettyOptions : CompactOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static string ToSingleLine(string text)
        {
            var sb = new StringBuilder(text.Length);
            bool prevSpace = true;

            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!prevSpace)
                    {
                        sb.Append(' ');
                        prevSpace = true;
                    }
                }
                else
                {
                    sb.Append(ch);
                    prevSpace = false;
                }
            }

            if (sb.Length > 0 && sb[^1] == ' ')
                sb.Length--;

            return sb.ToString();
        }

        public static string TrimLines(string text)
        {
            string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var trimmed = new List<string>(lines.Length);
            foreach (string line in lines)
                trimmed.Add(line.Trim());
            return string.Join(Environment.NewLine, trimmed);
        }
    }
}
