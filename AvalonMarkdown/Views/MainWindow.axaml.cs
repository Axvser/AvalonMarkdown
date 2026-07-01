using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using AvalonMarkdown.Services;
using AvalonMarkdown.ViewModels;

namespace AvalonMarkdown.Views;

public partial class MainWindow : Window
{
    private bool _ready;
    private readonly IMarkdownPreviewService _previewService;

    public MainWindow()
    {
        InitializeComponent();
        _previewService = new WebViewPreviewService(MarkdownPreview);

        var vm = new MainViewModel();
        DataContext = vm;

        var webDir = Path.Combine(AppContext.BaseDirectory, "Assets", "web");
        MarkdownEditor.TextChanged += OnMarkdownChanged;
        _previewService.NavigationCompleted += OnNavComplete;

        var indexPath = Path.Combine(webDir, "index.html");
        if (File.Exists(indexPath))
            MarkdownPreview.Source = new Uri("file:///" + indexPath.Replace('\\', '/'));
    }

    public MainWindow(IMarkdownPreviewService previewService)
    {
        _previewService = previewService;
        InitializeComponent();

        var vm = new MainViewModel();
        DataContext = vm;

        var webDir = Path.Combine(AppContext.BaseDirectory, "Assets", "web");
        MarkdownEditor.TextChanged += OnMarkdownChanged;
        _previewService.NavigationCompleted += OnNavComplete;

        var indexPath = Path.Combine(webDir, "index.html");
        if (File.Exists(indexPath))
            _ = _previewService.LoadHtmlAsync(indexPath);
    }

    private MainViewModel GetVm() => (MainViewModel)DataContext!;

    private void OnNavComplete(object? sender, EventArgs e)
    {
        _ready = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await ApplyConfigAsync();
                await SendMarkdownAsync(MarkdownEditor.Text);
            });
        });
    }

    private async void OnMarkdownChanged(object? sender, EventArgs e)
    {
        if (_ready)
            await SendMarkdownAsync(MarkdownEditor.Text);
    }

    private async Task SendMarkdownAsync(string? md)
    {
        if (string.IsNullOrEmpty(md)) return;
        var escaped = md.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
        try
        {
            await _previewService.InvokeScriptAsync($"renderMarkdown('{escaped}')");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebView] ❌ {ex.Message}");
        }
    }

    private async Task ApplyConfigAsync()
    {
        var config = GetVm().PreviewConfig;
        try
        {
            var theme = config.IsDarkTheme ? "dark" : "light";
            await _previewService.InvokeScriptAsync($"setTheme('{theme}')");
            await _previewService.InvokeScriptAsync(config.ToJsCallExpression());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebView] ❌ Config: {ex.Message}");
        }
    }
}
