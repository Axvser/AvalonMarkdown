using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using AvalonMarkdown.Services;

namespace AvalonMarkdown.Views;

/// <summary>
/// 统一的 Markdown 预览控件，封装 NativeWebView 并提供：
///   • 顶部工具栏（重启预览等）
///   • 错误捕获与内联显示（而非静默失败或崩溃）
/// </summary>
public partial class MarkdownView : UserControl
{
    private NativeWebView _webView = null!;
    private readonly IWebViewSourceProvider _sourceProvider;
    private bool _ready;
    private bool _htmlInjected;
    private string? _pendingMarkdown;
    private string _htmlContent = "";
    private int _loadSequence;
    private string _currentTheme = "dark";
    private bool _themeMonitored;

    /// <summary>获取或设置是否显示工具栏</summary>
    public bool ShowToolbar
    {
        get => ToolbarPanel.IsVisible;
        set
        {
            ToolbarPanel.IsVisible = value;
            ToggleToolbarButton.Content = value ? "Hide" : "Show";
        }
    }

    // ====================================================================
    // 公共事件
    // ====================================================================

    /// <summary>MarkdownView 完全就绪（HTML 注入 + CDN 脚本加载完成）时触发</summary>
    public event EventHandler? OnReady;

    /// <summary>内部发生可恢复错误时触发</summary>
    public event EventHandler<MarkdownViewErrorEventArgs>? ErrorOccurred;

    // ====================================================================
    // 构造
    // ====================================================================

    public MarkdownView()
        : this(new EmbeddedHtmlSourceProvider())
    {
    }

