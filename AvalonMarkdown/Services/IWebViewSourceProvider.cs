namespace AvalonMarkdown.Services;

/// <summary>
/// 提供 WebView 加载的完整 HTML 内容。
/// </summary>
public interface IWebViewSourceProvider
{
    /// <summary>获取完整的 HTML 页面内容（含内联 CSS/JS）</summary>
    string GetHtmlContent();
}
