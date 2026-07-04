using System.Threading.Tasks;
using Avalonia.Controls;
using AvalonMarkdown.Views;
using AvalonMarkdown.Test.Shared.ViewModels;

namespace AvalonMarkdown.Test.Shared.Views;

public partial class MainView : UserControl
{
    private readonly MarkdownView _preview;

    public MainView()
    {
        InitializeComponent();

        // 必须在此设置 DataContext，否则 OnReady 中无法获取 ViewModel
        DataContext = new MainViewModel();

        _preview = this.FindControl<MarkdownView>("Preview")!;
        _preview.OnReady += OnPreviewReady;
        MarkdownEditor.TextChanged += OnMarkdownChanged;
    }

    private async void OnPreviewReady(object? sender, System.EventArgs e)
    {
        // 旧版代码有 500ms 延迟，等待 WebView 内部 JS 完全就绪
        await Task.Delay(500);

        if (DataContext is MainViewModel vm)
            await _preview.RenderMarkdownAsync(vm.MarkdownText);
    }

    private async void OnMarkdownChanged(object? sender, System.EventArgs e)
    {
        if (_preview is not null)
            await _preview.RenderMarkdownAsync(MarkdownEditor.Text);
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
    }
}
