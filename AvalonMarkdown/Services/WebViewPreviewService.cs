using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace AvalonMarkdown.Services;

/// <summary>
/// Desktop WebView 实现的 Markdown 预览服务
/// </summary>
public class WebViewPreviewService : IMarkdownPreviewService
{
    private readonly NativeWebView _webView;

    public WebViewPreviewService(NativeWebView webView)
    {
        _webView = webView;
        _webView.NavigationCompleted += OnWebViewNavigationCompleted;
    }

    public event EventHandler? NavigationCompleted;

    public async Task LoadHtmlAsync(string htmlContent)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (File.Exists(htmlContent))
            {
                // Treat as file path
                _webView.Source = new Uri("file:///" + htmlContent.Replace('\\', '/'));
            }
        });
    }

    public async Task InvokeScriptAsync(string script)
    {
        var result = _webView.InvokeScript(script);
        if (result is Task t)
            await t.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private void OnWebViewNavigationCompleted(object? sender, EventArgs e)
    {
        NavigationCompleted?.Invoke(this, e);
    }
}
