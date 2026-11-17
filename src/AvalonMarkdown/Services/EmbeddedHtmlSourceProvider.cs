using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;

namespace AvalonMarkdown.Services;

/// <summary>
/// Unified HTML source provider — shared across all platforms.
/// Reads embedded resources via Avalonia AssetLoader,
/// inlines renderer.css / renderer.js, and injects the current system theme.
/// </summary>
public class EmbeddedHtmlSourceProvider : IWebViewSourceProvider
{
    // Matches the class attribute on <html> whatever it currently contains, so a
    // later index.html edit cannot silently break the loading-theme injection the
    // way a literal Replace("class=\"theme-dark\"") would.
    private static readonly Regex HtmlTagClass = new(
        @"<html\b(?<attrs>[^>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ClassAttribute = new(
        @"\bclass\s*=\s*""(?<classes>[^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ThemeToken = new(
        @"\btheme-(?:dark|light)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Lazy<string> _html;

    public EmbeddedHtmlSourceProvider()
    {
        _html = new Lazy<string>(BuildHtml);
    }

    public string GetHtmlContent()
    {
        var html = _html.Value;
        var themeClass = GetCurrentTheme() == "light" ? "theme-light" : "theme-dark";
        return ApplyThemeClass(html, themeClass);
    }

    /// <summary>
    /// Rewrite the <c>&lt;html&gt;</c> class so the loading placeholder is painted
    /// in the right theme before the WebView navigates. Idempotent, and tolerant of
    /// a custom index.html without a class attribute.
    /// </summary>
    internal static string ApplyThemeClass(string html, string themeClass)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        return HtmlTagClass.Replace(html, match =>
        {
            var attrs = match.Groups["attrs"].Value;
            var classMatch = ClassAttribute.Match(attrs);
            if (classMatch.Success)
            {
                var classes = ThemeToken.Replace(classMatch.Groups["classes"].Value, themeClass)
                    .Trim();
                if (classes.IndexOf(themeClass, StringComparison.OrdinalIgnoreCase) < 0)
                    classes = string.IsNullOrEmpty(classes) ? themeClass : classes + " " + themeClass;

                var replacedAttrs = attrs.Remove(classMatch.Index, classMatch.Length)
                    .Insert(classMatch.Index, $"class=\"{classes}\"");
                return "<html" + replacedAttrs + ">";
            }

            return "<html class=\"" + themeClass + "\"" + attrs + ">";
        }, 1);
    }

    private static string GetCurrentTheme()
    {
        var app = Application.Current;
        if (app == null) return "dark";
        return app.ActualThemeVariant == ThemeVariant.Light ? "light" : "dark";
    }

    private static string BuildHtml()
    {
        try
        {
            var html = ReadAsset("avares://AvalonMarkdown/Assets/AvalonMarkdown/WebView/index.html");
            if (html == null)
                return FallbackHtml("Cannot find index.html");

            var missing = new List<string>();

            // Inline renderer.css
            var css = ReadAsset("avares://AvalonMarkdown/Assets/AvalonMarkdown/WebView/renderer.css");
            if (css != null)
            {
                html = html.Replace(
                    """<link rel="stylesheet" href="renderer.css">""",
                    $"<style>{css}</style>");
            }
            else
            {
                missing.Add("renderer.css");
            }

            // Inline renderer.js
            var js = ReadAsset("avares://AvalonMarkdown/Assets/AvalonMarkdown/WebView/renderer.js");
            if (js != null)
            {
                html = html.Replace(
                    """<script src="renderer.js"></script>""",
                    $"<script>{js}</script>");
            }
            else
            {
                missing.Add("renderer.js");
            }

            if (missing.Count > 0)
            {
                // Without renderer.js nothing will render, and without CSS the page
                // is an unstyled wall of text. Say so in the page rather than leaving
                // the user staring at a blank preview.
                var warning =
                    "<div style=\"position:fixed;top:0;left:0;right:0;z-index:10000;" +
                    "background:#5c2a2a;color:#ffd7d7;padding:8px 12px;font:12px sans-serif;\">" +
                    "⚠ AvalonMarkdown is missing its embedded assets: " +
                    System.Net.WebUtility.HtmlEncode(string.Join(", ", missing)) +
                    "</div>";
                var withWarning = Regex.Replace(
                    html, "<body\\b[^>]*>", m => m.Value + warning, RegexOptions.IgnoreCase);
                html = withWarning != html ? withWarning : warning + html;
            }

            return html;
        }
        catch (Exception ex)
        {
            return FallbackHtml(ex.Message);
        }
    }

    private static string? ReadAsset(string uri)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    private static string FallbackHtml(string detail)
    {
        return $"""
<!DOCTYPE html>
<html>
<head><meta charset="UTF-8"><title>Error</title></head>
<body style="background:#1e1e1e;color:#f14c4c;display:flex;align-items:center;justify-content:center;height:100vh;font-family:sans-serif;">
  <p>⚠ Failed to load preview assets: {System.Net.WebUtility.HtmlEncode(detail)}</p>
</body>
</html>
""";
    }
}
