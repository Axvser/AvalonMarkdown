using System;
using System.IO;
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
    private readonly Lazy<string> _html;

    public EmbeddedHtmlSourceProvider()
    {
        _html = new Lazy<string>(BuildHtml);
    }

    public string GetHtmlContent()
    {
        var html = _html.Value;
        // Inject current system theme to match loading text and background color before WebView navigates
        var themeClass = GetCurrentTheme() == "light" ? "theme-light" : "theme-dark";
        html = html.Replace("class=\"theme-dark\"", $"class=\"{themeClass}\"");
        return html;
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
            var html = ReadAsset("avares://AvalonMarkdown/Assets/web/index.html");
            if (html == null)
                return FallbackHtml("Cannot find index.html");

            // Inline renderer.css
            var css = ReadAsset("avares://AvalonMarkdown/Assets/web/renderer.css");
            if (css != null)
            {
                html = html.Replace(
                    """<link rel="stylesheet" href="renderer.css">""",
                    $"<style>{css}</style>");
            }

            // Inline renderer.js
            var js = ReadAsset("avares://AvalonMarkdown/Assets/web/renderer.js");
            if (js != null)
            {
                html = html.Replace(
                    """<script src="renderer.js"></script>""",
                    $"<script>{js}</script>");
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
