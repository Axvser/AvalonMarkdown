using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using AvalonMarkdown.Services;
using AvalonMarkdown.ViewModels;

namespace AvalonMarkdown.Views;

/// <summary>
/// 统一的 MainView — 各平台共用。
/// MarkdownView 在代码中创建后放入 PreviewHost，避免 XAML 提前初始化导致浏览器端多实例。
/// </summary>
public partial class MainView : UserControl
{
    /// <summary>预览控件，由构造器初始化</summary>
    private MarkdownView MarkdownPreview { get; set; } = null!;

    public MainView()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        InitPreview(new EmbeddedHtmlSourceProvider());
    }

    /// <summary>
    /// 依赖注入构造：由平台层提供 IWebViewSourceProvider（如 Browser 的 UrlWebViewSourceProvider）。
    /// </summary>
    public MainView(IWebViewSourceProvider sourceProvider)
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        InitPreview(sourceProvider);
    }

    private void InitPreview(IWebViewSourceProvider sourceProvider)
    {
        MarkdownPreview = new MarkdownView(sourceProvider);
        MarkdownPreview.OnReady += OnNavComplete;
        MarkdownEditor.TextChanged += OnMarkdownChanged;
        PreviewHost.Children.Add(MarkdownPreview);
    }

    private MainViewModel GetVm() => (MainViewModel)DataContext!;

    private async void OnNavComplete(object? sender, EventArgs e)
    {
        await Task.Delay(500);
        await ApplyConfigAsync();
        await MarkdownPreview.RenderMarkdownAsync(MarkdownEditor.Text);
    }

    private async void OnMarkdownChanged(object? sender, EventArgs e)
    {
        await MarkdownPreview.RenderMarkdownAsync(MarkdownEditor.Text);
    }

    private async Task ApplyConfigAsync()
    {
        var config = GetVm().PreviewConfig;
        var theme = config.IsDarkTheme ? "dark" : "light";
        await MarkdownPreview.InvokeScriptAsync($"setTheme('{theme}')");
        await MarkdownPreview.ApplyConfigAsync(config.ToJsCallExpression());
    }
}