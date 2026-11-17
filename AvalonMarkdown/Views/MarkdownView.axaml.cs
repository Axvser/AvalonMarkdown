using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private bool _initialized;
    private string? _pendingMarkdown;
    private string? _lastRenderedText;
    private string _htmlContent = "";
    private LocalHtmlServer? _localServer;

    // Renders are asynchronous on Android/iOS, so a burst of Text changes can
    // overlap. Requests are serialised here and stale ones are dropped by id —
    // otherwise an older render can land after a newer one and show old content.
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private long _renderRequestId;

    // Incremented whenever the native view is torn down; work started under an
    // older generation must not resurrect a preview that has been detached.
    private int _lifecycleGeneration;
    private bool _initInProgress;
    private volatile bool _initFailed;
    private volatile bool _detached;
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
        PlaceholderProperty.Changed.AddClassHandler<MarkdownView>(OnPlaceholderChanged);
        EnableContextMenuProperty.Changed.AddClassHandler<MarkdownView>(OnEnableContextMenuChanged);
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

    /// <summary>
    /// Whether the preview page has loaded and is accepting render calls. An
    /// editor that attaches after the preview became ready can check this instead
    /// of waiting for another <see cref="OnReady"/> event that will never fire.
    /// </summary>
    public bool IsReady => _ready;

    /// <summary>
    /// Text shown when the preview has no content (like a TextBox placeholder).
    /// Defaults to empty → the pane is blank. Changing it re-applies to the renderer.
    /// </summary>
    public static readonly StyledProperty<string?> PlaceholderProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Placeholder));

    public string? Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>
    /// Whether right-clicking a Mermaid diagram, a function plot or a PlantUML image
    /// offers "Save as SVG / PNG" (and, on touch, a long press). Only those three
    /// members are intercepted — the platform's own context menu keeps working
    /// everywhere else. Defaults to enabled; set false to give the platform menu
    /// back across the whole preview.
    /// </summary>
    public static readonly StyledProperty<bool> EnableContextMenuProperty =
        AvaloniaProperty.Register<MarkdownView, bool>(nameof(EnableContextMenu), defaultValue: true);

    public bool EnableContextMenu
    {
        get => GetValue(EnableContextMenuProperty);
        set => SetValue(EnableContextMenuProperty, value);
    }

    private static void OnEnableContextMenuChanged(MarkdownView sender, AvaloniaPropertyChangedEventArgs e)
    {
        _ = sender.PushContextMenuAsync();
    }

    private async Task PushContextMenuAsync()
    {
        if (!_ready) return;   // SetReady() pushes it once the preview is live
        try
        {
            var flag = EnableContextMenu ? "true" : "false";
            await ApplyConfigAsync($"setPreviewConfig({{ contextMenuEnabled: {flag} }})");
        }
        catch (Exception ex)
        {
            ShowError("Context Menu", ex.Message);
        }
    }

    private static void OnPlaceholderChanged(MarkdownView sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender._ready)
            _ = sender.PushPlaceholderAsync();
    }

    private async Task PushPlaceholderAsync()
    {
        await InvokeScriptSafeAsync($"setPreviewConfig({{ placeholder: {MarkdownJs.StringLiteral(Placeholder)} }})");
    }

    private static void OnTextChanged(MarkdownView sender, AvaloniaPropertyChangedEventArgs e)
    {
        var newText = e.NewValue as string;

        // Skip if the new value is semantically identical to the last text that
        // ACTUALLY rendered. _lastRenderedText is only advanced on success, so a
        // failed render is retried rather than being remembered as rendered.
        if (MyersDiff.AreEqual(sender._lastRenderedText, newText))
            return;

        _ = sender.RenderMarkdownAsync(newText);
    }

    // ====================================================================
    // Public events
    // ====================================================================

    /// <summary>Fires when MarkdownView is fully ready (HTML injected + CDN scripts loaded)</summary>
    public event EventHandler? OnReady;

    /// <summary>Fires when a recoverable internal error occurs</summary>
    public event EventHandler<MarkdownViewErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// Fires when a hyperlink inside the preview is clicked. This is the single
    /// navigation pipeline for all links — subscribe to intercept and handle links
    /// yourself (e.g. in-app routing, internal pages, custom schemes). Set
    /// <see cref="MarkdownLinkClickedEventArgs.Handled"/> to true to suppress the
    /// default system-browser launch.
    /// </summary>
    public event EventHandler<MarkdownLinkClickedEventArgs>? LinkClicked;

    /// <summary>
    /// Fires for every console message emitted by the WebView's JavaScript
    /// (log / warn / error / unhandled rejection). Useful for diagnostics —
    /// fatal JS errors also surface through <see cref="ErrorOccurred"/>.
    /// </summary>
    public event EventHandler<MarkdownConsoleMessageEventArgs>? ConsoleMessage;

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
    }

    // WebView initialization is deferred until the first call to RenderMarkdownAsync,
    // ensuring the control has finished layout with its final size before the Browser
    // iframe is created. See RenderMarkdownAsync for the trigger logic.

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
            // Read-only preview: never take Avalonia focus. When the WebView2 controller
            // isn't ready, WebView2BaseAdapter.Focus() → ICoreWebView2Controller.MoveFocus
            // throws E_INVALIDARG (0x80070057) — e.g. focus restoration on window activation.
            // The native control manages its own focus when clicked.
            Focusable = false,
            // Keep hidden until first markdown text triggers initialization.
            // This avoids creating the Browser iframe at (0,0) before the
            // control has finished layout with its final size.
            IsVisible = false,
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
        if (_initInProgress || _ready)
            return;

        _initInProgress = true;
        _initFailed = false;

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
                var oldServer = Interlocked.Exchange(ref _localServer, null);
                if (oldServer is not null)
                {
                    try { await oldServer.DisposeAsync(); } catch { /* teardown is best effort */ }
                }

                var server = new LocalHtmlServer(_htmlContent);
                await server.StartAsync();
                _localServer = server;
                _webView.Source = new Uri(server.BaseUrl);
            }
        }
        catch (Exception ex)
        {
            _initFailed = true;
            _initialized = false;   // a later Text change / retry may try again
            _pendingMarkdown ??= Text;
            ShowError("Init failed", ex.Message);
        }
        finally
        {
            _initInProgress = false;
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
            // JSON-encode the page as a real JavaScript string literal. The old
            // hand-rolled escaper missed U+2028/U+2029 and any control character,
            // which could either break the expression or inject code.
            var script = $"document.open();document.write({MarkdownJs.StringLiteral(_htmlContent)});document.close();";
            var result = _webView.InvokeScript(script);
            if (result is Task t)
                await t.WaitAsync(TimeSpan.FromSeconds(5));

            ForceLayout();
        }
        catch (Exception ex)
        {
            ShowError("Inject failed", ex.Message);
            _htmlInjected = false;
            _initFailed = true;   // a later render may retry the whole injection
            return;
        }
    }

    private void SetReady()
    {
        if (_ready) return;
        _ready = true;
        _initFailed = false;

        // Reveal the WebView now that initialization has completed successfully
        // and the iframe has its final dimensions.
        _webView.IsVisible = true;

        // Execute synchronously: on Android, NavigationCompleted fires inside
        // onPageFinished(); InvokeScript (evaluateJavascript) must be called
        // synchronously in that context to be reliably delivered.
        PushThemeToWebView(GetCurrentTheme());
        _ = PushPlaceholderAsync();
        _ = PushContextMenuAsync();

        // Render any content that arrived before the preview was ready FIRST, so
        // that OnReady subscribers setting new Text (rendered afterwards) win.
        if (_pendingMarkdown != null)
        {
            var md = _pendingMarkdown;
            _pendingMarkdown = null;
            _ = RenderMarkdownAsync(md);

            // Android WebView may silently drop the first evaluateJavascript call,
            // so it gets a safety retry at the next dispatcher frame. Only retry if
            // the bound Text is still this document — otherwise the retry would
            // clobber content an OnReady subscriber has already supplied.
            if (OperatingSystem.IsAndroid())
                _ = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_detached && _ready && MyersDiff.AreEqual(Text, md))
                        _ = RenderMarkdownAsync(md);
                });
        }

        RaiseOnReady();

        // Browser-side iframe needs multiple layout passes to stabilize initial size
        if (OperatingSystem.IsBrowser())
            _ = StabilizeBrowserLayoutAsync(Volatile.Read(ref _lifecycleGeneration));
    }

    /// <summary>
    /// Browser-side deferred layout fix: WASM render pipeline needs multiple frames
    /// to complete initial layout; iframe may get a transient size at creation.
    /// Use incremental delays to repeatedly trigger layout updates for stabilization.
    /// </summary>
    private async Task StabilizeBrowserLayoutAsync(int generation)
    {
        try
        {
            for (int i = 0; i < 4; i++)
            {
                await Task.Delay(100 * (i + 1));
                if (_detached || generation != Volatile.Read(ref _lifecycleGeneration))
                    return;
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

    private async void OnNavigationCompleted(object? sender, EventArgs e)
    {
        var generation = Volatile.Read(ref _lifecycleGeneration);

        try
        {
            if (_detached || _htmlInjected || _ready)
                return;

            if (IsDesktop || !OperatingSystem.IsBrowser())
            {
                _htmlInjected = true;
                ForceLayout();

                if (IsDesktop)
                {
                    // Desktop (WebView2): NavigationCompleted fires after the page frame
                    // and all blocking CDN scripts have loaded — safe to SetReady immediately.
                    SetReady();
                }
                else
                {
                    // Android/iOS: NavigationCompleted may fire before JS CDN scripts
                    // (markdown-it, highlight.js, mermaid, etc.) have finished loading.
                    // Wait for JS readiness before signaling that the preview is ready.
                    await WaitForJsReadyAsync(generation);
                    if (!_detached && generation == Volatile.Read(ref _lifecycleGeneration))
                        SetReady();
                }
            }
            else
            {
                await InjectViaDocumentWriteAsync();
            }
        }
        catch (Exception ex)
        {
            // async void: an exception here would terminate the process.
            ShowError("Navigation", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Receives WebView internal messages (console.log/error/ready/link sent via JS bridge).
    /// The second parameter must be WebMessageReceivedEventArgs (not string) because
    /// NativeWebView.WebMessageReceived uses that delegate type.
    /// </summary>
    public void OnWebViewMessage(object? sender, WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.Body;
            if (string.IsNullOrEmpty(message))
                return;

            if (message.StartsWith("[READY]", StringComparison.OrdinalIgnoreCase))
            {
                if (!_ready && _htmlInjected && !_detached)
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

            if (message.StartsWith("[LOG]", StringComparison.OrdinalIgnoreCase) ||
                message.StartsWith("[WARN]", StringComparison.OrdinalIgnoreCase) ||
                message.StartsWith("[ERR]", StringComparison.OrdinalIgnoreCase) ||
                message.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                // Surface every JS console message for diagnostics, capped so one
                // runaway stack trace cannot bloat the control or the event args.
                var close = message.IndexOf(']');
                var level = close > 0 ? message[1..close].ToUpperInvariant() : "LOG";
                var text = close > 0 ? message[(close + 1)..].Trim() : message;
                text = Truncate(text, 16 * 1024);

                RaiseConsoleMessage(level, text);

                // Fatal JS errors also surface through ErrorOccurred (no error panel).
                if (level is "ERR" or "ERROR")
                    RaiseErrorOccurred("Render error", text);
                return;
            }

            if (message.StartsWith("[LINK]", StringComparison.OrdinalIgnoreCase))
            {
                var url = message[6..].Trim();
                if (!string.IsNullOrEmpty(url))
                    _ = HandleLinkClickedAsync(url);
                return;
            }

            if (message.StartsWith("[EXPORT]", StringComparison.OrdinalIgnoreCase))
            {
                var json = message[8..].Trim();
                if (!string.IsNullOrEmpty(json))
                    _ = HandleExportAsync(json);
                return;
            }
        }
        catch (Exception ex)
        {
            // A platform callback must never throw back into the WebView host.
            Debug.WriteLine($"[AvalonMarkdown] OnWebViewMessage failed: {ex}");
        }
    }

    // ====================================================================
    // Image export (right-click a diagram / plot / PlantUML image)
    // ====================================================================

    /// <summary>
    /// Write an image the preview prepared. <c>encoding</c> drives ONE branch on
    /// purpose — "utf8" for SVG markup, "base64" for PNG bytes — because two
    /// separate field names would eventually be mismatched.
    /// </summary>
    private async Task HandleExportAsync(string json)
    {
        // The renderer caps PNG at ~700 KB, but an SVG is text and a hostile or
        // buggy document could make it enormous. Bound it before parsing.
        const int MaxPayloadChars = 32 * 1024 * 1024;
        if (json.Length > MaxPayloadChars)
        {
            ShowError("Save Image", "The export payload was too large.");
            return;
        }

        ExportPayload? payload;
        try
        {
            // Source-generated metadata: reflection is disabled on Browser/WASM.
            payload = JsonSerializer.Deserialize(json, MarkdownJsonContext.Default.ExportPayload);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            ShowError("Save Image", $"Malformed export payload: {ex.Message}");
            return;
        }

        if (payload?.Data is null || string.IsNullOrEmpty(payload.Name))
        {
            ShowError("Save Image", "The export payload was empty.");
            return;
        }

        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null)
            {
                ShowError("Save Image", "No storage provider is available on this platform.");
                return;
            }

            var isPng = string.Equals(payload.Kind, "png", StringComparison.OrdinalIgnoreCase);
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save image",
                SuggestedFileName = MarkdownExport.SanitizeFileName(payload.Name),
                DefaultExtension = isPng ? "png" : "svg",
                FileTypeChoices = new[]
                {
                    isPng
                        ? new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } }
                        : new FilePickerFileType("SVG image") { Patterns = new[] { "*.svg" } }
                }
            });
            if (file is null) return;   // the user cancelled

            byte[] bytes;
            if (string.Equals(payload.Encoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    bytes = Convert.FromBase64String(payload.Data);
                }
                catch (FormatException)
                {
                    ShowError("Save Image", "The image data was not valid base64.");
                    return;
                }
            }
            else
            {
                // BOM-less UTF-8, to agree with the XML prolog the SVG carries.
                bytes = new UTF8Encoding(false).GetBytes(payload.Data);
            }

            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            ShowError("Save Image", ex.Message);
        }
    }

    // ====================================================================
    // JS readiness polling (Android / iOS)
    // ====================================================================

    /// <summary>
    /// Polls the WebView JS environment until <c>window.renderMarkdown</c> is available,
    /// or a 15-second timeout is reached. Used on Android/iOS where NavigationCompleted
    /// may fire before CDN scripts (markdown-it, highlight.js, etc.) finish loading.
    /// </summary>
    private async Task WaitForJsReadyAsync(int generation)
    {
        const int maxRetries = 75; // ~15 seconds at 200ms intervals
        for (var i = 0; i < maxRetries; i++)
        {
            if (_detached || generation != Volatile.Read(ref _lifecycleGeneration))
                return;
            if (await CheckJsReadyAsync())
                return;
            await Task.Delay(200);
        }

        if (_detached || generation != Volatile.Read(ref _lifecycleGeneration))
            return;

        // Timeout: JS didn't become ready — show error but still allow SetReady
        // so the preview doesn't hang forever.
        ShowError("JS Ready Timeout",
            "CDN scripts failed to load after 15s. Check network or tracking protection.");
    }

    /// <summary>
    /// Checks if <c>window.renderMarkdown</c> is available in the WebView JS context.
    /// Handles both Desktop (synchronous string result) and Android (Task&lt;string?&gt; via evaluateJavascript).
    ///
    /// IMPORTANT: The JS expression must return a <b>non-string</b> value (number or boolean),
    /// because Android's evaluateJavascript JSON-encodes string results (adding quotes),
    /// which would make a simple equality check like <c>value == "1"</c> always fail.
    /// Numbers and booleans JSON-encode without extra quoting.
    /// </summary>
    private async Task<bool> CheckJsReadyAsync()
    {
        try
        {
            // Use number 1/0 instead of string '1'/''
            // to avoid JSON encoding adding quotes on Android's evaluateJavascript.
            var script = "typeof window.renderMarkdown === 'function' ? 1 : 0";
            var result = _webView.InvokeScript(script);

            if (result is Task<string?> task)
            {
                // Android: InvokeScript returns Task<string?> via evaluateJavascript.
                // evaluateJavascript JSON-encodes the result; for numbers the encoding
                // is just the bare number string (no extra quotes).
                var value = await task.WaitAsync(TimeSpan.FromSeconds(3));
                return string.Equals(value?.Trim(), "1", StringComparison.Ordinal);
            }

            if (result is Task t)
            {
                // Unknown platform with Task return — await and assume ready
                await t.WaitAsync(TimeSpan.FromSeconds(3));
                return true;
            }

            // Desktop (WebView2): InvokeScript returns the string directly.
            // ExecuteScriptAsync also returns a JSON-encoded result.
            return string.Equals(result?.ToString()?.Trim(), "1", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>Renders Markdown content to the WebView</summary>
    public async Task RenderMarkdownAsync(string? markdown)
    {
        if (_detached)
        {
            _pendingMarkdown = markdown;
            return;
        }

        // Defer WebView initialization until the first markdown text arrives.
        // By this point the control has completed layout with its final size,
        // so the Browser iframe is never created at (0,0).
        if (!_initialized)
        {
            _initialized = true;
            _pendingMarkdown = markdown;
            await InitializeWebViewAsync();
            return;
        }

        if (!_ready)
        {
            _pendingMarkdown = markdown;

            // A previous initialization failed. A later Text change is the natural
            // retry signal — without this the control would wait for a readiness
            // event that can never arrive.
            if (_initFailed && !_initInProgress)
            {
                _initialized = true;
                await InitializeWebViewAsync();
            }
            return;
        }

        var id = Interlocked.Increment(ref _renderRequestId);

        await _renderGate.WaitAsync().ConfigureAwait(true);
        try
        {
            // A newer request arrived while this one was queued: it owns the DOM.
            if (id != Volatile.Read(ref _renderRequestId))
                return;

            var ok = await RenderCoreAsync(markdown).ConfigureAwait(true);

            if (id == Volatile.Read(ref _renderRequestId))
            {
                // Only a render that actually reached the page counts. On failure,
                // leaving this null lets the same Text be retried instead of being
                // short-circuited by MyersDiff.AreEqual forever.
                _lastRenderedText = ok ? (markdown ?? string.Empty) : null;
            }
        }
        finally
        {
            _renderGate.Release();
        }
    }

    private async Task<bool> RenderCoreAsync(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            // Empty is a real state: it clears data-source and shows the placeholder.
            var (emptyOk, _) = await InvokeScriptCoreAsync("renderMarkdown('')").ConfigureAwait(true);
            return emptyOk;
        }

        var script = $"renderMarkdown({MarkdownJs.StringLiteral(markdown)})";
        var (ok, _) = await InvokeScriptCoreAsync(script).ConfigureAwait(true);
        return ok;
    }

    /// <summary>Restarts preview (re-navigate / re-inject HTML)</summary>
    public async Task RestartPreviewAsync()
    {
        HideError();
        _ready = false;
        _htmlInjected = false;
        _initFailed = false;
        _initialized = true;   // restart is itself an initialization

        // Invalidate in-flight renders: their results must not be applied to the
        // fresh page, and the current document should be re-rendered once it is up.
        Interlocked.Increment(ref _renderRequestId);
        var current = Text;
        _pendingMarkdown = current;
        _lastRenderedText = null;

        try
        {
            _htmlContent = _sourceProvider.GetHtmlContent();

            if (OperatingSystem.IsBrowser())
            {
                await InjectViaDocumentWriteAsync();
            }
            else
            {
                // Dispose the previous server BEFORE binding a new one; otherwise
                // a restart that runs twice would leak listeners.
                var oldServer = Interlocked.Exchange(ref _localServer, null);
                if (oldServer is not null)
                {
                    try { await oldServer.DisposeAsync(); } catch { /* teardown is best effort */ }
                }

                var server = new LocalHtmlServer(_htmlContent);
                await server.StartAsync();
                _localServer = server;
                _webView.Source = new Uri(server.BaseUrl);
            }
        }
        catch (Exception ex)
        {
            _initFailed = true;
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
    public async Task ApplyConfigAsync(string? jsCallExpression)
    {
        if (!_ready || string.IsNullOrWhiteSpace(jsCallExpression)) return;
        await InvokeScriptSafeAsync(jsCallExpression);
    }

    /// <summary>
    /// Scrolls the preview to a vertical progress position (0–100) of the document.
    /// Useful for editor↔preview scroll sync or "jump to 30% of the page". Call after
    /// the preview is ready (see <see cref="OnReady"/>). Progress outside [0,100] is clamped.
    /// </summary>
    public async Task ScrollToProgressAsync(double percent)
    {
        if (!_ready) return;
        // Math.Clamp propagates NaN, and "NaN" is not a useful script argument.
        if (!double.IsFinite(percent)) percent = 0;
        var p = Math.Clamp(percent, 0, 100);
        await InvokeScriptSafeAsync($"setScrollProgress({p.ToString("0.##", CultureInfo.InvariantCulture)})");
    }

    /// <summary>
    /// Returns the current vertical scroll progress (0–100) of the document, or
    /// <c>null</c> if the preview is not ready / the value can't be read.
    /// </summary>
    public async Task<double?> GetScrollProgressAsync()
    {
        if (!_ready) return null;
        var result = await InvokeScriptAsync("getScrollProgress()");
        if (result is null) return null;
        if (double.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) &&
            double.IsFinite(v))
            return Math.Clamp(v, 0, 100);
        return null;
    }

    /// <summary>
    /// Scrolls the preview so the heading whose rendered text matches
    /// <paramref name="headingText"/> is at the top (exact match first, then prefix).
    /// Returns <c>true</c> if a heading was found and scrolled to. Combine with
    /// <see cref="GetHeadingsAsync"/> to build a table of contents.
    /// </summary>
    public async Task<bool> NavigateToHeadingAsync(string? headingText)
    {
        if (!_ready || string.IsNullOrWhiteSpace(headingText)) return false;
        var result = await InvokeScriptAsync($"navigateToHeading({MarkdownJs.StringLiteral(headingText.Trim())})");
        return string.Equals(result?.Trim(), "1", StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns all headings in the preview (rendered text + level) in document order,
    /// for building a table of contents. Returns an empty list if not ready.
    /// </summary>
    public async Task<IReadOnlyList<MarkdownHeading>> GetHeadingsAsync()
    {
        if (!_ready) return Array.Empty<MarkdownHeading>();
        var result = await InvokeScriptAsync("getHeadings()");
        if (string.IsNullOrEmpty(result)) return Array.Empty<MarkdownHeading>();
        try
        {
            return JsonSerializer.Deserialize(result, MarkdownJsonContext.Default.ListMarkdownHeading)
                   ?? new List<MarkdownHeading>();
        }
        catch (JsonException)
        {
            return Array.Empty<MarkdownHeading>();
        }
    }

    /// <summary>Execute custom JavaScript</summary>
    public async Task<string?> InvokeScriptAsync(string? script)
    {
        if (!_ready || string.IsNullOrWhiteSpace(script)) return null;
        return await InvokeScriptSafeAsync(script);
    }

    /// <summary>
    /// Replace the renderer's built-in CSS with a custom stylesheet generated
    /// by <c>MarkdownThemeViewModel.GenerateCss()</c>. The CSS text is injected
    /// into a <c>&lt;style id="custom-theme-css"&gt;</c> element in the WebView's
    /// document head, overriding the default theme rules.
    /// </summary>
    /// <param name="css">
    /// Complete CSS text using the exact same selector/variable naming as the
    /// built-in renderer.css.
    /// </param>
    public async Task ApplyCustomCssAsync(string? css)
    {
        if (!_ready) return;
        await InvokeScriptSafeAsync($"setCustomCss({MarkdownJs.StringLiteral(css ?? string.Empty)})");
    }

    // ====================================================================
    // Internal helper methods
    // ====================================================================

    private async Task<string?> InvokeScriptSafeAsync(string script)
    {
        var (_, value) = await InvokeScriptCoreAsync(script).ConfigureAwait(true);
        return value;
    }

    /// <summary>
    /// One script invocation with a uniform failure report. Returns <c>Ok=false</c>
    /// for a timeout or a thrown platform exception, which callers use to decide
    /// whether a render actually reached the page.
    /// </summary>
    private async Task<(bool Ok, string? Value)> InvokeScriptCoreAsync(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return (false, null);

        try
        {
            var result = _webView.InvokeScript(script);
            if (result is Task<string?> taskString)
            {
                // Android: evaluateJavascript returns Task<string?>. Extract the value
                // so callers like GetScrollProgressAsync work cross-platform.
                return (true, await taskString.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true));
            }
            if (result is Task t)
            {
                await t.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                return (true, null);
            }
            return (true, result?.ToString());
        }
        catch (TimeoutException)
        {
            ShowError("Timeout", $"Script >5s: {Truncate(script, 80)}");
            return (false, null);
        }
        catch (OperationCanceledException)
        {
            // The task was cancelled by the platform rather than by our deadline.
            ShowError("Timeout", $"Script >5s: {Truncate(script, 80)}");
            return (false, null);
        }
        catch (Exception ex)
        {
            ShowError("Script error", $"{ex.GetType().Name}: {ex.Message}");
            return (false, null);
        }
    }

    /// <summary>
    /// Routes a clicked link through the <see cref="LinkClicked"/> event — the single
    /// navigation pipeline. If no subscriber marks it handled, falls back to the
    /// system browser so existing behavior is preserved.
    /// </summary>
    private async Task HandleLinkClickedAsync(string url)
    {
        var args = new MarkdownLinkClickedEventArgs(url);
        if (LinkClicked is { } handlers)
        {
            foreach (EventHandler<MarkdownLinkClickedEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, args);
                }
                catch (Exception ex)
                {
                    // A buggy subscriber must not swallow the navigation pipeline
                    // (or crash the platform's click callback).
                    Debug.WriteLine($"[AvalonMarkdown] LinkClicked subscriber threw: {ex}");
                }
            }
        }
        if (args.Handled) return;

        await OpenUrlInBrowserAsync(url);
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
        title = Truncate(title, 200);
        message = Truncate(message, 4 * 1024);

        // Errors can surface from a continuation or a platform callback. Marshal
        // once so touching the visual tree is always safe.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ShowError(title, message));
            return;
        }

        ErrorTitle.Text = title;
        ErrorMessage.Text = message;
        ErrorPanel.IsVisible = true;
        RaiseErrorOccurred(title, message);
    }

    private void HideError()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(HideError);
            return;
        }

        ErrorPanel.IsVisible = false;
        ErrorTitle.Text = "";
        ErrorMessage.Text = "";
    }

    private static string Truncate(string? text, int max)
    {
        text ??= string.Empty;
        return text.Length <= max ? text : text[..max] + "…";
    }

    // ── Guarded event raising ──────────────────────────────────────────────
    // These events are raised from platform callbacks (async void paths). One
    // throwing subscriber would otherwise propagate into the host and can take
    // down the process, so every handler is isolated.

    private void RaiseOnReady()
    {
        if (OnReady is not { } handlers) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex) { Debug.WriteLine($"[AvalonMarkdown] OnReady subscriber threw: {ex}"); }
        }
    }

    private void RaiseErrorOccurred(string title, string message)
    {
        if (ErrorOccurred is not { } handlers) return;
        var args = new MarkdownViewErrorEventArgs(title, message);
        foreach (EventHandler<MarkdownViewErrorEventArgs> handler in handlers.GetInvocationList())
        {
            try { handler(this, args); }
            catch (Exception ex) { Debug.WriteLine($"[AvalonMarkdown] ErrorOccurred subscriber threw: {ex}"); }
        }
    }

    private void RaiseConsoleMessage(string level, string text)
    {
        if (ConsoleMessage is not { } handlers) return;
        var args = new MarkdownConsoleMessageEventArgs(level, text);
        foreach (EventHandler<MarkdownConsoleMessageEventArgs> handler in handlers.GetInvocationList())
        {
            try { handler(this, args); }
            catch (Exception ex) { Debug.WriteLine($"[AvalonMarkdown] ConsoleMessage subscriber threw: {ex}"); }
        }
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
        try
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
        catch (Exception ex)
        {
            Debug.WriteLine($"[AvalonMarkdown] Theme push failed: {ex}");
        }
    }

    /// <summary>Receive theme push: refresh background + send message to WebView JS</summary>
    private async void ApplyThemePush(string theme)
    {
        try
        {
            ApplyThemeColors(theme);
            if (_ready)
                await InvokeScriptSafeAsync($"setTheme({MarkdownJs.StringLiteral(theme)})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AvalonMarkdown] ApplyThemePush failed: {ex}");
        }
    }

    /// <summary>Sync current theme to the ready WebView JS</summary>
    private async void PushThemeToWebView(string theme)
    {
        try
        {
            if (_ready)
                await InvokeScriptSafeAsync($"setTheme({MarkdownJs.StringLiteral(theme)})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AvalonMarkdown] PushThemeToWebView failed: {ex}");
        }
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
    /// The WebView itself is kept so a re-attached control can navigate again, but
    /// the preview state is reset: any pending initialization/render belongs to the
    /// old visual tree and must not resurrect a detached control.
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _detached = true;
        Interlocked.Increment(ref _lifecycleGeneration);
        Interlocked.Increment(ref _renderRequestId);

        _ready = false;
        _htmlInjected = false;
        _initialized = false;
        _pendingMarkdown = Text;
        _lastRenderedText = null;

        var server = Interlocked.Exchange(ref _localServer, null);
        if (server is not null)
        {
            _ = Task.Run(async () =>
            {
                try { await server.DisposeAsync(); } catch { /* teardown is best effort */ }
            });
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (!_detached)
            return;

        _detached = false;
        Interlocked.Increment(ref _lifecycleGeneration);

        // Recreate the preview if there is content to show. Without this a control
        // that is detached and re-attached (tab switch, window re-parenting) stays
        // blank forever.
        if (!string.IsNullOrEmpty(Text))
            _ = RenderMarkdownAsync(Text);
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

/// <summary>
/// Event arguments for <see cref="MarkdownView.LinkClicked"/> — raised for every
/// hyperlink the user clicks inside the preview. The default (unhandled) behavior
/// opens the URL in the system browser; set <see cref="Handled"/> to true to take
/// over navigation (e.g. route to an in-app page).
/// </summary>
public class MarkdownLinkClickedEventArgs : EventArgs
{
    /// <summary>The target URL of the clicked link (may be relative or a custom scheme).</summary>
    public string Url { get; }

    /// <summary>
    /// Set to true inside a handler to indicate the link was handled and suppress
    /// the default system-browser launch.
    /// </summary>
    public bool Handled { get; set; }

    public MarkdownLinkClickedEventArgs(string url)
    {
        Url = url;
    }
}

/// <summary>
/// Event arguments for <see cref="MarkdownView.ConsoleMessage"/> — a console message
/// emitted by the WebView's JavaScript. <see cref="Level"/> is one of
/// <c>LOG</c> / <c>WARN</c> / <c>ERR</c>.
/// </summary>
public class MarkdownConsoleMessageEventArgs : EventArgs
{
    /// <summary>The console level: LOG, WARN, or ERR.</summary>
    public string Level { get; }

    /// <summary>The message text emitted by JavaScript.</summary>
    public string Message { get; }

    public MarkdownConsoleMessageEventArgs(string level, string message)
    {
        Level = level;
        Message = message;
    }
}

/// <summary>A heading in the rendered preview, as returned by <see cref="MarkdownView.GetHeadingsAsync"/>.</summary>
public class MarkdownHeading
{
    /// <summary>The rendered heading text (inline markdown stripped).</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>The heading level (1–6).</summary>
    [JsonPropertyName("level")]
    public int Level { get; set; }
}

// ====================================================================
// Myers diff algorithm — shortest edit script (bounded trace)
// ====================================================================

/// <summary>
/// Greedy Myers diff: O(ND) time with a trace table that is bounded for very
/// large inputs, falling back to a correct (non-minimal) script rather than
/// risking unbounded memory. The render pipeline itself only uses
/// <see cref="AreEqual"/>.
/// </summary>
internal static class MyersDiff
{
    // The greedy implementation below stores one trace row per edit distance.
    // That is O((N+M)^2) in the worst case, so larger inputs take the coarse but
    // correct path instead of risking an OOM. Diff() is not used by the render
    // pipeline (which only needs AreEqual), so minimality is not worth unbounded
    // memory here.
    private const int MaxTraceLength = 2048;

    /// <summary>Returns true if <paramref name="oldText"/> and <paramref name="newText"/> are semantically identical.</summary>
    public static bool AreEqual(string? oldText, string? newText)
    {
        if (ReferenceEquals(oldText, newText)) return true;
        if (oldText is null || newText is null) return false;
        if (oldText.Length != newText.Length) return false;
        return oldText.AsSpan().SequenceEqual(newText.AsSpan());
    }

    /// <summary>
    /// Computes an edit script that transforms <paramref name="oldText"/> into
    /// <paramref name="newText"/>. Common prefixes and suffixes are trimmed first,
    /// which is also the common case for streaming Markdown updates.
    /// </summary>
    public static List<EditOp> Diff(string? oldText, string? newText)
    {
        var a = oldText ?? string.Empty;
        var b = newText ?? string.Empty;
        var result = new List<EditOp>();

        var maxPrefix = Math.Min(a.Length, b.Length);
        var prefix = 0;
        while (prefix < maxPrefix && a[prefix] == b[prefix])
            prefix++;

        for (var i = 0; i < prefix; i++)
            result.Add(new EditOp(OpKind.Equal, a[i].ToString()));

        var suffix = 0;
        while (suffix < maxPrefix - prefix &&
               a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix])
            suffix++;

        ComputeMiddle(a, b, prefix, a.Length - suffix, prefix, b.Length - suffix, result);

        for (var i = a.Length - suffix; i < a.Length; i++)
            result.Add(new EditOp(OpKind.Equal, a[i].ToString()));

        return result;
    }

    private static void ComputeMiddle(
        string a, string b, int aStart, int aEnd, int bStart, int bEnd, List<EditOp> result)
    {
        var n = aEnd - aStart;
        var m = bEnd - bStart;

        if (n == 0)
        {
            for (var i = bStart; i < bEnd; i++)
                result.Add(new EditOp(OpKind.Insert, b[i].ToString()));
            return;
        }

        if (m == 0)
        {
            for (var i = aStart; i < aEnd; i++)
                result.Add(new EditOp(OpKind.Delete, a[i].ToString()));
            return;
        }

        if (n + m > MaxTraceLength)
        {
            // Correct, just not minimal.
            for (var i = aStart; i < aEnd; i++)
                result.Add(new EditOp(OpKind.Delete, a[i].ToString()));
            for (var j = bStart; j < bEnd; j++)
                result.Add(new EditOp(OpKind.Insert, b[j].ToString()));
            return;
        }

        // Greedy Myers forward pass: v[k] is the furthest x reached on diagonal
        // k = x - y. A trace row is snapshotted before each distance so the path
        // can be reconstructed backwards.
        var max = n + m;
        var offset = max;
        var v = new int[2 * max + 1];
        v[offset + 1] = 0;

        var trace = new List<int[]>(max + 1);
        var foundD = -1;

        for (var d = 0; d <= max && foundD < 0; d++)
        {
            trace.Add((int[])v.Clone());

            for (var k = -d; k <= d; k += 2)
            {
                int x;
                if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                    x = v[offset + k + 1];
                else
                    x = v[offset + k - 1] + 1;

                var y = x - k;
                while (x < n && y < m && a[aStart + x] == b[bStart + y])
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;

                if (x >= n && y >= m)
                {
                    foundD = d;
                    break;
                }
            }
        }

        if (foundD < 0)
        {
            // Unreachable for a finite grid, but never spin or under-report.
            for (var i = aStart; i < aEnd; i++)
                result.Add(new EditOp(OpKind.Delete, a[i].ToString()));
            for (var j = bStart; j < bEnd; j++)
                result.Add(new EditOp(OpKind.Insert, b[j].ToString()));
            return;
        }

        var reverse = new List<EditOp>(n + m);
        var cx = n;
        var cy = m;

        for (var d = foundD; d > 0; d--)
        {
            var traceRow = trace[d];
            var k = cx - cy;

            int prevK;
            if (k == -d || (k != d && traceRow[offset + k - 1] < traceRow[offset + k + 1]))
                prevK = k + 1;
            else
                prevK = k - 1;

            var prevX = traceRow[offset + prevK];
            var prevY = prevX - prevK;

            while (cx > prevX && cy > prevY)
            {
                reverse.Add(new EditOp(OpKind.Equal, a[aStart + cx - 1].ToString()));
                cx--;
                cy--;
            }

            if (cx == prevX)
            {
                // The step advanced y: an insertion of b[cy - 1].
                reverse.Add(new EditOp(OpKind.Insert, b[bStart + cy - 1].ToString()));
                cy--;
            }
            else
            {
                // The step advanced x: a deletion of a[cx - 1].
                reverse.Add(new EditOp(OpKind.Delete, a[aStart + cx - 1].ToString()));
                cx--;
            }
        }

        // d == 0 is a pure diagonal of equal bytes.
        while (cx > 0 && cy > 0)
        {
            reverse.Add(new EditOp(OpKind.Equal, a[aStart + cx - 1].ToString()));
            cx--;
            cy--;
        }
        while (cx > 0)
        {
            reverse.Add(new EditOp(OpKind.Delete, a[aStart + cx - 1].ToString()));
            cx--;
        }
        while (cy > 0)
        {
            reverse.Add(new EditOp(OpKind.Insert, b[bStart + cy - 1].ToString()));
            cy--;
        }

        reverse.Reverse();
        result.AddRange(reverse);
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
