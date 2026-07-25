using System;
using System.Collections.Generic;
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
    private string? _lastRenderedText;
    private string _htmlContent = "";
    private LocalHtmlServer? _localServer;
    // ====================================================================
    // Static theme management
    // ====================================================================
    private static readonly List<WeakReference<MarkdownView>> _instances = new();
    private static readonly object _lock = new();
    private static bool _themeSubscribed;

    // ====================================================================
    // Bindable Text property — uses Myers diff to avoid redundant re-renders
    // ====================================================================

    static MarkdownView()
    {
        TextProperty.Changed.AddClassHandler<MarkdownView>(OnTextChanged);
    }

    /// <summary>Gets or sets the Markdown text to render.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(
            nameof(Text),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay,
            enableDataValidation: false);

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(MarkdownView sender, AvaloniaPropertyChangedEventArgs e)
    {
        var newText = e.NewValue as string;

        // Myers diff: skip if the new value is semantically identical to the last rendered text.
        // This avoids redundant InvokeScript calls when the binding pushes the same value.
        if (MyersDiff.AreEqual(sender._lastRenderedText, newText))
            return;

        sender._lastRenderedText = newText;
        _ = sender.RenderMarkdownAsync(newText);
    }

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
        _ready = true;

        // Execute synchronously: on Android, NavigationCompleted fires inside
        // onPageFinished(); InvokeScript (evaluateJavascript) must be called
        // synchronously in that context to be reliably delivered.
        PushThemeToWebView(GetCurrentTheme());

        OnReady?.Invoke(this, EventArgs.Empty);

        if (_pendingMarkdown != null)
        {
            var md = _pendingMarkdown;
            _pendingMarkdown = null;
            _ = RenderMarkdownAsync(md);

            // Android WebView may silently drop the first evaluateJavascript
            // call. Schedule a safety retry at the next dispatcher frame.
            _ = Dispatcher.UIThread.InvokeAsync(() => _ = RenderMarkdownAsync(md));
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

// ====================================================================
// Myers diff algorithm — O(ND) shortest edit script
// ====================================================================

/// <summary>
/// Linear-space Myers diff (O(ND) time, O(N) space).
/// Uses the classic "middle snake" divide-and-conquer to compute the
/// shortest edit script between two strings — the same algorithm Git uses.
/// </summary>
internal static class MyersDiff
{
    /// <summary>Returns true if <paramref name="oldText"/> and <paramref name="newText"/> are semantically identical.</summary>
    public static bool AreEqual(string? oldText, string? newText)
    {
        if (ReferenceEquals(oldText, newText)) return true;
        if (oldText is null || newText is null) return false;
        if (oldText.Length != newText.Length) return false;
        return oldText.AsSpan().SequenceEqual(newText.AsSpan());
    }

    /// <summary>Computes the shortest edit script between two strings.</summary>
    public static List<EditOp> Diff(string? oldText, string? newText)
    {
        var result = new List<EditOp>();
        ComputeSes(oldText ?? "", newText ?? "", 0, (oldText ?? "").Length, 0, (newText ?? "").Length, result);
        return result;
    }

    private static void ComputeSes(string a, string b, int loA, int hiA, int loB, int hiB, List<EditOp> result)
    {
        // Trim common prefix
        while (loA < hiA && loB < hiB && a[loA] == b[loB])
        {
            result.Add(new EditOp(OpKind.Equal, b[loB].ToString()));
            loA++; loB++;
        }

        // Trim common suffix
        while (loA < hiA && loB < hiB && a[hiA - 1] == b[hiB - 1])
        {
            hiA--; hiB--;
        }

        var n = hiA - loA;
        var m = hiB - loB;

        if (n == 0 && m == 0) return;

        if (n == 0)
        {
            for (var i = loB; i < hiB; i++)
                result.Add(new EditOp(OpKind.Insert, b[i].ToString()));
            return;
        }

        if (m == 0)
        {
            for (var i = loA; i < hiA; i++)
                result.Add(new EditOp(OpKind.Delete, a[i].ToString()));
            return;
        }

        // Find the middle snake using the Myers algorithm
        var maxD = (n + m + 1) / 2;
        var maxSize = 2 * maxD + 1;
        var offset = maxD;

        var vf = new int[maxSize];  // forward search
        var vb = new int[maxSize];  // backward search

        Array.Fill(vf, -1);
        Array.Fill(vb, -1);

        vf[1 + offset] = loA;
        vb[1 + offset] = hiA;

        var x = loA;
        var y = loB;
        var found = false;

        for (var d = 0; d <= maxD; d++)
        {
            // Forward search
            for (var k = -d; k <= d; k += 2)
            {
                var idx = k + offset;
                x = (k == -d || (k != d && vf[idx - 1] < vf[idx + 1]))
                    ? vf[idx + 1]
                    : vf[idx - 1] + 1;
                y = x - k;

                while (x < hiA && y < hiB && a[x] == b[y])
                {
                    x++; y++;
                }

                vf[idx] = x;

                // Check for overlap with backward search
                var bk = hiA - hiB - k;
                var bidx = bk + offset;
                if (bk >= -(d - 1) && bk <= (d - 1) && bidx >= 0 && bidx < maxSize)
                {
                    if (vb[bidx] != -1 && vf[idx] >= vb[bidx])
                    {
                        found = true;
                        break;
                    }
                }
            }

            if (found) break;

            // Backward search
            for (var k = -d; k <= d; k += 2)
            {
                var idx = k + offset;
                x = (k == -d || (k != d && vb[idx - 1] > vb[idx + 1]))
                    ? vb[idx + 1]
                    : vb[idx - 1] - 1;
                y = x - (hiA - hiB - k);

                while (x > loA && y > loB && a[x - 1] == b[y - 1])
                {
                    x--; y--;
                }

                vb[idx] = x;

                // Check for overlap with forward search
                var fk = k + (hiA - hiB);
                var fidx = fk + offset;
                if (fk >= -(d) && fk <= d && fidx >= 0 && fidx < maxSize)
                {
                    if (vf[fidx] != -1 && vf[fidx] >= vb[idx])
                    {
                        found = true;
                        break;
                    }
                }
            }

            if (found) break;
        }

        // Recurse on left and right of the middle snake
        var midA = x;
        var midB = y;

        ComputeSes(a, b, loA, midA, loB, midB, result);

        // Add the middle snake itself (matching characters)
        while (midA < hiA && midB < hiB && a[midA] == b[midB])
        {
            result.Add(new EditOp(OpKind.Equal, a[midA].ToString()));
            midA++; midB++;
        }

        ComputeSes(a, b, midA, hiA, midB, hiB, result);
    }
}

/// <summary>Type of edit operation in a diff.</summary>
internal enum OpKind { Equal, Insert, Delete }

/// <summary>A single edit operation from a Myers diff.</summary>
internal readonly struct EditOp
{
    public OpKind Kind { get; }
    public string Value { get; }

    public EditOp(OpKind kind, string value)
    {
        Kind = kind;
        Value = value;
    }

    public void Deconstruct(out OpKind kind, out string value)
    {
        kind = Kind;
        value = Value;
    }
}
