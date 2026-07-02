using System;
using System.IO;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;

namespace AvalonMarkdown.Services;

/// <summary>
/// 统一 HTML 来源提供器 — 所有平台共用。
/// 通过 Avalonia AssetLoader 读取嵌入式资源，
/// 将 renderer.css / renderer.js 内联后输出，并注入当前系统主题。
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
        // 注入当前系统主题，确保 WebView 加载前 loading 文字和背景色已匹配
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
                return FallbackHtml("找不到 index.html");

            // 内联 renderer.css
            var css = ReadAsset("avares://AvalonMarkdown/Assets/web/renderer.css");
            if (css != null)
            {
                html = html.Replace(
                    """<link rel="stylesheet" href="renderer.css">""",
                    $"<style>{css}</style>");
            }

            // 内联 renderer.js
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
<head><meta charset="UTF-8"><title>错误</title></head>
<body style="background:#1e1e1e;color:#f14c4c;display:flex;align-items:center;justify-content:center;height:100vh;font-family:sans-serif;">
  <p>⚠ 无法加载预览资源: {System.Net.WebUtility.HtmlEncode(detail)}</p>
</body>
</html>
""";
    }
}
