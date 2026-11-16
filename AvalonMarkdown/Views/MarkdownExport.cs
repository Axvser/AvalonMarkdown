using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

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
    /// Encodes arbitrary text as a JavaScript string literal. JSON string escaping
    /// is exactly the subset JavaScript accepts, and it covers the characters a
    /// hand-rolled escaper tends to miss (control characters, U+2028/U+2029,
    /// quotes, backslashes).
    /// </summary>
    public static string StringLiteral(string? value)
    {
        var json = JsonSerializer.Serialize(value ?? string.Empty);
        // Build the separators from code points: a literal U+2028/U+2029 in a C#
        // source file is treated as a line break by the compiler.
        var lineSeparator = ((char)0x2028).ToString();
        var paragraphSeparator = ((char)0x2029).ToString();
        return json
            .Replace(lineSeparator, "\\u2028", StringComparison.Ordinal)
            .Replace(paragraphSeparator, "\\u2029", StringComparison.Ordinal);
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
