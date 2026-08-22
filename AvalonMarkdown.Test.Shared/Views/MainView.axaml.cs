using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using AvalonMarkdown.Views;
using AvalonMarkdown.Test.Shared.ViewModels;

namespace AvalonMarkdown.Test.Shared.Views;

public partial class MainView : UserControl
{
    private readonly MainViewModel _vm;
    private MarkdownView? _singlePreview;
    private MarkdownView? _multiPv1, _multiPv2, _multiPv3;
    private bool _multiViewMode;

    // ── Automated error-capture test (AVALON_CAPTURE_ERRORS=1) ──
    private readonly StringBuilder _captureLog = new();
    private bool _captureDone;
    private bool _captureStarted;

    public MainView()
    {
        InitializeComponent();

        _vm = new MainViewModel();
        DataContext = _vm;

        // Single view — binding handles Text, OnReady just tracks timing
        _singlePreview = this.FindControl<MarkdownView>("Preview")!;
        _singlePreview.OnReady += (_, _) =>
        {
            _vm.RecordReady(1);
            _vm.RecordRendered(1);
        };

        // When AVALON_CAPTURE_ERRORS=1, run the automated error-capture test and exit.
        if (Environment.GetEnvironmentVariable("AVALON_CAPTURE_ERRORS") == "1")
            StartCaptureTest();

        // LinkClicked example: intercept app:// links for in-app navigation.
        // Everything else (https://, mailto:, …) is left unhandled and still opens
        // in the system browser via the control's default behavior.
        _singlePreview.LinkClicked += HandleLinkClicked;

        // Preset buttons — setting MarkdownText triggers the Text binding
        SimplePresetButton.Click += (_, _) => _vm.MarkdownText = MainViewModel.GetSimpleMarkdown();
        FullPresetButton.Click += (_, _) => _vm.MarkdownText = MainViewModel.GetDefaultMarkdown();
        BigDocPresetButton.Click += (_, _) => _vm.MarkdownText = MainViewModel.GetBigDocumentMarkdown();

        // Multi-view toggle
        MultiViewToggle.Click += (_, _) => ToggleMultiView();

        // Editor collapse toggle
        EditorToggle.PointerPressed += (_, _) =>
        {
            _vm.ToggleEditor();
            EditorArrowExpanded.IsVisible = _vm.EditorExpanded;
            EditorArrowCollapsed.IsVisible = !_vm.EditorExpanded;
        };
    }

    // ====================================================================
    // Automated error-capture test
    // ====================================================================

    private void StartCaptureTest()
    {
        _captureLog.AppendLine("=== AvalonMarkdown error-capture test ===");

        // Capture unhandled C# exceptions.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _captureLog.AppendLine($"[FATAL][C#] UnhandledException: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _captureLog.AppendLine($"[FATAL][C#] UnobservedTaskException: {e.Exception}");
            e.SetObserved();
        };

        // Capture every JS console message (renderer.js bridges console.log/warn/error).
        _singlePreview!.ConsoleMessage += (_, e) =>
            _captureLog.AppendLine($"[JS:{e.Level}] {e.Message}");
        _singlePreview.ErrorOccurred += (_, e) =>
            _captureLog.AppendLine($"[C#-Event] ErrorOccurred: {e.Title} — {e.Message}");

