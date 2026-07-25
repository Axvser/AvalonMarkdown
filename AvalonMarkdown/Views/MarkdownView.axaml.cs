using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using AvalonMarkdown.Services;

namespace AvalonMarkdown.Views;

/// <summary>
/// Unified Markdown preview control wrapping NativeWebView, providing:
///   • Top toolbar (restart preview, etc.)
///   • Error capture and inline display (instead of silent failure or crash)
/// </summary>
public partial class MarkdownView : UserControl
{
    private NativeWebView _webView = null!;
    private readonly IWebViewSourceProvider _sourceProvider;
    private bool _ready;
    private bool _htmlInjected;
    private string? _pendingMarkdown;
    private string _htmlContent = "";
    private LocalHtmlServer? _localServer;
    private CancellationTokenSource? _loadCts;

    // ====================================================================
    // Static theme management — WeakReference tracks all active instances, reactive theme push
    // ====================================================================
    private static readonly List<WeakReference<MarkdownView>> _instances = new();
    private static readonly object _lock = new();
    private static bool _themeSubscribed;

    // ====================================================================
    // Public events
    // ====================================================================

    /// <summary>Fires when MarkdownView is fully ready (HTML injected + CDN scripts loaded)</summary>
    public event EventHandler? OnReady;

    /// <summary>Fires when a recoverable internal error occurs</summary>
    public event EventHandler<MarkdownViewErrorEventArgs>? ErrorOccurred;

    // ====================================================================
    // Construction
    // ====================================================================

    public MarkdownView()
        : this(new EmbeddedHtmlSourceProvider())
    {
    }

