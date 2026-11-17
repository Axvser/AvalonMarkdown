using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AvalonMarkdown.Views;

/// <summary>
/// Small, side-effect-free helpers used by the preview's export and script bridge.
/// Kept out of <see cref="MarkdownView"/> so they can be unit-tested without an
/// Avalonia application (touching a control type runs its static property
/// registration).
/// </summary>
internal static class MarkdownJs
{
    /// <summary>
    /// Encodes arbitrary text as a JavaScript string literal.
    ///
    /// Deliberately does NOT use <c>System.Text.Json</c>: the Browser/WASM head
    /// runs with reflection-based serialization disabled
    /// (<c>JsonSerializerIsReflectionDisabled</c>), so even
    /// <c>JsonSerializer.Serialize(string)</c> throws there. This encoder covers
    /// the characters a naive escaper misses: quotes, backslashes, control
    /// characters, U+2028/U+2029, and lone/paired surrogates.
    /// </summary>
    public static string StringLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    // U+2028/U+2029 were line terminators in older JavaScript;
                    // surrogates are escaped so a lone one cannot corrupt the
                    // script, while a valid pair round-trips to the same emoji.
                    if (c < ' ' || c == (char)0x2028 || c == (char)0x2029 || char.IsSurrogate(c))
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}

internal static class MarkdownExport
{
    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// The name comes from the document, so it is untrusted: strip anything that
    /// could escape the chosen folder or is illegal in a file name. Windows
    /// device names (<c>CON</c>, <c>NUL</c>, …) are prefixed because saving to
    /// one fails on Windows even though the name looks legal.
    /// </summary>
    public static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "image";

        var sb = new StringBuilder(Math.Min(name.Length, 256));
        foreach (var c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        }

        var clean = sb.ToString().Trim(' ', '.', '_');
        if (clean.Length == 0) clean = "image";
        if (clean.Length > 100) clean = clean[..100];

        var stem = System.IO.Path.GetFileNameWithoutExtension(clean);
        if (ReservedFileNames.Contains(stem))
            clean = "_" + clean;

        return clean;
    }
}