    /// <summary>
    /// 使用依赖注入创建 MarkdownView，允许各平台提供不同的页面来源。
    /// </summary>
    public MarkdownView(IWebViewSourceProvider sourceProvider)
    {
        _sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));

        InitializeComponent();

        CreateWebView();
        WireEvents();
        _ = InitializeWebViewAsync();

        StatusText.Text = "Loading…";
    }

    // ====================================================================
    // WebView 生命周期
    // ====================================================================

    private void CreateWebView()
    {
        _webView = new NativeWebView
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x1e, 0x1e, 0x1e)),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };
        WebViewHost.Children.Add(_webView);
    }

    private void WireEvents()
    {
        _webView.NavigationCompleted += OnNavigationCompleted;

        // 尝试订阅 WebViewMessages（部分平台/版本可能不支持）
        try
        {
            var msgEvent = _webView.GetType().GetEvent("WebViewMessages");
            if (msgEvent != null)
            {
                var handler = Delegate.CreateDelegate(msgEvent.EventHandlerType!,
                    this, nameof(OnWebViewMessage));
                msgEvent.AddEventHandler(_webView, handler);
            }
        }
        catch
        {
            // 不支持 WebViewMessages — 静默忽略
        }

        RestartButton.Click += async (_, _) => await RestartPreviewAsync();
        ToggleToolbarButton.Click += (_, _) => ShowToolbar = !ShowToolbar;
        DismissErrorButton.Click += (_, _) => HideError();

        SubscribeThemeChanges();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            _htmlContent = _sourceProvider.GetHtmlContent();
            _htmlInjected = false;

            if (IsDesktop)
            {
                // Desktop: 写临时文件后用 file:/// 导航（原始可靠方式，CDN/JS 全功能正常）
                var tempFile = WriteTempHtmlFile(_htmlContent);
                _webView.Source = new Uri("file:///" + tempFile.Replace('\\', '/'));
            }
            else
            {
                // Browser/Mobile: about:blank + document.write 注入
                _webView.Source = new Uri("about:blank");
            }

            // 安全网：5 秒后若仍未就绪（如某些平台 NavigationCompleted 未触发），强制注入
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                if (!_ready)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        if (!_htmlInjected)
                            await InjectViaDocumentWriteAsync();
                        else
                            SetReady();
                    });
                }
            });
        }
        catch (Exception ex)
        {
            ShowError("Init failed", ex.Message);
        }

        await Task.CompletedTask;
    }

    private static bool IsDesktop =>
        Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;

    private static string WriteTempHtmlFile(string html)
    {
        var dir = Path.Combine(Path.GetTempPath(), "AvalonMarkdown");
        Directory.CreateDirectory(dir);
        // 使用时间戳防缓存
        var path = Path.Combine(dir, $"preview_{DateTime.Now:HHmmssfff}.html");
        File.WriteAllText(path, html);
        // 清理 30 秒前的旧临时文件
        try
        {
            foreach (var f in Directory.GetFiles(dir, "preview_*.html"))
                if (f != path && File.GetLastWriteTime(f) < DateTime.Now.AddSeconds(-30))
                    File.Delete(f);
        } catch { }
        return path;
    }

    private async Task InjectViaDocumentWriteAsync()
    {
        if (_htmlInjected || string.IsNullOrEmpty(_htmlContent))
            return;

        _htmlInjected = true;

        try
        {
            var escaped = _htmlContent
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n")
                .Replace("\r", "\\n");

            var script = $"document.open();document.write('{escaped}');document.close();";
            var result = _webView.InvokeScript(script);
            if (result is Task t)
                await t.WaitAsync(TimeSpan.FromSeconds(5));

            ForceLayout();
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            ShowError("Inject failed", ex.Message);
            _htmlInjected = false;
            return;
        }

        SetReady();
    }

    private void SetReady()
    {
        if (_ready) return;
        _ready = true;
        StatusText.Text = "Ready";

        // WebView 就绪后立刻应用系统主题
        _ = ApplySystemThemeAsync();

        OnReady?.Invoke(this, EventArgs.Empty);

        if (_pendingMarkdown != null)
        {
            var md = _pendingMarkdown;
            _pendingMarkdown = null;
            _ = RenderMarkdownAsync(md);
        }
    }

    // ====================================================================
    // 事件处理
    // ====================================================================

    private void OnNavigationCompleted(object? sender, EventArgs e)
    {
        if (_htmlInjected)
            return;

        if (IsDesktop)
        {
            // Desktop: file:/// 已加载 → 内容自带 CDN/JS，等待脚本就绪
            ForceLayout();
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(SetReady);
            });
        }
        else
        {
            // Browser/Mobile: about:blank → document.write 注入
            _ = InjectViaDocumentWriteAsync();
        }
    }

    /// <summary>
    /// 接收 WebView 内部消息（console.log/error 等通过 chrome.webview.postMessage 发出）
    /// </summary>
    public void OnWebViewMessage(object? sender, string message)
    {
        if (message.StartsWith("[ERR]", StringComparison.OrdinalIgnoreCase) ||
            message.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase))
        {
            // 通过 Dispatcher 回到 UI 线程显示错误
            _ = Dispatcher.UIThread.InvokeAsync(() =>
                ShowError("Render error", message));
        }
    }

    // ====================================================================
    // 公共 API
    // ====================================================================

    /// <summary>渲染 Markdown 内容到 WebView</summary>
    public async Task RenderMarkdownAsync(string? markdown)
    {
        if (!_ready)
        {
            _pendingMarkdown = markdown;
            return;
        }

        if (string.IsNullOrEmpty(markdown))
        {
            _ = InvokeScriptSafeAsync("renderMarkdown('')");
            return;
        }

        var escaped = EscapeJsString(markdown);
        await InvokeScriptSafeAsync($"renderMarkdown('{escaped}')");
        StatusText.Text = "Rendered";
    }

    /// <summary>重启预览（重新导航到初始页面）</summary>
    public async Task RestartPreviewAsync()
    {
        var seq = ++_loadSequence;
        _ready = false;
        _htmlInjected = false;
        _pendingMarkdown = null;
        HideError();
        StatusText.Text = "Reloading…";

        try
        {
            _htmlContent = _sourceProvider.GetHtmlContent();

            if (IsDesktop)
            {
                var tempFile = WriteTempHtmlFile(_htmlContent);
                _webView.Source = new Uri("file:///" + tempFile.Replace('\\', '/'));
            }
            else
            {
                _webView.Source = new Uri("about:blank");
            }

            // 重启安全网：5 秒后备
            _ = StartRestartSafetyNetAsync(seq);
        }
        catch (Exception ex)
        {
            ShowError("Restart failed", ex.Message);
        }
    }

    private async Task StartRestartSafetyNetAsync(int seq)
    {
        await Task.Delay(5000);
        if (!_ready && seq == _loadSequence)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!_htmlInjected)
                    await InjectViaDocumentWriteAsync();
                else
                    SetReady();
            });
        }
    }

    /// <summary>在注入 HTML 后强制布局更新</summary>
    private void ForceLayout()
    {
        WebViewHost.InvalidateMeasure();
        WebViewHost.InvalidateArrange();
        _webView.InvalidateMeasure();
        _webView.InvalidateArrange();
    }

    /// <summary>应用预览配置（JS 调用）</summary>
    public async Task ApplyConfigAsync(string jsCallExpression)
    {
        if (!_ready) return;
        await InvokeScriptSafeAsync(jsCallExpression);
    }

    /// <summary>执行自定义 JavaScript</summary>
    public async Task<string?> InvokeScriptAsync(string script)
    {
        if (!_ready) return null;
        return await InvokeScriptSafeAsync(script);
    }

    // ====================================================================
    // 内部帮助方法
    // ====================================================================

    private async Task<string?> InvokeScriptSafeAsync(string script)
    {
        try
        {
            var result = _webView.InvokeScript(script);
            if (result is Task t)
            {
                await t.WaitAsync(TimeSpan.FromSeconds(5));
                return null;
            }
            return result?.ToString();
        }
        catch (OperationCanceledException)
        {
            ShowError("Timeout", $"Script >5s: {script[..Math.Min(script.Length, 80)]}");
            return null;
        }
        catch (Exception ex)
        {
            ShowError("Script error", $"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private void ShowError(string title, string message)
    {
        ErrorTitle.Text = title;
        ErrorMessage.Text = message;
        ErrorPanel.IsVisible = true;
        StatusText.Text = $"⚠ {title}";
        ErrorOccurred?.Invoke(this, new MarkdownViewErrorEventArgs(title, message));
    }

    private void HideError()
    {
        ErrorPanel.IsVisible = false;
        ErrorTitle.Text = "";
        ErrorMessage.Text = "";
        if (_ready)
            StatusText.Text = "Ready";
    }

    // ====================================================================
    // 主题管理
    // ====================================================================

    private void SubscribeThemeChanges()
    {
        if (_themeMonitored) return;
        _themeMonitored = true;

        var app = Avalonia.Application.Current;
        if (app == null) return;

        // 监听系统/应用主题切换
        app.ActualThemeVariantChanged += OnActualThemeChanged;
    }

    private void OnActualThemeChanged(object? sender, EventArgs e)
    {
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await ApplySystemThemeAsync();
        });
    }

    private async Task ApplySystemThemeAsync()
    {
        var theme = GetCurrentTheme();
        if (theme == _currentTheme) return;
        _currentTheme = theme;

        if (_ready)
            await InvokeScriptSafeAsync($"setTheme('{theme}')");
    }

    private static string GetCurrentTheme()
    {
        var app = Avalonia.Application.Current;
        if (app == null) return "dark";

        var variant = app.ActualThemeVariant;
        if (variant == ThemeVariant.Light)
            return "light";
        // Dark 或其他（null / Default）均回退暗色
        return "dark";
    }

    private static string EscapeJsString(string s)
    {
        return s.Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }

}

// ====================================================================
// 错误事件参数
// ====================================================================

public class MarkdownViewErrorEventArgs : EventArgs
{
    public string Title { get; }
    public string Message { get; }
    public DateTime Timestamp { get; } = DateTime.Now;

    public MarkdownViewErrorEventArgs(string title, string message)
    {
        Title = title;
        Message = message;
    }
}
