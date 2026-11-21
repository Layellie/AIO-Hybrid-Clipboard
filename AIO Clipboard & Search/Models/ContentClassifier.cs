using System;

namespace AIO_Hybrid_Clipboard.Models
{
    public enum ClipKind
    {
        Text,
        Url,
        Color,
        Email,
        FilePath,
        Code
    }

    /// <summary>
    /// Fast, allocation-light content classifier for clipboard text entries.
    /// Avoids backtracking regexes so huge clipboard pastes never stall the UI.
    /// </summary>
    public static class ContentClassifier
    {
        public static (ClipKind Kind, string? NormalizedColorHex) Classify(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (ClipKind.Text, null);

            ReadOnlySpan<char> trimmed = text.AsSpan().Trim();
            bool isSingleLine = trimmed.IndexOfAny('\r', '\n') < 0;

            if (isSingleLine && trimmed.Length <= 2048)
            {
                if (TryNormalizeHexColor(trimmed, out string? hex))
                    return (ClipKind.Color, hex);

                if (IsHttpUrl(trimmed))
                    return (ClipKind.Url, null);

                if (IsEmail(trimmed))
                    return (ClipKind.Email, null);

                if (IsWindowsFilePath(trimmed))
                    return (ClipKind.FilePath, null);
            }

            if (LooksLikeCode(trimmed))
                return (ClipKind.Code, null);

            return (ClipKind.Text, null);
        }

        public static bool TryNormalizeHexColor(ReadOnlySpan<char> span, out string? normalized)
        {
            normalized = null;
            if (span.Length is not (4 or 7 or 9) || span[0] != '#')
                return false;

            for (int i = 1; i < span.Length; i++)
            {
                if (!Uri.IsHexDigit(span[i]))
                    return false;
            }

            if (span.Length == 4)
            {
                // Expand #RGB to #RRGGBB for WPF BrushConverter compatibility.
                normalized = string.Create(7, (span[1], span[2], span[3]), static (dst, c) =>
                {
                    dst[0] = '#';
                    dst[1] = char.ToUpperInvariant(c.Item1);
                    dst[2] = char.ToUpperInvariant(c.Item1);
                    dst[3] = char.ToUpperInvariant(c.Item2);
                    dst[4] = char.ToUpperInvariant(c.Item2);
                    dst[5] = char.ToUpperInvariant(c.Item3);
                    dst[6] = char.ToUpperInvariant(c.Item3);
                });
                return true;
            }

            normalized = span.ToString().ToUpperInvariant();
            return true;
        }

        private static bool IsHttpUrl(ReadOnlySpan<char> span)
        {
            if (span.Contains(' '))
                return false;

            if (!span.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !span.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return false;

            return Uri.TryCreate(span.ToString(), UriKind.Absolute, out var uri) &&
                   !string.IsNullOrEmpty(uri.Host);
        }

        private static bool IsEmail(ReadOnlySpan<char> span)
        {
            if (span.Length < 5 || span.Length > 254 || span.Contains(' '))
                return false;

            int at = span.IndexOf('@');
            if (at <= 0 || at != span.LastIndexOf('@') || at >= span.Length - 3)
                return false;

            ReadOnlySpan<char> domain = span[(at + 1)..];
            int dot = domain.LastIndexOf('.');
            return dot > 0 && dot < domain.Length - 1;
        }

        private static bool IsWindowsFilePath(ReadOnlySpan<char> span)
        {
            if (span.Length < 3)
                return false;

            // Strip surrounding quotes copied via "Copy as path"
            if (span.Length >= 5 && span[0] == '"' && span[^1] == '"')
                span = span[1..^1];

            // Drive path: C:\... or C:/...
            if (char.IsLetter(span[0]) && span[1] == ':' && (span[2] == '\\' || span[2] == '/'))
                return true;

            // UNC path: \\server\share
            if (span.Length >= 5 && span[0] == '\\' && span[1] == '\\' && char.IsLetterOrDigit(span[2]))
                return true;

            return false;
        }

        private static bool LooksLikeCode(ReadOnlySpan<char> span)
        {
            // Sample at most the first 512 chars to stay O(1) on huge pastes.
            if (span.Length > 512)
                span = span[..512];

            if (span.StartsWith("git ", StringComparison.OrdinalIgnoreCase) ||
                span.StartsWith("dotnet ", StringComparison.OrdinalIgnoreCase) ||
                span.StartsWith("npm ", StringComparison.OrdinalIgnoreCase) ||
                span.StartsWith("pnpm ", StringComparison.OrdinalIgnoreCase) ||
                span.StartsWith("docker ", StringComparison.OrdinalIgnoreCase) ||
                span.StartsWith("kubectl ", StringComparison.OrdinalIgnoreCase) ||
                span.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase) ||
                span.StartsWith("INSERT ", StringComparison.OrdinalIgnoreCase) ||
                span.StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase) ||
                span.StartsWith("DELETE ", StringComparison.OrdinalIgnoreCase) ||
                span.StartsWith("curl ", StringComparison.OrdinalIgnoreCase) ||
                span.StartsWith("{", StringComparison.Ordinal) && span.EndsWith("}", StringComparison.Ordinal))
                return true;

            int signals = 0;
            if (span.Contains("=>", StringComparison.Ordinal)) signals += 2;
            if (span.Contains("();", StringComparison.Ordinal)) signals += 2;
            if (span.Contains("public ", StringComparison.Ordinal) ||
                span.Contains("private ", StringComparison.Ordinal) ||
                span.Contains("const ", StringComparison.Ordinal) ||
                span.Contains("function ", StringComparison.Ordinal) ||
                span.Contains("class ", StringComparison.Ordinal) ||
                span.Contains("return ", StringComparison.Ordinal) ||
                span.Contains("#include", StringComparison.Ordinal))
                signals += 2;

            if (span.IndexOf('{') >= 0 && span.IndexOf('}') > span.IndexOf('{')) signals++;
            if (span.IndexOf(';') >= 0 && span.IndexOf('\n') >= 0) signals++;

            return signals >= 2;
        }

        public static string BuildPreview(string text, int maxChars = 160)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Collapse newlines and runs of whitespace so cards stay tidy and aligned.
            Span<char> buf = stackalloc char[Math.Min(text.Length, maxChars + 1)];
            int pos = 0;
            bool prevSpace = true;

            foreach (char ch in text)
            {
                if (pos >= maxChars)
                {
                    buf[pos - 1] = '…';
                    break;
                }

                if (char.IsWhiteSpace(ch))
                {
                    if (!prevSpace)
                    {
                        buf[pos++] = ' ';
                        prevSpace = true;
                    }
                }
                else
                {
                    buf[pos++] = ch;
                    prevSpace = false;
                }
            }

            return new string(buf[..pos]).TrimEnd();
        }

        public const int HugeTextThresholdChars = 1_048_576; // 1M chars (~2 MB UTF-16)

        public static string BuildTooltipText(string text, int maxChars = 4000)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            if (text.Length <= maxChars)
                return text;

            return string.Concat(text.AsSpan(0, maxChars), "…");
        }

        public static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            // Bound scan to 1M chars using SIMD Span.Count so multi-MB pastes never stall the UI.
            ReadOnlySpan<char> slice = text.Length <= HugeTextThresholdChars
                ? text.AsSpan()
                : text.AsSpan(0, HugeTextThresholdChars);

            return 1 + slice.Count('\n');
        }
    }
}