    /// <summary>
    /// Creates MarkdownView with dependency injection, allowing different page sources per platform.
    /// </summary>
    public MarkdownView(IWebViewSourceProvider sourceProvider)
    {
        _sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));

        InitializeComponent();

        CreateWebView();
        WireEvents();

        // Register to static instance list (for reactive theme push)
        RegisterInstance();

        // Query current theme on each construction, not relying on any static cache
        ApplyThemeColors(GetCurrentTheme());

        _ = InitializeWebViewAsync();
    }

    // ====================================================================
    // Layout — Auto size correction
    // ====================================================================

    /// <summary>
    /// NativeWebView (NativeControlHost) has DesiredSize = (0,0) before HWND is created,
    /// causing Auto parent row/column collapse. Ensures at least available size is returned to maintain layout.
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
    // WebView lifecycle
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

        // Subscribe to WebMessageReceived — fires when JS sends a message via
        // chrome.webview.postMessage (WebView2 / WASM) or the native JS bridge.
        // Wrap in try-catch for platforms (e.g. Android) where the event may not
        // be fully supported at runtime despite being present at compile time.
        try
        {
            _webView.WebMessageReceived += OnWebViewMessage;
        }
        catch
        {
            // WebMessageReceived not supported on this platform — silently ignore.
            // The Desktop/Browser paths don't depend on it for SetReady().
        }

        DismissErrorButton.Click += (_, _) => HideError();
        RetryButton.Click += (_, _) => _ = RestartPreviewAsync();

        // Event-driven layout fix: fire once when WebViewHost first gets a valid size
        WebViewHost.EffectiveViewportChanged += OnHostViewportChanged;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            _htmlContent = _sourceProvider.GetHtmlContent();
            _htmlInjected = false;

            if (OperatingSystem.IsBrowser())
            {
                _webView.Source = new Uri("about:blank");
            }
            else
            {
                // Use a local HTTP server (http://127.0.0.1) instead of file:// or data: URIs.
                // YouTube's iframe embed rejects non-http origins with error 153.
                _localServer = new LocalHtmlServer(_htmlContent);
                await _localServer.StartAsync();
                _webView.Source = new Uri(_localServer.BaseUrl);
            }
        }
        catch (Exception ex)
        {
            ShowError("Init failed", ex.Message);
        }

        // Start a monitor that warns the user if the renderer takes too long to become ready.
        // No forced timeouts — the user decides whether to retry.
        StartLoadStuckMonitor();

        await Task.CompletedTask;
    }

    private static bool IsDesktop =>
        Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;

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
            // SetReady() is not called here — it's deferred until the JS [READY]
            // signal from renderer.js. A 20-second monitor will prompt the user
            // if the renderer takes too long to load.
        }
        catch (Exception ex)
        {
            ShowError("Inject failed", ex.Message);
            _htmlInjected = false;
            return;
        }
    }

    private void SetReady()
    {
        if (_ready) return;

        // Cancel the load-stuck monitor — we're ready
        _loadCts?.Cancel();

        _ready = true;

        // Sync theme to WebView JS environment after ready
        PushThemeToWebView(GetCurrentTheme());

        OnReady?.Invoke(this, EventArgs.Empty);

        if (_pendingMarkdown != null)
        {
            var md = _pendingMarkdown;
            _pendingMarkdown = null;
            _ = RenderMarkdownAsync(md);
        }

        // Browser-side iframe needs multiple layout passes to stabilize initial size
        if (OperatingSystem.IsBrowser())
            _ = StabilizeBrowserLayoutAsync();
    }

    /// <summary>
    /// Browser-side deferred layout fix: WASM render pipeline needs multiple frames
    /// to complete initial layout; iframe may get a transient size at creation.
    /// Use incremental delays to repeatedly trigger layout updates for stabilization.
    /// </summary>
    private async Task StabilizeBrowserLayoutAsync()
    {
        try
        {
            for (int i = 0; i < 4; i++)
            {
                await Task.Delay(100 * (i + 1));
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ForceLayout();
                });
            }
        }
        catch
        {
            // Silently — layout fix should not block the main flow
        }
    }

    // ====================================================================
    // Load-stuck monitor — warns the user without forcing ready
    // ====================================================================

    /// <summary>
    /// Starts a 20-second monitor that checks if the renderer has become ready.
    /// If not, shows a user-facing warning suggesting a reload. The user decides.
    /// No forced SetReady() — this is purely advisory.
    /// </summary>
    private void StartLoadStuckMonitor()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                // Wait 20 seconds then check on UI thread
                await Task.Delay(TimeSpan.FromSeconds(20), token);
                if (token.IsCancellationRequested) return;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested || _ready) return;

                    ShowError(
                        "Render Loading",
                        "The renderer is taking longer than expected to load. " +
                        "This may be caused by network issues or CDN blocking. " +
                        "Try ⟳ Retry to reload the preview.");
                });
            }
            catch (OperationCanceledException)
            {
                // Cancelled via SetReady() or page restart — expected
            }
        }, token);
    }

    // ====================================================================
    // Event handling
    // ====================================================================

    private void OnNavigationCompleted(object? sender, EventArgs e)
    {
        if (_htmlInjected || _ready)
            return;

        if (IsDesktop || !OperatingSystem.IsBrowser())
        {
            _htmlInjected = true;
            ForceLayout();
            // NavigationCompleted fires when the page frame has loaded all content,
            // including blocking CDN scripts (markdown-it, highlight.js, mermaid, etc.).
            // The JS [READY] signal from renderer.js serves as secondary confirmation
            // and helps verify the JS→C# bridge is functional.
            SetReady();
        }
        else
        {
            _ = InjectViaDocumentWriteAsync();
        }
    }

    /// <summary>
    /// Receives WebView internal messages (console.log/error/ready/link sent via JS bridge).
    /// The second parameter must be WebMessageReceivedEventArgs (not string) because
    /// NativeWebView.WebMessageReceived uses that delegate type.
    /// </summary>
    public void OnWebViewMessage(object? sender, WebMessageReceivedEventArgs e)
    {
        var message = e.Body;
        if (string.IsNullOrEmpty(message))
            return;

        if (message.StartsWith("[READY]", StringComparison.OrdinalIgnoreCase))
        {
            if (!_ready && _htmlInjected)
            {
                try
                {
                    _ = Dispatcher.UIThread.InvokeAsync(SetReady);
                }
                catch
                {
                    // Ignore — SetReady() happens via NavigationCompleted path
                }
            }
            return;
        }

        if (message.StartsWith("[ERR]", StringComparison.OrdinalIgnoreCase) ||
            message.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase))
        {
            // Don't show error panel proactively, only fire event for external subscription
            ErrorOccurred?.Invoke(this, new MarkdownViewErrorEventArgs("Render error", message));
            return;
        }

        if (message.StartsWith("[LINK]", StringComparison.OrdinalIgnoreCase))
        {
            var url = message[6..].Trim();
            if (!string.IsNullOrEmpty(url))
                _ = OpenUrlInBrowserAsync(url);
            return;
        }
    }

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>Renders Markdown content to the WebView</summary>
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

    /// <summary>Restarts preview (re-navigate / re-inject HTML)</summary>
    public async Task RestartPreviewAsync()
    {
        // Cancel any pending load-stuck monitor from the previous session
        _loadCts?.Cancel();

        _ready = false;
        _htmlInjected = false;
        _pendingMarkdown = null;
        HideError();

        try
        {
            _htmlContent = _sourceProvider.GetHtmlContent();

            if (OperatingSystem.IsBrowser())
            {
                await InjectViaDocumentWriteAsync();
            }
            else
            {
                // Dispose previous server if any, start a new one
                if (_localServer is not null)
                {
                    await _localServer.DisposeAsync();
                    _localServer = null;
                }
                _localServer = new LocalHtmlServer(_htmlContent);
                await _localServer.StartAsync();
                _webView.Source = new Uri(_localServer.BaseUrl);
            }

            // Start a fresh load-stuck monitor for the new session
            StartLoadStuckMonitor();
        }
        catch (Exception ex)
        {
            ShowError("Restart failed", ex.Message);
        }
    }

    /// <summary>Event-driven layout fix: sync iframe layout when WebViewHost first gets a valid size</summary>
    private void OnHostViewportChanged(object? sender, Avalonia.Layout.EffectiveViewportChangedEventArgs e)
    {
        // Only fire on first valid size
        if (e.EffectiveViewport.Width <= 0 || e.EffectiveViewport.Height <= 0)
            return;

        WebViewHost.EffectiveViewportChanged -= OnHostViewportChanged;

        if (_webView != null)
        {
            _webView.InvalidateMeasure();
            _webView.InvalidateArrange();
        }
    }

    /// <summary>Fallback: invalidate layout immediately after HTML injection, event-driven will process next frame</summary>
    private void ForceLayout()
    {
        WebViewHost.InvalidateMeasure();
        WebViewHost.InvalidateArrange();
        _webView?.InvalidateMeasure();
        _webView?.InvalidateArrange();
    }

    /// <summary>Apply preview configuration (JS call)</summary>
    public async Task ApplyConfigAsync(string jsCallExpression)
    {
        if (!_ready) return;
        await InvokeScriptSafeAsync(jsCallExpression);
    }

    /// <summary>Execute custom JavaScript</summary>
    public async Task<string?> InvokeScriptAsync(string script)
    {
        if (!_ready) return null;
        return await InvokeScriptSafeAsync(script);
    }

    /// <summary>
    /// Replace the renderer's built-in CSS with a custom stylesheet generated
    /// by <c>ThemeConfigViewModel.GenerateCss()</c>. The CSS text is injected
    /// into a <c>&lt;style id="custom-theme-css"&gt;</c> element in the WebView's
    /// document head, overriding the default theme rules.
    /// </summary>
    /// <param name="css">
    /// Complete CSS text using the exact same selector/variable naming as the
    /// built-in renderer.css.
    /// </param>
    public async Task ApplyCustomCssAsync(string css)
    {
        if (!_ready) return;
        var escaped = EscapeJsString(css);
        await InvokeScriptSafeAsync($"setCustomCss('{escaped}')");
    }

    // ====================================================================
    // Internal helper methods
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

    /// <summary>
    /// Opens a URL in the operating system's default browser.
    /// Uses <see cref="TopLevel.Launcher"/> (cross-platform: desktop, Android, iOS).
    /// </summary>
    private async Task OpenUrlInBrowserAsync(string url)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Launcher is { } launcher)
            {
                await launcher.LaunchUriAsync(new Uri(url));
            }
        }
        catch (Exception ex)
        {
            ShowError("Open Link", $"Failed to open {url}: {ex.Message}");
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
    // Static theme management — registration / push
    // ====================================================================

    /// <summary>Register current instance to static weak-reference list</summary>
    private void RegisterInstance()
    {
        lock (_lock)
        {
            // Clean up collected instances
            _instances.RemoveAll(wr => !wr.TryGetTarget(out _));
            _instances.Add(new WeakReference<MarkdownView>(this));

            // Subscribe to global theme change on first instantiation (only once)
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

    /// <summary>Global theme change callback — push to all alive MarkdownView instances</summary>
    private static void OnGlobalThemeChanged(object? sender, EventArgs e)
    {
        var theme = GetCurrentTheme();

        lock (_lock)
        {
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                if (_instances[i].TryGetTarget(out var view))
                {
                    // Dispatch asynchronously to UI thread, do not block theme event
                    _ = Dispatcher.UIThread.InvokeAsync(() => view.ApplyThemePush(theme));
                }
                else
                {
                    // Instance GC'd, remove
                    _instances.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>Receive theme push: refresh background + send message to WebView JS</summary>
    private async void ApplyThemePush(string theme)
    {
        ApplyThemeColors(theme);
        if (_ready)
            await InvokeScriptSafeAsync($"setTheme('{theme}')");
    }

    /// <summary>Sync current theme to the ready WebView JS</summary>
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

    /// <summary>Query current Avalonia effective theme, fallback to Dark on failure/unknown</summary>
    private static string GetCurrentTheme()
    {
        var app = Avalonia.Application.Current;
        if (app == null) return "dark";

        // Must use ActualThemeVariant: it returns the resolved final theme (Light/Dark).
        // Do not use RequestedThemeVariant because App.axaml sets Default,
        // which would return Default even when the system is Light, causing misdetection as dark.
        var variant = app.ActualThemeVariant;
        if (variant == ThemeVariant.Light) return "light";
        if (variant == ThemeVariant.Dark) return "dark";

        return "dark";
    }

    /// <summary>
    /// Clean up the local HTTP server when the control is removed from the visual tree.
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_localServer is not null)
        {
            _ = _localServer.DisposeAsync();
            _localServer = null;
        }
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
// Error event args
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
