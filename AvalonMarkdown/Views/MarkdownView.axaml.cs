using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
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

    // ====================================================================
    // 静态主题管理 — WeakReference 跟踪所有活动实例，响应式推送主题
    // ====================================================================
    private static readonly List<WeakReference<MarkdownView>> _instances = new();
    private static readonly object _lock = new();
    private static bool _themeSubscribed;

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

        // 注册到静态实例列表（响应式主题推送用）
        RegisterInstance();

        // 每次构造都查询当前主题，不依赖任何静态缓存
        ApplyThemeColors(GetCurrentTheme());

        _ = InitializeWebViewAsync();
    }

    // ====================================================================
    // 布局 — Auto 尺寸修正
    // ====================================================================

    /// <summary>
    /// NativeWebView（NativeControlHost）在 HWND 未创建时 DesiredSize = (0,0)，
    /// 导致 Auto 父容器行/列折叠。此处保证至少返回可用尺寸以维持布局。
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var result = base.MeasureOverride(availableSize);

        if ((result.Width <= 0 || double.IsNaN(result.Width)) &&
            availableSize.Width > 0 && !double.IsNaN(availableSize.Width) && !double.IsInfinity(availableSize.Width))
            result = new Size(availableSize.Width, result.Height);

        if ((result.Height <= 0 || double.IsNaN(result.Height)) &&
            availableSize.Height > 0 && !double.IsNaN(availableSize.Height) && !double.IsInfinity(availableSize.Height))
            result = new Size(result.Width, Math.Min(availableSize.Height, 300));

        return result;
    }

    // ====================================================================
    // WebView 生命周期
    // ====================================================================

    private void CreateWebView()
    {
        _webView = new NativeWebView
        {
            Background = GetCurrentTheme() == "light"
                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xff, 0xff, 0xff))
                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x1e, 0x1e, 0x1e)),
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

        DismissErrorButton.Click += (_, _) => HideError();

        // 事件驱动布局修正：WebViewHost 首次获得有效尺寸时触发一次
        WebViewHost.EffectiveViewportChanged += OnHostViewportChanged;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            _htmlContent = _sourceProvider.GetHtmlContent();
            _htmlInjected = false;

            if (IsDesktop)
            {
                var tempFile = WriteTempHtmlFile(_htmlContent);
                _webView.Source = new Uri("file:///" + tempFile.Replace('\\', '/'));
            }
            else if (OperatingSystem.IsBrowser())
            {
                _webView.Source = new Uri("about:blank");
            }
            else
            {
                var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_htmlContent));
                _webView.Source = new Uri("data:text/html;base64," + base64);
            }
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

        // 就绪后向 WebView JS 环境同步主题
        PushThemeToWebView(GetCurrentTheme());

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
        if (_htmlInjected || _ready)
            return;

        if (IsDesktop || !OperatingSystem.IsBrowser())
        {
            _htmlInjected = true;
            ForceLayout();
            SetReady();
        }
        else
        {
            _ = InjectViaDocumentWriteAsync();
        }
    }

    /// <summary>
    /// 接收 WebView 内部消息（console.log/error/ready 等通过 chrome.webview.postMessage 发出）
    /// </summary>
    public void OnWebViewMessage(object? sender, string message)
    {
        if (message.StartsWith("[READY]", StringComparison.OrdinalIgnoreCase))
        {
            if (!_ready && _htmlInjected)
            {
                _ = Dispatcher.UIThread.InvokeAsync(SetReady);
            }
            return;
        }

        if (message.StartsWith("[ERR]", StringComparison.OrdinalIgnoreCase) ||
            message.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase))
        {
            // 不主动显示错误面板，仅触发事件供外部订阅
            ErrorOccurred?.Invoke(this, new MarkdownViewErrorEventArgs("Render error", message));
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
    }

    /// <summary>重启预览（重新导航/注入 HTML）</summary>
    public async Task RestartPreviewAsync()
    {
        _ready = false;
        _htmlInjected = false;
        _pendingMarkdown = null;
        HideError();

        try
        {
            _htmlContent = _sourceProvider.GetHtmlContent();

            if (IsDesktop)
            {
                var tempFile = WriteTempHtmlFile(_htmlContent);
                _webView.Source = new Uri("file:///" + tempFile.Replace('\\', '/'));
            }
            else if (OperatingSystem.IsBrowser())
            {
                await InjectViaDocumentWriteAsync();
            }
            else
            {
                // Mobile (Android/iOS): 用 InvokeScript 执行 location.href 导航
                // 从 JS 侧发起导航，NativeWebView 的 NavigationCompleted 依然会触发
                var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_htmlContent));
                var script = "location.href='data:text/html;base64," + base64 + "'";
                var result = _webView.InvokeScript(script);
                if (result is Task t)
                    await t.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception ex)
        {
            ShowError("Restart failed", ex.Message);
        }
    }

    /// <summary>事件驱动布局修正：WebViewHost 首次获得有效尺寸时同步 iframe 布局</summary>
    private void OnHostViewportChanged(object? sender, Avalonia.Layout.EffectiveViewportChangedEventArgs e)
    {
        // 仅在首次获得有效尺寸时触发
        if (e.EffectiveViewport.Width <= 0 || e.EffectiveViewport.Height <= 0)
            return;

        WebViewHost.EffectiveViewportChanged -= OnHostViewportChanged;

        if (_webView != null)
        {
            _webView.InvalidateMeasure();
            _webView.InvalidateArrange();
        }
    }

    /// <summary>备用：注入 HTML 后立即标记布局失效，事件驱动会在下一帧处理</summary>
    private void ForceLayout()
    {
        WebViewHost.InvalidateMeasure();
        WebViewHost.InvalidateArrange();
        _webView?.InvalidateMeasure();
        _webView?.InvalidateArrange();
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
        ErrorOccurred?.Invoke(this, new MarkdownViewErrorEventArgs(title, message));
    }

    private void HideError()
    {
        ErrorPanel.IsVisible = false;
        ErrorTitle.Text = "";
        ErrorMessage.Text = "";
    }

    // ====================================================================
    // 静态主题管理 — 注册/推送
    // ====================================================================

    /// <summary>将当前实例加入静态弱引用列表</summary>
    private void RegisterInstance()
    {
        lock (_lock)
        {
            // 清理已回收的实例
            _instances.RemoveAll(wr => !wr.TryGetTarget(out _));
            _instances.Add(new WeakReference<MarkdownView>(this));

            // 首次实例化时订阅全局主题变化（只订阅一次）
            if (!_themeSubscribed)
            {
                var app = Avalonia.Application.Current;
                if (app != null)
                {
                    app.ActualThemeVariantChanged += OnGlobalThemeChanged;
                    _themeSubscribed = true;
                }
            }
        }
    }

    /// <summary>全局主题切换回调 — 推送给所有活着的 MarkdownView 实例</summary>
    private static void OnGlobalThemeChanged(object? sender, EventArgs e)
    {
        var theme = GetCurrentTheme();

        lock (_lock)
        {
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                if (_instances[i].TryGetTarget(out var view))
                {
                    // 异步派发到 UI 线程，不阻塞主题事件
                    _ = Dispatcher.UIThread.InvokeAsync(() => view.ApplyThemePush(theme));
                }
                else
                {
                    // 实例已 GC，移除
                    _instances.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>收到主题推送：刷新背景色 + 向 WebView JS 发消息</summary>
    private async void ApplyThemePush(string theme)
    {
        ApplyThemeColors(theme);
        if (_ready)
            await InvokeScriptSafeAsync($"setTheme('{theme}')");
    }

    /// <summary>向已就绪的 WebView JS 同步当前主题</summary>
    private async void PushThemeToWebView(string theme)
    {
        if (_ready)
            await InvokeScriptSafeAsync($"setTheme('{theme}')");
    }

    private void ApplyThemeColors(string theme)
    {
        if (theme == "light")
        {
            WebViewHost.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xff, 0xff, 0xff));
            if (_webView != null)
                _webView.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xff, 0xff, 0xff));
        }
        else
        {
            WebViewHost.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x1e, 0x1e, 0x1e));
            if (_webView != null)
                _webView.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x1e, 0x1e, 0x1e));
        }
    }

    /// <summary>查询当前 Avalonia 有效主题，失败/未知时回退 Dark</summary>
    private static string GetCurrentTheme()
    {
        var app = Avalonia.Application.Current;
        if (app == null) return "dark";

        // 必须用 ActualThemeVariant：它返回解析后的最终主题（Light/Dark）
        // 不可用 RequestedThemeVariant，因为 App.axaml 设的是 Default，
        // 即使系统是 Light 也会返回 Default，导致误判为 dark。
        var variant = app.ActualThemeVariant;
        if (variant == ThemeVariant.Light) return "light";
        if (variant == ThemeVariant.Dark) return "dark";

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
