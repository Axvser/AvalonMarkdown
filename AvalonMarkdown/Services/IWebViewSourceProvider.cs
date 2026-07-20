namespace AvalonMarkdown.Services;

/// <summary>
/// Provides the complete HTML content for WebView loading.
/// </summary>
public interface IWebViewSourceProvider
{
    /// <summary>Gets the complete HTML page content (with inlined CSS/JS)</summary>
    string GetHtmlContent();
}