        // Start the sequence once the preview is ready; watchdog in case it never is.
        _singlePreview.OnReady += (_, _) => BeginCaptureSequence();
        _ = Task.Delay(TimeSpan.FromSeconds(45)).ContinueWith(_ => FinishCapture(timeout: true));
    }

    private void BeginCaptureSequence()
    {
        if (_captureStarted) return;
        _captureStarted = true;
        _ = RunCaptureSequenceAsync();
    }

    private async Task RunCaptureSequenceAsync()
    {
        try
        {
            // Each phase waits for the JS "✅ Render complete" signal (via ConsoleMessage),
            // then a grace delay lets async mermaid/plantuml errors land.
            await RunPhaseAsync("Simple", () => _vm.MarkdownText = MainViewModel.GetSimpleMarkdown());
            await RunPhaseAsync("Full (incl. intentionally-broken mermaid/plantuml)",
                () => _vm.MarkdownText = MainViewModel.GetDefaultMarkdown());
            await RunPhaseAsync("BigDoc", () => _vm.MarkdownText = MainViewModel.GetBigDocumentMarkdown());

            // Verify the native scroll-progress API round-trips through the WebView.
            _captureLog.AppendLine("\n=== Phase: ScrollToProgressAsync round-trip ===");
            foreach (var target in new[] { 0.0, 30.0, 70.0, 100.0 })
            {
                await _singlePreview!.ScrollToProgressAsync(target);
                await Task.Delay(250);
                var read = await _singlePreview.GetScrollProgressAsync();
                _captureLog.AppendLine($"[scroll] set={target} read={read?.ToString("0.#") ?? "null"}");
            }

            // Agent-style streaming: rapid updates with a growing table must keep the
            // view pinned to the bottom (auto-scroll follows). This is the table case
            // where a delayed scroll event used to misclassify auto-scroll as a user
            // scroll and suppress itself for the whole cooldown.
            _captureLog.AppendLine("\n=== Phase: auto-scroll follows streaming table ===");
            var seed = new StringBuilder("# Streaming\n\n| # | A | B | C | D |\n|---|---|---|---|---|\n");
            for (int i = 0; i < 30; i++) seed.AppendLine($"| {i} | a{i} | b{i} | c{i} | d{i} |");
            _vm.MarkdownText = seed.ToString();
            await Task.Delay(400); // let the initial render + auto-scroll settle
            var streamLagged = false;
            for (int i = 0; i < 12; i++)
            {
                seed.AppendLine($"| {30 + i} | extra-a{i} | extra-b{i} | extra-c{i} | extra-d{i} |");
                _vm.MarkdownText = seed.ToString();
                await Task.Delay(80);
                var p = await _singlePreview!.GetScrollProgressAsync();
                if (p is null || p < 95)
                {
                    streamLagged = true;
                    _captureLog.AppendLine($"[stream] iter={i} progress={p?.ToString("0.#") ?? "null"} (expect ~100)");
                }
            }
            _captureLog.AppendLine($"[stream] done lagged={streamLagged} (expect False)");

            // Verify heading listing + navigation (for TOC).
            _captureLog.AppendLine("\n=== Phase: heading navigation ===");
            var headings = await _singlePreview!.GetHeadingsAsync();
            _captureLog.AppendLine($"[headings] count={headings.Count}");
            foreach (var h in headings.Take(6))
                _captureLog.AppendLine($"[heading] h{h.Level}: {h.Text}");

            var lastH2 = headings.LastOrDefault(h => h.Level == 2);
            if (lastH2 is not null)
            {
                var found = await _singlePreview.NavigateToHeadingAsync(lastH2.Text);
                await Task.Delay(300);
                var prog = await _singlePreview.GetScrollProgressAsync();
                _captureLog.AppendLine($"[nav] to '{lastH2.Text}' found={found} progress={prog?.ToString("0.#") ?? "null"}");
            }
            var miss = await _singlePreview.NavigateToHeadingAsync("This heading does not exist");
            _captureLog.AppendLine($"[nav] missing-heading found={miss} (expect False)");

            // Verify external-script noise filtering: an injected google.com/extension
            // error must be dropped, a real preview error must still surface.
            _captureLog.AppendLine("\n=== Phase: external error filter ===");
            await _singlePreview.InvokeScriptAsync(
                "window.onerror('injected-extension-error', 'https://www.google.com/js/th/abc.js', 1, 1, new Error('ext'))");
            await _singlePreview.InvokeScriptAsync(
                "window.onerror('real-preview-error', 'https://example.com/app.js', 1, 1, new Error('real'))");
            await Task.Delay(500);
            _captureLog.AppendLine("[filter] done — real-preview-error should be present, injected-extension-error absent");

            // Verify the app:// link pipeline end-to-end: click dispatch → post('LINK')
            // → C# LinkClicked → demo swaps content (this is what Android hit).
            _captureLog.AppendLine("\n=== Phase: app:// link interception ===");
            await RunPhaseAsync("Full", () => _vm.MarkdownText = MainViewModel.GetDefaultMarkdown());
            var clickResult = await _singlePreview.InvokeScriptAsync(
                "var a = document.querySelector('a[data-href=\"app://about\"]'); a ? (a.click(), 1) : 0;");
            await Task.Delay(500);
            var swapped = _vm.MarkdownText.StartsWith("# 📖");
            var bridgeDetected = await _singlePreview.InvokeScriptAsync(
                "var b = (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) || (window.AvaloniaWebViewBridge && window.AvaloniaWebViewBridge.postMessage) || typeof window.invokeCSharpAction === 'function' || (window.external && typeof window.external.notify === 'function') || (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.postAvWebViewMessage); b ? 1 : 0;");
            _captureLog.AppendLine($"[applink] dispatch={clickResult} markdown-swapped={swapped} hasBridge={bridgeDetected}");

            FinishCapture(timeout: false);
        }
        catch (Exception ex)
        {
            _captureLog.AppendLine($"[FATAL][C#] capture driver: {ex}");
            FinishCapture(timeout: false);
        }
    }

    private async Task RunPhaseAsync(string name, Action load)
    {
        _captureLog.AppendLine($"\n=== Phase: {name} ===");
        load();

        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<MarkdownConsoleMessageEventArgs>? watcher = null;
        watcher = (_, e) =>
        {
            if (e.Level == "LOG" && e.Message.Contains("Render complete")) completed.TrySetResult(true);
        };
        _singlePreview!.ConsoleMessage += watcher;
        try
        {
            if (await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(12))) == completed.Task)
                _captureLog.AppendLine($"[phase] '{name}' render completed");
            else
                _captureLog.AppendLine($"[phase] '{name}' TIMEOUT waiting for render-complete");
        }
        finally
        {
            _singlePreview.ConsoleMessage -= watcher;
        }

        // Grace period for async diagram (mermaid/plantuml) errors to surface.
        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    private void FinishCapture(bool timeout)
    {
        if (_captureDone) return;
        _captureDone = true;

        if (timeout)
            _captureLog.AppendLine("\n[FATAL] watchdog timeout — preview never became ready");

        try
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "error-capture.log");
            File.WriteAllText(path, _captureLog.ToString());
            Console.WriteLine($"[capture] wrote {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[capture] failed to write log: {ex.Message}");
        }
        Environment.Exit(timeout ? 1 : 0);
    }

    /// <summary>
    /// Demo of <see cref="MarkdownView.LinkClicked"/>: routes <c>app://</c> links
    /// internally (swapping the preview to the target in-app page) and suppresses the
    /// default browser launch. All other links are intentionally left unhandled so
    /// they keep opening in the system browser.
    /// </summary>
    private void HandleLinkClicked(object? sender, MarkdownLinkClickedEventArgs e)
    {
        if (e.Url.StartsWith("app://", StringComparison.OrdinalIgnoreCase))
        {
            _vm.MarkdownText = MainViewModel.GetInternalPage(e.Url);
            e.Handled = true; // suppress the default system-browser launch
        }
    }

    // ====================================================================
    // Single view — binding handles rendering via Text property
    // ====================================================================

    // Private void OnPreviewReady removed — binding + _pendingMarkdown
    // handles the "Text set before OnReady" timing automatically.

    // ====================================================================
    // Multi-view toggle
    // ====================================================================

    private void ToggleMultiView()
    {
        _multiViewMode = !_multiViewMode;
        MultiViewToggle.Content = _multiViewMode ? "Single" : "Multi";
        SingleView.IsVisible = !_multiViewMode;
        MultiView.IsVisible = _multiViewMode;

        if (_multiViewMode)
        {
            _vm.IsMultiView = true;
            _vm.ResetMultiView();
            InitMultiView();
        }
        else
        {
            _vm.IsMultiView = false;
        }
    }

    private void InitMultiView()
    {
        if (_multiPv1 is not null) return; // already initialized

        _multiPv1 = this.FindControl<MarkdownView>("Preview1")!;
        _multiPv2 = this.FindControl<MarkdownView>("Preview2")!;
        _multiPv3 = this.FindControl<MarkdownView>("Preview3")!;

        // Set Text directly — the property handles deferred rendering if not yet ready
        _multiPv1.Text = MainViewModel.GetSimpleMarkdown();
        _multiPv2.Text = MainViewModel.GetDefaultMarkdown();
        _multiPv3.Text = MainViewModel.GetBigDocumentMarkdown();

        // OnReady still useful for timing measurement
        _multiPv1.OnReady += (_, _) => { _vm.RecordReady(1); _vm.RecordRendered(1); };
        _multiPv2.OnReady += (_, _) => { _vm.RecordReady(2); _vm.RecordRendered(2); };
        _multiPv3.OnReady += (_, _) => { _vm.RecordReady(3); _vm.RecordRendered(3); };

        // Full doc (Preview2) also contains the app:// link — apply the same handler.
        _multiPv2.LinkClicked += HandleLinkClicked;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
    }
}
